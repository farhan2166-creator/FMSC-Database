using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

/// <summary>One authenticated check per opening/month. SQL owns eligibility, locks and posting.</summary>
internal static class AutomaticMonthlyBilling
{
    private static bool running;
    private static DateTime? checkedMonth;
    private static int checkedActor;
    internal static event Action<string>? StatusChanged;

    internal static Task<DataSet> ReviewAsync() => ExecuteAsync(true, false, false, null);

    private static Task<DataSet> ExecuteAsync(bool preview, bool includeMissed, bool reviewMissed, string? snapshot)
    {
        int actor = CurrentUser.UserId;
        return DatabaseHelper.QueryUserSetAsync("RunAutomaticMonthlyBilling",
            DatabaseHelper.Bit("@Preview", preview), DatabaseHelper.Bit("@IncludeMissed", includeMissed),
            DatabaseHelper.Bit("@ReviewMissed", reviewMissed), DatabaseHelper.P("@ExpectedSnapshot", snapshot),
            DatabaseHelper.Int("@UserId", actor));
    }

    internal static async Task CheckAsync(IWin32Window owner, bool force = false)
    {
        DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (running || (!force && checkedMonth == month && checkedActor == CurrentUser.UserId) || CurrentUser.UserId <= 0 ||
            !CurrentUser.HasPermission("Fees.Collect")) return;
        running = true;
        int actor = CurrentUser.UserId;
        try
        {
            using DataSet review = await ReviewAsync();
            if (CurrentUser.UserId != actor || owner is Control { IsDisposed: true }) return;
            DataRow summary = review.Tables[0].Rows[0];
            bool missed = Convert.ToInt32(summary["MissedCharges"]) > 0;
            bool administrator = CurrentUser.HasPermission("Students.Administer");
            bool includeMissed = false;
            bool reviewed = false;
            int actionable = review.Tables[2].AsEnumerable().Count(row => !Convert.ToBoolean(row["IsLegacyReference"]));
            int references = review.Tables[2].Rows.Count - actionable;
            if (missed && administrator)
            {
                using var choice = new BillingRecoveryDialog(review.Tables[1]);
                choice.ShowDialog(owner);
                includeMissed = choice.IncludeMissed;
                reviewed = choice.Reviewed;
            }
            if (CurrentUser.UserId != actor || owner is Control { IsDisposed: true }) return;
            if (Convert.ToInt32(summary["CurrentCharges"]) > 0 ||
                Convert.ToInt32(summary["PendingCreditApplications"]) > 0 || includeMissed || reviewed)
            {
                using DataSet posted = await ExecuteAsync(false, includeMissed, reviewed,
                    Convert.ToString(summary["Snapshot"]));
                int created = Convert.ToInt32(posted.Tables[0].Rows[0]["ChargesCreated"]);
                StatusChanged?.Invoke($"Monthly billing: {created:N0} charges created. " +
                    $"{actionable:N0} students need review; {references:N0} legacy references excluded.");
            }
            else StatusChanged?.Invoke($"Monthly billing is up to date. {actionable:N0} students need review; {references:N0} legacy references excluded.");
            checkedMonth = month;
            checkedActor = CurrentUser.UserId;
        }
        catch (Exception ex)
        {
            // No success marker on failure. Reopening the workspace or reconnecting permits a fresh review.
            StatusChanged?.Invoke("Monthly billing needs attention. Open Billing Status to review and retry.");
            ModernMessageDialog.ShowError(owner, DbMessage.UserMessage(ex), "Monthly Billing");
        }
        finally { running = false; }
    }

    internal static void Reset() => checkedMonth = null;
}

internal sealed class BillingRecoveryDialog : ApplicationWindowForm
{
    internal bool IncludeMissed { get; private set; }
    internal bool Reviewed { get; private set; }

    internal BillingRecoveryDialog(DataTable months)
    {
        Text = "Missed Monthly Fees";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(750, 470);
        MinimumSize = new Size(620, 370);
        var text = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(680, 0),
            Padding = new Padding(12), Text = "Some earlier months have not been billed. Review the months and amounts below.\n" +
            "Bill missed months too: creates the listed earlier charges as well as this month's fees.\n" +
            "Bill this month only: leaves earlier gaps unbilled and available for later administrative recovery.\n" +
            "Previously reversed fees and periods before approved billing commencement are excluded." };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoGenerateColumns = false };
        foreach (string column in new[] { "BillingMonth", "Students", "Charges", "Amount" })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column, DataPropertyName = column,
                HeaderText = column == "BillingMonth" ? "Billing month" : column });
        grid.DataSource = months.Copy();
        grid.Columns["BillingMonth"].DefaultCellStyle.Format = "MMMM yyyy";
        grid.Columns["Amount"].DefaultCellStyle.Format = "N0";
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), WrapContents = true };
        Button Choice(string caption, bool all)
        {
            var button = new Button { Text = caption, AutoSize = true, Padding = new Padding(8) };
            button.Click += (_, _) => { IncludeMissed = all; Reviewed = true; DialogResult = DialogResult.OK; };
            buttons.Controls.Add(button);
            return button;
        }
        Choice("Bill the missed months too", true);
        Choice("Bill this month only", false);
        var later = new Button { Text = "Decide later", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(later);
        CancelButton = later;
        Controls.Add(grid); Controls.Add(text); Controls.Add(buttons);
    }
}
