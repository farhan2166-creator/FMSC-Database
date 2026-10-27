namespace FMSC_SMS.Core;

/// <summary>Shared parsing rules for values typed or pasted into UI inputs.</summary>
public static class InputParse
{
    public static bool TryWholeAmount(string? text, out decimal value)
    {
        value=0m;
        string candidate=(text??"").Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(candidate,@"\A-?(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)\z") &&
            decimal.TryParse(candidate,System.Globalization.NumberStyles.AllowLeadingSign|System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture,out value);
    }
    public static bool TryWholeValue(object? raw,out decimal value)
    {
        if(raw is decimal amount){value=amount;return WholeRupees.IsWhole(amount);}
        return TryWholeAmount(Convert.ToString(raw,System.Globalization.CultureInfo.InvariantCulture),out value);
    }

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
