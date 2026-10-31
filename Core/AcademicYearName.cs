using System.Globalization;

namespace FMSC_SMS.Core;

internal static class AcademicYearName
{
    internal const string ValidationMessage = "Enter consecutive four-digit years, for example 2025-2026.";

    internal static bool TryParse(string? value, out int startYear)
    {
        startYear = 0;
        string text = (value ?? string.Empty).Trim();
        if (text.Length != 9 || text[4] != '-' ||
            text.Where((_, index) => index != 4).Any(c => c is < '0' or > '9')) return false;
        if (!int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int first) ||
            !int.TryParse(text.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int last) ||
            first < 1000 || last != first + 1) return false;
        startYear = first;
        return true;
    }
}
