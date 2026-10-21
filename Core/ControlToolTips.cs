using System.Runtime.CompilerServices;

namespace FMSC_SMS.Core;

/// <summary>Keep explanatory help, shortcuts and validation on one tooltip per control.</summary>
internal static class ControlToolTips
{
    private sealed class Hint
    {
        internal ToolTip? DescriptionOwner, ShortcutOwner, ErrorOwner, ActiveOwner;
        internal string Description = "", Error = "";
        internal readonly Dictionary<object, string[]> Shortcuts = new();
    }
    private static readonly ConditionalWeakTable<Control, Hint> Hints = new();

    internal static void SetDescription(ToolTip owner, Control target, string? text)
    {
        if (target.IsDisposed || target.Disposing) return;
        var hint = Hints.GetOrCreateValue(target);
        hint.DescriptionOwner = owner;
        hint.Description = text ?? "";
        Apply(target, hint);
    }

    internal static void SetShortcuts(ToolTip owner, Control target, object source, IEnumerable<string> shortcuts)
    {
        if (target.IsDisposed || target.Disposing) return;
        var hint = Hints.GetOrCreateValue(target);
        var keys = shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length == 0) hint.Shortcuts.Remove(source);
        else hint.Shortcuts[source] = keys;
        hint.ShortcutOwner = owner;
        Apply(target, hint);
    }

    internal static void SetValidation(ToolTip owner, Control target, string? message)
    {
        if (target.IsDisposed || target.Disposing) return;
        var hint = Hints.GetOrCreateValue(target);
        if (!string.IsNullOrEmpty(message)) { hint.ErrorOwner = owner; hint.Error = message; }
        else if (ReferenceEquals(hint.ErrorOwner, owner)) { hint.ErrorOwner = null; hint.Error = ""; }
        Apply(target, hint);
        // Wrapped inputs receive mouse events on their native child controls.
        foreach (Control child in target.Controls) SetValidation(owner, child, message);
    }

    private static void Apply(Control target, Hint hint)
    {
        ToolTip? owner;
        string text;
        if (hint.ErrorOwner is not null)
        {
            owner = hint.ErrorOwner;
            text = hint.Error;
        }
        else
        {
            owner = hint.DescriptionOwner ?? hint.ShortcutOwner;
            var keys = hint.Shortcuts.Values.SelectMany(value => value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            text = hint.Description;
            if (string.IsNullOrWhiteSpace(text) && keys.Length > 0 && target is Button) text = target.Text;
            var missing = keys.Where(key => !text.Contains(key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0)
                text = string.IsNullOrWhiteSpace(text) ? string.Join(" / ", missing) : $"{text} ({string.Join(" / ", missing)})";
        }
        if (hint.ActiveOwner is not null && !ReferenceEquals(hint.ActiveOwner, owner))
            hint.ActiveOwner.SetToolTip(target, null);
        hint.ActiveOwner = owner;
        if (owner is not null && owner.GetToolTip(target) != text) owner.SetToolTip(target, text);
    }
}
