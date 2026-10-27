using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;
/// <summary>Read-only recurring fee estimates through the selected enrollment's academic year end.</summary>
public sealed class FutureFeesDialog : ApplicationWindowForm
{
    private readonly int enrollmentId;
    private readonly string studentDisplay;
    private readonly DataGridView grid = HumanUi.Grid(false);
    private readonly Label subtitle = new() {
        Dock = DockStyle.Top,
        Height = Math.Max(24, AppMetrics.TextHeight(AppTheme.NormalFont) + 8),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText
    };
    private readonly ModernButton refreshButton = new() {
        Text = UiText.Actions.Refresh,
        Width = AppScale.Scale(100),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary"
    };
    public FutureFeesDialog(int enrollmentId, string studentDisplay)
    {
        FinanceHelp.Attach(this,grid,"Preview recurring fees through the academic year’s end. This screen creates no bills or payments.","Rs. 400 tuition for two remaining months is an estimate of Rs. 800. The preview does not add Rs. 800 to outstanding.");
        this.enrollmentId = enrollmentId;
        this.studentDisplay = studentDisplay;
        Text = "Future Fees";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(900), AppScale.Scale(560));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        subtitle.Text = $"Student: {studentDisplay}";
        BuildLayout();
        refreshButton.Click += (_, _) => LoadGrid();
        Shown += (_, _) => LoadGrid();
    }
    private void BuildLayout()
    {
        var title = new Label {
            Text = "Future Fees",
            Dock = DockStyle.Top,
            Height = Math.Max(30, AppMetrics.TextHeight(AppTheme.PageTitleFont) + 8),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text
        };
        var note = new Label {
            Text = "Estimated charges through the academic year's end - not yet billed",
            Dock = DockStyle.Top,
            Height = AppMetrics.TextHeight(AppTheme.SmallFont) + AppMetrics.SpacingSm,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.MutedText
        };
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.Columns.Add("BillingMonth", "Billing Month");
        grid.Columns.Add("FeeTypeName", UiText.Fees.FeeType);
        grid.Columns.Add("Frequency", UiText.Fees.Frequency);
        grid.Columns.Add("ExpectedAmount", UiText.Fees.Amount);
        grid.Columns.Add("ExpectedDiscountAmount", UiText.Fees.Discount);
        grid.Columns.Add("ExpectedNetAmount", UiText.Fees.NetAmount);
        grid.Columns.Add("FeeTypeId", "FeeTypeId");
        grid.Columns["FeeTypeId"].Visible = false;
        grid.Columns.Add("StudentFeeAssignmentId", "Id");
        grid.Columns["StudentFeeAssignmentId"].Visible = false;
        foreach (var name in new[] { "ExpectedAmount", "ExpectedDiscountAmount", "ExpectedNetAmount" })
        {
            grid.Columns[name].DefaultCellStyle = new DataGridViewCellStyle {
                Format = "0.##",
                Alignment = DataGridViewContentAlignment.MiddleRight
            };
        }
        grid.Columns["BillingMonth"].FillWeight = 140;
        grid.Columns["FeeTypeName"].FillWeight = 140;
        grid.Columns["ExpectedDiscountAmount"].FillWeight = 120;
        HumanUi.ApplyGridStyle(grid, AppTheme.ModuleAccent("Finance"));
        var closeButton = new ModernButton {
            Text = UiText.Actions.Close,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            DialogResult = DialogResult.OK
        };
        var buttons = HumanUi.ButtonRow(refreshButton, closeButton);
        buttons.Dock = DockStyle.Bottom;
        buttons.Margin = new Padding(0, 10, 0, 0);
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        title.Font = AppTheme.ButtonFont;
        title.Dock = DockStyle.Fill;
        subtitle.Dock = DockStyle.Fill;
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(AppMetrics.SpacingMd)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,
            2 * AppMetrics.TextHeight(AppTheme.NormalFont) + AppMetrics.SpacingMd));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(grid, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = closeButton;
    }
    private void LoadGrid()
    {
        try
        {
            var dt = DatabaseHelper.QueryUser(
                "GetStudentFeeProjection",
                DatabaseHelper.Int("@EnrollmentId", enrollmentId),
                // Request the full supported horizon; the projection caps it at academic year end.
                DatabaseHelper.Int("@MonthsAhead", 24));
            grid.Rows.Clear();
            foreach (DataRow row in dt.Rows)
            {
                grid.Rows.Add(
                    FormatMonth(row["ExpectedBillingMonth"]),
                    Convert.ToString(row["FeeTypeName"]),
                    Convert.ToString(row["Frequency"]),
                    row["ExpectedAmount"],
                    row["ExpectedDiscountAmount"],
                    row["ExpectedNetAmount"],
                    Convert.ToInt32(row["FeeTypeId"]),
                    Convert.ToInt32(row["StudentFeeAssignmentId"]));
            }
            subtitle.Text = $"Student: {studentDisplay}";
            if (dt.Rows.Count == 0)
            {
                SetNoRowsMessage();
            }
        }
        catch (Exception ex)
        {
            /* The list goes with the failure and the heading says why it is empty. */
            grid.Rows.Clear();
            subtitle.Text =
                $"Student: {studentDisplay}    -    the projection could not be read; "
                + "this list stays empty until Refresh succeeds.";
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Future Fees", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void SetNoRowsMessage()
    {
        // No exception, just nothing to project (e.g. no active recurring assignments, or all future months already billed) - the grid being empty already communicates this; nothing further needed.
    }
    private static string FormatMonth(object value)
    {
        if (value == null || value == DBNull.Value)
            return string.Empty;
        return DateTime.TryParse(Convert.ToString(value), out DateTime parsed)
            ? parsed.ToString("MMMM yyyy")
            : Convert.ToString(value) ?? string.Empty;
    }
}

