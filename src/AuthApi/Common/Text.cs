using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AuthApi.Common;

internal static partial class Text
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Trims and collapses inner whitespace; null when blank.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Whitespace().Replace(value.Trim(), " ");
    }

    /// <summary>Lower-case, accent-free, single-spaced form used to compare Vietnamese names.</summary>
    public static string Fold(string value)
    {
        var lower = value.Trim().ToLowerInvariant().Replace('đ', 'd');
        var builder = new StringBuilder();
        foreach (var c in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }

        return Whitespace().Replace(builder.ToString().Replace('.', ' '), " ").Trim();
    }
}
