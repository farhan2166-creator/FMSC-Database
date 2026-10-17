namespace FMSC_SMS.Core;

/// <summary>Formats identity digits without making separators extra editing steps.</summary>
internal static class NationalIdentityInput
{
    internal static void Attach(ModernTextBox control)
    {
        var editor = control.Controls.OfType<TextBox>().Single();
        bool formatting = false, pending = false;
        control.PlaceholderText = "12345-1234567-1";

        void Apply(string digits, int caretDigits)
        {
            formatting = true;
            try
            {
                string formatted = Format(digits), previous = editor.Text;
                int prefix = 0, suffix = 0;
                while (prefix < previous.Length && prefix < formatted.Length && previous[prefix] == formatted[prefix]) prefix++;
                while (suffix < previous.Length - prefix && suffix < formatted.Length - prefix &&
                    previous[^(suffix + 1)] == formatted[^(suffix + 1)]) suffix++;
                // Replace just the changed region so ordinary typing/deleting keeps native Undo.
                editor.SelectionStart = prefix;
                editor.SelectionLength = previous.Length - prefix - suffix;
                editor.SelectedText = formatted.Substring(prefix, formatted.Length - prefix - suffix);
                editor.SelectionStart = Caret(editor.Text, caretDigits);
                editor.SelectionLength = 0;
            }
            finally { formatting = false; }
        }
        void FormatCurrent()
        {
            if (editor.IsDisposed || formatting || !TryDigits(editor.Text, out string digits)) return;
            string formatted = Format(digits);
            if (formatted == editor.Text) return;
            int start = CountDigits(editor.Text[..editor.SelectionStart]);
            int end = CountDigits(editor.Text[..(editor.SelectionStart + editor.SelectionLength)]);
            Apply(digits, start);
            editor.SelectionLength = Math.Max(0, Caret(editor.Text, end) - editor.SelectionStart);
        }
        editor.TextChanged += (_, _) => {
            if (formatting) return;
            // A native paste/cut updates the caret after TextChanged. Wait for that
            // message to finish so mid-field paste keeps its correct position.
            if (editor.Focused && editor.IsHandleCreated)
            {
                if (pending) return;
                pending = true;
                editor.BeginInvoke((Action)(() => { pending = false; FormatCurrent(); }));
            }
            else FormatCurrent();
        };
        editor.KeyPress += (_, e) => {
            if (char.IsControl(e.KeyChar) || editor.ReadOnly) return;
            if (e.KeyChar is < '0' or > '9') { e.Handled = true; return; }
            if (!TryDigits(editor.Text, out string digits)) return;
            int start = CountDigits(editor.Text[..editor.SelectionStart]);
            int end = CountDigits(editor.Text[..(editor.SelectionStart + editor.SelectionLength)]);
            string updated = digits[..start] + e.KeyChar + digits[end..];
            e.Handled = true;
            if (updated.Length <= 13) Apply(updated, start + 1);
        };
        editor.KeyDown += (_, e) => {
            if (e.Modifiers != Keys.None || editor.ReadOnly || e.KeyCode is not (Keys.Back or Keys.Delete) ||
                !TryDigits(editor.Text, out string digits)) return;
            int start = CountDigits(editor.Text[..editor.SelectionStart]);
            int end = CountDigits(editor.Text[..(editor.SelectionStart + editor.SelectionLength)]);
            if (start == end)
            {
                if (e.KeyCode == Keys.Back && start > 0) start--;
                else if (e.KeyCode == Keys.Delete && end < digits.Length) end++;
                else { e.SuppressKeyPress = true; return; }
            }
            Apply(digits[..start] + digits[end..], start);
            e.SuppressKeyPress = true;
        };
        FormatCurrent();
    }

    private static bool TryDigits(string text, out string digits)
    {
        digits = string.Concat(text.Where(character => character is >= '0' and <= '9'));
        // Preserve bad/overlong pasted values for validation; never silently truncate an identity.
        return digits.Length <= 13 && text.All(character => character is >= '0' and <= '9' or '-' || char.IsWhiteSpace(character));
    }
    private static int CountDigits(string text) => text.Count(character => character is >= '0' and <= '9');
    private static string Format(string digits) => digits.Length <= 5 ? digits :
        digits.Length <= 12 ? digits[..5] + "-" + digits[5..] : digits[..5] + "-" + digits[5..12] + "-" + digits[12..];
    private static int Caret(string formatted, int digits)
    {
        int position = 0, count = 0;
        while (position < formatted.Length && count < digits)
            if (formatted[position++] is >= '0' and <= '9') count++;
        if (position < formatted.Length && formatted[position] == '-') position++;
        return position;
    }
}
