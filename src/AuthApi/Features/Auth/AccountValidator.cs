using System.Text;
using System.Text.RegularExpressions;

namespace AuthApi.Features.Auth;

internal static class AccountValidator
{
    /// <summary>Returns the request with phone, name and email in their stored form.</summary>
    public static global::RegisterRequest Normalize(global::RegisterRequest request) => request with
    {
        Phone = AuthApi.Common.PhoneNumbers.Normalize(request.Phone) ?? request.Phone,
        FullName = AuthApi.Common.Text.Clean(request.FullName) ?? request.FullName
    };

    /// <summary>Call on a request that went through <see cref="Normalize"/>.</summary>
    public static Dictionary<string, string[]> ValidateRegistration(global::RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (AuthApi.Common.Text.Clean(request.FullName) is not { Length: >= 2 and <= 100 })
        {
            errors["fullName"] = ["Full name is required (2-100 characters)."];
        }

        if (!AuthApi.Common.PhoneNumbers.IsValidMobile(request.Phone))
        {
            errors["phone"] = [AuthApi.Common.PhoneNumbers.RuleMessage];
        }

        if (!IsValidPassword(request.Password))
        {
            errors["password"] = ["Password must be at least 8 characters and no more than 72 UTF-8 bytes."];
        }

        if (NormalizeEmail(request.Email, out var emailError) is null && emailError is not null)
        {
            errors["email"] = [emailError];
        }

        return errors;
    }

    /// <summary>
    /// Trims and lower-cases an optional email. Blank means "no email" (null, no error);
    /// an invalid value returns null with an error message.
    /// </summary>
    public static string? NormalizeEmail(string? email, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 254 || !EmailPattern.IsMatch(normalized))
        {
            error = "Email must be a valid email address of at most 254 characters.";
            return null;
        }

        return normalized;
    }

    private static readonly Regex EmailPattern = new(
        @"\A[^@\s]+@[^@\s]+\.[^@\s]+\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= 8
        && Encoding.UTF8.GetByteCount(password) <= 72;
}
