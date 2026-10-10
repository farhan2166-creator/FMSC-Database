using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Configuration;
internal sealed record SavedFeeScope(int ClassId, string ClassName, int FeeTypeId, string FeeTypeName);
/// <summary>Applies fee-configuration changes to existing students from an effective month.</summary>
public sealed class ApplyFeeConfigurationPage : V2Page
{
    private readonly ModernComboBox campus = HumanUi.Combo();
    private readonly ModernComboBox classBox = HumanUi.Combo();
    private readonly ModernComboBox feeType = HumanUi.Combo();
    private readonly ModernComboBox effectiveFrom = HumanUi.Combo();
    private DateTime EffectiveMonth => effectiveFrom.SelectedValue is DateTime month
        ? month : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
    private readonly ModernTextBox reason = HumanUi.TextBox(false, 500);
    // Stored so preview/apply logic can update the button state.
    private ModernButton applyButton = null!;
    // Stored so preview logic can disable and re-enable the button.
    private ModernButton previewButton = null!;
    private bool previewIsCurrent;
    private readonly int? matrixCampusId;
    private readonly SavedFeeScope[]? matrixScopes;
    private DataTable? previewRows;
    private SavedFeeScope[] previewScopes = Array.Empty<SavedFeeScope>();
    private bool applying;
    private DataTable academicYears = new();
    private DateTime earliestMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private int monthRequest;
    internal bool IsApplying => applying;
    /// <summary>every control that defines which students a preview is for, in one comparable string. The ids come from TryGetScope so the signature cannot drift from the query.</summary>
    private string ScopeSignature()
    {
        TryGetScope(out int campusId, out int classId, out int feeTypeId);
        return string.Join(
            '|',
            campusId,
            classId,
            feeTypeId,
            EffectiveMonth.ToString("yyyy-MM-dd"));
    }
    public ApplyFeeConfigurationPage()
        : this(null, null) { }
    internal ApplyFeeConfigurationPage(int? campusId, SavedFeeScope[]? scopes)
        : base(
            PageTitles.UpdateStudentFeeRates,
            "Review which student fees will change, then choose the month the new rates start.")
    {
        matrixCampusId = campusId;
        matrixScopes = scopes;
        BindMonths(new DataTable());
        previewButton = AddAction("Review Fee Changes", async (_, _) => {
            if (previewButton.Enabled && !applying) await PreviewAsync();
        }, "Secondary");
        /* Apply Saved Rates writes the new fee amount to every already-enrolled student the preview listed. */
        applyButton = AddAction("Apply Saved Rates", async (_, _) => {
            if (applyButton.Enabled && !applying) await ApplyAsync();
        }, "Warning");
        applyButton.Enabled = false;
        SetSaveShortcut(applyButton);
        RelocateFromActions(previewButton, applyButton);
        // Only changes to the affected students or effective month invalidate the preview.
        campus.SelectedIndexChanged += (_, _) => { InvalidatePreview(); LoadClassesForCampus(); _ = RefreshAllowedMonthsAsync(); };
        classBox.SelectedIndexChanged += (_, _) => { InvalidatePreview(); _ = RefreshAllowedMonthsAsync(); };
        feeType.SelectedIndexChanged += (_, _) => { InvalidatePreview(); _ = RefreshAllowedMonthsAsync(); };
        effectiveFrom.SelectedIndexChanged += (_, _) => InvalidatePreview();
        var filters = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = AppMetrics.FieldRowHeight,
            Margin = new Padding(0, 0, 0, 8)
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        filters.Controls.Add(HumanUi.Field("Campus", campus, true), 0, 0);
        filters.Controls.Add(HumanUi.Field("Class", classBox, scopes is null), 1, 0);
        filters.Controls.Add(HumanUi.Field("Fee Type", feeType, scopes is null), 2, 0);
        var applyRow = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = AppMetrics.FieldRowHeight,
            Margin = new Padding(0, 8, 0, 8)
        };
        applyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        applyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
        applyRow.Controls.Add(HumanUi.Field("New Rates Start From", effectiveFrom, true), 0, 0);
        applyRow.Controls.Add(HumanUi.Field("Reason (optional)", reason), 1, 0);
        /* Preview and Apply were removed from the action bar and never placed, so the page had no buttons. */
        var closeButton = new ModernButton {
            Text = UiText.Actions.Close, Width = AppScale.Scale(100), Height = AppMetrics.ButtonHeight, ThemeRole = "Secondary"
        };
        closeButton.Click += (_, _) => { if (!applying) FindForm()?.Close(); };
        var buttonRow = scopes is null ? HumanUi.ButtonRow(previewButton, applyButton)
            : HumanUi.ButtonRow(previewButton, applyButton, closeButton);
        buttonRow.Dock = DockStyle.Fill;
        buttonRow.Margin = new Padding(0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(applyRow, 0, 1);
        root.Controls.Add(buttonRow, 0, 2);
        root.Controls.Add(Grid, 0, 3);
        SetContent(root);
        FeeTypeCatalog.Changed += OnFeeTypesChanged;
        Disposed += (_, _) => FeeTypeCatalog.Changed -= OnFeeTypesChanged;
        Defer(LoadLookupsAsync);
    }
    private void OnFeeTypesChanged() => _ = RefreshFeeTypesAsync();
    private void BindMonths(DataTable years)
    {
        DateTime selected = EffectiveMonth;
        if (selected < earliestMonth) selected = earliestMonth;
        var months = new SortedSet<DateTime> { selected, earliestMonth };
        foreach (DataRow row in years.Rows)
        {
            if (row["StartDate"] is not DateTime start || row["EndDate"] is not DateTime end) continue;
            for (var month = new DateTime(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
                if (month >= earliestMonth) months.Add(month);
        }
        var table = new DataTable();
        table.Columns.Add("Month", typeof(DateTime));
        table.Columns.Add("MonthLabel", typeof(string));
        foreach (var month in months) table.Rows.Add(month, month.ToString("MMMM yyyy"));
        effectiveFrom.DisplayMember = "MonthLabel";
        effectiveFrom.ValueMember = "Month";
        effectiveFrom.DataSource = table;
        effectiveFrom.SelectedValue = selected;
    }
    private async Task RefreshAllowedMonthsAsync()
    {
        int request = ++monthRequest;
        effectiveFrom.Enabled = false;
        previewButton.Enabled = false;
        if (applying || !TryGetScope(out int campusId, out int classId, out int feeTypeId)) return;
        try
        {
            DateTime first = new(DateTime.Today.Year, DateTime.Today.Month, 1);
            var scopes = matrixScopes is null
                ? new[] { new SavedFeeScope(classId, classBox.Text, feeTypeId, feeType.Text) }
                : matrixScopes.Where(s => (classId == 0 || s.ClassId == classId) &&
                    (feeTypeId == 0 || s.FeeTypeId == feeTypeId)).ToArray();
            foreach (var scope in scopes)
            {
                var rows = await DatabaseHelper.QueryUserAsync("GetStudentsAffectedByFeeConfiguration",
                    DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@ClassId", scope.ClassId),
                    DatabaseHelper.Int("@FeeTypeId", scope.FeeTypeId));
                if (IsDisposed || request != monthRequest) return;
                if (!rows.Columns.Contains("LastBilledMonth")) continue;
                foreach (DataRow row in rows.Rows)
                {
                    if (row["LastBilledMonth"] is not DateTime billed) continue;
                    DateTime safeMonth = new DateTime(billed.Year, billed.Month, 1).AddMonths(1);
                    if (safeMonth > first) first = safeMonth;
                }
            }
            earliestMonth = first;
            BindMonths(academicYears);
            effectiveFrom.Enabled = true;
            previewButton.Enabled = effectiveFrom.Enabled;
        }
        catch (Exception ex) { if (!IsDisposed && request == monthRequest) Fail(ex); }
    }
    /// <summary>A fee type was saved on the Fee Types tab: read the list again and keep the operator's choice if it is still there.</summary>
    private async Task RefreshFeeTypesAsync()
    {
        if (matrixScopes is not null) { InvalidatePreview(); return; }
        try
        {
            int selectedFeeTypeId = feeType.SelectedValue == null
                ? 0
                : Convert.ToInt32(feeType.SelectedValue);
            var feeTypes = await DatabaseHelper.QueryUserAsync(
                "GetFeeTypes");
            if (IsDisposed)
                return;
            feeType.DisplayMember = "FeeTypeName";
            feeType.ValueMember = "FeeTypeId";
            feeType.DataSource = feeTypes;
            feeType.SelectedIndex = -1;
            if (selectedFeeTypeId > 0)
                feeType.SelectedValue = selectedFeeTypeId;
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void InvalidatePreview()
    {
        previewIsCurrent = false;
        applyButton.Enabled = false;
    }
    private async Task LoadLookupsAsync()
    {
        try
        {
            Task<DataTable> campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            Task<DataTable> yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            Task<DataTable> feeTypesTask = DatabaseHelper.QueryUserAsync(
                "GetFeeTypes");
            await Task.WhenAll(campusesTask, feeTypesTask, yearsTask);
            if (IsDisposed) return;
            academicYears = await yearsTask;
            BindMonths(academicYears);
            var campuses = await campusesTask;
            campus.DisplayMember = "CampusName";
            campus.ValueMember = "CampusId";
            campus.DataSource = campuses;
            campus.SelectedIndex = -1;
            if (matrixCampusId is > 0)
            {
                campus.SelectedValue = matrixCampusId.Value;
                campus.Enabled = false;
            }
            var feeTypes = await feeTypesTask;
            feeType.DisplayMember = "FeeTypeName";
            feeType.ValueMember = "FeeTypeId";
            feeType.DataSource = feeTypes;
            feeType.SelectedIndex = -1;
            if (matrixScopes is not null)
            {
                var table = new DataTable();
                table.Columns.Add("FeeTypeId", typeof(int));
                table.Columns.Add("FeeTypeName", typeof(string));
                table.Rows.Add(0, "All saved fee types");
                foreach (var scope in matrixScopes.DistinctBy(s => s.FeeTypeId))
                    table.Rows.Add(scope.FeeTypeId, scope.FeeTypeName);
                feeType.DataSource = table;
                feeType.SelectedValue = 0;
                effectiveFrom.Focus();
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void LoadClassesForCampus()
    {
        try
        {
            classBox.DataSource = null;
            if (campus.SelectedValue == null)
            {
                classBox.Enabled = false;
                return;
            }
            int campusId = Convert.ToInt32(campus.SelectedValue);
            var assignments = DatabaseHelper.QueryUser(
                "GetClassSections",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Int("@ClassId", 0),
                DatabaseHelper.Int("@SectionId", 0));
            var classes = assignments.AsEnumerable()
                .Select(r => (
                    ClassId: Convert.ToInt32(r["ClassId"]),
                    ClassName: Convert.ToString(r["ClassName"]) ?? ""))
                .Distinct()
                .ToList();
            var table = new DataTable();
            table.Columns.Add("ClassId", typeof(int));
            table.Columns.Add("ClassName", typeof(string));
            if (matrixScopes is not null) table.Rows.Add(0, "All saved classes");
            foreach (var c in classes)
                if (matrixScopes is null || matrixScopes.Any(s => s.ClassId == c.ClassId))
                    table.Rows.Add(c.ClassId, c.ClassName);
            classBox.DisplayMember = "ClassName";
            classBox.ValueMember = "ClassId";
            classBox.DataSource = table;
            classBox.SelectedIndex = -1;
            if (matrixScopes is not null) classBox.SelectedValue = 0;
            classBox.Enabled = table.Rows.Count > 0;
        }
        catch (Exception ex) { Fail(ex); }
    }
    private bool TryGetScope(out int campusId, out int classId, out int feeTypeId)
    {
        campusId = campus.SelectedValue == null ? 0 : Convert.ToInt32(campus.SelectedValue);
        classId = classBox.SelectedValue == null ? 0 : Convert.ToInt32(classBox.SelectedValue);
        feeTypeId = feeType.SelectedValue == null ? 0 : Convert.ToInt32(feeType.SelectedValue);
        return campusId > 0 && (matrixScopes is not null || classId > 0 && feeTypeId > 0);
    }
    // PreviewAsync re-queries and rebuilds the grid that ApplyAsync's Enabled decision (Step 3) depends on.
    private async Task PreviewAsync()
    {
        InvalidatePreview();
        previewButton.Enabled = false;
        SetStatus("Loading affected student fees…", PageStatusKind.Busy);
        try
        {
            try
            {
                if (!TryGetScope(out int campusId, out int classId, out int feeTypeId))
                {
                    Warn("Select a campus, class and fee type first.");
                    return;
                }
                /* The scope this preview is run for. previewIsCurrent is a plain boolean that InvalidatePreview clears the instant a scope control changes. */
                string requestedScope = ScopeSignature();
                var scopes = matrixScopes is null
                    ? new[] { new SavedFeeScope(classId, classBox.Text, feeTypeId, feeType.Text) }
                    : matrixScopes.Where(s => (classId == 0 || s.ClassId == classId) &&
                        (feeTypeId == 0 || s.FeeTypeId == feeTypeId)).ToArray();
                DataTable? rows = null;
                foreach (var scope in scopes)
                {
                    var table = await DatabaseHelper.QueryUserAsync("GetStudentsAffectedByFeeConfiguration",
                        DatabaseHelper.Int("@CampusId", campusId), DatabaseHelper.Int("@ClassId", scope.ClassId),
                        DatabaseHelper.Int("@FeeTypeId", scope.FeeTypeId));
                    if (!table.Columns.Contains("FeeTypeName")) table.Columns.Add("FeeTypeName", typeof(string));
                    foreach (DataRow row in table.Rows) row["FeeTypeName"] = scope.FeeTypeName;
                    if (rows is null) rows = table.Copy(); else rows.Merge(table);
                }
                if (IsDisposed ||
                    !string.Equals(requestedScope, ScopeSignature(), StringComparison.Ordinal))
                {
                    return;
                }
                previewRows = rows ?? new DataTable();
                await AddFatherNamesAsync(previewRows);
                AddDisplayColumns(previewRows);
                if (IsDisposed || !string.Equals(requestedScope, ScopeSignature(), StringComparison.Ordinal)) return;
                previewScopes = scopes;
                await BindReportAsync(() => Task.FromResult(previewRows));
                if (IsDisposed || !string.Equals(requestedScope, ScopeSignature(), StringComparison.Ordinal)) return;
                ConfigureGrid();
                previewIsCurrent = true;
                // A single static MinDate can't safely represent every affected student in a bulk operation (each has its own last-billed month).
                int conflicting = CountConflictingRows(EffectiveMonth);
                string baseMessage = previewRows.Rows.Count == 0
                    ? "No enrolled students are on an amount different from the current configuration - nothing to apply."
                    : $"{previewRows.Rows.Count} student fee assignment(s) differ from the saved rates.";
                if (conflicting > 0)
                {
                    baseMessage += conflicting == previewRows.Rows.Count
                        ? $" Bills already exist for all {conflicting:N0} fee changes in {EffectiveMonth:MMMM yyyy} or later. Choose a later start month."
                        : $" {conflicting:N0} fee changes will remain unchanged because bills already exist in {EffectiveMonth:MMMM yyyy} or later. Choose a later start month to include them.";
                }
                SetStatus(baseMessage, conflicting > 0);
                // this is the ONLY place Apply is ever re-enabled - a fresh, successful preview, for the exact filters currently selected…
                applyButton.Enabled = previewRows.Rows.Count > 0 && conflicting < previewRows.Rows.Count;
            }
            catch (Exception ex) { Fail(ex); }
        }
        finally
        {
            previewButton.Enabled = effectiveFrom.Enabled;
        }
    }
    /// <summary>counts how many rows in the current preview grid already have a billed charge on or after the given month.</summary>
    private int CountConflictingRows(DateTime effectiveFromValue)
    {
        if (previewRows is null || !previewRows.Columns.Contains("LastBilledMonth"))
            return 0;
        DateTime normalizedEffectiveFrom = new(effectiveFromValue.Year, effectiveFromValue.Month, 1);
        int conflicts = 0;
        foreach (DataRow row in previewRows.Rows)
        {
            object value = row["LastBilledMonth"];
            if (value == null || value == DBNull.Value)
                continue;
            DateTime lastBilledMonth = Convert.ToDateTime(value);
            DateTime normalizedLastBilledMonth = new(lastBilledMonth.Year, lastBilledMonth.Month, 1);
            if (normalizedEffectiveFrom <= normalizedLastBilledMonth)
                conflicts++;
        }
        return conflicts;
    }
    private static async Task AddFatherNamesAsync(DataTable rows)
    {
        if (!rows.Columns.Contains("FatherName")) rows.Columns.Add("FatherName", typeof(string));
        if (rows.Rows.Count == 0) return;
        var ids = rows.AsEnumerable().Select(r => Convert.ToInt32(r["EnrollmentId"])).Distinct().ToArray();
        int userId = FMSC_SMS.Pages.Security.CurrentUser.UserId;
        var names = await Task.Run(() => DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
            SELECT E.EnrollmentId, S.FatherName
            FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
            JOIN OPENJSON(@EnrollmentIds) WITH (EnrollmentId int '$') I ON I.EnrollmentId = E.EnrollmentId;
            """, DatabaseHelper.Int("@UserId", userId),
            DatabaseHelper.P("@EnrollmentIds", System.Text.Json.JsonSerializer.Serialize(ids))));
        var byEnrollment = names.AsEnumerable().ToDictionary(r => Convert.ToInt32(r["EnrollmentId"]), r => r["FatherName"]);
        foreach (DataRow row in rows.Rows)
            row["FatherName"] = byEnrollment.GetValueOrDefault(Convert.ToInt32(row["EnrollmentId"]), DBNull.Value);
    }
    private static void AddDisplayColumns(DataTable rows)
    {
        foreach (string column in new[] { "StudentDetails", "ClassSection" })
            if (!rows.Columns.Contains(column)) rows.Columns.Add(column, typeof(string));
        string Text(DataRow row, string column) => rows.Columns.Contains(column) ? Convert.ToString(row[column])?.Trim() ?? "" : "";
        foreach (DataRow row in rows.Rows)
        {
            string student = Text(row, "StudentName"), father = Text(row, "FatherName");
            row["StudentDetails"] = father.Length == 0 ? student : $"{student} s/d/o {father}";
            string className = Text(row, "ClassName"), section = Text(row, "SectionName");
            row["ClassSection"] = string.Join(" - ", new[] { className, section }.Where(value => value.Length > 0));
        }
    }
    private void ConfigureGrid()
    {
        if (Grid.Columns.Count == 0)
            return;
        HideColumn("StudentFeeAssignmentId");
        HideColumn("EnrollmentId");
        HideColumn("LastBilledMonth"); // Validation-only column; hidden from the grid.
        HideColumn("CampusName");
        HideColumn("RegistrationNumber");
        HideColumn("StudentName");
        HideColumn("FatherName");
        HideColumn("ClassName");
        HideColumn("SectionName");
        // Admission number, student and father identify the student within the selected campus.
        ShowColumn("AdmissionNumber", "Admission Number", 115);
        SetHeader("StudentDetails", "Student", 300);
        SetHeader("ClassSection", "Class - Section", 120);
        SetHeader("FeeTypeName", "Fee Type", 140);
        SetHeader("CurrentAmount", "Current Amount", 130);
        SetHeader("NewAmount", "New Amount", 130);
        SetHeader("CurrentFrequency", "Current Frequency", 130);
        SetHeader("ConfiguredFrequency", "New Frequency", 130);
        SetHeader("AssignmentEffectiveFrom", "Current Rate From", 150);
        if (Grid.Columns.Contains("AssignmentEffectiveFrom"))
            Grid.Columns["AssignmentEffectiveFrom"].DefaultCellStyle.Format = "MMMM yyyy";
        string[] order = { "AdmissionNumber", "StudentDetails", "ClassSection", "FeeTypeName", "CurrentAmount", "NewAmount", "CurrentFrequency", "ConfiguredFrequency", "AssignmentEffectiveFrom" };
        int index = 0;
        foreach (string name in order)
        {
            if (!Grid.Columns.Contains(name)) continue;
            var column = Grid.Columns[name];
            column.DisplayIndex = index++;
            column.MinimumWidth = name == "StudentDetails" ? 240 : name == "ClassSection" ? 100 : 90;
            column.FillWeight = column.Width;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        }
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }
    private void HideColumn(string columnName)
    {
        if (Grid.Columns.Contains(columnName))
            Grid.Columns[columnName].Visible = false;
    }
    private void ShowColumn(string columnName, string header, int width)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        Grid.Columns[columnName].Visible = true;
        SetHeader(columnName, header, width);
        Grid.Columns[columnName].DisplayIndex = 0;
    }
    private void SetHeader(string columnName, string header, int width)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        Grid.Columns[columnName].HeaderText = header;
        Grid.Columns[columnName].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        Grid.Columns[columnName].Width = width;
    }
    // ApplyAsync writes via ApplyFeeConfigurationToExistingAssignments and is only ever meant to be enabled by a fresh Preview (Step 3's own state machine).
    private async Task ApplyAsync()
    {
        if (applying || !ValidateRequiredInputs())
            return;
        applyButton.Enabled = false;
        try
        {
            try
            {
                if (!TryGetScope(out int campusId, out int classId, out int feeTypeId))
                {
                    Warn("Select a campus, class and fee type first.");
                    return;
                }
                if (!previewIsCurrent)
                {
                    Warn("Choose Review Fee Changes first. Review again if you change the campus, class, fee type or start month.");
                    return;
                }
                if (previewRows is null || previewRows.Rows.Count == 0)
                {
                    Warn("There is nothing to apply - no students differ from the current configuration.");
                    return;
                }
                int affectedCount = previewRows.Rows.Count;
                // block outright only when the chosen month is guaranteed to fail for every previewed student (the database would reject each one individually via Step 1's guard).
                int conflictingCount = CountConflictingRows(EffectiveMonth);
                if (conflictingCount > 0 && conflictingCount == affectedCount)
                {
                    Warn(
                        $"Bills already exist for all {affectedCount:N0} fee changes in {EffectiveMonth:MMMM yyyy} or later. " +
                        "Choose a later start month and review the changes again. Existing bills and payments will not be changed.");
                    return;
                }
                /* One unmistakable statement of the financial scope before the write: what it applies to, when it takes effect, who it affects, how many are expected to succeed and how many… */
                string previewMessage =
                    "Update the student fees listed in your review?\r\n\r\n" +
                    $"Campus: {campus.Text}\r\n" +
                    $"Class: {classBox.Text}\r\n" +
                    $"Fee type: {feeType.Text}\r\n" +
                    $"Effective from: {EffectiveMonth:MMMM yyyy}\r\n" +
                    (string.IsNullOrWhiteSpace(reason.Text) ? "\r\n" : $"Reason: {reason.Text.Trim()}\r\n\r\n") +
                    $"Fee changes reviewed: {affectedCount:N0}\r\n" +
                    $"Ready to update: {affectedCount - conflictingCount:N0}\r\n" +
                    (conflictingCount > 0
                        ? $"Will remain unchanged: {conflictingCount:N0} - bills already exist for these fees in " +
                          "the chosen month or a later month. Choose a later start month to include them.\r\n\r\n"
                        : "\r\n") +
                    "The New Amount column shows the rates to apply. Existing fee bills and payments will not change. " +
                    "The new rates are used when future fees are posted, starting from the chosen month.";
                if (!Confirm(previewMessage, "Confirm Student Fee Changes", defaultYes: false))
                    return;
                /* everything the write sends and everything the result reports is captured here, before the await. */
                DateTime appliedEffectiveFrom = new(EffectiveMonth.Year, EffectiveMonth.Month, 1);
                string enteredReason = reason.Text.Trim();
                string appliedReason = enteredReason.Length == 0
                    ? "Applied saved fee configuration rates."
                    : enteredReason;
                string appliedFeeTypeName = feeType.Text;
                string appliedCampusName = campus.Text;
                string appliedClassName = classBox.Text;
                int total = 0, succeeded = 0, failed = 0;
                var failures = new DataTable();
                var scopesToApply = previewScopes.ToArray();
                applying = true;
                SetStatus("Applying saved rates to existing student fees…", PageStatusKind.Busy);
                previewButton.Enabled = false;
                classBox.Enabled = feeType.Enabled = effectiveFrom.Enabled = reason.Enabled = false;
                previewIsCurrent = false;
                foreach (var scope in scopesToApply)
                {
                    DataSet result = await DatabaseHelper.QueryUserSetAsync(
                        "ApplyFeeConfigurationToExistingAssignments",
                        DatabaseHelper.Int("@CampusId", campusId),
                        DatabaseHelper.Int("@ClassId", scope.ClassId),
                        DatabaseHelper.Int("@FeeTypeId", scope.FeeTypeId),
                        DatabaseHelper.Date("@EffectiveFrom", appliedEffectiveFrom),
                        DatabaseHelper.P("@Reason", appliedReason));
                    var outcome = FeeRateApplyResult.Read(result);
                    total += outcome.Total;
                    succeeded += outcome.Succeeded;
                    failed += outcome.Failed;
                    failures.Merge(outcome.Failures);
                }
                applyButton.Enabled = false;
                SetStatus(
                    $"Apply complete: {succeeded:N0} of {total:N0} fee assignment(s) updated" +
                    (failed > 0 ? $", {failed:N0} failed." : "."),
                    failed > 0 ? PageStatusKind.Warning : PageStatusKind.Success);
                string resultMessage =
                    $"Applied saved rates for {appliedCampusName} / {appliedClassName} / {appliedFeeTypeName}, " +
                    $"effective {appliedEffectiveFrom:MMMM yyyy}.\n\n" +
                    $"Total: {total:N0}\nSucceeded: {succeeded:N0}\nFailed: {failed:N0}";
                if (failed > 0)
                {
                    const int maxListed = 15;
                    var lines = failures.AsEnumerable()
                        .Take(maxListed)
                        .Select(r => $"  - Assignment {r["StudentFeeAssignmentId"]}: {r["ErrorMessage"]}");
                    resultMessage += "\n\nFailures:\n" + string.Join("\n", lines);
                    if (failures.Rows.Count > maxListed)
                        resultMessage += $"\n  ... and {failures.Rows.Count - maxListed:N0} more.";
                }
                ModernMessageDialog.Show(
                    FindForm(),
                    resultMessage,
                    PageTitles.UpdateStudentFeeRates,
                    MessageBoxButtons.OK,
                    failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                /* only clear the reason that was actually sent. A reason typed while the write was in flight belongs to the operator. */
                if (string.Equals(reason.Text.Trim(), enteredReason, StringComparison.Ordinal))
                    reason.Clear();
                // Re-preview so the grid reflects what just happened (students that succeeded should no longer appear as affected).
                await PreviewAsync();
            }
            catch (Exception ex) { Fail(ex); }
        }
        finally
        {
            applying = false;
            previewButton.Enabled = true;
            classBox.Enabled = feeType.Enabled = effectiveFrom.Enabled = reason.Enabled = true;
            applyButton.Enabled = previewIsCurrent
                && previewRows is not null && previewRows.Rows.Count > 0
                && CountConflictingRows(EffectiveMonth) < previewRows.Rows.Count;
            if (!IsDisposed && !FormFlow.HasInputFocus(this)) effectiveFrom.Focus();
        }
    }
}


