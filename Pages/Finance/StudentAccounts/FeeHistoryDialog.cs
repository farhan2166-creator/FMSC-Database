using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;
/// <summary>Modal wrapper around the existing, unmodified StudentAccountStatementPage, opened from FeeCollectionPage's new "History" button. update: the limitation above is now closed.</summary>
public sealed class FeeHistoryDialog : ApplicationWindowForm
{
    public FeeHistoryDialog(int preloadAdmissionId = 0)
    {
        Text = "Fee Statement History";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(1180), AppScale.Scale(760));
        MinimumSize = new Size(AppScale.Scale(900), AppScale.Scale(560));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        var page = new StudentAccountStatementPage(preloadAdmissionId) {
            Dock = DockStyle.Fill
        };
        var closeButton = new ModernButton {
            Text = UiText.Actions.Close,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            DialogResult = DialogResult.OK
        };
        var buttons = HumanUi.ButtonRow(closeButton);
        buttons.Dock = DockStyle.Bottom;
        buttons.Margin = new Padding(0, 10, 0, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(page, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        CancelButton = closeButton;
    }
}

