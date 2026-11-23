using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using AuthApi.Features.Admin;
using AuthApi.Features.Carts;
using AuthApi.Features.Catalog;
using AuthApi.Features.Customers;
using AuthApi.Features.Email;
using AuthApi.Features.Inventory;
using AuthApi.Features.Orders;
using AuthApi.Features.Notifications;
using AuthApi.Features.Products;
using AuthApi.Features.Purchasing;
using AuthApi.Features.Promotions;
using AuthApi.Features.Reports;
using AuthApi.Features.Returns;
using AuthApi.Features.Reviews;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration["MONGODB_URI"]
    ?? throw new InvalidOperationException("MONGODB_URI must be configured.");
var databaseName = builder.Configuration["MONGODB_DATABASE"]
    ?? throw new InvalidOperationException("MONGODB_DATABASE must be configured.");
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
if (Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 UTF-8 bytes.");
}

var tokenKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
var mongoDatabase = new MongoClient(connectionString).GetDatabase(databaseName);
var users = mongoDatabase.GetCollection<User>("users");
var deliveryAddresses = mongoDatabase.GetCollection<DeliveryAddressDocument>("addresses");
var counters = mongoDatabase.GetCollection<Counter>("counters");
var products = mongoDatabase.GetCollection<ProductDocument>("products");
var categories = mongoDatabase.GetCollection<CategoryDocument>("categories");
var productImages = mongoDatabase.GetCollection<ProductImageDocument>("product_images");
var productVariants = mongoDatabase.GetCollection<ProductVariantDocument>("product_variants");
var sizes = mongoDatabase.GetCollection<SizeDocument>("sizes");
var colors = mongoDatabase.GetCollection<ColorDocument>("colors");
var inventory = mongoDatabase.GetCollection<InventoryDocument>("inventory");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "se310-thesis-BE",
            ValidateAudience = true,
            ValidAudience = "se310-thesis-BE-client",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = tokenKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// Brute-force guard for login/register: a fixed window per client address. 0 turns it off
// (the e2e scripts do), compose uses RateLimit__AuthPerMinute.
var authPerMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many attempts. Please wait a moment and try again." }, token);
    };
    options.AddPolicy("auth", httpContext => authPerMinute <= 0
        ? RateLimitPartition.GetNoLimiter("off")
        : RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddHostedService<EmailSenderWorker>();

var app = builder.Build();

await users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
    Builders<User>.IndexKeys.Ascending(user => user.Phone),
    new CreateIndexOptions { Unique = true, Name = "phone_unique" }));
await InitializeUserCounterAsync(users, counters);
await InitializeAddressCounterAsync(deliveryAddresses, counters);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var counterService = new AuthApi.Data.CounterService(counters);
await NotificationService.SetupAsync(mongoDatabase, counterService);
await PromotionSetup.SetupAsync(mongoDatabase, counterService);
await InventoryService.SetupAsync(mongoDatabase, counterService);
await AdminOrderService.SetupAsync(mongoDatabase, counterService);
await CatalogSetup.SetupAsync(mongoDatabase, counterService);
await AdminAttributeService.SetupAsync(mongoDatabase, counterService, AttributeKind.Material);
await AdminAttributeService.SetupAsync(mongoDatabase, counterService, AttributeKind.Occasion);
await MediaEndpoints.SetupAsync(mongoDatabase, counterService);
await SupplierService.SetupAsync(mongoDatabase, counterService);
await GoodsReceiptService.SetupAsync(mongoDatabase, counterService);
await ReturnService.SetupAsync(mongoDatabase, counterService);
var notificationService = new NotificationService(mongoDatabase, counterService);
var voucherIssuer = new VoucherIssuer(
    mongoDatabase, counterService, notificationService, app.Services.GetRequiredService<EmailQueue>(), app.Logger);
