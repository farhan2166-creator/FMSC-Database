namespace FMSC_SMS.Core;

/// <summary>Shared parsing rules for values typed or pasted into UI inputs.</summary>
public static class InputParse
{
    public static bool TryAmount(string? text, out decimal value)
    {
        value = 0m;
        string candidate = (text ?? string.Empty).Trim();
        foreach (string prefix in new[] { "PKR", "Rs.", "Rs" })
        {
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            candidate = candidate.Substring(prefix.Length).Trim();
            break;
        }
        if (candidate.Length == 0)
            return false;
        const System.Globalization.NumberStyles styles =
            System.Globalization.NumberStyles.Number;
        return decimal.TryParse(
                   candidate,
                   styles,
                   System.Globalization.CultureInfo.CurrentCulture,
                   out value) ||
               decimal.TryParse(
                   candidate,
                   styles,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out value);
    }
}
