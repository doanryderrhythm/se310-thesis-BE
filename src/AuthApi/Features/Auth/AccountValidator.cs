using System.Text;
using System.Text.RegularExpressions;

namespace AuthApi.Features.Auth;

internal static class AccountValidator
{
    public static Dictionary<string, string[]> ValidateRegistration(global::RegisterRequest request)
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

    private static bool IsValidPhone(string? phone) =>
        phone is not null && Regex.IsMatch(phone, @"\A[0-9]{10}\z", RegexOptions.CultureInvariant);

    private static bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= 8
        && Encoding.UTF8.GetByteCount(password) <= 72;
}
