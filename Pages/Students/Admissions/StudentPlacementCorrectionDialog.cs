using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Maintenance;
namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>One reviewed correction path for current and verified legacy placement.</summary>
internal sealed class StudentPlacementCorrectionDialog : ApplicationWindowForm
{
    private readonly LookupBox record = new(), year = new() { Enabled = false };
    private readonly PlacementPicker placement = new();
    private readonly ModernComboBox status = new();
    private readonly ModernDatePicker start = HumanUi.Date(), leave = HumanUi.Date(), billing = HumanUi.Date();
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500), impact = HumanUi.TextBox(true, 0);
    private readonly CheckBox legacy = new() { Text = "Verified legacy dates (before admission or overlapping history)", AutoSize = true };
    private readonly ModernButton review = new() { Text = "Review", ThemeRole = "Secondary" },
        confirm = new() { Text = "Confirm Placement", ThemeRole = "Primary" },
        apply = new() { Text = "Apply Correction", ThemeRole = "Warning", Enabled = false },
        dates = new() { Text = "Correct Dates", ThemeRole = "Secondary" },
        close = new() { Text = "Close", DialogResult = DialogResult.Cancel };
    private readonly TableLayoutPanel fields;
    private readonly int admission;
    private int currentAcademicYear;
    private DataTable enrollments = new();
    private DataRow? plan;
    private bool busy, loading;
    private bool legacySetupPending;
    private bool Confirmed => !legacySetupPending && SelectedRecord()?["PlacementConfirmedAt"] is DateTime;
    private bool CanReviewPlacement => record.SelectedId == -1 || SelectedRecord() is not null && !Confirmed;
    private bool HasPlacementEdits => SelectedRecord() is DataRow current &&
        (year.SelectedId != Convert.ToInt32(current["AcademicYearId"]) || placement.SelectedId != Convert.ToInt32(current["ClassSectionId"]) ||
         status.Text != Convert.ToString(current["Status"]) || start.Value.Date != Convert.ToDateTime(current["EnrollmentDate"]).Date ||
         !Equals(current["LeavingDate"], leave.Checked ? (object)leave.Value.Date : DBNull.Value) ||
         !Equals(current["BillingStartDate"], billing.Checked ? (object)billing.Value.Date : DBNull.Value));
    internal bool Changed { get; private set; }

    internal StudentPlacementCorrectionDialog(int admissionId, string studentName)
    {
        admission = admissionId;
        Text = "Correct Student Placement";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(850), AppScale.Scale(650));
        MinimumSize = new Size(AppScale.Scale(740), AppScale.Scale(600));
        leave.ShowCheckBox = billing.ShowCheckBox = true; leave.Checked = billing.Checked = false;
        status.Items.AddRange(new object[] { "Active", "Left", "Promoted" });
        impact.ReadOnly = true;
        fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField("Record to correct", record, 0, 0); AddField("Academic year", year, 1, 0);
        AddField("Campus / Class / Section", placement, 0, 1); AddField("Status", status, 1, 1);
        AddField("Placement Start Date", start, 0, 2); AddField("Leaving / Promotion date", leave, 1, 2, false);
        AddField("Billing start (optional)", billing, 0, 3, false);
        var reasonField = HumanUi.Field("Correction reason", reason, true);
        reasonField.Height = AppMetrics.FieldRowHeight + 25;
        fields.Controls.Add(reasonField, 1, 3);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill,
            Text = $"{AdmissionNumbers.ForId(admission)} · {studentName}\nCorrect a mistake here. Use Promotion or Transfer for an actual move." }, 0, 0);
        root.Controls.Add(fields, 0, 1); root.Controls.Add(legacy, 0, 2);
        var buttons = HumanUi.ButtonRow(dates, review, apply, confirm, close); buttons.Dock = DockStyle.Fill;
        root.Controls.Add(impact, 0, 3); root.Controls.Add(buttons, 0, 4);
        Controls.Add(root); CancelButton = close;
        record.SelectedIndexChanged += (_, _) => LoadSelected();
        year.SelectedIndexChanged += (_, _) => ResetReview(); placement.SelectedIndexChanged += (_, _) => ResetReview();
        status.SelectedIndexChanged += (_, _) => { leave.Enabled = Convert.ToString(status.SelectedItem) != "Active"; if (!leave.Enabled) leave.Checked = false; ResetReview(); };
        start.ValueChanged += (_, _) => ResetReview(); leave.ValueChanged += (_, _) => ResetReview();
        billing.ValueChanged += (_, _) => ResetReview();
        reason.TextChanged += (_, _) => ResetReview(); legacy.CheckedChanged += (_, _) => ResetReview();
        review.Click += async (_, _) => await ReviewAsync();
        apply.Click += async (_, _) => await ApplyAsync();
        dates.Click += async (_, _) => await CorrectDatesAsync();
        UiAccessibility.Describe(dates, "Correct placement, billing or leaving dates", "Review date corrections and their effect on charges and certificates.");
        confirm.Click += async (_, _) => {
            if (busy || record.SelectedId <= 0 || Confirmed) return;
            SetBusy(true);
            try
            {
                if (await StudentPlacementConfirmation.ConfirmAsync(this, record.SelectedId, record.Text))
                { Changed = true; await LoadAsync(record.SelectedId); }
            }
            catch (Exception ex) { impact.Text = DbMessage.UserMessage(ex); }
            finally { SetBusy(false); }
        };
        Shown += async (_, _) => await LoadAsync();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        ResetReview();
    }
    private void AddField(string label, Control input, int column, int row, bool required = true)
    {
        var field = HumanUi.Field(label, input, required); field.Height = AppMetrics.FieldRowHeight;
        fields.Controls.Add(field, column, row);
    }
    private void ResetReview()
    {
        plan = null; apply.Enabled = false;
        confirm.Enabled = !busy && record.SelectedId > 0 && !Confirmed && !HasPlacementEdits;
        if (!loading) impact.Text = "Choose the record, correct its details, then Review. Existing fee rates are preserved; invalid unpaid charges will be listed before applying.";
    }
    private void SetBusy(bool value)
    {
        busy = value; fields.Enabled = close.Enabled = !value;
        legacy.Enabled = review.Enabled = !value && CanReviewPlacement;
        dates.Enabled = !value && record.SelectedId > 0;
        confirm.Enabled = !value && record.SelectedId > 0 && !Confirmed && !HasPlacementEdits;
        apply.Enabled = !value && !Confirmed && plan is not null && Convert.ToInt32(plan["BlockedCount"]) == 0;
    }
    private async Task LoadAsync(int selected = -1)
    {
        SetBusy(true); loading = true;
        try
        {
            var student = await DatabaseHelper.QueryUserAsync("GetStudentDetails", DatabaseHelper.Int("@AdmissionId", admission));
            legacySetupPending = student.Rows.Count > 0 && Convert.ToBoolean(student.Rows[0]["LegacySetupPending"]);
            enrollments = await DatabaseHelper.QueryUserAsync("GetStudentEnrollmentsForLifecycle", DatabaseHelper.Int("@AdmissionId", admission));
            var years = await DatabaseHelper.QueryUserAsync("GetAcademicYears");
            currentAcademicYear = years.AsEnumerable().FirstOrDefault(r => Convert.ToBoolean(r["IsCurrent"])) is DataRow currentYear
                ? Convert.ToInt32(currentYear["AcademicYearId"]) : 0;
            var placements = await DatabaseHelper.QueryUserAsync("GetClassSections", DatabaseHelper.Int("@CampusId", 0), DatabaseHelper.Int("@ClassId", 0), DatabaseHelper.Int("@SectionId", 0));
            year.Bind(years, "AcademicYearId", "YearName"); placement.Bind(placements);
            var choices = new DataTable(); choices.Columns.Add("Id", typeof(int)); choices.Columns.Add("Display");
            foreach (DataRow row in enrollments.Rows) choices.Rows.Add(row["EnrollmentId"], $"{row["YearName"]} · {row["ClassName"]} · {row["SectionName"]} ({row["Status"]})");
            choices.Rows.Add(-1, "Add verified missing placement");
            record.Bind(choices, "Id", "Display");
            if (selected < 0) selected = enrollments.AsEnumerable().FirstOrDefault(r => Convert.ToString(r["Status"]) == "Active") is DataRow active
                ? Convert.ToInt32(active["EnrollmentId"]) : enrollments.Rows.Count > 0 ? Convert.ToInt32(enrollments.Rows[0]["EnrollmentId"]) : -1;
            record.SelectedValue = selected;
            loading = false; LoadSelected();
        }
        catch (Exception ex) { impact.Text = DbMessage.UserMessage(ex); }
        finally { loading = false; SetBusy(false); }
    }
    private DataRow? SelectedRecord() => enrollments.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["EnrollmentId"]) == record.SelectedId);
    private void LoadSelected()
    {
        if (loading) return;
        loading = true;
        var current = SelectedRecord();
        // Existing history keeps its recorded year; a new placement uses the configured current year.
        year.SelectedValue = current?["AcademicYearId"] ?? currentAcademicYear; placement.SelectedValue = current?["ClassSectionId"] ?? 0;
        status.SelectedItem = current?["Status"] ?? "Active";
        start.Value = current is null ? DateTime.Today : Convert.ToDateTime(current["EnrollmentDate"]);
        leave.Checked = current?["LeavingDate"] is DateTime;
        if (current?["LeavingDate"] is DateTime leaving) leave.Value = leaving;
        billing.Checked = current?["BillingStartDate"] is DateTime;
        if (current?["BillingStartDate"] is DateTime billed) billing.Value = billed;
        reason.Clear(); legacy.Checked = false; dates.Enabled = current is not null;
        loading = false; ResetReview();
        foreach (Control field in fields.Controls) field.Visible = CanReviewPlacement || field.Contains(record);
        fields.SetColumnSpan(record.Parent!, CanReviewPlacement ? 1 : 2);
        legacy.Visible = review.Visible = apply.Visible = CanReviewPlacement;
        confirm.Visible = false;
        dates.Visible = Confirmed;
        Text = CanReviewPlacement ? "Review Student Placement" : "Placement History / Dates";
        if (record.SelectedId == 0) impact.Text = "Select a placement record to view its available corrections. To record verified missing history, choose Add verified missing placement explicitly.";
        if (Confirmed) impact.Text = $"Confirmed placement\r\n{record.Text}\r\n\r\nUse Promotion to move to another academic year, Transfer to change campus/class/section, or Re-admission when returning after leaving.\r\n\r\nFor a genuine date error, use Correct Dates below.";
    }
    private Task<DataTable> RunAsync(string? snapshot = null) => PlacementCorrection.ReviewAsync(admission,
        Math.Max(0, record.SelectedId), year.SelectedId, placement.SelectedId, start.Value.Date,
        leave.Checked ? leave.Value.Date : null, billing.Checked ? billing.Value.Date : null,
        Convert.ToString(status.SelectedItem) ?? "", reason.Text.Trim(), legacy.Checked, snapshot);
    private async Task ReviewAsync()
    {
        if (busy || !CanReviewPlacement) return;
        if (record.SelectedId == 0) { impact.Text = "Choose an existing record or Add verified missing placement."; return; }
        ResetReview(); SetBusy(true);
        try
        {
            plan = (await RunAsync()).Rows[0];
            impact.Text = $"{year.Text}\r\n{placement.Text}\r\n{status.Text}: {start.Value:dd MMM yyyy}" +
                (leave.Checked ? $" → {leave.Value:dd MMM yyyy}" : "") +
                "\r\nBilling starts: " + (billing.Checked ? billing.Value.ToString("dd MMM yyyy") : "placement start date") +
                $"\r\n\r\nReverse {plan["CancelCount"]} out-of-period unpaid charge(s): Rs. {Convert.ToDecimal(plan["CancelledAmount"]):N0}." +
                $"\r\nKeep {plan["RetainedCharges"]} valid charge(s). Existing fee rates and payments are preserved." +
                (Convert.ToBoolean(plan["Creating"]) ? "\r\nThis records a draft only. No fees or bills are created. Mark the legacy record verified in Admission when ready." :
                "\r\nNo new charges are posted. Review fee setup in Admission after correcting placement.") +
                (Convert.ToInt32(plan["BlockedCount"]) > 0 ? $"\r\n\r\n{plan["BlockedCount"]} affected charge(s) have payments. Resolve those in Fee Corrections, then review again." :
                "\r\n\r\nApply saves the correction and listed reversals together, with your reason in history.");
        }
        catch (Exception ex) { plan = null; impact.Text = DbMessage.UserMessage(ex); }
        finally { SetBusy(false); }
    }
    private async Task ApplyAsync()
    {
        if (busy || plan is null) return;
        SetBusy(true);
        try
        {
            await RunAsync(Convert.ToString(plan["Snapshot"]));
            Changed = true; StudentSearchCache.Invalidate(); DialogResult = DialogResult.OK;
            busy = false; Close();
        }
        catch (Exception ex) { plan = null; impact.Text = DbMessage.UserMessage(ex); }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private async Task CorrectDatesAsync()
    {
        if (busy || SelectedRecord() is not DataRow current) return;
        SetBusy(true);
        try
        {
            bool corrected;
            if (Convert.ToString(current["Status"]) == "Active")
            {
                if (!CurrentUser.HasPermission("Fees.Adjust")) { impact.Text = "Placement and billing date correction requires Fees.Adjust permission."; return; }
                using var dialog = new EnrollmentDateCorrectionDialog(record.Text, record.SelectedId, current,
                    Convert.ToDateTime(current["EnrollmentDate"]), current["BillingStartDate"] is DateTime billing ? billing : null, "", false);
                corrected = dialog.ShowDialog(this) == DialogResult.OK;
            }
            else
            {
                var source = await DatabaseHelper.QueryUserSetAsync("PreviewLifecycleDateRepair", DatabaseHelper.Int("@AdmissionId", admission));
                if (Convert.ToString(current["Status"]) == "Left")
                {
                    using var dialog = new LeavingDateCorrectionDialog(admission, record.SelectedId, source, null, "");
                    corrected = dialog.ShowDialog(this) == DialogResult.OK;
                }
                else
                {
                    using var dialog = new LifecycleDateRepairDialog(admission, source, record.SelectedId, focused: true);
                    corrected = dialog.ShowDialog(this) == DialogResult.OK;
                }
            }
            if (corrected) { Changed = true; await LoadAsync(record.SelectedId); }
        }
        catch (Exception ex) { impact.Text = DbMessage.UserMessage(ex); }
        finally { SetBusy(false); }
    }
}
