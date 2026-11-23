using SkiaSharp;

namespace AuthApi.Features.Catalog;

/// <summary>
/// Product image standard (shared by the upload API and the policy endpoint):
/// portrait 3:4 frame like most fashion shops, at least 900 x 1200 px, JPEG / PNG / WebP up to 20 MB.
/// The file is really decoded (not trusted by name or content type), rotated by its EXIF orientation,
/// converted to sRGB, stripped of metadata and stored as WebP plus 400 / 800 / 1200 px wide versions
/// (never enlarged).
/// </summary>
internal static class ImageStandard
{
    public const long MaxBytes = 20L * 1024 * 1024;
    public const int MinWidth = 900;
    public const int MinHeight = 1200;
    public const int MaxMasterSide = 2400;
    public const long MaxPixels = 40_000_000;
    public const double FrameRatio = 3.0 / 4.0; // width / height
    public const double RatioTolerance = 0.05;
    public static readonly int[] Widths = [400, 800, 1200];
    public const int RecommendedWidth = 1200;
    public const int RecommendedHeight = 1600;
    public const int RecommendedImageCount = 3;

    public static object Describe() => new
    {
        aspectRatio = "3:4",
        minWidth = MinWidth,
        minHeight = MinHeight,
        recommendedSize = $"{RecommendedWidth}x{RecommendedHeight}",
        maxFileSizeMb = MaxBytes / 1024 / 1024,
        formats = new[] { "jpeg", "png", "webp" },
        storedAs = "webp",
        widths = Widths,
        recommendedImageCount = RecommendedImageCount,
        note = "Ảnh lệch tỷ lệ 3:4 vẫn được nhận; đặt điểm lấy nét (focalX, focalY) để khung hiển thị cắt đẹp."
    };
}

internal sealed record ProcessedImage(
    string BaseName, int Width, int Height, List<int> Widths, string Format, long OriginalBytes, string[] Warnings);

internal sealed class ImageRejectedException(string message) : Exception(message);

internal static class ImageProcessor
{
    /// <summary>Recognises the format from magic bytes, not from the client-supplied name or type.</summary>
    public static string? Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpeg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return "png";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
            && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return "webp";
        return null;
    }

    /// <summary>Validates, normalises and writes the image versions into <paramref name="folder"/>.</summary>
    public static ProcessedImage Process(byte[] data, string folder, string baseName)
    {
        var format = Detect(data) ?? throw new ImageRejectedException("Only JPEG, PNG or WebP images are accepted.");

        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData)
            ?? throw new ImageRejectedException("The file is not a readable image.");
        var info = codec.Info;
        if ((long)info.Width * info.Height > ImageStandard.MaxPixels)
            throw new ImageRejectedException("The image has more than 40 megapixels.");

        var origin = codec.EncodedOrigin;
        // Decode the whole picture; Skia would otherwise hand back a half-decoded bitmap for a truncated file.
        var pixelInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var source = new SKBitmap(pixelInfo);
        if (codec.GetPixels(pixelInfo, source.GetPixels()) != SKCodecResult.Success)
            throw new ImageRejectedException("The file is truncated or corrupt.");
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;

        if (width < ImageStandard.MinWidth || height < ImageStandard.MinHeight)
        {
            throw new ImageRejectedException(
                $"The image is {width}x{height} px; the minimum is {ImageStandard.MinWidth}x{ImageStandard.MinHeight} px.");
        }

        using var image = SKImage.FromBitmap(source);

        // Master: oriented, sRGB, at most 2400 px on the long side.
        var scale = Math.Min(1.0, (double)ImageStandard.MaxMasterSide / Math.Max(width, height));
        var masterWidth = (int)Math.Round(width * scale);
        var masterHeight = (int)Math.Round(height * scale);
        var written = new List<string>();
        try
        {
            using (var master = Render(image, source.Width, source.Height, origin, masterWidth, masterHeight))
                Save(master, Path.Combine(folder, $"{baseName}.webp"), 85, written);

            var widths = new List<int>();
            foreach (var target in ImageStandard.Widths)
            {
                if (target >= masterWidth) continue; // never enlarge
                var targetHeight = (int)Math.Round((double)masterHeight * target / masterWidth);
                using var small = Render(image, source.Width, source.Height, origin, target, targetHeight);
                Save(small, Path.Combine(folder, $"{baseName}-w{target}.webp"), 80, written);
                widths.Add(target);
            }

            return new ProcessedImage(baseName, masterWidth, masterHeight, widths, format, data.Length, Warnings(width, height));
        }
        catch
        {
            foreach (var file in written)
            {
                try { File.Delete(file); } catch (IOException) { }
            }
            throw;
        }
    }

    public static string[] Warnings(int width, int height)
    {
        var warnings = new List<string>();
        var ratio = (double)width / height;
        if (Math.Abs(ratio - ImageStandard.FrameRatio) / ImageStandard.FrameRatio > ImageStandard.RatioTolerance)
        {
            warnings.Add($"Ảnh {width}x{height} lệch tỷ lệ 3:4; khung hiển thị sẽ cắt ảnh. Hãy đặt điểm lấy nét (focalX, focalY) hoặc chọn ảnh khác.");
        }
        if (width < ImageStandard.RecommendedWidth || height < ImageStandard.RecommendedHeight)
        {
            warnings.Add($"Nên dùng ảnh từ {ImageStandard.RecommendedWidth}x{ImageStandard.RecommendedHeight} px để ảnh phóng to vẫn nét.");
        }
        return [.. warnings];
    }

    private static void Save(SKBitmap bitmap, string path, int quality, List<string> written)
    {
        using var picture = SKImage.FromBitmap(bitmap);
        using var encoded = picture.Encode(SKEncodedImageFormat.Webp, quality)
            ?? throw new ImageRejectedException("The image could not be converted.");
        using (var file = File.Create(path))
        {
            encoded.SaveTo(file);
        }
        written.Add(path);
    }

    /// <summary>Draws the source rotated by its EXIF origin onto a white sRGB canvas of the given size.</summary>
    private static SKBitmap Render(SKImage image, int sourceWidth, int sourceHeight, SKEncodedOrigin origin, int outWidth, int outHeight)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var orientedWidth = swap ? sourceHeight : sourceWidth;
        float sw = sourceWidth, sh = sourceHeight;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, sw, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, sw, 0, -1, sh, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, sh, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, sh, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, sh, -1, 0, sw, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, sw, 0, 0, 1),
            _ => SKMatrix.Identity
        };

        var target = new SKBitmap(new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul,
            SKColorSpace.CreateSrgb()));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.White);
        canvas.Scale((float)outWidth / orientedWidth);
        canvas.Concat(in matrix);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        return target;
    }
}
