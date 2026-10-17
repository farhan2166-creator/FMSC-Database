namespace FMSC_SMS.Core;

/// <summary>Explicit display details; values are rendered as text, never interpreted as markup.</summary>
public sealed record MessageDialogContent(
    string Summary,
    string? Explanation = null,
    IReadOnlyList<(string Label, string Value)>? Facts = null,
    DataTable? Records = null,
    string? Notice = null)
{
    internal static MessageDialogContent FromText(string? message)
    {
        string text = (message ?? string.Empty).Replace("\r\n", "\n");
        int split = text.IndexOf('\n');
        return split < 0 ? new(text) : new(text[..split], text[(split + 1)..].TrimStart('\n'));
    }
}
