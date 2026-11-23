using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AuthApi.Common;

internal static partial class Slugs
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex SlugPattern();

    /// <summary>"Áo thun Đen" → "ao-thun-den".</summary>
    public static string From(string text)
    {
        var lower = text.Trim().ToLowerInvariant().Replace('đ', 'd');
        var builder = new StringBuilder();
        foreach (var c in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }

        var slug = NonSlugChars().Replace(builder.ToString(), "-").Trim('-');
        return slug.Length > 180 ? slug[..180].TrimEnd('-') : slug;
    }

    public static bool IsValid(string slug) => slug.Length <= 180 && SlugPattern().IsMatch(slug);
}
