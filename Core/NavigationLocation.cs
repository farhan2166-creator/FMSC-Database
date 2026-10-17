namespace FMSC_SMS.Core;

internal static class NavigationLocation
{
    internal static string Format(string module, string title, string? section = null)
    {
        var parts = new List<string>();
        foreach (string? part in new[] { module, title, section })
        {
            string value = part?.Trim() ?? string.Empty;
            if (value.Length > 0 && (parts.Count == 0 || !value.Equals(parts[^1], StringComparison.OrdinalIgnoreCase)))
                parts.Add(value);
        }
        return string.Join(" › ", parts);
    }
}
