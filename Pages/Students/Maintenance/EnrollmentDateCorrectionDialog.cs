using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Maintenance;

/// <summary>Reviews and applies the existing active-enrollment reconciliation contract.</summary>
internal sealed class EnrollmentDateCorrectionDialog : ApplicationWindowForm
{
    private readonly ModernDatePicker start = HumanUi.Date();
    private readonly ModernDatePicker billing = HumanUi.Date();
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500);
    private readonly CheckBox legacy = new() { Text = "Acknowledge dates outside admission / academic year or overlapping placement history", AutoSize = true };
    private readonly ModernTextBox impact = HumanUi.TextBox(true, 0);
    private readonly ModernButton review = new() { Text = "Review Correction", ThemeRole = "Secondary" };
    private readonly ModernButton apply = new() { Text = "Apply Correction", ThemeRole = "Warning", Enabled = false };
    private readonly ModernButton cancel = new() { Text = UiText.Actions.Cancel, DialogResult = DialogResult.Cancel };
    private DataRow? plan;
    private bool busy;
    private DateTime reviewedStart;
    private DateTime? reviewedBilling;
    private string reviewedReason = "";
    private bool reviewedLegacy;
    internal string ResultMessage { get; private set; } = "";

    internal EnrollmentDateCorrectionDialog(string studentLabel, int enrollmentId, DataRow current, DateTime proposedStart, DateTime? proposedBilling, string initialReason, bool allowLegacy)
    {
        Text = "Correct Placement Dates";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(840), AppScale.Scale(600));
        MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(520));
        start.Value = proposedStart; billing.ShowCheckBox = true; billing.Checked = proposedBilling.HasValue;
        if (proposedBilling.HasValue) billing.Value = proposedBilling.Value;
        reason.Text = initialReason; legacy.Checked = allowLegacy;
        impact.ReadOnly = true; impact.Dock = DockStyle.Fill;
        var guidance = new Label { Dock = DockStyle.Fill, AutoSize = true,
            Text = $"{studentLabel}\nEnrollment {enrollmentId} — {current["YearName"]}\nLoaded placement start date: {Convert.ToDateTime(current["EnrollmentDate"]):dd MMM yyyy}; billing start: " +
                (current["BillingStartDate"] is DateTime currentBilling ? currentBilling.ToString("dd MMM yyyy") : "not set") +
                ".\nReview the date and billing changes together. Status, placement and payments stay unchanged." };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        fields.Controls.Add(HumanUi.Field("Placement Start Date", start), 0, 0);
        fields.Controls.Add(HumanUi.Field("Billing start", billing), 1, 0);
        fields.Controls.Add(HumanUi.Field("Reason", reason, true), 0, 1); fields.SetColumnSpan(fields.GetControlFromPosition(0, 1)!, 2);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight * 3));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(guidance, 0, 0); root.Controls.Add(fields, 0, 1); root.Controls.Add(legacy, 0, 2);
        root.Controls.Add(impact, 0, 3); root.Controls.Add(HumanUi.ButtonRow(review, apply, cancel), 0, 4);
        Controls.Add(root); CancelButton = cancel;
        void ResetReview()
        {
            plan = null; apply.Enabled = false;
            impact.Text = "Review the correction to see which charges will be added or cancelled. Nothing is saved until Apply Correction.";
        }
        void SetBusy(bool value)
        {
            busy = value; fields.Enabled = legacy.Enabled = review.Enabled = cancel.Enabled = !value;
            apply.Enabled = !value && plan is not null && Convert.ToInt32(plan["BlockedCount"]) == 0;
        }
        object BillingValue() => billing.Checked ? billing.Value.Date : DBNull.Value;
        start.ValueChanged += (_, _) => ResetReview(); billing.ValueChanged += (_, _) => ResetReview();
        reason.TextChanged += (_, _) => ResetReview(); legacy.CheckedChanged += (_, _) => ResetReview();
        review.Click += async (_, _) => {
            if (busy) return;
            ResetReview();
            if (string.IsNullOrWhiteSpace(reason.Text)) { impact.Text = "Enter a reason for this correction."; return; }
            SetBusy(true);
            try
            {
                var preview = await DatabaseHelper.QueryUserAsync("ReconcileEnrollmentDates",
                    DatabaseHelper.Int("@EnrollmentId", enrollmentId), DatabaseHelper.Date("@EnrollmentDate", start.Value.Date),
                    DatabaseHelper.P("@BillingStartDate", BillingValue()), DatabaseHelper.P("@Reason", reason.Text.Trim()),
                    DatabaseHelper.Bit("@AllowLegacyExceptions", legacy.Checked), DatabaseHelper.Bit("@Preview", true));
                if (IsDisposed) return;
                plan = preview.Rows[0];
                reviewedStart = start.Value.Date; reviewedBilling = billing.Checked ? billing.Value.Date : null;
                reviewedReason = reason.Text.Trim(); reviewedLegacy = legacy.Checked;
                bool blocked = Convert.ToInt32(plan["BlockedCount"]) > 0;
                impact.Text = (blocked ? "CORRECTION BLOCKED — Apply is disabled. Nothing has changed.\r\n\r\n" : "") +
                    $"Proposed Placement Start Date: {start.Value:dd MMM yyyy}\r\nProposed billing start: " +
                    (billing.Checked ? billing.Value.ToString("dd MMM yyyy") : "not set") +
                    $"\r\n\r\nWould cancel {plan["CancelCount"]} charge(s): Rs. {Convert.ToDecimal(plan["CancelledAmount"]):0.##}" +
                    $"\r\nWould add {plan["AddCount"]} charge(s): Rs. {Convert.ToDecimal(plan["AddedAmount"]):0.##}" +
                    $"\r\nReview covers billing through {Convert.ToDateTime(plan["ThroughDate"]):MMM yyyy}." +
                    (plan["ReturnDate"] is DateTime returned ? $"\r\nResulting confirmed return date: {returned:dd MMM yyyy}." : "") +
                    (blocked
                        ? $"\r\n\r\n{plan["BlockedCount"]} affected charge(s) have payment or other recorded history. These dates cannot be applied while that history exists. Review the affected charges and their payments in Student Fees / Fee Corrections before reviewing this correction again. Existing dates, charges and payments remain unchanged."
                        : "\r\n\r\nCancelled charges remain in history. Payments stay unchanged. Apply saves the reviewed dates and charge changes together.");
            }
            catch (Exception ex) { plan = null; impact.Text = DbMessage.UserMessage(ex); }
            finally { if (!IsDisposed) SetBusy(false); }
        };
        apply.Click += async (_, _) => {
            if (busy || plan is null || Convert.ToInt32(plan["BlockedCount"]) > 0) return;
            if (reviewedStart != start.Value.Date || reviewedBilling != (billing.Checked ? billing.Value.Date : (DateTime?)null)
                || reviewedReason != reason.Text.Trim() || reviewedLegacy != legacy.Checked)
            { ResetReview(); return; }
            SetBusy(true);
            try
            {
                var result = await DatabaseHelper.QueryUserAsync("ReconcileEnrollmentDates",
                    DatabaseHelper.Int("@EnrollmentId", enrollmentId), DatabaseHelper.Date("@EnrollmentDate", start.Value.Date),
                    DatabaseHelper.P("@BillingStartDate", BillingValue()), DatabaseHelper.P("@Reason", reason.Text.Trim()),
                    DatabaseHelper.Bit("@AllowLegacyExceptions", legacy.Checked), DatabaseHelper.Bit("@Preview", false),
                    DatabaseHelper.P("@ExpectedSnapshot", Convert.ToString(plan["Snapshot"])));
                ResultMessage = $"Placement dates corrected: {result.Rows[0]["CancelledCharges"]} charge(s) cancelled, {result.Rows[0]["AddedCharges"]} added. Payments unchanged.";
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { plan = null; impact.Text = DbMessage.UserMessage(ex) + " Review again before applying."; }
            finally { if (!IsDisposed) SetBusy(false); }
        };
        FormClosing += (_, e) => { if (busy && DialogResult != DialogResult.OK) e.Cancel = true; };
        ResetReview();
    }
}
