using System.Text.RegularExpressions;

namespace AuthApi.Common;

/// <summary>
/// Vietnamese mobile numbers. Accepts "0912 345 678", "0912.345.678", "+84 912 345 678" or
/// "84912345678" and stores the canonical 10-digit form "0912345678".
/// </summary>
internal static partial class PhoneNumbers
{
    public const string RuleMessage =
        "Phone must be a Vietnamese mobile number: 10 digits starting with 03, 05, 07, 08 or 09.";

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\A0[35789][0-9]{8}\z")]
    private static partial Regex MobilePattern();

    [GeneratedRegex(@"\A[0-9]{10}\z")]
    private static partial Regex TenDigits();

    /// <summary>Strips separators and the +84/84 country code. Returns null for blank input.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = Separators().Replace(input.Trim(), "");
        if (digits.StartsWith("+84")) digits = "0" + digits[3..];
        else if (digits.StartsWith("84") && digits.Length == 11) digits = "0" + digits[2..];
        return digits;
    }

    /// <summary>Strict rule for new data (registration, recipient phones).</summary>
    public static bool IsValidMobile(string? normalized) => normalized is not null && MobilePattern().IsMatch(normalized);

    /// <summary>Lenient rule for logging in, so accounts created before the stricter rule still work.</summary>
    public static bool IsTenDigits(string? normalized) => normalized is not null && TenDigits().IsMatch(normalized);
}
