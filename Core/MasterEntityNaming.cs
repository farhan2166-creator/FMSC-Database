namespace FMSC_SMS.Core;

internal static class MasterEntityNaming
{
    internal static string Singularize(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "record";
        int lastSpace = title.LastIndexOf(' ');
        string prefix = lastSpace >= 0 ? title[..(lastSpace + 1)] : string.Empty;
        string word = lastSpace >= 0 ? title[(lastSpace + 1)..] : title;
        string lower = word.ToLowerInvariant();
        if (word.Length >= 3 && lower.EndsWith("ies", StringComparison.Ordinal))
            word = word[..^3] + "y";
        else if (lower.EndsWith("ses", StringComparison.Ordinal) ||
                 lower.EndsWith("xes", StringComparison.Ordinal) ||
                 lower.EndsWith("ches", StringComparison.Ordinal) ||
                 lower.EndsWith("shes", StringComparison.Ordinal))
            word = word[..^2];
        else if (lower.EndsWith("s", StringComparison.Ordinal) &&
                 !lower.EndsWith("ss", StringComparison.Ordinal))
            word = word[..^1];
        return prefix + word;
    }
}
