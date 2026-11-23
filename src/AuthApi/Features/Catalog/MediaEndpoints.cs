using System.Text.RegularExpressions;
using AuthApi.Security;
using MongoDB.Driver;

namespace AuthApi.Features.Catalog;

/// <summary>
/// Product image upload (admin) and serving. Files live in the folder given by Uploads__Path
/// (a Docker volume in compose) and are exposed under /api/media so they share the /api proxy.
/// </summary>
internal static partial class MediaEndpoints
{
    private const long MaxBytes = 5 * 1024 * 1024;

    [GeneratedRegex(@"\A[0-9a-f]{32}\.(jpg|png|webp)\z")]
    private static partial Regex FileNamePattern();

    public static void MapMediaEndpoints(
        this IEndpointRouteBuilder app, IMongoCollection<User> users, string uploadFolder)
    {
        Directory.CreateDirectory(uploadFolder);
        var currentUsers = new CurrentUserService(users);

        app.MapPost("/api/admin/uploads/images", async (HttpContext context) =>
        {
            var (_, error) = await currentUsers.ResolveAsync(context, UserRoles.Admin);
            if (error is not null) return error;
            if (!context.Request.HasFormContentType || context.Request.ContentLength > MaxBytes + 64 * 1024)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Send a multipart form with a 'file' field of at most 5 MB."] });

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > MaxBytes)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Provide an image file of at most 5 MB."] });

            await using var stream = file.OpenReadStream();
            var header = new byte[12];
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length), context.RequestAborted);
            var extension = Detect(header.AsSpan(0, read));
            if (extension is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Only JPEG, PNG or WebP images are accepted."] });

            var name = $"{Guid.NewGuid():N}.{extension}";
            stream.Position = 0;
            await using (var target = File.Create(Path.Combine(uploadFolder, name)))
            {
                await stream.CopyToAsync(target, context.RequestAborted);
            }

            return Results.Created($"/api/media/{name}", new UploadResponse($"/api/media/{name}", name, file.Length));
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

    /// <summary>Recognises the format from magic bytes, not from the client-supplied name or type.</summary>
    private static string? Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return "png";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
            && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return "webp";
        return null;
    }
}
