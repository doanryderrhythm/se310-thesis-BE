using System.Text.RegularExpressions;
using AuthApi.Data;
using AuthApi.Security;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

[BsonIgnoreExtraElements]
internal sealed class MediaDocument
{
    [BsonElement("media_id")] public long MediaId { get; set; }
    /// <summary>File base name (a GUID); the master is {file}.webp, versions are {file}-w{width}.webp.</summary>
    [BsonElement("file")] public string File { get; set; } = string.Empty;
    [BsonElement("width")] public int Width { get; set; }
    [BsonElement("height")] public int Height { get; set; }
    [BsonElement("widths")] public List<int> Widths { get; set; } = [];
    [BsonElement("original_format")] public string? OriginalFormat { get; set; }
    [BsonElement("original_bytes")] public long OriginalBytes { get; set; }
    [BsonElement("uploaded_by")] public long? UploadedBy { get; set; }
    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Product image upload (admin) and serving. Files live in the folder given by Uploads__Path
/// (a Docker volume in compose) and are exposed under /api/media so they share the /api proxy.
/// See <see cref="ImageStandard"/> for the accepted images.
/// </summary>
internal static partial class MediaEndpoints
{
    [GeneratedRegex(@"\A[0-9a-f]{32}(-w(400|800|1200))?\.(jpg|png|webp)\z")]
    private static partial Regex FileNamePattern();

    public static string UrlFor(string file, int? width = null) =>
        width is null ? $"/api/media/{file}.webp" : $"/api/media/{file}-w{width}.webp";

    public static Dictionary<string, string>? Srcset(string? file, IEnumerable<int>? widths) =>
        file is null || widths is null
            ? null
            : widths.OrderBy(w => w).ToDictionary(w => $"w{w}", w => UrlFor(file, w));

    public static async Task SetupAsync(IMongoDatabase db, CounterService counters)
    {
        await IdCounters.SyncAsync(db, counters, "media", "media", "media_id");
    }

    public static void MapMediaEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, IMongoDatabase db, CounterService counters,
        string uploadFolder)
    {
        Directory.CreateDirectory(uploadFolder);
        var currentUsers = new CurrentUserService(users);
        var media = db.GetCollection<MediaDocument>("media");

        static IResult Invalid(string message) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

        app.MapGet("/api/media-standard", () => Results.Ok(ImageStandard.Describe()));

        app.MapPost("/api/admin/uploads/images", async (HttpContext context) =>
        {
            var (admin, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            if (!context.Request.HasFormContentType || context.Request.ContentLength > ImageStandard.MaxBytes + 64 * 1024)
                return Invalid("Send a multipart form with a 'file' field of at most 20 MB.");

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > ImageStandard.MaxBytes)
                return Invalid("Provide an image file of at most 20 MB.");

            byte[] data;
            await using (var stream = file.OpenReadStream())
            {
                using var buffer = new MemoryStream((int)file.Length);
                await stream.CopyToAsync(buffer, context.RequestAborted);
                data = buffer.ToArray();
            }

            var baseName = Guid.NewGuid().ToString("N");
            ProcessedImage processed;
            try
            {
                processed = await Task.Run(() => ImageProcessor.Process(data, uploadFolder, baseName), context.RequestAborted);
            }
            catch (ImageRejectedException rejected)
            {
                return Invalid(rejected.Message);
            }

            var document = new MediaDocument
            {
                MediaId = await counters.NextAsync("media"), File = baseName, Width = processed.Width,
                Height = processed.Height, Widths = processed.Widths, OriginalFormat = processed.Format,
                OriginalBytes = processed.OriginalBytes, UploadedBy = admin!.UserId, CreatedAt = DateTime.UtcNow
            };
            await media.InsertOneAsync(document);

            var url = UrlFor(baseName);
            return Results.Created(url, new UploadResponse(
                url, $"{baseName}.webp", processed.OriginalBytes, document.MediaId, processed.Width, processed.Height,
                Math.Round((double)processed.Width / processed.Height, 4), processed.Format,
                Srcset(baseName, processed.Widths)!, processed.Warnings));
        }).RequireAuthorization();

        app.MapGet("/api/media/{fileName}", (string fileName, HttpContext context) =>
        {
            if (!FileNamePattern().IsMatch(fileName)) return Results.NotFound();
            var path = Path.Combine(uploadFolder, fileName);
            if (!File.Exists(path)) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var contentType = fileName.EndsWith(".png") ? "image/png" : fileName.EndsWith(".webp") ? "image/webp" : "image/jpeg";
            return Results.File(path, contentType);
        });
    }
}
