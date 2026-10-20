using System.Text.Json;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>Review one pending student's placement and opening bill before atomic activation.</summary>
/// <remarks>
/// Decision confirmed on 20 October 2026: placement start records when the student joined
/// this class. Billing start is chosen independently to bring legacy students into billing
/// one at a time. For both legacy and new students, apply the School Profile cutoff to the
/// billing start date, never the placement start. With cutoff 16, an April placement and
/// a 20 October billing start produce November monthly fees. Admission and selected
/// one-time fees use the chosen billing date. See migration 29 for activation enforcement.
/// </remarks>
internal sealed class StudentBillingSetupDialog : ApplicationWindowForm
{
    private readonly int admission;
    private int enrollment;
    private readonly LookupBox year = new() { Enabled = false };
    private readonly ModernComboBox admissionFeeHandling = HumanUi.Combo();
    private bool AdmissionFeeAlreadyPaid => admissionFeeHandling.SelectedIndex == 0;
    private readonly PlacementPicker placement = new();
    private readonly ModernTextBox studentName = HumanUi.TextBox(false, 500), fatherName = HumanUi.TextBox(false, 500),
        reason = HumanUi.TextBox(false, 500), impact = HumanUi.TextBox(true, 0);
    private readonly ModernDatePicker start = HumanUi.Date(), billing = HumanUi.Date();
    private readonly KeyboardDataGridView fees = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false };
    private readonly ModernButton review = new() { Text = "Preview Activation", ThemeRole = "Secondary" },
        activate = new() { Text = "Confirm & Activate", ThemeRole = "Primary", Enabled = false },
        reloadFees = new() { Text = "Load Class Fees", ThemeRole = "Secondary" },
        history = new() { Text = "Placement History", ThemeRole = "Secondary" },
        close = new() { Text = "Close", DialogResult = DialogResult.Cancel };
    private readonly TableLayoutPanel root;
    private DataTable placements = new(), feeRows = new();
    private string? snapshot;
    private bool busy, loading;
    internal bool Changed { get; private set; }

    internal StudentBillingSetupDialog(int admissionId)
    {
        admission = admissionId;
        Text = "Student Setup / Start Billing";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(900), AppScale.Scale(760));
        MinimumSize = new Size(AppScale.Scale(800), AppScale.Scale(680));
        root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(UiDesignSystem.Spacing.Md), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 70)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 30)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Font = AppTheme.NormalFont,
            Margin = new Padding(0, 0, 0, UiDesignSystem.Spacing.Sm),
            Text = $"Admission {AdmissionNumbers.ForId(admission)} · Student setup and billing\nReview the placement, fee handling and opening bill before activating this student." }, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void Field(string label, Control input, int col, int row) { var field = HumanUi.Field(label, input, true); field.Height = AppMetrics.FieldRowHeight; fields.Controls.Add(field, col, row); }
        Field("Student name", studentName, 0, 0); Field("Father name", fatherName, 1, 0);
        Field("Academic year", year, 0, 1); Field("Campus / Class / Section", placement, 1, 1);
        Field("Placement start", start, 0, 2); Field("Billing starts from", billing, 1, 2);
        admissionFeeHandling.Items.AddRange(new object[] { "Already paid before system", "Charge admission fee now" });
        admissionFeeHandling.SelectedIndex = -1;
        Field("Admission fee handling", admissionFeeHandling, 0, 3);
        var remarksField = HumanUi.Field("Remarks", reason, false);
        remarksField.Height = AppMetrics.FieldRowHeight;
        fields.Controls.Add(remarksField, 1, 3);
        root.Controls.Add(fields, 0, 1);
        var feeTools = HumanUi.ButtonRow(reloadFees, history);
        feeTools.Dock = DockStyle.Fill;
        feeTools.Margin = new Padding(0, 0, 0, UiDesignSystem.Spacing.Sm);
        root.Controls.Add(feeTools, 0, 2);
        HumanUi.ApplyGridStyle(fees, AppTheme.ModuleAccent("Students"));
        UiHelper.SetGridEmptyMessage(fees, "Choose a placement, then load its class fees.");
        root.Controls.Add(fees, 0, 3);
        impact.ReadOnly = true; impact.Dock = DockStyle.Fill;
        impact.Margin = new Padding(0, UiDesignSystem.Spacing.Sm, 0, 0);
        root.Controls.Add(impact, 0, 4);
        var actions = HumanUi.ButtonRow(review, activate, close);
        actions.Dock = DockStyle.Fill;
        root.Controls.Add(actions, 0, 5); Controls.Add(root); CancelButton = close;
        foreach (var text in new[] { studentName, fatherName, reason }) text.TextChanged += (_, _) => Reset();
        year.SelectedIndexChanged += (_, _) => Reset();
        admissionFeeHandling.SelectedIndexChanged += (_, _) => { ApplyAdmissionFeeChoice(); Reset(); };
        placement.SelectedIndexChanged += (_, _) => { if (!loading) { feeRows.Clear(); Reset(); impact.Text = "Placement changed. Load Class Fees and review the amounts before activation."; } };
        start.ValueChanged += (_, _) => Reset();
        billing.ValueChanged += (_, _) => Reset();
        fees.CellValueChanged += (_, _) => Reset();
        fees.CurrentCellDirtyStateChanged += (_, _) => { if (fees.IsCurrentCellDirty) fees.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        fees.DataError += (_, e) => { e.ThrowException = false; Reset(); impact.Text = "Enter valid whole-rupee amounts and discounts."; };
        reloadFees.Click += async (_, _) => await LoadFeesAsync(false);
        review.Click += async (_, _) => await ReviewAsync(); activate.Click += async (_, _) => await ActivateAsync();
        history.Click += async (_, _) => {
            if (busy) return;
            using var dialog = new StudentPlacementCorrectionDialog(admission, studentName.Text);
            dialog.ShowDialog(this);
            if (dialog.Changed) { Changed = true; await LoadAsync(); }
        };
        Shown += async (_, _) => await LoadAsync();
        FinanceHelp.Attach(this,start,FinanceHelp.Topic.PlacementStart);
        FinanceHelp.Attach(this,billing,FinanceHelp.Topic.BillingStart);
        FinanceHelp.Attach(this,review,FinanceHelp.Topic.Review);
        FinanceHelp.Attach(this,admissionFeeHandling,"Choose whether the admission fee was paid before system billing, or must be charged now.","Already paid records an acknowledgement only; it does not create a new charge or a new payment receipt.");
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private void Reset() { snapshot = null; activate.Enabled = false; if (!loading) impact.Text = "Review the setup to see the opening charges. Monthly fees after the opening month use normal fee posting."; }
    private void SetBusy(bool value) { busy = value; root.Enabled = !value; activate.Enabled = !value && snapshot is not null; }
    private async Task LoadAsync()
    {
        SetBusy(true); loading = true;
        try
        {
            var students = await DatabaseHelper.QueryUserAsync("GetStudentDetails", DatabaseHelper.Int("@AdmissionId", admission));
            var student = students.Rows[0]; studentName.Text = Convert.ToString(student["StudentName"]); fatherName.Text = Convert.ToString(student["FatherName"]);
            reason.Clear();
            var years = await DatabaseHelper.QueryUserAsync("GetAcademicYears"); year.Bind(years, "AcademicYearId", "YearName");
            placements = await DatabaseHelper.QueryUserAsync("GetClassSections", DatabaseHelper.Int("@CampusId", 0), DatabaseHelper.Int("@ClassId", 0), DatabaseHelper.Int("@SectionId", 0)); placement.Bind(placements);
            var records = await DatabaseHelper.QueryUserAsync("GetStudentEnrollmentsForLifecycle", DatabaseHelper.Int("@AdmissionId", admission));
            var current = records.AsEnumerable().FirstOrDefault(r => Convert.ToString(r["Status"]) == "Active");
            enrollment = current is null ? 0 : Convert.ToInt32(current["EnrollmentId"]);
            if (current is not null)
            {
                year.SelectedValue = current["AcademicYearId"]; placement.SelectedValue = current["ClassSectionId"];
                start.Value = Convert.ToDateTime(current["EnrollmentDate"]);
                billing.Value = current["BillingStartDate"] is DateTime billed ? billed : start.Value;
            }
            else
            {
                var currentYear = years.AsEnumerable().FirstOrDefault(r => Convert.ToBoolean(r["IsCurrent"]));
                year.SelectedValue = currentYear?["AcademicYearId"] ?? 0;
            }
            loading = false;
            if (placement.SelectedId > 0) await LoadFeesAsync(true);
            Reset();
        }
        catch (Exception ex) { impact.Text = DbMessage.UserMessage(ex); }
        finally { loading = false; SetBusy(false); }
    }
    private async Task LoadFeesAsync(bool preserveExisting)
    {
        SetBusy(true); Reset();
        try
        {
            var selected = placements.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["ClassSectionId"]) == placement.SelectedId);
            if (selected is null) { impact.Text = "Select a placement first."; return; }
            var configs = await DatabaseHelper.QueryUserAsync("GetFeeConfigurations", DatabaseHelper.Int("@CampusId", Convert.ToInt32(selected["CampusId"])), DatabaseHelper.Int("@ClassId", Convert.ToInt32(selected["ClassId"])));
            var existing = preserveExisting && enrollment > 0 ? await DatabaseHelper.QueryUserAsync("GetStudentFeeAssignments", DatabaseHelper.Int("@EnrollmentId", enrollment)) : new DataTable();
            feeRows = new DataTable(); feeRows.Columns.Add("Selected", typeof(bool)); feeRows.Columns.Add("FeeTypeId", typeof(int)); feeRows.Columns.Add("Fee", typeof(string));
            feeRows.Columns.Add("Amount", typeof(decimal)); feeRows.Columns.Add("DiscountAmount", typeof(decimal)); feeRows.Columns.Add("Frequency", typeof(string)); feeRows.Columns.Add("Mandatory", typeof(bool)); feeRows.Columns.Add("IsAdmission", typeof(bool));
            foreach (DataRow config in configs.Rows)
            {
                string frequency = Convert.ToString(config["Frequency"]) ?? "";
                if (frequency is not ("Monthly" or "Yearly" or "As Needed")) continue;
                var old = existing.AsEnumerable().FirstOrDefault(r => Equals(r["FeeTypeId"], config["FeeTypeId"]) && r["EffectiveTo"] is DBNull);
                feeRows.Rows.Add(old is not null || Convert.ToBoolean(config["IsMandatory"]), config["FeeTypeId"], config["FeeTypeName"], old?["StandardAmount"] ?? config["Amount"], old?["DiscountAmount"] ?? 0m, old?["Frequency"] ?? frequency, config["IsMandatory"], string.Equals(Convert.ToString(config["FeeTypeCode"]), "ADMISSION", StringComparison.OrdinalIgnoreCase));
            }
            // Preserve selected existing fees even if the class no longer has that configuration.
            foreach (DataRow old in existing.Rows)
                if (old["EffectiveTo"] is DBNull && (Convert.ToString(old["Frequency"]) is "Monthly" or "Yearly" or "As Needed") && !feeRows.AsEnumerable().Any(r => Equals(r["FeeTypeId"], old["FeeTypeId"])))
                    feeRows.Rows.Add(true, old["FeeTypeId"], old["FeeTypeName"], old["StandardAmount"], old["DiscountAmount"], old["Frequency"], old["IsMandatory"], string.Equals(Convert.ToString(old["FeeTypeCode"]), "ADMISSION", StringComparison.OrdinalIgnoreCase));
            fees.DataSource = feeRows; fees.Columns["FeeTypeId"].Visible = false;
            fees.Columns["IsAdmission"].Visible = false;
            foreach (DataGridViewColumn column in fees.Columns) { column.ReadOnly = column.Name is not ("Selected" or "Amount" or "DiscountAmount"); column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
            foreach (DataGridViewRow row in fees.Rows) if (Convert.ToBoolean(row.Cells["Mandatory"].Value)) row.Cells["Selected"].ReadOnly = true;
            ApplyAdmissionFeeChoice();
            impact.Text = "Review selected fees and discounts, then click Preview Activation. Selected As Needed fees are charged once on activation.";
        }
        catch (Exception ex) { impact.Text = DbMessage.UserMessage(ex); }
        finally { SetBusy(false); }
    }
    private void ApplyAdmissionFeeChoice()
    {
        if (!feeRows.Columns.Contains("IsAdmission")) return;
        foreach (DataRow row in feeRows.Rows)
            if (Convert.ToBoolean(row["IsAdmission"])) row["Selected"] = admissionFeeHandling.SelectedIndex == 1;
        foreach (DataGridViewRow row in fees.Rows)
        {
            if (!Convert.ToBoolean(row.Cells["IsAdmission"].Value)) continue;
            row.Cells["Selected"].ReadOnly = true;
            row.Cells["Amount"].ReadOnly = row.Cells["DiscountAmount"].ReadOnly = admissionFeeHandling.SelectedIndex != 1;
        }
    }
    private Task<DataTable> RunAsync(string? expected = null)
    {
        if (admissionFeeHandling.SelectedIndex < 0) throw new InvalidOperationException("Choose whether the admission fee was already paid before system billing or should be charged now.");
        fees.EndEdit(); ((CurrencyManager?)fees.BindingContext?[feeRows])?.EndCurrentEdit();
        string json = JsonSerializer.Serialize(feeRows.AsEnumerable().Where(r => Convert.ToBoolean(r["Selected"])).Select(r => new {
            FeeTypeId = Convert.ToInt32(r["FeeTypeId"]), Amount = Convert.ToDecimal(r["Amount"]), DiscountAmount = Convert.ToDecimal(r["DiscountAmount"]), Frequency = Convert.ToString(r["Frequency"]) }));
        return DatabaseHelper.QueryUserAsync("ActivateStudentBilling", DatabaseHelper.Int("@AdmissionId", admission), DatabaseHelper.Int("@EnrollmentId", enrollment),
            DatabaseHelper.Int("@YearId", year.SelectedId), DatabaseHelper.Int("@PlacementId", placement.SelectedId), DatabaseHelper.Date("@StartDate", start.Value.Date),
            DatabaseHelper.Date("@BillingStart", billing.Value.Date), DatabaseHelper.P("@StudentName", studentName.Text.Trim()), DatabaseHelper.P("@FatherName", fatherName.Text.Trim()),
            DatabaseHelper.P("@Fees", json), DatabaseHelper.P("@Reason", string.IsNullOrWhiteSpace(reason.Text) ? "Student setup" : reason.Text.Trim()), DatabaseHelper.Bit("@Preview", expected is null), DatabaseHelper.P("@ExpectedSnapshot", expected), DatabaseHelper.Bit("@AdmissionFeeAlreadyPaid", AdmissionFeeAlreadyPaid));
    }
    private async Task ReviewAsync()
    {
        if (busy) return; Reset(); SetBusy(true);
        try
        {
            // Keep the chosen billing date unchanged; SQL derives its monthly fee period.
            // Preview and activation must use this same date, independently of class history.
            var policy = await DatabaseHelper.QuerySqlAsync("SELECT dbo.GetMonthlyBillingStartDate(@Date) MonthlyStart;", DatabaseHelper.Date("@Date", billing.Value.Date));
            var monthlyStart = Convert.ToDateTime(policy.Rows[0]["MonthlyStart"]);
            var row = (await RunAsync()).Rows[0]; snapshot = Convert.ToString(row["Snapshot"]);
            impact.Text = $"{studentName.Text} · {year.Text}\r\n{placement.Text}\r\nIn this class from {start.Value:dd MMM yyyy}.\r\nBilling starts from {billing.Value:dd MMM yyyy}; monthly fees from {monthlyStart:MMMM yyyy} after applying the school cutoff.\r\nAdmission and selected one-time fees use the billing start date.\r\nAdmission fee: {(AdmissionFeeAlreadyPaid ? "already paid before system — acknowledgement only, no charge or receipt" : "charge now")}.\r\nOpening charges: {row["ChargeCount"]} · Rs {Convert.ToDecimal(row["OpeningTotal"]):N0}.\r\nOnly this student will be activated. Existing unbilled fee assignments will be replaced by the reviewed selection. No earlier monthly charges are generated.\r\nConfirm with Confirm & Activate.";
        }
        catch (Exception ex) { snapshot = null; impact.Text = DbMessage.UserMessage(ex); }
        finally { SetBusy(false); }
    }
    private async Task ActivateAsync()
    {
        if (busy || snapshot is null) return; string expected = snapshot; SetBusy(true);
        try { await RunAsync(expected); Changed = true; StudentSearchCache.Invalidate(); busy = false; DialogResult = DialogResult.OK; Close(); }
        catch (Exception ex) { snapshot = null; impact.Text = DbMessage.UserMessage(ex); }
        finally { if (!IsDisposed) SetBusy(false); }
    }
}
