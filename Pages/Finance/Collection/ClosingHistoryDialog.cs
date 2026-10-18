using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Collection;

internal sealed class ClosingHistoryDialog : ApplicationWindowForm
{
    internal ClosingHistoryDialog(DataTable history, DateTime day)
    {
        Text = "Closing History";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(AppScale.Scale(1200), AppScale.Scale(650));
        MinimumSize = new Size(AppScale.Scale(850), AppScale.Scale(480));
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;

        var card = HumanUi.Card($"Closing History · {day:dd-MMM-yyyy}",
            "Review submitted revisions, approvals and amounts by payment method.");
        card.Dock = DockStyle.Fill;
        var grid = HumanUi.Grid(false);
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = false;
        grid.DataSource = history;
        HumanUi.ApplyGridStyle(grid, AppTheme.Primary);
        void FormatColumns()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Name.Contains("Amount") || column.Name == "Difference")
                    column.DefaultCellStyle.Format = "#,0.##";
                else if (column.ValueType == typeof(DateTime))
                    column.DefaultCellStyle.Format = "dd-MMM-yyyy h:mm tt";
            }
        }
        grid.DataBindingComplete += (_, _) => FormatColumns();
        FormatColumns();
        UiHelper.SetGridEmptyMessage(grid, "No closing revisions for this date.");
        card.Content.Controls.Add(grid);

        var close = new ModernButton { Text = "Close", ThemeRole = "Secondary", DialogResult = DialogResult.Cancel };
        var actions = HumanUi.ButtonRow(close);
        actions.Dock = DockStyle.Fill;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(AppMetrics.SpacingMd)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        CancelButton = close;
    }
}
