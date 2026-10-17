namespace FMSC_SMS.Core;
/// <summary>Neutralises spreadsheet formula injection in exported CSV.</summary>
public static class CsvSafety
{
    /// <summary>Leading characters a spreadsheet treats as the start of a formula.</summary>
    private static readonly char[] FormulaTriggers = { '=', '+', '-', '@', '\t', '\r', '\n' };
    /// <summary>Returns the value with a leading apostrophe when it would otherwise be read as a formula. Null and empty values are returned unchanged.</summary>
    public static string Neutralise(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;
        return WouldBeFormula(value)
            ? "'" + value
            : value;
    }
    /// <summary>True when the value would be treated as a formula if exported as-is. Exposed so the behaviour can be asserted without exporting a file.</summary>
    public static bool WouldBeFormula(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        char first = value[0];
        if (Array.IndexOf(FormulaTriggers, first) < 0)
            return false;
        // A signed decimal such as -250 or -12.50 is a number in the spreadsheet, not a formula. Prefixing it would turn fee discounts and credit balances into text.
        if (first == '-' && IsPlainNumber(value.AsSpan(1)))
            return false;
        return true;
    }
    /// <summary>True when <paramref name="text"/> is only digits with at most one decimal separator ('.' or ','). Culture-agnostic on purpose: CSV conversion uses the current culture, so either separator can appear.</summary>
    private static bool IsPlainNumber(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return false;
        bool seenDigit = false;
        bool seenDecimal = false;
        foreach (char ch in text)
        {
            if (char.IsAsciiDigit(ch))
            {
                seenDigit = true;
                continue;
            }
            if ((ch == '.' || ch == ',') && !seenDecimal)
            {
                seenDecimal = true;
                continue;
            }
            return false;
        }
        return seenDigit;
    }
}
