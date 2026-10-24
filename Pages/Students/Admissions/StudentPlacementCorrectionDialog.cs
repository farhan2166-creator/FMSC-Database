using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Maintenance;
namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>One reviewed correction path for current and verified legacy placement.</summary>
internal sealed class StudentPlacementCorrectionDialog : ApplicationWindowForm
{
    private readonly ListBox record = new() { Dock = DockStyle.Fill, IntegralHeight = false,
        Height = AppScale.Scale(110), Font = AppTheme.NormalFont };
    private readonly LookupBox year = new() { Enabled = false };
    private readonly PlacementPicker placement = new();
    private readonly ModernComboBox status = new();
    private readonly ModernDatePicker start = HumanUi.Date(), leave = HumanUi.Date(), billing = HumanUi.Date();
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500), impact = HumanUi.TextBox(true, 0);
    private readonly CheckBox legacy = new() { Text = "Verified legacy dates (before admission or overlapping history)", AutoSize = true };
    private readonly ModernButton review = new() { Text = "Review", ThemeRole = "Secondary" },
        confirm = new() { Text = "Confirm Placement", ThemeRole = "Primary" },
        apply = new() { Text = "Apply Correction", ThemeRole = "Warning", Enabled = false },
        dates = new() { Text = "Correct Dates", ThemeRole = "Secondary" },
        edit = new() { Text = "Review / Complete Placement", ThemeRole = "Primary" },
        addHistory = new() { Text = "Add Missing Historical Placement", ThemeRole = "Secondary" },
        close = new() { Text = "Close", DialogResult = DialogResult.Cancel };
    private readonly TableLayoutPanel fields;
    private readonly int admission;
    private readonly string studentName;
    // History stays read-only. An explicit action opens a separate editor; zero creates missing history.
    private readonly int? editingEnrollment;
    private int SelectedEnrollmentId => editingEnrollment ??
        (int.TryParse(Convert.ToString(record.SelectedValue), out int id) ? id : 0);
    private int currentAcademicYear;
    private DataTable enrollments = new();
    private DataTable missingWindows = new();
    private DataTable missingPlacementChoices = new(), allPlacements = new();
    private DataRow? SelectedMissingPlacement => missingPlacementChoices.AsEnumerable().FirstOrDefault(row =>
        Convert.ToInt32(row["WindowId"]) == year.SelectedId && Convert.ToInt32(row["ClassSectionId"]) == placement.SelectedId);
    private bool updatingPeriod;
    private DataRow? MissingWindow => missingWindows.AsEnumerable().FirstOrDefault(row =>
        Convert.ToInt32(row["WindowId"]) == year.SelectedId);
    private int SelectedYearId => editingEnrollment == 0 && MissingWindow is DataRow window
        ? Convert.ToInt32(window["AcademicYearId"]) : year.SelectedId;
    private DataRow? plan;
    private bool busy, loading;
    private bool legacySetupPending;
    private bool Confirmed => !legacySetupPending && SelectedRecord()?["PlacementConfirmedAt"] is DateTime;
    private bool CanReviewPlacement => editingEnrollment.HasValue &&
        (editingEnrollment == 0 ? MissingWindow is not null : SelectedRecord() is not null && !Confirmed);
    private bool HasPlacementEdits => SelectedRecord() is DataRow current &&
        (year.SelectedId != Convert.ToInt32(current["AcademicYearId"]) || placement.SelectedId != Convert.ToInt32(current["ClassSectionId"]) ||
         status.Text != Convert.ToString(current["Status"]) || start.Value.Date != Convert.ToDateTime(current["EnrollmentDate"]).Date ||
         !Equals(current["LeavingDate"], leave.Checked ? (object)leave.Value.Date : DBNull.Value) ||
         !Equals(current["BillingStartDate"], billing.Checked ? (object)billing.Value.Date : DBNull.Value));
    internal bool Changed { get; private set; }

    internal StudentPlacementCorrectionDialog(int admissionId, string studentName, bool initialize = true)
        : this(admissionId, studentName, null, initialize) { }

    private StudentPlacementCorrectionDialog(int admissionId, string studentName, int? editingEnrollment, bool initialize = true)
    {
        admission = admissionId;
        this.studentName = studentName;
        this.editingEnrollment = editingEnrollment;
        Text = editingEnrollment == 0 ? "Add Missing Historical Placement" :
            editingEnrollment.HasValue ? "Review / Complete Placement" : "Placement History";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(850), AppScale.Scale(650));
        MinimumSize = new Size(AppScale.Scale(740), AppScale.Scale(600));
        leave.ShowCheckBox = billing.ShowCheckBox = true; leave.Checked = billing.Checked = false;
        status.Items.AddRange(new object[] { "Active", "Left", "Promoted" });
        impact.ReadOnly = true;
        fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField("Placement history", record, 0, 0, false);
        record.Parent!.Height = AppScale.Scale(140);
        AddField("Academic year", year, 1, 0);
        AddField("Campus / Class / Section", placement, 0, 1); AddField("Status", status, 1, 1);
        AddField("Placement Start Date", start, 0, 2); AddField("Leaving / Promotion date", leave, 1, 2, false);
        AddField("Billing start (optional)", billing, 0, 3, false);
        var reasonField = HumanUi.Field("Correction reason", reason, true);
        reasonField.Height = AppMetrics.FieldRowHeight + 25;
        fields.Controls.Add(reasonField, 1, 3);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var introduction = new Label { AutoSize = true, Dock = DockStyle.Fill,
            MaximumSize = new Size(AppScale.Scale(780), 0),
            Text = $"{AdmissionNumbers.ForId(admission)} · {studentName}\n" +
                (editingEnrollment == 0 ? "Record a verified gap in this student's history. Use Promotion, Transfer or Re-admission for an actual move."
                : editingEnrollment.HasValue ? "Review the unconfirmed placement, then review its impact before saving."
                : "Select a placement to view its details and available actions.") };
        root.Controls.Add(introduction, 0, 0);
        root.SizeChanged += (_, _) => introduction.MaximumSize = new Size(
            Math.Max(200, root.ClientSize.Width - root.Padding.Horizontal - introduction.Margin.Horizontal - SystemInformation.VerticalScrollBarWidth), 0);
        root.Controls.Add(fields, 0, 1); root.Controls.Add(legacy, 0, 2);
        addHistory.Width = AppScale.Scale(275);
        edit.Width = AppScale.Scale(240);
        var buttons = HumanUi.ButtonRow(dates, edit, addHistory, review, apply, confirm, close); buttons.Dock = DockStyle.Fill;
        dates.Width = review.Width = apply.Width = confirm.Width = AppScale.Scale(160);
        edit.Width = AppScale.Scale(240); close.Width = AppScale.Scale(110);
        root.Controls.Add(impact, 0, 3); root.Controls.Add(buttons, 0, 4);
        Controls.Add(root); CancelButton = close;
        record.SelectedIndexChanged += (_, _) => LoadSelected();
        year.SelectedIndexChanged += (_, _) => { ConfigureMissingPeriod(true); ResetReview(); };
        placement.SelectedIndexChanged += (_, _) => { ConfigureMissingPeriod(false); ResetReview(); };
        status.SelectedIndexChanged += (_, _) => { leave.Enabled = Convert.ToString(status.SelectedItem) != "Active"; if (!leave.Enabled) leave.Checked = false; ConfigureMissingPeriod(false); ResetReview(); };
        start.ValueChanged += (_, _) => { ConfigureMissingPeriod(false); ResetReview(); }; leave.ValueChanged += (_, _) => { ConfigureMissingPeriod(false); ResetReview(); };
        billing.ValueChanged += (_, _) => ResetReview();
        reason.TextChanged += (_, _) => ResetReview(); legacy.CheckedChanged += (_, _) => ResetReview();
        review.Click += async (_, _) => await ReviewAsync();
        apply.Click += async (_, _) => await ApplyAsync();
        dates.Click += async (_, _) => await CorrectDatesAsync();
        edit.Click += async (_, _) => await OpenEditorAsync(SelectedEnrollmentId);
        addHistory.Click += async (_, _) => await OpenEditorAsync(0);
        UiAccessibility.Describe(dates, "Correct placement, billing or leaving dates", "Review date corrections and their effect on charges and certificates.");
        confirm.Click += async (_, _) => {
            if (busy || SelectedEnrollmentId <= 0 || Confirmed) return;
            SetBusy(true);
            try
            {
                if (await StudentPlacementConfirmation.ConfirmAsync(this, SelectedEnrollmentId, record.Text))
                { Changed = true; await LoadAsync(SelectedEnrollmentId); }
            }
            catch (Exception ex) { OperationFeedback.Set(impact,DbMessage.UserMessage(ex),true); }
            finally { SetBusy(false); }
        };
        if (initialize) Shown += async (_, _) => await LoadAsync();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        LoadSelected();
    }
    private void AddField(string label, Control input, int column, int row, bool required = true)
    {
        var field = HumanUi.Field(label, input, required); field.Height = AppMetrics.FieldRowHeight;
        fields.Controls.Add(field, column, row);
    }
    private void ResetReview()
    {
        if (loading || updatingPeriod) return;
        plan = null; apply.Enabled = false;
        confirm.Enabled = !busy && SelectedEnrollmentId > 0 && !Confirmed && !HasPlacementEdits;
        if (editingEnrollment.HasValue) impact.Text = editingEnrollment == 0
            ? MissingWindow is DataRow window
                ? $"Available gap: {Convert.ToDateTime(window["GapStart"]):dd MMM yyyy} to {Convert.ToDateTime(window["GapEnd"]):dd MMM yyyy}. Complete the details, then Review."
                : "No missing historical placement can be added. No eligible gaps are available. Use Re-admission for a return after departure."
            : "Complete the details and reason, then choose Review. Check the effect on fees before applying.";
    }
    private void SetBusy(bool value)
    {
        busy = value; fields.Enabled = close.Enabled = !value;
        legacy.Enabled = !value && CanReviewPlacement;
        review.Enabled = !value && CanReviewPlacement && (editingEnrollment != 0 || SelectedMissingPlacement is not null);
        dates.Enabled = !value && SelectedRecord() is not null;
        edit.Enabled = !value && SelectedRecord() is not null && !Confirmed;
        addHistory.Enabled = !value && missingWindows.Rows.Count > 0;
        addHistory.AccessibleDescription = missingWindows.Rows.Count > 0 ? "Record a verified gap in placement history."
            : "No missing historical placement can be added: no eligible gaps are available.";
        confirm.Enabled = !value && SelectedEnrollmentId > 0 && !Confirmed && !HasPlacementEdits;
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
            allPlacements = await DatabaseHelper.QueryUserAsync("GetClassSections", DatabaseHelper.Int("@CampusId", 0), DatabaseHelper.Int("@ClassId", 0), DatabaseHelper.Int("@SectionId", 0));
            missingPlacementChoices = await MissingPlacementPolicy.ReadAsync(admission);
            missingWindows = missingPlacementChoices.DefaultView.ToTable(true, "WindowId", "AcademicYearId", "YearName", "GapStart", "GapEnd", "PromotionDate");
            BindYears(years); placement.Bind(allPlacements);
            BindHistory(selected);
            loading = false; LoadSelected();
        }
        catch (Exception ex) { OperationFeedback.Set(impact,DbMessage.UserMessage(ex),true); }
        finally { loading = false; SetBusy(false); }
    }
    private void BindYears(DataTable years)
    {
        if (editingEnrollment != 0) { year.Bind(years, "AcademicYearId", "YearName"); return; }
        if (!missingWindows.Columns.Contains("Display")) missingWindows.Columns.Add("Display");
        foreach (DataRow row in missingWindows.Rows)
            row["Display"] = $"{row["YearName"]} · {Convert.ToDateTime(row["GapStart"]):dd MMM yyyy} – {Convert.ToDateTime(row["GapEnd"]):dd MMM yyyy}";
        year.Bind(missingWindows, "WindowId", "Display", "Choose an available historical period");
    }
    private void ConfigureMissingPeriod(bool resetDates)
    {
        if (editingEnrollment != 0 || loading || updatingPeriod) return;
        updatingPeriod = true;
        try
        {
            if (resetDates)
            {
                int previous = placement.SelectedId;
                var eligible = missingPlacementChoices.AsEnumerable().Where(row => Convert.ToInt32(row["WindowId"]) == year.SelectedId)
                    .Select(row => Convert.ToInt32(row["ClassSectionId"])).ToHashSet();
                var filtered = allPlacements.Clone();
                foreach (DataRow row in allPlacements.Rows)
                    if (eligible.Contains(Convert.ToInt32(row["ClassSectionId"]))) filtered.ImportRow(row);
                placement.Bind(filtered);
                placement.SelectedValue = eligible.Contains(previous) ? previous : 0;
            }
            if (MissingWindow is not DataRow window) { review.Enabled = false; return; }
            DateTime first = Convert.ToDateTime(window["GapStart"]), last = Convert.ToDateTime(window["GapEnd"]);
            string selectedStatus = status.Text;
            status.Items.Clear(); status.Items.Add("Left");
            if (window["PromotionDate"] is DateTime && SelectedMissingPlacement is DataRow choice && Convert.ToBoolean(choice["CanPromote"])) status.Items.Add("Promoted");
            status.SelectedItem = status.Items.Contains(selectedStatus) ? selectedStatus : "Left";
            Limit(start, first, last, resetDates ? first : start.Value);
            DateTime end = status.Text == "Promoted" ? Convert.ToDateTime(window["PromotionDate"]) : last;
            leave.ShowCheckBox = false; leave.Checked = true;
            Limit(leave, status.Text == "Promoted" ? end : start.Value.Date, end, resetDates ? end : leave.Value);
            billing.Checked = false;
            billing.Enabled = false;
            review.Enabled = !busy && SelectedMissingPlacement is not null;
        }
        finally { updatingPeriod = false; }

        static void Limit(ModernDatePicker picker, DateTime min, DateTime max, DateTime value)
        {
            picker.MinDate = DateTimePicker.MinimumDateTime; picker.MaxDate = DateTimePicker.MaximumDateTime;
            picker.Value = value < min ? min : value > max ? max : value;
            picker.MinDate = min; picker.MaxDate = max;
        }
    }
    private void BindHistory(int selected)
    {
        var choices = new DataTable(); choices.Columns.Add("Id", typeof(int)); choices.Columns.Add("Display");
        foreach (DataRow row in enrollments.Rows)
            choices.Rows.Add(row["EnrollmentId"], $"{row["YearName"]} · {row["ClassName"]} · {row["SectionName"]} ({row["Status"]})");
        record.DataSource = null;
        record.ValueMember = "Id"; record.DisplayMember = "Display";
        record.DataSource = choices;
        if (!choices.AsEnumerable().Any(row => Convert.ToInt32(row["Id"]) == selected))
            selected = enrollments.AsEnumerable().FirstOrDefault(row => Convert.ToString(row["Status"]) == "Active") is DataRow active
                ? Convert.ToInt32(active["EnrollmentId"]) : enrollments.Rows.Count > 0 ? Convert.ToInt32(enrollments.Rows[0]["EnrollmentId"]) : 0;
        if (selected > 0) record.SelectedValue = selected;
        else record.SelectedIndex = -1;
    }
    private DataRow? SelectedRecord() => enrollments.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["EnrollmentId"]) == SelectedEnrollmentId);
    private void LoadSelected()
    {
        if (loading) return;
        loading = true;
        if (editingEnrollment == 0)
            foreach (var picker in new[] { start, leave, billing })
            {
                picker.MinDate = DateTimePicker.MinimumDateTime;
                picker.MaxDate = DateTimePicker.MaximumDateTime;
            }
        var current = SelectedRecord();
        // Existing history keeps its recorded year; a new placement uses the configured current year.
        year.SelectedValue = editingEnrollment == 0
            ? missingWindows.Rows.Count > 0 ? missingWindows.Rows[0]["WindowId"] : 0
            : current?["AcademicYearId"] ?? currentAcademicYear;
        placement.SelectedValue = current?["ClassSectionId"] ?? 0;
        status.SelectedItem = current?["Status"] ??
            (enrollments.AsEnumerable().Any(row => Convert.ToString(row["Status"]) == "Active") ? "Left" : "Active");
        start.Value = current is null ? DateTime.Today : Convert.ToDateTime(current["EnrollmentDate"]);
        leave.Checked = current?["LeavingDate"] is DateTime || current is null && status.Text != "Active";
        leave.Value = DateTime.Today;
        if (current?["LeavingDate"] is DateTime leaving) leave.Value = leaving;
        billing.Checked = current?["BillingStartDate"] is DateTime;
        if (current?["BillingStartDate"] is DateTime billed) billing.Value = billed;
        reason.Clear(); legacy.Checked = false; dates.Enabled = current is not null;
        loading = false;
        ConfigureMissingPeriod(true); ResetReview();
        foreach (Control field in fields.Controls)
            field.Visible = editingEnrollment.HasValue ? CanReviewPlacement && !field.Contains(record) : field.Contains(record);
        fields.SetColumnSpan(record.Parent!, 2);
        if (editingEnrollment.HasValue)
        {
            fields.SetColumn(year.Parent!, 0);
            fields.SetColumnSpan(year.Parent!, 2);
        }
        year.Enabled = editingEnrollment == 0;
        review.Visible = apply.Visible = CanReviewPlacement;
        legacy.Visible = CanReviewPlacement && editingEnrollment != 0;
        billing.Enabled = editingEnrollment != 0;
        if (editingEnrollment == 0) billing.Checked = false;
        confirm.Visible = false;
        dates.Visible = !editingEnrollment.HasValue && Confirmed;
        edit.Visible = !editingEnrollment.HasValue && current is not null && !Confirmed;
        addHistory.Visible = !editingEnrollment.HasValue;
        if (!editingEnrollment.HasValue) ShowHistorySummary(current);
        else if (!CanReviewPlacement && editingEnrollment != 0) impact.Text = "This placement is already confirmed or no longer available. Close this form and select it in Placement History.";
        SetBusy(busy);
    }
    private void ShowHistorySummary(DataRow? current)
    {
        if (current is null)
        {
            impact.Text = "No placement records are available.\r\n\r\n" + (missingWindows.Rows.Count > 0
                ? "Use Add Missing Historical Placement only to record verified missing history."
                : "No missing historical placement can be added: no eligible gaps are available.");
            return;
        }
        string DateText(string column) => current[column] is DateTime date ? date.ToString("dd MMM yyyy") : "Not recorded";
        impact.Text = $"{record.Text}\r\n{(Confirmed ? "Confirmed placement" : "Unconfirmed placement")}\r\n\r\n" +
            $"Placement starts: {DateText("EnrollmentDate")}\r\nBilling starts: {DateText("BillingStartDate")}\r\nLeaving / promotion date: {DateText("LeavingDate")}\r\n\r\n" +
            (Confirmed ? "To fix a date error, choose Correct Dates.\r\n\r\nUse Promotion for a new academic year, Transfer to change campus/class/section, or Re-admission after leaving."
            : "Choose Review / Complete Placement to check this draft and its effect on fees. Confirm the first current placement in Admission after completing setup.");
        if (missingWindows.Rows.Count == 0) impact.Text += "\r\n\r\nNo missing historical placement can be added: no eligible gaps are available.";
    }
    private async Task OpenEditorAsync(int enrollmentId)
    {
        if (busy || editingEnrollment.HasValue || enrollmentId == 0 && missingWindows.Rows.Count == 0 || enrollmentId > 0 && (SelectedRecord() is null || Confirmed)) return;
        SetBusy(true);
        try
        {
            using var dialog = new StudentPlacementCorrectionDialog(admission, studentName, enrollmentId);
            dialog.ShowDialog(this);
            if (dialog.Changed)
            {
                Changed = true;
                await LoadAsync(enrollmentId > 0 ? enrollmentId : -1);
            }
        }
        finally { SetBusy(false); }
    }
    private Task<DataTable> RunAsync(string? snapshot = null) => PlacementCorrection.ReviewAsync(admission,
        Math.Max(0, SelectedEnrollmentId), SelectedYearId, placement.SelectedId, start.Value.Date,
        leave.Checked ? leave.Value.Date : null, editingEnrollment != 0 && billing.Checked ? billing.Value.Date : null,
        Convert.ToString(status.SelectedItem) ?? "", reason.Text.Trim(), legacy.Checked, snapshot);
    private async Task ReviewAsync()
    {
        if (busy || !CanReviewPlacement) return;
        if (editingEnrollment == 0 && SelectedMissingPlacement is null)
        {
            OperationFeedback.Set(impact,"Choose a campus / class / section that fits the earlier and later placements.",true);
            return;
        }
        ResetReview(); SetBusy(true);
        try
        {
            plan = (await RunAsync()).Rows[0];
            impact.Text = $"{year.Text}\r\n{placement.Text}\r\n{status.Text}: {start.Value:dd MMM yyyy}" +
                (leave.Checked ? $" → {leave.Value:dd MMM yyyy}" : "") +
                (editingEnrollment == 0 ? "\r\nBilling: not applicable to historical placement." :
                    "\r\nBilling starts: " + (billing.Checked ? billing.Value.ToString("dd MMM yyyy") : "placement start date")) +
                $"\r\n\r\nReverse {plan["CancelCount"]} out-of-period unpaid charge(s): Rs. {Convert.ToDecimal(plan["CancelledAmount"]):N0}." +
                $"\r\nKeep {plan["RetainedCharges"]} valid charge(s). Existing fee rates and payments are preserved." +
                (Convert.ToBoolean(plan["Creating"]) ? "\r\nThis records historical placement only. No fees or bills are created; the student's current status is preserved." :
                "\r\nNo new charges are posted. Review fee setup in Admission after correcting placement.") +
                (Convert.ToInt32(plan["BlockedCount"]) > 0 ? $"\r\n\r\n{plan["BlockedCount"]} affected charge(s) have payments. Resolve those in Fee Corrections, then review again." :
                "\r\n\r\nApply saves the correction and listed reversals together, with your reason in history.");
            OperationFeedback.Apply(impact,Convert.ToInt32(plan["BlockedCount"]! )>0);
        }
        catch (Exception ex) { plan = null; OperationFeedback.Set(impact,DbMessage.UserMessage(ex),true); }
        finally { SetBusy(false); }
    }
    private async Task ApplyAsync()
    {
        if (busy || !CanReviewPlacement || plan is null) return;
        SetBusy(true);
        try
        {
            await RunAsync(Convert.ToString(plan["Snapshot"]));
            Changed = true; StudentSearchCache.Invalidate(); DialogResult = DialogResult.OK;
            busy = false; Close();
        }
        catch (Exception ex) { plan = null; OperationFeedback.Set(impact,DbMessage.UserMessage(ex),true); }
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
                if (!CurrentUser.HasPermission("Fees.Adjust")) { OperationFeedback.Set(impact,"Placement and billing date correction requires Fees.Adjust permission.",true); return; }
                using var dialog = new EnrollmentDateCorrectionDialog(record.Text, SelectedEnrollmentId, current,
                    Convert.ToDateTime(current["EnrollmentDate"]), current["BillingStartDate"] is DateTime billing ? billing : null, "", false);
                corrected = dialog.ShowDialog(this) == DialogResult.OK;
            }
            else
            {
                var source = await DatabaseHelper.QueryUserSetAsync("PreviewLifecycleDateRepair", DatabaseHelper.Int("@AdmissionId", admission));
                if (Convert.ToString(current["Status"]) == "Left")
                {
                    using var dialog = new LeavingDateCorrectionDialog(admission, SelectedEnrollmentId, source, null, "");
                    corrected = dialog.ShowDialog(this) == DialogResult.OK;
                }
                else
                {
                    using var dialog = new LifecycleDateRepairDialog(admission, source, SelectedEnrollmentId, focused: true);
                    corrected = dialog.ShowDialog(this) == DialogResult.OK;
                }
            }
            if (corrected) { Changed = true; await LoadAsync(SelectedEnrollmentId); }
        }
        catch (Exception ex) { OperationFeedback.Set(impact,DbMessage.UserMessage(ex),true); }
        finally { SetBusy(false); }
    }
}
