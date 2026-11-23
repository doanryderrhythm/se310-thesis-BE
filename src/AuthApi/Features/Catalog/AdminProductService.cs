using System.Text.RegularExpressions;
using AuthApi.Common;
using AuthApi.Data;
using AuthApi.Features.Inventory;
using AuthApi.Features.Products;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

internal sealed partial class AdminProductService(
    IMongoDatabase db, CounterService counters, InventoryService stock,
    AdminAttributeService materials, AdminAttributeService occasions)
{
    private readonly IMongoCollection<ProductDocument> products = db.GetCollection<ProductDocument>("products");
    private readonly IMongoCollection<CategoryDocument> categories = db.GetCollection<CategoryDocument>("categories");
    private readonly IMongoCollection<ProductImageDocument> images = db.GetCollection<ProductImageDocument>("product_images");
    private readonly IMongoCollection<ProductVariantDocument> variants = db.GetCollection<ProductVariantDocument>("product_variants");
    private readonly IMongoCollection<SizeDocument> sizes = db.GetCollection<SizeDocument>("sizes");
    private readonly IMongoCollection<ColorDocument> colors = db.GetCollection<ColorDocument>("colors");
    private readonly IMongoCollection<InventoryDocument> inventory = db.GetCollection<InventoryDocument>("inventory");
    private readonly IMongoCollection<SizeChartDocument> charts = db.GetCollection<SizeChartDocument>("size_charts");
    private readonly IMongoCollection<BsonDocument> orderItems = db.GetCollection<BsonDocument>("order_items");
    private readonly IMongoCollection<BsonDocument> cartItems = db.GetCollection<BsonDocument>("cart_items");
    private readonly IMongoCollection<MediaDocument> media = db.GetCollection<MediaDocument>("media");

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z")]
    private static partial Regex SkuPattern();

    private const int DefaultLowStock = 5;
    private static readonly string[] ProductStatuses = ["draft", "active", "hidden"];

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string Normalize(string? status) => status == "inactive" ? "hidden" : status ?? "draft";

    // ---- List / detail ------------------------------------------------------------------------

    public async Task<PageResponse<AdminProductListItem>> ListAsync(
        string? q, long? categoryId, string? status, string? sort, int page, int pageSize,
        long? materialId = null, long? occasionId = null)
    {
        var b = Builders<ProductDocument>.Filter;
        var filter = b.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(q.Trim()), "i");
            var skuMatches = await variants.Find(Builders<ProductVariantDocument>.Filter.Regex(v => v.Sku, pattern))
                .Project(v => v.ProductId).ToListAsync();
            filter &= b.Regex(p => p.Name, pattern) | b.In(p => p.ProductId, skuMatches);
        }
        if (categoryId is { } cid) filter &= b.Eq(p => p.CategoryId, cid);
        if (materialId is { } mid) filter &= b.Eq(p => p.MaterialId, mid);
        if (occasionId is { } oid) filter &= b.AnyEq(p => p.OccasionIds, oid);
        if (!string.IsNullOrWhiteSpace(status))
            filter &= status == "hidden" ? b.In(p => p.Status, ["hidden", "inactive"]) : b.Eq(p => p.Status, status);

        var total = await products.CountDocumentsAsync(filter);
        var find = products.Find(filter);
        find = sort switch
        {
            "name" => find.SortBy(p => p.Name).ThenByDescending(p => p.ProductId),
            "price" => find.SortBy(p => p.BasePrice).ThenByDescending(p => p.ProductId),
            _ => find.SortByDescending(p => p.CreatedAt).ThenByDescending(p => p.ProductId)
        };
        var list = await find.Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        var bundle = await new ProductCatalogService(db).LoadForProductsAsync(list);
        var materialNames = (await materials.AllAsync()).ToDictionary(x => x.Key, x => x.Value["name"].AsString);

        return new PageResponse<AdminProductListItem>(list.Select(p =>
        {
            var vs = bundle.VariantsByProduct.GetValueOrDefault(p.ProductId, []);
            var prices = vs.Select(v => v.Price ?? p.BasePrice).Where(x => x is not null).Select(x => x!.Value).ToArray();
            var thumb = bundle.ImagesByProduct.GetValueOrDefault(p.ProductId, [])
                .OrderBy(i => i.SortOrder ?? int.MaxValue).ThenBy(i => i.ImageId).FirstOrDefault();
            return new AdminProductListItem(
                p.ProductId, p.Name, p.Slug, p.CategoryId, bundle.CategoryNames.GetValueOrDefault(p.CategoryId),
                Normalize(p.Status), thumb?.ImageUrl, vs.Length,
                prices.Length > 0 ? prices.Min() : p.BasePrice, prices.Length > 0 ? prices.Max() : p.BasePrice,
                vs.Where(v => v.Status == "active").Sum(v => bundle.AvailableFor(v.VariantId)), p.CreatedAt, p.UpdatedAt,
                p.MaterialId, p.MaterialId is { } m ? materialNames.GetValueOrDefault(m) : null);
        }).ToArray(), page, pageSize, total);
    }

    public async Task<AdminProductDetail?> GetAsync(long productId)
    {
        var product = await products.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
        if (product is null) return null;
        var bundle = await new ProductCatalogService(db).LoadForProductsAsync([product]);
        var vs = bundle.VariantsByProduct.GetValueOrDefault(productId, []).OrderBy(v => v.VariantId).ToArray();
        var sold = (await orderItems.DistinctAsync<long>("variant_id",
            Builders<BsonDocument>.Filter.In("variant_id", vs.Select(v => v.VariantId)))).ToList().ToHashSet();
        var materialList = await materials.AllAsync();
        var occasionList = await occasions.AllAsync();
        var imageList = bundle.ImagesByProduct.GetValueOrDefault(productId, [])
            .OrderBy(i => i.SortOrder ?? int.MaxValue).ThenBy(i => i.ImageId).ToArray();
        return new AdminProductDetail(
            product.ProductId, product.Name, product.Slug, product.CategoryId, bundle.CategoryNames.GetValueOrDefault(product.CategoryId),
            product.Description, product.Material, product.BasePrice, Normalize(product.Status), product.SizeChartId,
            imageList.Select((i, index) => new AdminImageResponse(i.ImageId, i.ImageUrl, i.AltText, i.ColorId, i.SortOrder ?? index,
                i.IsPrimary ?? index == 0, i.MediaId, i.Width, i.Height, i.FocalX, i.FocalY,
                MediaEndpoints.Srcset(i.MediaFile, i.Widths))).ToArray(),
            vs.Select(v =>
            {
                bundle.Colors.TryGetValue(v.ColorId, out var color);
                bundle.Inventory.TryGetValue(v.VariantId, out var inv);
                return new AdminVariantResponse(v.VariantId, v.Sku, v.SizeId, bundle.SizeNames.GetValueOrDefault(v.SizeId),
                    v.ColorId, color?.Name, color?.HexCode, v.Price ?? product.BasePrice, Normalize(v.Status),
                    inv?.QuantityOnHand ?? 0, inv?.QuantityReserved ?? 0, bundle.AvailableFor(v.VariantId),
                    inv?.LowStockThreshold ?? DefaultLowStock, sold.Contains(v.VariantId));
            }).ToArray(),
            product.CreatedAt, product.UpdatedAt,
            product.MaterialId, product.MaterialId is { } mid && materialList.TryGetValue(mid, out var material) ? material["name"].AsString : null,
            product.Material,
            (product.OccasionIds ?? []).Where(occasionList.ContainsKey)
                .Select(id => new NamedRef(id, occasionList[id]["name"].AsString)).ToArray(),
            product.CareInstructions, ImageWarnings(imageList));
    }

    private static string[] ImageWarnings(ProductImageDocument[] list)
    {
        var warnings = new List<string>();
        if (list.Length > 0 && list.Length < ImageStandard.RecommendedImageCount)
            warnings.Add($"Sản phẩm có {list.Length} ảnh; nên có ít nhất {ImageStandard.RecommendedImageCount} ảnh (ảnh chính, góc khác, chi tiết chất liệu).");
        foreach (var (image, index) in list.Select((x, n) => (x, n)))
        {
            if (image.Width is not { } w || image.Height is not { } h) continue;
            if (Math.Abs((double)w / h - ImageStandard.FrameRatio) / ImageStandard.FrameRatio > ImageStandard.RatioTolerance)
                warnings.Add($"Ảnh {index + 1} ({w}x{h}) lệch tỷ lệ 3:4, khung hiển thị sẽ cắt ảnh; hãy đặt điểm lấy nét.");
        }
        return [.. warnings];
    }

    // ---- Create ----------------------------------------------------------------------------------

    public async Task<(AdminProductDetail? Product, IResult? Error)> CreateAsync(long adminId, CreateProductRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var name = r.Name?.Trim();
        var status = r.Status ?? "draft";
        if (name is null || name.Length is < 2 or > 200) errors["name"] = ["Name must be between 2 and 200 characters."];
        if (!ProductStatuses.Contains(status)) errors["status"] = ["Status must be 'draft', 'active' or 'hidden'."];
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? (name is null ? "" : Slugs.From(name)) : r.Slug.Trim();
        if (!Slugs.IsValid(slug)) errors["slug"] = ["Slug must be lowercase letters, digits and hyphens (at most 180 characters)."];
        var description = r.Description?.Trim();
        if (description is { Length: > 3000 }) errors["description"] = ["Description must be at most 3000 characters."];
        if (status == "active" && (description is null || description.Length < 20))
            errors["description"] = ["An active product needs a description of 20 to 3000 characters."];
        var materialDetail = Text.Clean(r.MaterialDetail ?? r.Material);
        if (materialDetail is { Length: > 150 }) errors["materialDetail"] = ["Material detail must be at most 150 characters."];
        var care = Text.Clean(r.CareInstructions);
        if (care is { Length: > 1000 }) errors["careInstructions"] = ["Care instructions must be at most 1000 characters."];
        long? materialId = r.MaterialId is > 0 ? r.MaterialId : null;
        var occasionIds = (r.OccasionIds ?? []).Distinct().ToList();
        await ValidateAttributesAsync(materialId, occasionIds, errors);
        if (r.BasePrice is not > 0 or > CampaignMaxPrice) errors["basePrice"] = ["Base price must be greater than 0 and at most 100,000,000."];
        if (r.CategoryId is null || !await categories.Find(c => c.CategoryId == r.CategoryId).AnyAsync())
            errors["categoryId"] = ["Category does not exist."];
        if (r.SizeChartId is { } chartId && !await charts.Find(c => c.SizeChartId == chartId).AnyAsync())
            errors["sizeChartId"] = ["Size chart does not exist."];

        var imageInputs = r.Images ?? [];
        var variantInputs = r.Variants ?? [];
        var mediaById = await ValidateImagesAsync(imageInputs, errors);
        var sizeIds = variantInputs.Where(v => v.SizeId is not null).Select(v => v.SizeId!.Value).Distinct().ToArray();
        var colorIds = variantInputs.Where(v => v.ColorId is not null).Select(v => v.ColorId!.Value).Distinct().ToArray();
        var knownSizes = (await sizes.Find(Builders<SizeDocument>.Filter.In(s => s.SizeId, sizeIds)).ToListAsync()).Select(s => s.SizeId).ToHashSet();
        var knownColors = (await colors.Find(Builders<ColorDocument>.Filter.In(c => c.ColorId, colorIds)).ToListAsync()).Select(c => c.ColorId).ToHashSet();
        var variantErrors = new List<string>();
        if (variantInputs.Length > 100) variantErrors.Add("At most 100 variants.");
        foreach (var (v, index) in variantInputs.Select((v, i) => (v, i)))
            variantErrors.AddRange(ValidateVariant(v, index, knownSizes, knownColors));
        // SKUs are stored upper-case, so uniqueness is effectively case-insensitive.
        var skus = variantInputs.Select(v => v.Sku?.Trim().ToUpperInvariant()).Where(s => s is not null).ToList();
        if (skus.Distinct().Count() != skus.Count) variantErrors.Add("SKUs must be unique within the product.");
        if (variantInputs.Select(v => (v.SizeId, v.ColorId)).Distinct().Count() != variantInputs.Length)
            variantErrors.Add("Each size and color combination may appear only once.");
        if (variantErrors.Count > 0) errors["variants"] = [.. variantErrors];
        if (status == "active")
        {
            if (imageInputs.Length == 0) errors["images"] = ["An active product needs at least one image."];
            if (materialId is null) errors["materialId"] = ["An active product needs a material."];
            if (occasionIds.Count == 0) errors["occasionIds"] = ["An active product needs at least one occasion."];
            if (!variantInputs.Any(v => (v.Status ?? "active") == "active"))
                errors["variants"] = [.. variantErrors, "An active product needs at least one active variant."];
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        if (await products.Find(p => p.Slug == slug).AnyAsync()) return (null, Invalid("slug", "Slug already exists."));
        if (variantInputs.Length > 0)
        {
            var taken = await variants.Find(Builders<ProductVariantDocument>.Filter.In(v => v.Sku, variantInputs.Select(v => v.Sku!.Trim().ToUpperInvariant()))).Project(v => v.Sku).FirstOrDefaultAsync();
            if (taken is not null) return (null, Invalid("variants", $"SKU {taken} already exists."));
        }

        var now = DateTime.UtcNow;
        var product = new ProductDocument
        {
            ProductId = await counters.NextAsync("products"), CategoryId = r.CategoryId!.Value, SizeChartId = r.SizeChartId,
            Name = name!, Slug = slug, Description = description, Material = materialDetail,
            MaterialId = materialId, OccasionIds = occasionIds.Count > 0 ? occasionIds : null, CareInstructions = care,
            SearchText = SearchTextOf(name!, description),
            BasePrice = r.BasePrice, Status = status, CreatedAt = now, UpdatedAt = now
        };
        try
        {
            await products.InsertOneAsync(product);
            await InsertImagesAsync(product.ProductId, product.Name, imageInputs, mediaById);
            foreach (var v in variantInputs) await InsertVariantAsync(product, v, adminId);
        }
        catch (Exception)
        {
            await DeleteProductTreeAsync(product.ProductId);
            throw;
        }

        return (await GetAsync(product.ProductId), null);
    }

    private const double CampaignMaxPrice = 100_000_000;

    public static string SearchTextOf(string name, string? description) =>
        Text.Fold($"{name} {description}");

    private async Task ValidateAttributesAsync(long? materialId, List<long> occasionIds, Dictionary<string, string[]> errors)
    {
        if (materialId is { } m && !(await materials.ExistingIdsAsync([m])).Contains(m))
            errors["materialId"] = ["Material does not exist."];
        if (occasionIds.Count > 10) errors["occasionIds"] = ["Choose at most 10 occasions."];
        else if (occasionIds.Count > 0 && (await occasions.ExistingIdsAsync(occasionIds)).Count != occasionIds.Count)
            errors["occasionIds"] = ["One or more occasions do not exist."];
    }

    // ---- Update / status -------------------------------------------------------------------------

    public async Task<(AdminProductDetail? Product, IResult? Error)> UpdateAsync(long productId, UpdateProductRequest r)
    {
        var product = await products.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
        if (product is null) return (null, Results.NotFound());

        var errors = new Dictionary<string, string[]>();
        var u = Builders<ProductDocument>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow);
        var finalName = product.Name;
        var finalDescription = product.Description;
        if (r.Name is not null)
        {
            var name = r.Name.Trim();
            if (name.Length is < 2 or > 200) errors["name"] = ["Name must be between 2 and 200 characters."];
            else
            {
                u = u.Set(p => p.Name, name);
                finalName = name;
            }
        }
        if (r.Slug is not null)
        {
            var slug = r.Slug.Trim();
            if (!Slugs.IsValid(slug)) errors["slug"] = ["Slug must be lowercase letters, digits and hyphens (at most 180 characters)."];
            else if (await products.Find(p => p.Slug == slug && p.ProductId != productId).AnyAsync()) errors["slug"] = ["Slug already exists."];
            else u = u.Set(p => p.Slug, slug);
        }
        if (r.CategoryId is { } cid)
        {
            if (!await categories.Find(c => c.CategoryId == cid).AnyAsync()) errors["categoryId"] = ["Category does not exist."];
            else u = u.Set(p => p.CategoryId, cid);
        }
        if (r.Description is not null)
        {
            var d = r.Description.Trim();
            if (d.Length > 3000 || (Normalize(product.Status) == "active" && d.Length < 20))
                errors["description"] = ["Description must be 20 to 3000 characters for an active product."];
            else
            {
                u = u.Set(p => p.Description, d.Length == 0 ? null : d);
                finalDescription = d.Length == 0 ? null : d;
            }
        }
        var detail = r.MaterialDetail ?? r.Material;
        if (detail is not null)
        {
            var cleaned = Text.Clean(detail);
            if (cleaned is { Length: > 150 }) errors["materialDetail"] = ["Material detail must be at most 150 characters."];
            else u = u.Set(p => p.Material, cleaned);
        }
        if (r.CareInstructions is not null)
        {
            var care = Text.Clean(r.CareInstructions);
            if (care is { Length: > 1000 }) errors["careInstructions"] = ["Care instructions must be at most 1000 characters."];
            else u = care is null ? u.Unset(p => p.CareInstructions) : u.Set(p => p.CareInstructions, care);
        }
        var isActive = Normalize(product.Status) == "active";
        if (r.MaterialId is { } materialId)
        {
            if (materialId == 0)
            {
                if (isActive) errors["materialId"] = ["An active product needs a material."];
                else u = u.Unset(p => p.MaterialId);
            }
            else if (!(await materials.ExistingIdsAsync([materialId])).Contains(materialId)) errors["materialId"] = ["Material does not exist."];
            else u = u.Set(p => p.MaterialId, materialId);
        }
        if (r.OccasionIds is { } occasionInput)
        {
            var ids = occasionInput.Distinct().ToList();
            if (ids.Count == 0)
            {
                if (isActive) errors["occasionIds"] = ["An active product needs at least one occasion."];
                else u = u.Unset(p => p.OccasionIds);
            }
            else if (ids.Count > 10) errors["occasionIds"] = ["Choose at most 10 occasions."];
            else if ((await occasions.ExistingIdsAsync(ids)).Count != ids.Count) errors["occasionIds"] = ["One or more occasions do not exist."];
            else u = u.Set(p => p.OccasionIds, ids);
        }
        if (r.BasePrice is { } price)
        {
            if (price is <= 0 or > CampaignMaxPrice) errors["basePrice"] = ["Base price must be greater than 0 and at most 100,000,000."];
            else u = u.Set(p => p.BasePrice, price);
        }
        if (r.SizeChartId is { } chart)
        {
            if (chart == 0) u = u.Set(p => p.SizeChartId, null);
            else if (!await charts.Find(c => c.SizeChartId == chart).AnyAsync()) errors["sizeChartId"] = ["Size chart does not exist."];
            else u = u.Set(p => p.SizeChartId, chart);
        }
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));
        if (r.Name is not null || r.Description is not null) u = u.Set(p => p.SearchText, SearchTextOf(finalName, finalDescription));

        try
        {
            await products.UpdateOneAsync(p => p.ProductId == productId, u);
        }
        catch (MongoWriteException e) when (e.WriteError?.Code == 11000)
        {
            return (null, Invalid("slug", "Slug already exists."));
        }

        return (await GetAsync(productId), null);
    }

    public async Task<(AdminProductDetail? Product, IResult? Error)> SetStatusAsync(long productId, string? status)
    {
        var product = await products.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
        if (product is null) return (null, Results.NotFound());
        if (status is null || !ProductStatuses.Contains(status))
            return (null, Invalid("status", "Status must be 'draft', 'active' or 'hidden'."));

        if (status == "active")
        {
            var problems = new List<string>();
            if ((product.Description?.Trim().Length ?? 0) < 20) problems.Add("a description of at least 20 characters");
            if (!await images.Find(i => i.ProductId == productId).AnyAsync()) problems.Add("at least one image");
            if (!await variants.Find(v => v.ProductId == productId && v.Status == "active").AnyAsync()) problems.Add("at least one active variant");
            if (product.MaterialId is null) problems.Add("a material");
            if ((product.OccasionIds?.Count ?? 0) == 0) problems.Add("at least one occasion");
            if (problems.Count > 0)
                return (null, Results.Conflict(new { message = $"An active product needs {string.Join(", ", problems)}." }));
        }

        await products.UpdateOneAsync(p => p.ProductId == productId,
            Builders<ProductDocument>.Update.Set(p => p.Status, status).Set(p => p.UpdatedAt, DateTime.UtcNow));
        return (await GetAsync(productId), null);
    }

    // ---- Images ----------------------------------------------------------------------------------------

    public async Task<(AdminProductDetail? Product, IResult? Error)> ReplaceImagesAsync(long productId, ImageInput[]? input)
    {
        var product = await products.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
        if (product is null) return (null, Results.NotFound());
        var list = input ?? [];
        var errors = new Dictionary<string, string[]>();
        var mediaById = await ValidateImagesAsync(list, errors);
        if (list.Length == 0 && Normalize(product.Status) == "active")
            errors["images"] = ["An active product needs at least one image."];
        if (errors.Count > 0) return (null, Results.ValidationProblem(errors));

        var oldIds = await images.Find(i => i.ProductId == productId).Project(i => i.ImageId).ToListAsync();
        await InsertImagesAsync(productId, product.Name, list, mediaById);
        await images.DeleteManyAsync(Builders<ProductImageDocument>.Filter.In(i => i.ImageId, oldIds));
        await products.UpdateOneAsync(p => p.ProductId == productId, Builders<ProductDocument>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow));
        return (await GetAsync(productId), null);
    }

    /// <summary>Validates image input and returns the uploaded media referenced by <c>mediaId</c>.</summary>
    private async Task<Dictionary<long, MediaDocument>> ValidateImagesAsync(ImageInput[] list, Dictionary<string, string[]> errors)
    {
        var problems = new List<string>();
        if (list.Length > 20) problems.Add("At most 20 images.");
        var colorIds = list.Where(i => i.ColorId is not null).Select(i => i.ColorId!.Value).Distinct().ToArray();
        var known = (await colors.Find(Builders<ColorDocument>.Filter.In(c => c.ColorId, colorIds)).ToListAsync()).Select(c => c.ColorId).ToHashSet();
        var mediaIds = list.Where(i => i.MediaId is not null).Select(i => i.MediaId!.Value).Distinct().ToArray();
        var mediaById = (await media.Find(Builders<MediaDocument>.Filter.In(m => m.MediaId, mediaIds)).ToListAsync())
            .ToDictionary(m => m.MediaId);
        foreach (var (image, index) in list.Select((i, n) => (i, n)))
        {
            var label = $"Image {index + 1}";
            if (image.MediaId is { } mediaId)
            {
                if (!mediaById.ContainsKey(mediaId)) problems.Add($"{label}: the uploaded file (mediaId {mediaId}) does not exist.");
            }
            else
            {
                var url = image.Url?.Trim();
                if (string.IsNullOrEmpty(url) || url.Length > 500 || !(url.StartsWith('/') || url.StartsWith("http://") || url.StartsWith("https://")))
                    problems.Add($"{label}: send a mediaId from the upload API, or a url that is a path starting with / or an http(s) URL of at most 500 characters.");
            }
            if (image.AltText is { Length: > 255 }) problems.Add($"{label}: altText must be at most 255 characters.");
            if (image.ColorId is { } cid && !known.Contains(cid)) problems.Add($"{label}: color does not exist.");
            if (image.FocalX is < 0 or > 1 || image.FocalY is < 0 or > 1) problems.Add($"{label}: focalX and focalY must be between 0 and 1.");
        }
        if (problems.Count > 0) errors["images"] = [.. problems];
        return mediaById;
    }

    private async Task InsertImagesAsync(
        long productId, string productName, ImageInput[] list, Dictionary<long, MediaDocument> mediaById)
    {
        // The image flagged primary goes first, otherwise the order is sortOrder (or the order sent).
        var ordered = list.Select((i, n) => (Image: i, Sort: i.SortOrder ?? n))
            .OrderByDescending(x => x.Image.IsPrimary == true).ThenBy(x => x.Sort).ToArray();
        foreach (var (item, index) in ordered.Select((x, n) => (x, n)))
        {
            var image = item.Image;
            var upload = image.MediaId is { } id ? mediaById[id] : null;
            await images.InsertOneAsync(new ProductImageDocument
            {
                ImageId = await counters.NextAsync("product_images"), ProductId = productId,
                ImageUrl = upload is null ? image.Url!.Trim() : MediaEndpoints.UrlFor(upload.File),
                AltText = string.IsNullOrWhiteSpace(image.AltText) ? $"{productName} - ảnh {index + 1}" : image.AltText.Trim(),
                ColorId = image.ColorId, SortOrder = index, IsPrimary = index == 0,
                MediaId = upload?.MediaId, MediaFile = upload?.File, Width = upload?.Width, Height = upload?.Height,
                Widths = upload?.Widths, FocalX = image.FocalX, FocalY = image.FocalY
            });
        }
    }

    // ---- Variants ---------------------------------------------------------------------------------------------

    public async Task<(AdminProductDetail? Product, IResult? Error)> AddVariantAsync(long adminId, long productId, VariantInput input)
    {
        var product = await products.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
        if (product is null) return (null, Results.NotFound());
        var knownSizes = (await sizes.Find(s => s.SizeId == input.SizeId).ToListAsync()).Select(s => s.SizeId).ToHashSet();
        var knownColors = (await colors.Find(c => c.ColorId == input.ColorId).ToListAsync()).Select(c => c.ColorId).ToHashSet();
        var problems = ValidateVariant(input, 0, knownSizes, knownColors).ToArray();
        if (problems.Length > 0) return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["variant"] = problems }));
        if (await variants.Find(v => v.ProductId == productId && v.SizeId == input.SizeId && v.ColorId == input.ColorId).AnyAsync())
            return (null, Results.Conflict(new { message = "This size and color combination already exists for the product." }));
        var newSku = input.Sku!.Trim().ToUpperInvariant();
        if (await variants.Find(v => v.Sku == newSku).AnyAsync())
            return (null, Results.Conflict(new { message = $"SKU {newSku} already exists." }));
        try
        {
            await InsertVariantAsync(product, input, adminId);
        }
        catch (MongoWriteException e) when (e.WriteError?.Code == 11000)
        {
            return (null, Results.Conflict(new { message = "SKU or size/color combination already exists." }));
        }

        await products.UpdateOneAsync(p => p.ProductId == productId, Builders<ProductDocument>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow));
        return (await GetAsync(productId), null);
    }

    public async Task<(AdminProductDetail? Product, IResult? Error)> UpdateVariantAsync(long variantId, UpdateVariantRequest r)
    {
        var variant = await variants.Find(v => v.VariantId == variantId).FirstOrDefaultAsync();
        if (variant is null) return (null, Results.NotFound());
        var u = Builders<ProductVariantDocument>.Update;
        var updates = new List<UpdateDefinition<ProductVariantDocument>>();
        if (r.Sku is not null)
        {
            var sku = r.Sku.Trim().ToUpperInvariant();
            if (!SkuPattern().IsMatch(sku)) return (null, Invalid("sku", "SKU must be 1-100 characters: letters, digits, '.', '_' or '-'."));
            if (await variants.Find(v => v.Sku == sku && v.VariantId != variantId).AnyAsync())
                return (null, Results.Conflict(new { message = $"SKU {sku} already exists." }));
            updates.Add(u.Set(v => v.Sku, sku));
        }
        if (r.Price is { } price)
        {
            if (price is <= 0 or > CampaignMaxPrice) return (null, Invalid("price", "Price must be greater than 0 and at most 100,000,000."));
            updates.Add(u.Set(v => v.Price, price));
        }
        if (r.Status is not null)
        {
            if (r.Status is not ("active" or "hidden")) return (null, Invalid("status", "Status must be 'active' or 'hidden'."));
            if (r.Status == "hidden" && await IsLastActiveVariantOfActiveProductAsync(variant))
                return (null, Results.Conflict(new { message = "An active product must keep at least one active variant; hide the product instead." }));
            updates.Add(u.Set(v => v.Status, r.Status));
        }
        if (updates.Count > 0) await variants.UpdateOneAsync(v => v.VariantId == variantId, u.Combine(updates));
        await products.UpdateOneAsync(p => p.ProductId == variant.ProductId, Builders<ProductDocument>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow));
        return (await GetAsync(variant.ProductId), null);
    }

    public async Task<(AdminProductDetail? Product, IResult? Error)> DeleteVariantAsync(long variantId)
    {
        var variant = await variants.Find(v => v.VariantId == variantId).FirstOrDefaultAsync();
        if (variant is null) return (null, Results.NotFound());
        if (await orderItems.Find(Builders<BsonDocument>.Filter.Eq("variant_id", variantId)).AnyAsync())
            return (null, Results.Conflict(new { message = "This variant appears in orders and cannot be deleted; hide it instead." }));
        if (await IsLastActiveVariantOfActiveProductAsync(variant))
            return (null, Results.Conflict(new { message = "An active product must keep at least one active variant; hide the product instead." }));

        await cartItems.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq("variant_id", variantId));
        await inventory.DeleteOneAsync(i => i.VariantId == variantId);
        await variants.DeleteOneAsync(v => v.VariantId == variantId);
        return (await GetAsync(variant.ProductId), null);
    }

    private async Task<bool> IsLastActiveVariantOfActiveProductAsync(ProductVariantDocument variant)
    {
        if (variant.Status != "active") return false;
        var product = await products.Find(p => p.ProductId == variant.ProductId).FirstOrDefaultAsync();
        if (product is null || Normalize(product.Status) != "active") return false;
        return !await variants.Find(v => v.ProductId == variant.ProductId && v.Status == "active" && v.VariantId != variant.VariantId).AnyAsync();
    }

    private IEnumerable<string> ValidateVariant(VariantInput v, int index, HashSet<long> knownSizes, HashSet<long> knownColors)
    {
        var label = $"Variant {index + 1}";
        if (v.Sku is null || !SkuPattern().IsMatch(v.Sku.Trim())) yield return $"{label}: SKU must be 1-100 characters: letters, digits, '.', '_' or '-'.";
        if (v.SizeId is null || !knownSizes.Contains(v.SizeId.Value)) yield return $"{label}: size does not exist.";
        if (v.ColorId is null || !knownColors.Contains(v.ColorId.Value)) yield return $"{label}: color does not exist.";
        if (v.Price is <= 0 or > CampaignMaxPrice) yield return $"{label}: price must be greater than 0 and at most 100,000,000.";
        if (v.Status is not (null or "active" or "hidden")) yield return $"{label}: status must be 'active' or 'hidden'.";
        if (v.InitialOnHand is > 0) yield return $"{label}: initialOnHand is no longer accepted; receive stock with a goods receipt.";
        if (v.LowStockThreshold is < 0 or > 100_000) yield return $"{label}: lowStockThreshold must be between 0 and 100000.";
    }

    private async Task InsertVariantAsync(ProductDocument product, VariantInput v, long adminId)
    {
        var variant = new ProductVariantDocument
        {
            VariantId = await counters.NextAsync("product_variants"), ProductId = product.ProductId,
            SizeId = v.SizeId!.Value, ColorId = v.ColorId!.Value, Sku = v.Sku!.Trim().ToUpperInvariant(),
            Price = v.Price ?? product.BasePrice, Status = v.Status ?? "active"
        };
        await variants.InsertOneAsync(variant);
        await stock.InitializeAsync(variant.VariantId, 0, v.LowStockThreshold ?? DefaultLowStock, adminId);
    }

    private async Task DeleteProductTreeAsync(long productId)
    {
        var variantIds = await variants.Find(v => v.ProductId == productId).Project(v => v.VariantId).ToListAsync();
        await inventory.DeleteManyAsync(Builders<InventoryDocument>.Filter.In(i => i.VariantId, variantIds));
        await variants.DeleteManyAsync(v => v.ProductId == productId);
        await images.DeleteManyAsync(i => i.ProductId == productId);
        await products.DeleteOneAsync(p => p.ProductId == productId);
    }
}