var accountService = new AuthApi.Features.Auth.AccountService(users, counters);
app.MapPost("/api/auth/register", async (RegisterRequest request) =>
{
    request = AuthApi.Features.Auth.AccountValidator.Normalize(request);
    var validationErrors = AuthApi.Features.Auth.AccountValidator.ValidateRegistration(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    if (await accountService.PhoneExistsAsync(request.Phone))
    {
        return Results.Conflict(new { message = "A user with this phone number already exists." });
    }

    User user;
    try
    {
        user = await accountService.CreateAsync(request, "customer");
    }
    catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
    {
        return Results.Conflict(new { message = "A user with this phone number already exists." });
    }

    // Welcome vouchers must never make a successful registration fail.
    try
    {
        await voucherIssuer.IssueSignupVouchersAsync(user);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Issuing signup vouchers failed for user {UserId}.", user.UserId);
    }

    return Results.Ok(CreateAuthResponse(user, tokenKey));
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (LoginRequest request) =>
{
    request = request with { Phone = AuthApi.Common.PhoneNumbers.Normalize(request.Phone) ?? request.Phone };
    var validationErrors = ValidateLogin(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var user = await users.Find(candidate => candidate.Phone == request.Phone).FirstOrDefaultAsync();
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(CreateAuthResponse(user, tokenKey));
}).RequireRateLimiting("auth");

// Change the password of the signed-in account (customer or admin). Tokens issued before the change stop
// working; the response carries a fresh one for this session.
async Task<IResult> ChangePasswordAsync(HttpContext context, string role, ChangePasswordRequest request)
{
    var (user, error) = await new AuthApi.Security.CurrentUserService(users).ResolveAsync(context, role);
    if (error is not null) return error;

    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrEmpty(request.CurrentPassword))
        errors["currentPassword"] = ["Current password is required."];
    if (!AuthApi.Features.Auth.AccountValidator.IsValidPassword(request.NewPassword))
        errors["newPassword"] = ["Password must be at least 8 characters and no more than 72 UTF-8 bytes."];
    else if (request.NewPassword == request.CurrentPassword)
        errors["newPassword"] = ["The new password must be different from the current one."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user!.PasswordHash))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["Current password is incorrect."] });

    var now = DateTime.UtcNow;
    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword!, workFactor: 12);
    user.PasswordChangedAt = now;
    await users.UpdateOneAsync(candidate => candidate.UserId == user.UserId,
        Builders<User>.Update.Set(candidate => candidate.PasswordHash, user.PasswordHash)
            .Set(candidate => candidate.PasswordChangedAt, now).Set(candidate => candidate.UpdatedAt, now));
    return Results.Ok(CreateAuthResponse(user, tokenKey));
}

app.MapPost("/api/customers/me/password", (HttpContext context, ChangePasswordRequest request) =>
    ChangePasswordAsync(context, "customer", request)).RequireAuthorization().RequireRateLimiting("auth");
app.MapPost("/api/admin/me/password", (HttpContext context, ChangePasswordRequest request) =>
    ChangePasswordAsync(context, "admin", request)).RequireAuthorization().RequireRateLimiting("auth");

