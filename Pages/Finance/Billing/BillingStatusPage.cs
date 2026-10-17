using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Billing;

public sealed class BillingStatusPage : V2Page
{
    private readonly Label summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
    private readonly ModernComboBox view = HumanUi.Combo();
    private DataSet? review;

    public BillingStatusPage() : base("Billing Status", "Review monthly fees and students who need setup. Legacy records are retained without automatic back-billing.")
    {
        view.Items.AddRange(new object[] { "Months awaiting billing", "Students needing attention", "Legacy references (excluded)" });
        view.SelectedIndex = 0;
        view.SelectedIndexChanged += (_, _) => Display();
        AddAction("Refresh", async (_, _) => await RefreshAsync(), "Secondary");
        AddAction("Run Billing / Review Missed Months", async (_, _) => {
            await AutomaticMonthlyBilling.CheckAsync(FindForm()!, force: true);
            await RefreshAsync();
        }, "Primary");
        var root = new Panel { Dock = DockStyle.Fill };
        var filter = HumanUi.Field("Show", view);
        filter.Dock = DockStyle.Top;
        root.Controls.Add(Grid); root.Controls.Add(filter); root.Controls.Add(summary);
        SetContent(root);
        Disposed += (_, _) => review?.Dispose();
        Defer(RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        try
        {
            DataSet current = await AutomaticMonthlyBilling.ReviewAsync();
            if (IsDisposed) { current.Dispose(); return; }
            review?.Dispose(); review = current;
            DataRow row = current.Tables[0].Rows[0];
            summary.Text = $"Current month: {Convert.ToDateTime(row["BillingMonth"]):MMMM yyyy} · " +
                $"{row["CurrentCharges"]} charges pending · Rs. {Convert.ToDecimal(row["CurrentAmount"]):N0}\n" +
                $"{current.Tables[2].AsEnumerable().Count(item => !Convert.ToBoolean(item["IsLegacyReference"])):N0} students need attention. " +
                "Legacy references are excluded. Earlier gaps remain available for reviewed recovery.";
            Display();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Display()
    {
        if (review is null) return;
        Grid.DataSource = view.SelectedIndex == 0 ? review.Tables[1] :
            new DataView(review.Tables[2]) { RowFilter = view.SelectedIndex == 2 ? "IsLegacyReference = true" : "IsLegacyReference = false" };
        if (Grid.Columns.Contains("BillingMonth")) Grid.Columns["BillingMonth"].DefaultCellStyle.Format = "MMMM yyyy";
        if (Grid.Columns.Contains("Amount")) Grid.Columns["Amount"].DefaultCellStyle.Format = "N0";
    }
}
