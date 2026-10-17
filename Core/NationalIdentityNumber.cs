using System.Text.RegularExpressions;
namespace FMSC_SMS.Core;
internal static class NationalIdentityNumber
{
    internal static bool TryNormalizeOptional(string? input, out string normalized)
    {
        string value = (input ?? string.Empty).Trim();
        normalized = string.Empty;
        if (value.Length == 0) return true;
        if (!Regex.IsMatch(value, @"^(?:[0-9]{13}|[0-9]{5}-[0-9]{7}-[0-9])$")) return false;
        string digits = value.Replace("-", "");
        normalized = $"{digits[..5]}-{digits[5..12]}-{digits[12]}";
        return true;
    }
    internal static string NormalizeOptional(string? input) => TryNormalizeOptional(input, out string normalized)
        ? normalized : throw new ArgumentException("Enter the 13-digit B-Form / CNIC identity number.");
}
