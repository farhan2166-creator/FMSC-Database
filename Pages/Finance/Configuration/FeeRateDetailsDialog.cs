using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Configuration;

/// <summary>Edits one matrix cell; the matrix's Save Grid owns the database write.</summary>
internal sealed class FeeRateDetailsDialog : ApplicationWindowForm
{
    private readonly ModernNumericTextBox amount = new() { Minimum = 0, Maximum = 1000000000, WholeRupeesOnly = true, DecimalPlaces = 2, ThousandsSeparator = true, Dock = DockStyle.Fill };
    private readonly ModernComboBox frequency = HumanUi.Combo();
    private readonly ModernTextBox remarks = HumanUi.TextBox(false, 500);
    internal decimal Amount => amount.Value;
    internal string Frequency => frequency.Text;
    internal string Remarks => remarks.Text.Trim();

    internal FeeRateDetailsDialog(string className, string feeTypeName, decimal value, string frequencyValue, string remarksValue)
    {
        Text = $"{className} — {feeTypeName}";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = AppTheme.PageBackground;
        ClientSize = new Size(AppScale.Scale(510), AppScale.Scale(285));
        frequency.Items.AddRange(new object[] { "Monthly", "Yearly", "As Needed" });
        frequency.SelectedItem = frequencyValue;
        if (frequency.SelectedIndex < 0) frequency.SelectedIndex = 0;
        amount.Value = value;
        remarks.Text = remarksValue;
        var apply = new ModernButton { Text = "Use in Grid", Width = AppScale.Scale(140), Height = AppMetrics.ButtonHeight, ThemeRole = "Success" };
        var cancel = new ModernButton { Text = UiText.Actions.Cancel, Width = AppScale.Scale(100), Height = AppMetrics.ButtonHeight, ThemeRole = "Secondary", DialogResult = DialogResult.Cancel };
        apply.Click += (_, _) => {
            if(!WholeRupees.IsWhole(amount.Value)){
                ModernMessageDialog.Show(this,"Enter a whole-rupee fee. Fractions are not rounded.","Fee Amount",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                amount.Focus();return;
            }
            DialogResult = DialogResult.OK; Close();
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(HumanUi.Field("Amount", amount, true), 0, 0);
        layout.Controls.Add(HumanUi.Field("Frequency", frequency, true), 0, 1);
        layout.Controls.Add(HumanUi.Field("Remarks", remarks), 0, 2);
        layout.Controls.Add(HumanUi.ButtonRow(apply, cancel), 0, 3);
        Controls.Add(layout);
        AcceptButton = apply;
        SetSaveShortcut(apply);
        CancelButton = cancel;
        Shown += (_, _) => amount.Focus();
    }
}