app.MapGet("/api/products/search", async (string? name, long? categoryId) =>
{
    if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Name is required and must be at most 100 characters."]
        });
    }

    var namePattern = new BsonRegularExpression(Regex.Escape(name.Trim()), "i");
    var productFilter = Builders<ProductDocument>.Filter.Regex(product => product.Name, namePattern)
        & Builders<ProductDocument>.Filter.Eq(product => product.Status, "active");
    if (categoryId.HasValue)
        productFilter &= Builders<ProductDocument>.Filter.Eq(product => product.CategoryId, categoryId.Value);

    var matchingProducts = await products
        .Find(productFilter)
        .SortBy(product => product.Name)
        .Limit(50)
        .ToListAsync();

    if (matchingProducts.Count == 0)
    {
        return Results.Ok(Array.Empty<ProductResponse>());
    }

    var productIds = matchingProducts.Select(product => product.ProductId).ToArray();
    var matchingImages = await productImages
        .Find(Builders<ProductImageDocument>.Filter.In(image => image.ProductId, productIds))
        .ToListAsync();
    matchingImages = [.. matchingImages.OrderBy(image => image.SortOrder ?? int.MaxValue).ThenBy(image => image.ImageId)];
    var matchingVariants = await productVariants
        .Find(Builders<ProductVariantDocument>.Filter.In(variant => variant.ProductId, productIds))
        .SortBy(variant => variant.VariantId)
        .ToListAsync();
    var categoryIds = matchingProducts.Select(product => product.CategoryId).Distinct().ToArray();
    var matchingCategories = await categories
        .Find(Builders<CategoryDocument>.Filter.In(category => category.CategoryId, categoryIds))
        .ToListAsync();
    var variantIds = matchingVariants.Select(variant => variant.VariantId).ToArray();
    var matchingInventory = await inventory
        .Find(Builders<InventoryDocument>.Filter.In(item => item.VariantId, variantIds))
        .ToListAsync();
    var sizeIds = matchingVariants.Select(variant => variant.SizeId).Distinct().ToArray();
    var colorIds = matchingVariants.Select(variant => variant.ColorId).Distinct().ToArray();
    var matchingSizes = await sizes
        .Find(Builders<SizeDocument>.Filter.In(size => size.SizeId, sizeIds))
        .ToListAsync();
    var matchingColors = await colors
        .Find(Builders<ColorDocument>.Filter.In(color => color.ColorId, colorIds))
        .ToListAsync();

    var categoryNames = matchingCategories.ToDictionary(category => category.CategoryId, category => category.Name);
    var sizeNames = matchingSizes.ToDictionary(size => size.SizeId, size => size.Name);
    var colorsById = matchingColors.ToDictionary(color => color.ColorId);
    var inventoryByVariantId = matchingInventory.ToDictionary(item => item.VariantId);
    var imagesByProductId = matchingImages
        .GroupBy(image => image.ProductId)
        .ToDictionary(group => group.Key, group => group.ToArray());
    var variantsByProductId = matchingVariants
        .GroupBy(variant => variant.ProductId)
        .ToDictionary(group => group.Key, group => group.ToArray());

    var response = matchingProducts.Select(product => new ProductResponse(
        product.ProductId, product.CategoryId,
        categoryNames.GetValueOrDefault(product.CategoryId),
        product.SizeChartId,
        product.Name, product.Description, product.Material, product.BasePrice,
        product.Status,
        imagesByProductId.GetValueOrDefault(product.ProductId, [])
            .Select(image => new ProductImageResponse(image.ImageUrl, image.AltText))
            .ToArray(),
        variantsByProductId.GetValueOrDefault(product.ProductId, [])
            .Select(variant =>
            {
                colorsById.TryGetValue(variant.ColorId, out var color);
                inventoryByVariantId.TryGetValue(variant.VariantId, out var stock);
                return new ProductVariantResponse(
                    variant.VariantId, variant.Sku,
                    sizeNames.GetValueOrDefault(variant.SizeId),
                    color?.Name, color?.HexCode,
                    variant.Price, variant.Status,
                    stock?.QuantityOnHand, stock?.QuantityReserved);
            })
            .ToArray()))
        .ToArray();

    return Results.Ok(response);
});

await AuthApi.Features.Carts.CartService.SetupAsync(mongoDatabase, counterService);
await AuthApi.Features.Reviews.ReviewEndpoints.SetupAsync(mongoDatabase, counterService);
app.MapProductEndpoints(mongoDatabase, builder.Configuration);
app.MapCartEndpoints(mongoDatabase, users, counterService);
app.MapReviewEndpoints(mongoDatabase, users, counterService);
app.MapAdminReviewEndpoints(mongoDatabase, users);
var locationService = new AuthApi.Features.Locations.LocationService(mongoDatabase);
app.MapCustomerEndpoints(users, deliveryAddresses, counters, locationService);
AuthApi.Features.Locations.LocationEndpoints.MapLocationEndpoints(app, locationService);
app.MapAdminEndpoints(mongoDatabase, users, counters);
var catalogService = new ProductCatalogService(mongoDatabase);
var couponService = new CouponService(mongoDatabase, counterService, catalogService);
var inventoryService = new InventoryService(mongoDatabase, counterService);
app.MapCampaignEndpoints(users, new CampaignService(mongoDatabase, counterService, voucherIssuer));
app.MapCouponEndpoints(users, couponService);
var materialService = new AdminAttributeService(mongoDatabase, counterService, AttributeKind.Material);
var occasionService = new AdminAttributeService(mongoDatabase, counterService, AttributeKind.Occasion);
app.MapAdminCatalogEndpoints(users, new AdminTaxonomyService(mongoDatabase, counterService),
    new AdminProductService(mongoDatabase, counterService, inventoryService, materialService, occasionService),
    materialService, occasionService);
