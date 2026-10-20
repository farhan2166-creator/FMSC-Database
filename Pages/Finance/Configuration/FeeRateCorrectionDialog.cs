using FMSC_SMS.Core;
using System.Text.Json;

namespace FMSC_SMS.Pages.Finance.Configuration;

internal sealed class FeeRateCorrectionDialog : ApplicationWindowForm
{
    private readonly int campusId, classId, feeTypeId;
    private readonly string scopeLabel;
    private readonly ModernDatePicker from = HumanUi.Date(), through = HumanUi.Date();
    private readonly ModernComboBox disposition = HumanUi.Combo();
    private readonly ModernTextBox reason = HumanUi.TextBox(false, 500);
    private readonly DataGridView grid = HumanUi.Grid(false);
    private readonly Label impact = new() { AutoSize = true, Padding = new Padding(8) };
    private readonly ModernButton apply = new() { Text = "Apply Selected Corrections", ThemeRole = "Warning", Enabled = false };
    private DataTable? reviewed;
    private bool busy;
    private int reviewGeneration;

    internal FeeRateCorrectionDialog(int campus, int classNumber, int feeType, string scope)
    {
        campusId = campus; classId = classNumber; feeTypeId = feeType;
        scopeLabel = scope;
        Text = "Apply Fee Matrix Rates to Existing Bills"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(1050), AppScale.Scale(680)); MinimumSize = new Size(760, 500);
        from.Value = through.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        from.Format = through.Format = DateTimePickerFormat.Custom; from.CustomFormat = through.CustomFormat = "MMMM yyyy";
        disposition.Items.AddRange(new object[] { "Keep excess payment as advance", "Refund the excess payment" }); disposition.SelectedIndex = 0;
        FinanceHelp.Attach(this,from,FinanceHelp.Topic.Period);FinanceHelp.Attach(this,through,FinanceHelp.Topic.Period);
        FinanceHelp.Attach(this,disposition,FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this,apply,FinanceHelp.Topic.ExistingBills);
        void InvalidateReview() { reviewGeneration++; reviewed = null; apply.Enabled = false; }
        from.ValueChanged += (_, _) => InvalidateReview(); through.ValueChanged += (_, _) => InvalidateReview();
        var review = new ModernButton { Text = "Preview Bills", ThemeRole = "Secondary" };
        review.Click += async (_, _) => await ReviewAsync(); apply.Click += async (_, _) => await ApplyAsync();
        var close = new ModernButton { Text = "Close", DialogResult = DialogResult.Cancel }; CancelButton = close;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.Controls.Add(HumanUi.Field("First bill month", from), 0, 0); fields.Controls.Add(HumanUi.Field("Last bill month", through), 1, 0);
        fields.Controls.Add(HumanUi.Field("If the reduced fee was already paid", disposition), 0, 1); fields.Controls.Add(HumanUi.Field("Reason", reason), 1, 1);
        root.Controls.Add(fields, 0, 0);
        impact.Text = $"Scope: {scopeLabel}\nPreview monthly bills for active enrollments. Admission and other annual fees are excluded. Future assignment rates remain under Apply Saved Rates.";
        impact.MaximumSize = new Size(AppScale.Scale(950), 0); root.Controls.Add(impact, 0, 1);
        grid.Dock = DockStyle.Fill; grid.MultiSelect = true; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UiHelper.SetGridEmptyMessage(grid, "Choose the bill months, then click Preview Bills.");
        root.Controls.Add(grid, 0, 2);
        var buttons = HumanUi.ButtonRow(review, apply, close); buttons.Dock = DockStyle.Fill;
        root.Controls.Add(buttons, 0, 3); Controls.Add(root);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }

    private async Task ReviewAsync()
    {
        if (busy) return;
        int generation = ++reviewGeneration;
        try
        {
            DataTable result = await DatabaseHelper.QueryUserAsync("PreviewFeeRateCorrections",
                DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@ClassId", classId), DatabaseHelper.Int("@FeeTypeId", feeTypeId),
                DatabaseHelper.Date("@FromMonth", new DateTime(from.Value.Year, from.Value.Month, 1)),
                DatabaseHelper.Date("@ThroughMonth", new DateTime(through.Value.Year, through.Value.Month, 1)));
            if (IsDisposed || generation != reviewGeneration) { result.Dispose(); return; }
            reviewed = result;
            grid.DataSource = reviewed; grid.ClearSelection();
            UiHelper.SetGridEmptyMessage(grid, "No eligible monthly bills for this scope and period. Check the campus, class and fee type on the previous screen.");
            impact.Text = $"Scope: {scopeLabel}\n{reviewed.Rows.Count} monthly bill(s) found. Admission and other annual fees are excluded.\n" +
                "Select the bills to change. A plus difference increases the fee. A minus difference reduces unpaid fees first; any excess already paid becomes advance money or a refund.\n" +
                "Original payments remain recorded. Use Apply Saved Rates separately to change students' saved fee plans.";
            apply.Enabled = reviewed.Rows.Count > 0;
        }
        catch (Exception ex) { ModernMessageDialog.ShowError(this, DbMessage.UserMessage(ex)); }
    }

    private async Task ApplyAsync()
    {
        if (busy || reviewed is null) return;
        var selected = grid.SelectedRows.Cast<DataGridViewRow>().Where(row => row.DataBoundItem is DataRowView)
            .Select(row => ((DataRowView)row.DataBoundItem).Row).ToArray();
        if (selected.Length == 0 || string.IsNullOrWhiteSpace(reason.Text))
        { ModernMessageDialog.ShowInfo(this, "Select the bills and enter a reason."); return; }
        decimal difference = selected.Sum(row => Convert.ToDecimal(row["Difference"]));
        if (!ModernMessageDialog.Confirm(this, $"Change {selected.Length} selected bills?\nTotal fees {(difference<0?"decrease by":difference>0?"increase by":"change by")} Rs. {Math.Abs(difference):N0}\n{disposition.Text}\nReason: {reason.Text.Trim()}", "Confirm Bill Changes", defaultYes: false)) return;
        var changes = selected.Select(row => new {
            BillingChargeId = Convert.ToInt64(row["BillingChargeId"]), NewAmount = Convert.ToDecimal(row["NewAmount"]),
            ExpectedNet = Convert.ToDecimal(row["ExpectedNet"]), ExpectedBalance = Convert.ToDecimal(row["ExpectedBalance"]), RequestId = Guid.NewGuid()
        }).ToArray();
        busy = true; apply.Enabled = false;
        Collection.CollectionRecoveryJournal? journal = null;
        try
        {
            journal = Collection.CollectionRecoveryJournal.Begin($"Selected bill rate corrections: {JsonSerializer.Serialize(changes)}; {reason.Text.Trim()}");
            await DatabaseHelper.QueryUserAsync("ApplyFeeRateCorrections", DatabaseHelper.P("@Changes", JsonSerializer.Serialize(changes)),
                DatabaseHelper.P("@PaidReductionAction", disposition.SelectedIndex == 0 ? "Credit" : "Refund"), DatabaseHelper.P("@Reason", reason.Text.Trim()));
            journal.Complete(); reviewed = null;
            ModernMessageDialog.ShowInfo(this, "Selected bill corrections saved. Review the updated bills and credit/refund history.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number >= 50000)
        { journal?.Complete(); ModernMessageDialog.ShowError(this, DbMessage.UserMessage(ex)); }
        catch (Exception ex)
        { ModernMessageDialog.ShowError(this, "Verify the financial result before trying again. " + DbMessage.UserMessage(ex)); }
        finally { journal?.Dispose(); busy = false; }
    }
}
