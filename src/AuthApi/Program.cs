using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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

var app = builder.Build();

await users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
    Builders<User>.IndexKeys.Ascending(user => user.Phone),
    new CreateIndexOptions { Unique = true, Name = "phone_unique" }));
await InitializeUserCounterAsync(users, counters);

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/register", async (RegisterRequest request) =>
{
    var validationErrors = ValidateRegistration(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var phone = request.Phone!;
    var fullName = request.FullName!.Trim();
    var password = request.Password!;

    if (await users.Find(user => user.Phone == phone).AnyAsync())
    {
        return Results.Conflict(new { message = "A user with this phone number already exists." });
    }

    var user = new User
    {
        UserId = await NextUserIdAsync(counters),
        FullName = fullName,
        Phone = phone,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
        Role = "customer",
        Status = "active",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    try
    {
        await users.InsertOneAsync(user);
    }
    catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
    {
        return Results.Conflict(new { message = "A user with this phone number already exists." });
    }

    return Results.Ok(CreateAuthResponse(user, tokenKey));
});

app.MapPost("/api/auth/login", async (LoginRequest request) =>
{
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
});

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
    var productFilter = Builders<ProductDocument>.Filter.Regex(product => product.Name, namePattern);
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
        .SortBy(image => image.ImageId)
        .ToListAsync();
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

app.Run();

static Dictionary<string, string[]> ValidateRegistration(RegisterRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100)
    {
        errors["fullName"] = ["Full name is required and must be at most 100 characters."];
    }

    if (!IsValidPhone(request.Phone))
    {
        errors["phone"] = ["Phone must contain exactly 10 digits."];
    }

    if (!IsValidPassword(request.Password))
    {
        errors["password"] = ["Password must be at least 8 characters and no more than 72 UTF-8 bytes."];
    }

    return errors;
}

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

static bool IsValidPhone(string? phone) =>
    phone is not null && Regex.IsMatch(phone, @"\A[0-9]{10}\z", RegexOptions.CultureInvariant);

static bool IsValidPassword(string? password) =>
    !string.IsNullOrEmpty(password)
    && password.Length >= 8
    && Encoding.UTF8.GetByteCount(password) <= 72;

static async Task<long> NextUserIdAsync(IMongoCollection<Counter> counters)
{
    var counter = await counters.FindOneAndUpdateAsync(
        item => item.Id == "users",
        Builders<Counter>.Update.Inc(item => item.SequenceValue, 1),
        new FindOneAndUpdateOptions<Counter>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        });
    return counter.SequenceValue;
}

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

static AuthResponse CreateAuthResponse(User user, SymmetricSecurityKey tokenKey)
{
    var expiresAt = DateTime.UtcNow.AddHours(1);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.FullName),
        new Claim(ClaimTypes.Role, user.Role)
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
        new UserResponse(user.UserId, user.FullName, user.Phone, user.Role));
}

internal sealed record RegisterRequest(string? FullName, string? Phone, string? Password);
internal sealed record LoginRequest(string? Phone, string? Password);
internal sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);
internal sealed record UserResponse(long UserId, string FullName, string Phone, string Role);
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
}

[BsonIgnoreExtraElements]
internal sealed class CategoryDocument
{
    [BsonElement("category_id")]
    public long CategoryId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;
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
}

[BsonIgnoreExtraElements]
internal sealed class InventoryDocument
{
    [BsonElement("variant_id")]
    public long VariantId { get; set; }

    [BsonElement("quantity_on_hand")]
    public int? QuantityOnHand { get; set; }

    [BsonElement("quantity_reserved")]
    public int? QuantityReserved { get; set; }
}