app.MapAdminInventoryEndpoints(users, new AdminInventoryService(mongoDatabase, inventoryService));
app.MapPurchasingEndpoints(users, new SupplierService(mongoDatabase, counterService),
    new GoodsReceiptService(mongoDatabase, counterService, inventoryService, app.Logger), builder.Configuration);
app.MapMediaEndpoints(users, mongoDatabase, counterService,
    builder.Configuration["Uploads:Path"] ?? Path.Combine(app.Environment.ContentRootPath, "uploads"));
app.MapReportEndpoints(users, new ReportService(mongoDatabase));
app.MapReturnEndpoints(users, new ReturnService(
    mongoDatabase, counterService, inventoryService, notificationService, app.Logger));
var adminOrderService = new AdminOrderService(
    mongoDatabase, counterService, inventoryService, couponService, voucherIssuer, notificationService, app.Logger);
app.MapAdminOrderEndpoints(users, adminOrderService);
var bankAccount = builder.Configuration["Store:BankAccountNumber"];
app.MapCustomerOrderEndpoints(users, new CustomerOrderService(mongoDatabase, adminOrderService,
    string.IsNullOrWhiteSpace(bankAccount)
        ? null
        : new BankTransferInfo(builder.Configuration["Store:BankName"], bankAccount, builder.Configuration["Store:BankAccountName"])));
app.MapPurchaseEndpoints(users, new PurchaseService(
    mongoDatabase, counterService, inventoryService, couponService));
app.MapNotificationEndpoints(users, notificationService);

app.Run();

static Dictionary<string, string[]> ValidateLogin(LoginRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (!IsValidPhone(request.Phone))
    {
        errors["phone"] = ["Phone must contain exactly 10 digits."];
    }

    if (string.IsNullOrEmpty(request.Password))
    {
        errors["password"] = ["Password is required."];
    }

    return errors;
}

// Lenient on purpose: accounts registered before the mobile-prefix rule must still log in.
static bool IsValidPhone(string? phone) => AuthApi.Common.PhoneNumbers.IsTenDigits(phone);

static async Task InitializeUserCounterAsync(
    IMongoCollection<User> users,
    IMongoCollection<Counter> counters)
{
    var largestUserId = await users.Find(Builders<User>.Filter.Empty)
        .SortByDescending(user => user.UserId)
        .Project(user => user.UserId)
        .FirstOrDefaultAsync();
    var update = Builders<Counter>.Update.Max(counter => counter.SequenceValue, largestUserId);

    try
    {
        await counters.UpdateOneAsync(
            counter => counter.Id == "users",
            update,
            new UpdateOptions { IsUpsert = true });
    }
    catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
    {
        await counters.UpdateOneAsync(counter => counter.Id == "users", update);
    }
}

static async Task InitializeAddressCounterAsync(
    IMongoCollection<DeliveryAddressDocument> addresses,
    IMongoCollection<Counter> counters)
{
    var largestAddressId = await addresses.Find(Builders<DeliveryAddressDocument>.Filter.Empty)
        .SortByDescending(address => address.AddressId)
        .Project(address => address.AddressId)
        .FirstOrDefaultAsync();
    var update = Builders<Counter>.Update.Max(counter => counter.SequenceValue, largestAddressId);

    try
    {
        await counters.UpdateOneAsync(
            counter => counter.Id == "addresses",
            update,
            new UpdateOptions { IsUpsert = true });
    }
    catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
    {
        await counters.UpdateOneAsync(counter => counter.Id == "addresses", update);
    }
}

