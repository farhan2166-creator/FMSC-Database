namespace FMSC_SMS.Core;

/// <summary>Shared accessible naming for native and wrapped inputs.</summary>
public static class UiAccessibility
{
    public static void Describe(
        Control control,
        string? name,
        string? description = null)
    {
        if (control is null)
            return;
        if (!string.IsNullOrWhiteSpace(name))
            control.AccessibleName = name;
        if (!string.IsNullOrWhiteSpace(description))
            control.AccessibleDescription = description;
        if (control is IAccessibleInput input)
        {
            input.ApplyAccessibleInfo(
                string.IsNullOrWhiteSpace(control.AccessibleName)
                    ? null
                    : control.AccessibleName,
                string.IsNullOrWhiteSpace(control.AccessibleDescription)
                    ? null
                    : control.AccessibleDescription);
        }
    }

    public static string CleanCaption(string? caption)
    {
        string text = (caption ?? string.Empty).Trim();
        return text.EndsWith("*", StringComparison.Ordinal)
            ? text.Substring(0, text.Length - 1).TrimEnd()
            : text;
    }
}
