namespace FMSC_SMS.Core;

/// <summary>Shared appearance and order for record-editing and preview/apply actions.</summary>
internal static class ActionButtonStyle
{
    private static string Label(string text)
    {
        string label = text.Replace("&&", "&").Trim();
        int first = 0;
        while (first < label.Length && !char.IsLetter(label[first])) first++;
        return label[first..];
    }
    private static bool Starts(string label, string word) =>
        label.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
        (label.Length == word.Length || !char.IsLetterOrDigit(label[word.Length]));
    internal static string Role(string text, string fallback)
    {
        // Navigation, tabs and deliberately custom buttons have their own appearance.
        if (fallback is "Navigation" or "TabActive" or "TabInactive" or "Outline" or "Custom") return fallback;
        string label = Label(text);
        bool Any(params string[] words) => words.Any(word => Starts(label, word));
        if (Any("Delete", "Remove", "Reverse", "Restore", "Destroy")) return UiDesignSystem.Actions.Danger;
        if (label.StartsWith("Save &", StringComparison.OrdinalIgnoreCase)) return UiDesignSystem.Actions.Secondary;
        if (Any("Save", "Update", "Backup", "Back Up")) return UiDesignSystem.Actions.Success;
        if (Any("New", "Clear", "Reset", "Cancel", "Close", "Back", "Next Student"))
            return UiDesignSystem.Actions.Secondary;
        if (Any("Warning", "Retry")) return UiDesignSystem.Actions.Warning;
        if (Any("Apply", "Post", "Collect", "Confirm", "Process", "Execute", "Create", "Issue", "Mark Collected"))
            return UiDesignSystem.Actions.Primary;
        if (Any("Load", "Refresh", "Reload", "Search", "Find", "Print", "Export", "Preview",
            "View", "Review", "Browse", "Choose", "Locate", "History", "Select"))
            return UiDesignSystem.Actions.Secondary;
        // Preserve explicit warnings and unusual actions such as the one-time cleanup.
        return fallback;
    }
    private static int? Order(Control button)
    {
        string label = Label(button.Text);
        if (Starts(label, "New")) return 10;
        if (Starts(label, "Preview")) return 15;
        if (label.StartsWith("Save &", StringComparison.OrdinalIgnoreCase)) return 30;
        if (Starts(label, "Save") && !Starts(label, "Save View")) return 20;
        if (Starts(label, "Apply") || Starts(label, "Post") || Starts(label, "Collect") || Starts(label, "Confirm")) return 20;
        if (Starts(label, "Clear") || Starts(label, "Reset")) return 40;
        if (Starts(label, "Delete") || Starts(label, "Remove") || Starts(label, "Reverse")) return 50;
        if (Starts(label, "Close") || Starts(label, "Cancel")) return 60;
        return null;
    }
    internal static Control[] Ordered(IEnumerable<Control> controls)
    {
        var result = controls.ToArray();
        // Keep unrelated tools and separators in place; reorder only action slots.
        int start = 0;
        while (start < result.Length)
        {
            if (result[start] is not Button) { start++; continue; }
            int end = start;
            while (end < result.Length && result[end] is Button) end++;
            var slots = Enumerable.Range(start, end - start).Where(i => Order(result[i]).HasValue).ToArray();
            var ordered = slots.Select(i => result[i]).OrderBy(Order).ToArray();
            for (int i = 0; i < slots.Length; i++) result[slots[i]] = ordered[i];
            start = end;
        }
        return result;
    }
    internal static void Arrange(FlowLayoutPanel row)
    {
        var ordered = Ordered(row.Controls.Cast<Control>());
        row.SuspendLayout();
        try
        {
            for (int i = 0; i < ordered.Length; i++)
            {
                row.Controls.SetChildIndex(ordered[i], i);
                if (ordered[i] is ModernButton button)
                {
                    button.TabIndex = i;
                    button.Margin = new Padding(i > 0 && Order(button) == 50 ? AppMetrics.SpacingMd : AppMetrics.SpacingXs,
                        0, AppMetrics.SpacingXs, 0);
                }
            }
        }
        finally { row.ResumeLayout(); }
    }
}