static AuthResponse CreateAuthResponse(User user, SymmetricSecurityKey tokenKey)
{
    var expiresAt = DateTime.UtcNow.AddHours(1);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.FullName),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
    };
    var token = new JwtSecurityToken(
        issuer: "se310-thesis-BE",
        audience: "se310-thesis-BE-client",
        claims: claims,
        expires: expiresAt,
        signingCredentials: new SigningCredentials(tokenKey, SecurityAlgorithms.HmacSha256));

    return new AuthResponse(
        new JwtSecurityTokenHandler().WriteToken(token),
        expiresAt,
        new UserResponse(user.UserId, user.FullName, user.Phone, user.Role, user.Email));
}

internal sealed record RegisterRequest(string? FullName, string? Phone, string? Password, string? Email = null);
internal sealed record LoginRequest(string? Phone, string? Password);
internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
internal sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);
internal sealed record UserResponse(long UserId, string FullName, string Phone, string Role, string? Email = null);
internal sealed record ProductResponse(
    long ProductId, long CategoryId, string? CategoryName, long? SizeChartId,
    string Name, string? Description, string? Material, double? BasePrice, string? Status,
    ProductImageResponse[] Images,
    ProductVariantResponse[] Variants);
internal sealed record ProductImageResponse(string? ImageUrl, string? AltText);
internal sealed record ProductVariantResponse(
    long VariantId,
    string? Sku, string? Size, string? Color, string? HexCode,
    double? Price, string? Status,
    int? QuantityOnHand, int? QuantityReserved);

[BsonIgnoreExtraElements]
internal sealed class User
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("user_id")]
    public long UserId { get; set; }

    [BsonElement("full_name")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("phone")]
    public string Phone { get; set; } = string.Empty;

    [BsonElement("email")]
    [BsonIgnoreIfNull]
    public string? Email { get; set; }

    [BsonElement("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("status_reason")]
    [BsonIgnoreIfNull]
    public string? StatusReason { get; set; }

    [BsonElement("status_changed_at")]
    [BsonIgnoreIfNull]
    public DateTime? StatusChangedAt { get; set; }

    /// <summary>Tokens issued before this moment are rejected (see CurrentUserService).</summary>
    [BsonElement("password_changed_at")]
    [BsonIgnoreIfNull]
    public DateTime? PasswordChangedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class DeliveryAddressDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("address_id")]
    public long AddressId { get; set; }

    [BsonElement("user_id")]
    public long UserId { get; set; }

    [BsonElement("recipient_name")]
    public string? RecipientName { get; set; }

    [BsonElement("recipient_phone")]
    public string? RecipientPhone { get; set; }

    [BsonElement("address_line")]
    public string? AddressLine { get; set; }

    [BsonElement("ward")]
    public string? Ward { get; set; }

    [BsonElement("district")]
    public string? District { get; set; }

    [BsonElement("province")]
    public string? Province { get; set; }

    [BsonElement("province_code")]
    public int? ProvinceCode { get; set; }

    [BsonElement("district_code")]
    public int? DistrictCode { get; set; }

    [BsonElement("ward_code")]
    public int? WardCode { get; set; }

    [BsonElement("is_default")]
    public bool? IsDefault { get; set; }

    [BsonElement("created_at")]
    public DateTime? CreatedAt { get; set; }
}

