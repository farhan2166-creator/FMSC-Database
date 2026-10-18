using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckNumericPlaceholders()
    {
        using var form = new Form();
        using var number = new ModernNumericTextBox { DecimalPlaces = 2, Dock = DockStyle.Top };
        using var other = new ModernTextBox { Dock = DockStyle.Bottom };
        form.Controls.Add(number); form.Controls.Add(other);
        form.Show(); other.Focus(); Application.DoEvents();
        Check(number.Text == "" && number.Value == 0 && number.PlaceholderText == "0.00", "Zero is a cue rather than editable text");
        number.Focus(); Application.DoEvents();
        Check(number.PlaceholderText == "", "The zero cue disappears on focus");
        number.Value = 125; number.Text = "";
        Check(number.Value == 0, "Clearing a previous amount resets it instead of restoring the old amount");
        number.Value = 450;
        Check(number.Text == "450.00" && number.Value == 450, "Nonzero loaded amounts remain visible and unchanged");
        number.WholeRupeesOnly = true; number.Text = "12.5";
        Check(number.Value == 12.5m, "Fractional drafts remain available for whole-rupee validation");
        other.ZeroAsPlaceholder = true; other.Text = "0.00";
        other.Focus(); Application.DoEvents();
        var input = other.Controls.OfType<TextBox>().Single();
        Check(input.Text == "" && other.Text == "0" && other.PlaceholderText == "", "Legacy numeric parsers retain zero while the focused editor is empty");
        number.Focus(); Application.DoEvents();
        Check(other.PlaceholderText == "0", "Legacy numeric cue returns on blur");
        other.Text = "500";
        Check(input.Text == "500", "Legacy nonzero input stays visible");
        using var text = new ModernTextBox { Text = "0" };
        Check(text.Text == "0", "Ordinary text fields are unaffected");
    }
}