internal sealed class Counter
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("sequence_value")]
    public long SequenceValue { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ProductDocument
{
    [BsonElement("product_id")]
    public long ProductId { get; set; }

    [BsonElement("category_id")]
    public long CategoryId { get; set; }

    [BsonElement("size_chart_id")]
    public long? SizeChartId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("material")]
    public string? Material { get; set; }

    [BsonElement("base_price")]
    public double? BasePrice { get; set; }

    [BsonElement("status")]
    public string? Status { get; set; }

    [BsonElement("slug")]
    public string? Slug { get; set; }

    [BsonElement("material_id")]
    [BsonIgnoreIfNull]
    public long? MaterialId { get; set; }

    [BsonElement("occasion_ids")]
    [BsonIgnoreIfNull]
    public List<long>? OccasionIds { get; set; }

    [BsonElement("care_instructions")]
    [BsonIgnoreIfNull]
    public string? CareInstructions { get; set; }

    /// <summary>Lower-cased, accent-free name + description used by the public search.</summary>
    [BsonElement("search_text")]
    [BsonIgnoreIfNull]
    public string? SearchText { get; set; }

    [BsonElement("created_at")]
    public DateTime? CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class CategoryDocument
{
    [BsonElement("category_id")]
    public long CategoryId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("slug")]
    public string? Slug { get; set; }

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("status")]
    public string? Status { get; set; }

    [BsonElement("display_order")]
    public int? DisplayOrder { get; set; }

    [BsonElement("created_at")]
    public DateTime? CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ProductImageDocument
{
    [BsonElement("image_id")]
    public long ImageId { get; set; }

    [BsonElement("product_id")]
    public long ProductId { get; set; }

    [BsonElement("image_url")]
    public string? ImageUrl { get; set; }

    [BsonElement("alt_text")]
    public string? AltText { get; set; }

    [BsonElement("color_id")]
    public long? ColorId { get; set; }

    [BsonElement("sort_order")]
    public int? SortOrder { get; set; }

    [BsonElement("is_primary")]
    [BsonIgnoreIfNull]
    public bool? IsPrimary { get; set; }

    /// <summary>Set when the image came from the upload pipeline (see MediaEndpoints).</summary>
    [BsonElement("media_id")]
    [BsonIgnoreIfNull]
    public long? MediaId { get; set; }

    [BsonElement("media_file")]
    [BsonIgnoreIfNull]
    public string? MediaFile { get; set; }

    [BsonElement("width")]
    [BsonIgnoreIfNull]
    public int? Width { get; set; }

    [BsonElement("height")]
    [BsonIgnoreIfNull]
    public int? Height { get; set; }

    [BsonElement("widths")]
    [BsonIgnoreIfNull]
    public List<int>? Widths { get; set; }

    /// <summary>Focus point 0..1 used for object-position when the frame crops the photo.</summary>
    [BsonElement("focal_x")]
    [BsonIgnoreIfNull]
    public double? FocalX { get; set; }

    [BsonElement("focal_y")]
    [BsonIgnoreIfNull]
    public double? FocalY { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ProductVariantDocument
{
    [BsonElement("variant_id")]
    public long VariantId { get; set; }

    [BsonElement("product_id")]
    public long ProductId { get; set; }

    [BsonElement("size_id")]
    public long SizeId { get; set; }

    [BsonElement("color_id")]
    public long ColorId { get; set; }

    [BsonElement("sku")]
    public string? Sku { get; set; }

    [BsonElement("price")]
    public double? Price { get; set; }

    [BsonElement("status")]
    public string? Status { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class SizeDocument
{
    [BsonElement("size_id")]
    public long SizeId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("status")]
    public string? Status { get; set; }

    [BsonElement("display_order")]
    [BsonIgnoreIfNull]
    public int? DisplayOrder { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ColorDocument
{
    [BsonElement("color_id")]
    public long ColorId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("hex_code")]
    public string? HexCode { get; set; }

    [BsonElement("status")]
    public string? Status { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class InventoryDocument
{
    [BsonElement("inventory_id")]
    public long InventoryId { get; set; }

    [BsonElement("variant_id")]
    public long VariantId { get; set; }

    [BsonElement("low_stock_threshold")]
    public int? LowStockThreshold { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("quantity_on_hand")]
    public int? QuantityOnHand { get; set; }

    [BsonElement("quantity_reserved")]
    public int? QuantityReserved { get; set; }

    /// <summary>Moving average of the unit cost of received goods (goods receipts).</summary>
    [BsonElement("average_cost")]
    [BsonIgnoreIfNull]
    public double? AverageCost { get; set; }
}
