using FMSC_SMS.Core;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class CollegeLeaveCertificatePage : V2Page, IUnsavedChanges
{
    internal override bool FocusEntryField() => CertificateUi.FocusEntry(student, Grid, leave);
    private const string CertificateTypeName = "College";
    private const int MaxDuplicateIssues = 2;
    private readonly StudentPicker student = new();
    private readonly CertificateCollectionFlow copyFlow = new("Leave");
    private string? draftBaseline;
    private string DraftSnapshot()=>System.Text.Json.JsonSerializer.Serialize(new {Date=leave.ShowCheckBox&&!leave.Checked?(DateTime?)null:leave.Value.Date,Confirmed=leave.Checked,Promoted=promoted.SelectedId,Academic=academic.Text.Trim(),Attendance=attendance.Text.Trim(),Remarks=remarks.Text.Trim()});
    private void CaptureDraftBaseline()=>draftBaseline=DraftSnapshot();
    public bool HasUnsavedChanges=>student.SelectedId>0&&selectedLeaveRecordId==0&&!HasCertificateForCurrentEnrollment()&&draftBaseline is not null&&draftBaseline!=DraftSnapshot();
    public string UnsavedChangesMessage=>"The new certificate has unsaved details. Discard this draft?";
    private bool ConfirmDraftDiscard()=>!HasUnsavedChanges||Confirm(UnsavedChangesMessage,UiText.Dialogs.UnsavedChanges);
    private readonly LeaveFeeReviewBar feeReview;
    private readonly ModernTextBox currentClass =
        HumanUi.TextBox(false, 150);
    private readonly LookupBox promoted = new();
    private readonly ModernTextBox admissionDateDisplay = HumanUi.TextBox(false, 150);
    private readonly ModernTextBox dateOfBirthDisplay = HumanUi.TextBox(false, 150);
    private DateTime? currentAdmissionDate;
    private DateTime? currentDateOfBirth;
    private bool HasRequiredStudentDates => currentAdmissionDate.HasValue && currentDateOfBirth.HasValue;
    private readonly ModernTextBox academic =
        HumanUi.TextBox(false, 200);
    private readonly ModernTextBox attendance =
        HumanUi.TextBox(false, 200);
    private readonly ModernTextBox remarks =
        HumanUi.TextBox(false, 500);
    private readonly ModernDatePicker leave =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    private int currentClassId;
    /* Finding 4 / BD-21: duplicate checks use this leaving episode, not all prior certificates. */
    private int currentEnrollmentId;
    /* Finding 7 - true only when the leave date on screen came from the student's recorded withdrawal date. When it did not, the date is a deliberate choice. */
    private bool leaveDatePrefilled;
    /* Finding 13 - a search debounce tick that lands while a modal (confirmation, collector prompt, print preview) is open runs inside that modal's message loop and re-binds the picker underneath… */
    private bool suppressStudentChanged;
    private int selectedLeaveRecordId;
    private int selectedIssueId;
    private int selectedCertificateAdmissionId;
    private bool selectedIssueCollected;
    private readonly ModernButton createCertificateButton = new() {
        SavesChanges = true, Text = UiText.Certificates.Create,
        Width = AppScale.Scale(170)
    };
    private readonly ModernButton clearButton = new() {
        Text = UiText.Certificates.New,
        Width = AppScale.Scale(120)
    };
    private readonly ModernButton issueAgainButton = new() {
        Text = UiText.Certificates.IssueAgainAction,
        Width = AppScale.Scale(140)
    };
    private readonly ModernButton collectButton = new() {
        Text = UiText.Certificates.MarkCollected,
        Width = AppScale.Scale(150)
    };
    private readonly ModernButton printButton = new() {
        Text = UiText.Actions.Print,
        Width = AppScale.Scale(120)
    };
    private readonly ModernButton refreshButton = new() {
        Text = UiText.Actions.Refresh,
        Width = AppScale.Scale(120)
    };
    public CollegeLeaveCertificatePage() : this(true) { }
    internal CollegeLeaveCertificatePage(bool initialize)
        : base("College Leaving Certificate", "Create, issue, collect and print leaving certificates for college students.")
    {
        RecordTextPolicy.WireInput(remarks,"@LeavingReason");
        feeReview = new LeaveFeeReviewBar(() => student.SelectedId, () => currentEnrollmentId, () => leave.Value.Date, () => HasCertificateForCurrentEnrollment());
        leave.ValueChanged += (_, _) => feeReview.RefreshFees();
        ConfigureFieldSuggestions();
        academic.TextChanged+=(_,_)=>ApplyWorkflowState();attendance.TextChanged+=(_,_)=>ApplyWorkflowState();remarks.TextChanged+=(_,_)=>ApplyWorkflowState();promoted.SelectedIndexChanged+=(_,_)=>ApplyWorkflowState();
        copyFlow.Wire(this,()=>selectedIssueId,id=>{LoadCertificates(student.SelectedId);SelectSavedCertificate(id);},()=>draftBaseline!=DraftSnapshot());
        createCertificateButton.Click +=
            (_, _) => CreateCertificate();
        clearButton.Click +=
            (_, _) => ClearForm();
        issueAgainButton.Click +=
            (_, _) => IssueAgain();
        collectButton.Click +=
            (_, _) => MarkCollected();
        printButton.Click +=
            (_, _) => PrintSelected();
        refreshButton.Click +=
            (_, _) => { RefreshCertificates(); feeReview.RefreshFees(); };
        /* every write AND every read on this page goes to a procedure that requires Students.Leave (CreateLeaveCertificate, IssueLeaveCertificate, MarkLeaveCertificateCollected, GetLeaveCertificateRecords, GetLeaveCertificatePrintData). Only Clear stays ungated - it touches nothing. */
        GateAction(createCertificateButton, "Students.Leave");
        GateAction(issueAgainButton, "Students.Leave");
        GateAction(collectButton, "Students.Leave");
        GateAction(printButton, "Students.Leave");
        GateAction(refreshButton, "Students.Leave");
        RelocateFromActions(createCertificateButton, clearButton);
        currentClass.ReadOnly = true;
        currentClass.TabStop = false;
        admissionDateDisplay.ReadOnly = dateOfBirthDisplay.ReadOnly = true;
        admissionDateDisplay.TabStop = dateOfBirthDisplay.TabStop = false;
        TrackStudentSearchStatus(student);
        /* The shared student finder (StudentPicker): the list narrows on every keystroke and nothing is chosen until the operator clicks a row, presses Enter on a highlighted row, or types an… */
        if(initialize)StudentSearchCache.Warm();
        student.SelectionChangeGuard=_=>ConfirmDraftDiscard();
        /* Only college students are offered (the class's academic program is College). */
        student.RowFilter = row =>
            row.Table.Columns.Contains("ProgramType") &&
            !row.IsNull("ProgramType") &&
            string.Equals(Convert.ToString(row["ProgramType"]), "College", StringComparison.OrdinalIgnoreCase);
        student.PlaceholderText = StudentPicker.SearchPlaceholder;
        student.UserSelectionChanged +=
            (_, _) => {
                if (suppressStudentChanged)
                    return;
                StudentChanged();
            };
        student.SelectionCleared +=
            (_, _) => {
                if (suppressStudentChanged)
                    return;
                StudentChanged();
            };
        Grid.CellMouseClick +=
            Grid_CellMouseClick;
        Defer(
            () => {
                ResetToStart();
            });
        Defer(LoadFieldSuggestions);
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "Create for a college student without a certificate. Creation marks the student Left and records the first issue.");
        var fields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 5; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
        fields.Controls.Add(
            CertificateUi.Field("Student", student, true), 0, 0);
        fields.Controls.Add(
            CertificateUi.Field("Leave Date", leave, true), 1, 0);
        fields.Controls.Add(CertificateUi.Field("Admission Date", admissionDateDisplay), 0, 1);
        fields.Controls.Add(CertificateUi.Field("Date of Birth", dateOfBirthDisplay), 1, 1);
        fields.Controls.Add(
            CertificateUi.Field("Current Class", currentClass, true), 0, 2);
        fields.Controls.Add(
            CertificateUi.Field("Class Promoted To", promoted, true), 1, 2);
        fields.Controls.Add(
            CertificateUi.Field("Academic Record", academic, true), 0, 3);
        fields.Controls.Add(
            CertificateUi.Field("Attendance Record", attendance, true), 1, 3);
        fields.Controls.Add(
            CertificateUi.Field("Remarks", remarks, true), 0, 4);
        details.Content.Controls.Add(fields);
        fields.Controls.Add(feeReview, 1, 4);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, clearButton);
        certButtonRow.Dock =
            DockStyle.Bottom;
        details.Content.Controls.Add(certButtonRow);
        var records =
            HumanUi.Card(
                UiText.Certificates.IssuanceHistory,
                UiText.Certificates.IssueHistoryGuidance);
        var certificateActions =
            CertificateButtonRow.Create(printButton, collectButton, issueAgainButton, refreshButton);
        certificateActions.Dock =
            DockStyle.Bottom;
        records.Content.Controls.Add(certificateActions);
        records.Content.Controls.Add(Grid);
        CertificateUi.WireNavigation(this, student, Grid, clearButton, fields, certButtonRow, certificateActions,
            index => Grid_CellMouseClick(Grid, new DataGridViewCellMouseEventArgs(0,index,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))));
        Grid.BringToFront();
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(5)
                + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(records, 0, 1);
        CertificateUi.CompactFields(details,fields,root,new[]{40f,20f,20f,20f},new[]{(0,0,1),(1,0,1),(0,1,2),(2,1,2),(2,0,1),(3,0,1),(0,2,2),(2,2,2),(0,3,2),(2,3,2)},1200);
        SetContent(
            root,
            "Select a college student to create. Then issue again, mark collected or print.");
        ApplyWorkflowState();
    }
    private void StudentChanged()
    {
        if (suppressStudentChanged)
            return;
        try
        {
            ClearCertificateSelection();
            ClearCertificateFields();
            ClearStudentDetails();
            int admissionId =
                student.SelectedId;
            if (admissionId <= 0)
            {
                certificateResults =
                    new DataTable();
                Bind(certificateResults);
                FormatCertificateGrid();
                ApplyWorkflowState();
                return;
            }
            DataRow[] matches =
                studentResults.Select($"AdmissionId = {admissionId}");
            if (matches.Length == 0)
            {
                ApplyWorkflowState();
                return;
            }
            DataRow row =
                matches[0];
            LoadStudentDates(admissionId);
            currentClassId =
                GetInt(row, "ClassId");
            // Finding 4 / BD-21: enrollment for the new leaving episode.
            currentEnrollmentId =
                GetInt(row, "EnrollmentId");
            currentClass.Text =
                GetString(row, "ClassName");
            if (string.IsNullOrWhiteSpace(currentClass.Text))
            {
                currentClass.Text =
                    "Not assigned";
            }
            BindPromotionClasses();
            /* Finding 5 - ported from the school Leave page. */
            if (string.Equals(GetString(row, "Status"), "Left", StringComparison.OrdinalIgnoreCase))
            {
                leaveDatePrefilled =
                    TryPrefillLeaveDateFromExistingWithdrawal(admissionId);
                /* The retroactive branch of dbo.CreateLeaveCertificate WRITES this date onto the student and the enrollment, so today must not go in as though it had been found. */
                if (!leaveDatePrefilled)
                {
                    leave.ShowCheckBox = true;
                    leave.Checked = false;
                }
            }
            LoadCertificates(admissionId);
            feeReview.RefreshFees();
            CaptureDraftBaseline();
            CertificateUi.FocusHistory(Grid, () => SelectSavedCertificate(0), leave);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally{ApplyWorkflowState();CaptureDraftBaseline();}
    }
    /// <summary>Finding 5 - ported from the school Leave page.</summary>
    private bool TryPrefillLeaveDateFromExistingWithdrawal(int admissionId)
    {
        try
        {
            DataTable snapshot = DatabaseHelper.QueryUser("GetStudentDetails", DatabaseHelper.Int("@AdmissionId", admissionId));
            if (snapshot.Rows.Count == 0)
                return false;
            DataRow snapshotRow = snapshot.Rows[0];
            if (snapshotRow.Table.Columns.Contains("WithdrawalDate") &&
                snapshotRow["WithdrawalDate"] != DBNull.Value)
            {
                leave.Value = Convert.ToDateTime(snapshotRow["WithdrawalDate"]);
                return true;
            }
            return false;
        }
        catch (Exception nonFatalException58)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException58, "CollegeLeaveCertificatePage: TryPrefillLeaveDateFromExistingWithdrawal fallback");
            // Non-critical - the operator chooses the date himself; the caller makes that an explicit choice rather than leaving today in place.
            return false;
        }
    }
    private bool IsSelectedStudentLeft()
    {
        if (student.SelectedId <= 0)
            return false;
        DataRow[] matches =
            studentResults.Select($"AdmissionId = {student.SelectedId}");
        if (matches.Length == 0)
            return false;
        return string.Equals(
            GetString(matches[0], "Status"), "Left", StringComparison.OrdinalIgnoreCase);
    }
    private void BindPromotionClasses()
    {
        try
        {
            promoted.ClearLookup();
            if (currentClassId <= 0)
                return;
            DataTable raw =
                DatabaseHelper.QueryUser(
                    "GetLeaveCertificatePromotionClasses",
                    DatabaseHelper.Int("@CurrentClassId", currentClassId));
            var dt =
                new DataTable();
            dt.Columns.Add("ClassId", typeof(int));
            dt.Columns.Add("Display", typeof(string));
            dt.Columns.Add("ClassName", typeof(string));
            foreach (DataRow row in raw.Rows)
            {
                int classId =
                    GetInt(row, "ClassId");
                string className =
                    GetString(row, "ClassName");
                string classCode =
                    GetString(row, "ClassCode");
                string display =
                    string.IsNullOrWhiteSpace(classCode)
                        ? className
                        : $"{className} — {classCode}";
                dt.Rows.Add(classId, display, className);
            }
            promoted.Bind(dt, "ClassId", "Display");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void LoadCertificates(int admissionId)
    {
        try
        {
            ClearCertificateSelection();
            certificateResults =
                new DataTable();
            if (admissionId <= 0)
            {
                Bind(certificateResults);
                FormatCertificateGrid();
                ApplyWorkflowState();
                return;
            }
            DataTable table =
                DatabaseHelper.QueryUser(
                    "GetLeaveCertificateRecords",
                    DatabaseHelper.Int(
                        "@AdmissionId",
                        admissionId),
                    DatabaseHelper.P("@CertificateType", CertificateTypeName));
            CertificateUi.FormatLeavingReasons(table);
            certificateResults = CertificateRecordOrder.NewestFirst(table, "LeaveCertificateIssueId");
            Bind(certificateResults);
            FormatCertificateGrid();
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException59)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException59, "CollegeLeaveCertificatePage: LoadCertificates fallback");
            }
            ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            Fail(ex);
            /* Finding 11 - the load failed, so the grid must not keep showing the previous student's history, and the buttons must be recomputed for the empty list. */
            certificateResults =
                new DataTable();
            Bind(certificateResults);
            FormatCertificateGrid();
            ApplyWorkflowState();
        }
    }
    private void SelectSavedCertificate(int id)
    {

        CertificateRecordOrder.Select(Grid, "LeaveCertificateIssueId", id,
            index => Grid_CellMouseClick(Grid, new DataGridViewCellMouseEventArgs(0,index,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))));
    }
    private void FormatCertificateGrid()
    {
        CertificateRecordOrder.ArrangeColumns(Grid, "LeaveCertificateIssueId");
        /* The click handlers below read the row from certificateResults by the clicked row INDEX, which only matches the grid while it is in the table's own order. */
        LockGridOrder();
        SetGridWidth("LeaveRecordId", 80);
        SetGridWidth("LeaveCertificateIssueId", 95);
        SetGridWidth("AdmissionNumber", 80);
        SetGridWidth("StudentName", 150);
        SetGridWidth("FatherName", 130);
        SetGridWidth("ClassStudied", 110);
        SetGridWidth("ClassPromotedTo", 120);
        SetGridWidth("LeaveDate", 95);
        SetGridWidth("IssuedAt", 145);
        SetGridWidth("IssuedByUserId", 90);
        SetGridWidth("IssuedBy", 130);
        SetGridWidth("CollectionStatus", 115);
        SetGridWidth("CollectedAt", 145);
        SetGridWidth("CollectedBy", 150);
        SetGridWidth("IssueType", 95);
        HideGridColumn("AdmissionId");
        HideGridColumn("IssuedByUserId");
        HideGridColumn("LeaveRecordId");
        HideGridColumn("CertificateType");
        // Finding 4: retain the hidden episode key for duplicate checks.
        HideGridColumn("EnrollmentId");
        CertificateCollectionFlow.FormatGrid(Grid);
        CertificateRecordOrder.FitColumns(Grid);
    }
    private void HideGridColumn(string columnName)
    {
        if (!Grid.Columns.Contains(columnName))
        {
            return;
        }
        Grid.Columns[columnName].Visible =
            false;
    }
    private void Grid_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if(e.RowIndex>=0&&!ConfirmDraftDiscard()){Grid.ClearSelection();Grid.CurrentCell=null;return;}
        if (e.Button != MouseButtons.Left ||
            e.RowIndex < 0 ||
            e.RowIndex >= certificateResults.Rows.Count)
        {
            return;
        }
        try
        {
            DataGridViewRow gridRow =
                Grid.Rows[e.RowIndex];
            if (gridRow.IsNewRow)
                return;
            Grid.ClearSelection();
            gridRow.Selected = true;
            DataRow row =
                certificateResults.Rows[
                    e.RowIndex];
            int leaveRecordId =
                GetInt(row, "LeaveRecordId");
            int issueId =
                GetInt(row, "LeaveCertificateIssueId");
            int admissionId =
                GetInt(row, "AdmissionId");
            if (leaveRecordId <= 0 ||
                issueId <= 0 ||
                admissionId <= 0)
            {
                ClearCertificateSelection();
                SetStatus("The selected certificate issuance contains invalid data.", true);
                return;
            }
            selectedLeaveRecordId =
                leaveRecordId;
            selectedIssueId =
                issueId;
            selectedCertificateAdmissionId =
                admissionId;
            selectedIssueCollected =
                string.Equals(
                    GetString(row, "CollectionStatus"), "Collected", StringComparison.OrdinalIgnoreCase);
            leave.Value=GetDate(row,"LeaveDate")??leave.Value;
            currentClass.Text=GetString(row,"ClassStudied");
            academic.Text=GetString(row,"AcademicRecord");attendance.Text=GetString(row,"AttendanceRecord");remarks.Text=GetString(row,"Remarks");
            SetStudentDates(row);
            CertificateCollectionFlow.SelectPromotion(promoted,GetString(row,"ClassPromotedTo"));
            CaptureDraftBaseline();ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            ClearCertificateSelection();
            SetStatus($"Unable to read certificate issuance: {DbMessage.UserMessage(ex)}", true);
        }
    }
    private void ApplyWorkflowState()
    {
        bool hasStudent =
            student.SelectedId > 0;
        bool hasCertificate =
            certificateResults.Rows.Count > 0;
        bool hasCertificateForThisLeaving =
            HasCertificateForCurrentEnrollment();
        bool hasSelectedIssue =
            selectedIssueId > 0;
        bool studentIsActive =
            IsSelectedStudentActive();
        bool studentIsLeft =
            IsSelectedStudentLeft();
        bool latestIssueCollected =
            IsLatestIssueCollected();
        bool duplicateLimitReached =
            DuplicateLimitReached();
        student.Enabled = true;
        currentClass.Enabled = false;
        currentClass.ReadOnly = true;
        /* Finding 5 - dbo.CreateLeaveCertificate accepts a student already marked Left through its retroactive branch, for the College type just as for School. */
        bool canCreate =
            hasStudent &&
            !hasCertificateForThisLeaving &&
            (studentIsActive || studentIsLeft);
        bool editableCopy=hasSelectedIssue&&!selectedIssueCollected&&IsLatestSelectedIssue();
        leave.Enabled = canCreate&&!hasSelectedIssue;
        promoted.Enabled = academic.Enabled = attendance.Enabled = remarks.Enabled = editableCopy||(canCreate&&!hasSelectedIssue);
        createCertificateButton.Enabled = editableCopy||(canCreate&&!hasSelectedIssue&&HasRequiredStudentDates);
        createCertificateButton.Text=hasSelectedIssue?"Save Changes":UiText.Certificates.Create;
        clearButton.Enabled = true;
        refreshButton.Enabled =
            hasStudent;
        issueAgainButton.Enabled =
            hasSelectedIssue && IsLatestSelectedIssue() &&
            latestIssueCollected &&
            !duplicateLimitReached;
        collectButton.Enabled =
            hasSelectedIssue &&
            !selectedIssueCollected && draftBaseline==DraftSnapshot();
        printButton.Enabled = hasSelectedIssue;
        copyFlow.Available=true;
        if(copyFlow.Apply(certificateResults,"LeaveCertificateIssueId",selectedIssueId,createCertificateButton,printButton,draftBaseline!=DraftSnapshot(),promoted,academic,attendance,remarks))
        {
            collectButton.Enabled=false;
            SetStatus(copyFlow.Status);return;
        }
        if (!hasStudent)
        {
            SetStatus("Search for and select a college student.");
            return;
        }
        /* Finding 4 / BD-21: selected-copy guidance takes precedence over the new leaving episode. */
        if (hasSelectedIssue)
        {
            if (!selectedIssueCollected)
            {
                SetStatus("Ready to print. Check the details, print, then Mark Collected to lock this copy.");
                return;
            }
            if (IsLatestSelectedIssue())
            {
                SetStatus("Collected. Print this copy or use Issue Again.");
                return;
            }
            SetStatus("Collected. You may print this copy.");
            return;
        }
        if (!hasCertificateForThisLeaving)
        {
            if (!HasRequiredStudentDates)
            {
                SetStatus("Admission date and date of birth are required. Complete the missing dates in the student record, then refresh.", true);
                return;
            }
            if (!studentIsActive && !studentIsLeft)
            {
                SetStatus("This student is not active. A new college leaving certificate cannot be created.", true);
                return;
            }
            // Finding 4: identify the listed leaving episode.
            string earlierEpisodeNote =
                hasCertificate
                    ? "A leaving certificate from an earlier placement is listed below; it stays there for printing. "
                    : string.Empty;
            if (studentIsLeft)
            {
                /* Findings 5 and 7 - the retroactive path, and only claim the date came from the record when it did. */
                SetStatus(
                    earlierEpisodeNote +
                    (leaveDatePrefilled
                        ? "This student already left without a certificate being issued. " +
                          "Complete the details below - the leave date shown was pulled from " +
                          "when they were marked Left, correct it if needed - and create the " +
                          "certificate now."
                        : "This student already left without a certificate being issued, and no " +
                          "leaving date is on file. Tick the Leave Date box and choose the date " +
                          "they left, complete the details below, and create the certificate now."));
                return;
            }
            SetStatus(earlierEpisodeNote + "Complete the certificate information and create the college leaving certificate.");
            return;
        }
        if (duplicateLimitReached)
        {
            SetStatus(
                $"The maximum number of certificate issuances (1 original + {MaxDuplicateIssues} duplicates) " +
                "has already been reached for this student. No further copies can be issued.",
                true);
            return;
        }
        if (latestIssueCollected)
        {
            SetStatus("Latest copy collected. Select a copy to print, or use Issue Again.");
            return;
        }
        SetStatus("Current copy not collected.");
    }
    /// <summary>Finding 4 - true when this student's CURRENT leaving episode already has a certificate of this type, which is exactly what dbo.CreateLeaveCertificate refuses (57013).</summary>
    private bool HasCertificateForCurrentEnrollment()
    {
        if (certificateResults.Rows.Count == 0)
            return false;
        if (currentEnrollmentId <= 0 ||
            !certificateResults.Columns.Contains("EnrollmentId"))
        {
            return true;
        }
        foreach (DataRow row in certificateResults.Rows)
        {
            if (GetInt(row, "EnrollmentId") == currentEnrollmentId)
                return true;
        }
        return false;
    }
    private int CountDuplicateIssues()
    {
        /* Finding 4 / BD-21: duplicates are per LeaveRecordId; Issue Again uses its newest record. */
        int newestRecordId = selectedLeaveRecordId>0?selectedLeaveRecordId:GetExistingLeaveRecordId();
        int count = 0;
        foreach (DataRow row in certificateResults.Rows)
        {
            if (newestRecordId > 0 &&
                GetInt(row, "LeaveRecordId") != newestRecordId)
            {
                continue;
            }
            if (string.Equals(
                    GetString(row, "IssueType"), "Duplicate", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }
    private bool DuplicateLimitReached()
    {
        return CountDuplicateIssues() >=
               MaxDuplicateIssues;
    }
    private bool IsLatestIssueCollected()
    {
        var row=certificateResults.AsEnumerable().FirstOrDefault(row=>selectedLeaveRecordId<=0||GetInt(row,"LeaveRecordId")==selectedLeaveRecordId);
        return CertificateCollectionFlow.Locked(row);
    }
    private bool IsLatestSelectedIssue()
    {
        if(selectedIssueId<=0||selectedLeaveRecordId<=0)return false;
        var row=certificateResults.AsEnumerable().FirstOrDefault(row=>GetInt(row,"LeaveRecordId")==selectedLeaveRecordId);
        return row is not null&&GetInt(row,"LeaveCertificateIssueId")==selectedIssueId;
    }
    private bool IsSelectedStudentActive()
    {
        if (student.SelectedId <= 0)
            return false;
        DataRow[] matches =
            studentResults.Select($"AdmissionId = {student.SelectedId}");
        if (matches.Length == 0)
            return false;
        return string.Equals(
            GetString(matches[0], "Status"), "Active", StringComparison.OrdinalIgnoreCase);
    }
    private void CreateCertificate()
    {
        if(selectedIssueId>0)
        {
            if(selectedIssueCollected||!IsLatestSelectedIssue())return;
            try { DatabaseHelper.ExecuteUser("UpdateLeavingCertificateCopy",DatabaseHelper.Int("@CopyId",selectedIssueId),DatabaseHelper.P("@ClassPromotedTo",GetSelectedPromotionClassName()),DatabaseHelper.P("@AcademicRecord",academic.Text),DatabaseHelper.P("@AttendanceRecord",attendance.Text),DatabaseHelper.P("@Remarks",RecordTextPolicy.TitleCase(remarks.Text)),CertificateCollectionFlow.VersionParameter(certificateResults,"LeaveCertificateIssueId",selectedIssueId));
                int id=selectedIssueId;LoadCertificates(student.SelectedId);SelectSavedCertificate(id);Info("Certificate changes saved. Student status and fees are unchanged."); }
            catch(Exception ex){Fail(ex);}
            return;
        }
        if (!ValidateRequiredInputs())
            return;
        /* Finding 12 - Info writes the status line, but the reload below calls Bind ("N record(s)") and ApplyWorkflowState after it. */
        string? successMessage =
            null;
        // Finding 13 - this path opens a modal confirmation; park the search.
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn("Select a college student first.");
                return;
            }
            /* Finding 4 / BD-21: match SQL duplicate refusal to the current leaving episode. */
            if (HasCertificateForCurrentEnrollment())
            {
                Warn("This student already has a leaving certificate for this placement.");
                return;
            }
            DataRow[] matches =
                studentResults.Select($"AdmissionId = {student.SelectedId}");
            if (matches.Length == 0)
            {
                Alert("The selected student record could not be found.");
                return;
            }
            /* Finding 5 - a student already marked Left with no certificate goes down dbo.CreateLeaveCertificate's retroactive branch, which accepts the College type too. Ported from the school Leave page. */
            DataRow studentRow = matches[0];
            LoadStudentDates(student.SelectedId);
            if (currentAdmissionDate is not DateTime admissionDate || !currentDateOfBirth.HasValue)
            {
                Warn("Admission date and date of birth are required. Complete the missing dates in the student record, then refresh.");
                return;
            }
            string studentStatus =
                GetString(studentRow, "Status");
            bool isRetroactive =
                string.Equals(studentStatus, "Left", StringComparison.OrdinalIgnoreCase);
            if (!isRetroactive &&
                !string.Equals(studentStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                Warn(
                    "Only an active college student, or one already marked Left " +
                    "without a certificate, can receive a new college leaving certificate.");
                return;
            }
            if (currentClassId <= 0)
            {
                Warn("The selected student does not have a valid current class.");
                return;
            }
            if (!string.Equals(GetString(studentRow, "ProgramType"), "College", StringComparison.OrdinalIgnoreCase))
            {
                Warn("This page is for college students only. Use the Leave Certificate page for school students.");
                return;
            }
            if (promoted.SelectedId <= 0)
            {
                Warn("Select the class the student is being promoted to.");
                return;
            }
            if (string.IsNullOrWhiteSpace(academic.Text))
            {
                Warn("Enter the student's academic record.");
                return;
            }
            if (string.IsNullOrWhiteSpace(attendance.Text))
            {
                Warn("Enter the student's attendance record.");
                return;
            }
            if (string.IsNullOrWhiteSpace(remarks.Text))
            {
                Warn("Enter remarks for the college leaving certificate.");
                return;
            }
            /* Finding 7 - no withdrawal date was found for this Left student, so the picker starts unticked. */
            if (leave.ShowCheckBox && !leave.Checked)
            {
                Warn("Tick the Leave Date box and choose the date this student left before creating the certificate.");
                return;
            }
            DateTime leaveDate =
                leave.Value.Date;
            if (leaveDate > DateTime.Today)
            {
                Warn("Leave date cannot be in the future.");
                return;
            }
            if (leaveDate < admissionDate)
            {
                Warn("Leave date cannot be earlier than the student's admission date.");
                return;
            }
            // Finding 5 - the retroactive path does not close anything; say so.
            if (!feeReview.Prepare()) return;
            int admissionId =
                student.SelectedId;
            createCertificateButton.Enabled = false;
            DatabaseHelper.Execute(
                "CreateReviewedLeaveCertificate",
                new[] {
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AdmissionId",
                    admissionId),
                DatabaseHelper.Date(
                    "@LeaveDate",
                    leaveDate),
                DatabaseHelper.P(
                    "@ClassPromotedTo",
                    DatabaseHelper.OptionalText(
                        GetSelectedPromotionClassName())),
                DatabaseHelper.P(
                    "@AcademicRecord",
                    DatabaseHelper.OptionalText(
                        academic.Text)),
                DatabaseHelper.P(
                    "@AttendanceRecord",
                    DatabaseHelper.OptionalText(
                        attendance.Text)),
                DatabaseHelper.P(
                    "@Remarks",
                    DatabaseHelper.OptionalText(
                        RecordTextPolicy.TitleCase(remarks.Text))),
                DatabaseHelper.P("@CertificateType", CertificateTypeName) }.Concat(feeReview.Parameters()).ToArray());
            successMessage =
                "College leaving certificate created successfully. " +
                "The original issuance has also been recorded.";
            AddSuggestionIfNew(academic, academic.Text);
            AddSuggestionIfNew(attendance, attendance.Text);
            AddSuggestionIfNew(remarks, remarks.Text);
            /* The student lookup is deliberately NOT re-bound here. */
            ClearCertificateFields();
            LoadCertificates(admissionId);
            feeReview.RefreshFees();
            SelectSavedCertificate(0);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            ApplyWorkflowState();
            if (successMessage is not null)
            {
                Info(successMessage);
            }
        }
    }
    private void IssueAgain()
    {
        // Finding 12 - see CreateCertificate: the confirmation is set last.
        string? successMessage =
            null;
        // Finding 13 - this path opens a modal confirmation; park the search.
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn("Select a college student first.");
                return;
            }
            if (certificateResults.Rows.Count == 0)
            {
                Warn("The student does not have an existing leaving certificate.");
                return;
            }
            if (!IsLatestIssueCollected())
            {
                Warn("The current certificate issuance must be collected before another issuance can be created.");
                return;
            }
            if (DuplicateLimitReached())
            {
                Warn(
                    $"This certificate has already been issued as a duplicate {MaxDuplicateIssues} time(s), " +
                    "the maximum allowed. No further copies can be issued for this student.");
                return;
            }
            int leaveRecordId = selectedLeaveRecordId;
            if (leaveRecordId <= 0)
            {
                Alert("The student's leaving certificate record could not be found.");
                return;
            }
            var reviewed = CertificateReissueFlow.ReviewLeave(FindForm(), leaveRecordId);
            if (reviewed is null) return;
            issueAgainButton.Enabled = false;
            DatabaseHelper.Execute("IssueLeaveCertificate",new[] { DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@LeaveRecordId",leaveRecordId) }.Concat(reviewed).ToArray());
            successMessage =
                "Certificate issued again successfully. The new issuance has been added to the history.";
            LoadCertificates(student.SelectedId);SelectSavedCertificate(0);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            ApplyWorkflowState();
            if (successMessage is not null)
            {
                Info(successMessage);
            }
        }
    }
    private int GetExistingLeaveRecordId()
    {
        if (certificateResults.Rows.Count == 0)
            return 0;
        return GetInt(certificateResults.Rows[0], "LeaveRecordId");
    }
    private void MarkCollected()
    {
        if(draftBaseline!=DraftSnapshot()){Warn("Save the certificate changes before marking it collected.");return;}

        int savedIssueId = selectedIssueId;
        // Finding 12 - see CreateCertificate: the confirmation is set last.
        string? successMessage =
            null;
        // Finding 13 - this path opens the modal collector prompt; park the search.
        try
        {
            if (selectedIssueId <= 0)
            {
                Warn("Select an issuance from the history first.");
                return;
            }
            if (selectedIssueCollected)
            {
                Warn("This certificate issuance has already been collected.");
                return;
            }
            int admissionId =
                selectedCertificateAdmissionId;
            if (admissionId <= 0)
            {
                Warn("The selected issuance has an invalid student.");
                return;
            }
            string? collectedBy =
                PromptCollectedBy();
            if (collectedBy is null)
                return;
            collectButton.Enabled = false;
            DatabaseHelper.Execute(
                "MarkLeaveCertificateCollected",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@LeaveCertificateIssueId",
                    selectedIssueId),
                DatabaseHelper.P("@CollectedBy", collectedBy),CertificateCollectionFlow.VersionParameter(certificateResults,"LeaveCertificateIssueId",selectedIssueId));
            successMessage =
                "Certificate issuance marked as collected.";
            LoadCertificates(admissionId);
            feeReview.RefreshFees();
            SelectSavedCertificate(savedIssueId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            ApplyWorkflowState();
            if (successMessage is not null)
            {
                Info(successMessage);
            }
        }
    }
    private void PrintSelected()
    {
        // Finding 13 - the preview is modal; park the search while it is open.
        try
        {
            if (selectedLeaveRecordId <= 0)
            {
                Warn("Select a certificate issuance from the history first.");
                return;
            }
            if (selectedIssueId <= 0)
            {
                Warn(UiText.Certificates.InvalidIssuance);
                return;
            }
            PrintCertificate(selectedLeaveRecordId, selectedIssueId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
        }
    }
    private void PrintCertificate(int leaveRecordId, int leaveCertificateIssueId = 0)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetLeaveCertificatePrintData",
                DatabaseHelper.Int(
                    "@LeaveRecordId",
                    leaveRecordId),
                DatabaseHelper.Int("@LeaveCertificateIssueId", leaveCertificateIssueId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "CollegeLeaveCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("College Leaving Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "College Leaving Certificate", reportPath, "LeaveCertificateData", data);
    }
    private string? PromptCollectedBy() => CertificateCollectionDialog.Show(this);
    private void ConfigureFieldSuggestions()
    {
        foreach (var field in new[] { academic, attendance, remarks })
        {
            field.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            field.AutoCompleteSource = AutoCompleteSource.CustomSource;
            field.AutoCompleteCustomSource = new AutoCompleteStringCollection();
        }
    }
    private void LoadFieldSuggestions()
    {
        LoadFieldSuggestions(academic, "AcademicRecord");
        LoadFieldSuggestions(attendance, "AttendanceRecord");
        LoadFieldSuggestions(remarks, "Remarks");
    }
    private void LoadFieldSuggestions(ModernTextBox field, string columnField)
    {
        try
        {
            DataTable table = DatabaseHelper.QueryUser("GetLeaveCertificateFieldSuggestions", DatabaseHelper.P("@Field", columnField));
            var source = new AutoCompleteStringCollection();
            foreach (DataRow row in table.Rows)
            {
                string? value = Convert.ToString(row["Value"]);
                if (!string.IsNullOrWhiteSpace(value))
                    source.Add(value);
            }
            field.AutoCompleteCustomSource = source;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(LoadFieldSuggestions)} failed: {ex.Message}");
        }
    }
    private static void AddSuggestionIfNew(ModernTextBox field, string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (field.AutoCompleteCustomSource.Cast<string>()
                .Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        field.AutoCompleteCustomSource.Add(value);
    }
    private string GetSelectedPromotionClassName()
    {
        if (promoted.SelectedId <= 0)
            return string.Empty;
        if (promoted.SelectedItem
            is not DataRowView row)
        {
            return string.Empty;
        }
        return row.Row.Table.Columns.Contains("ClassName") ? GetString(row.Row, "ClassName") : CertificateUi.PromotionClassText(GetString(row.Row, "Display"));
    }
    private void RefreshCertificates()
    {
        if(!ConfirmDraftDiscard())return;
        if (student.SelectedId <= 0) return;
        try { LoadStudentDates(student.SelectedId); }
        catch (Exception ex) { Fail(ex); return; }
        int selectedId = selectedIssueId;
        LoadCertificates(student.SelectedId);
        if (selectedId > 0) SelectSavedCertificate(selectedId);
    }
    private void ClearStudentDetails()
    {
        SetStudentDates(null);
        currentClassId = 0;
        currentEnrollmentId = 0;
        feeReview.ClearFees();
        currentClass.Clear();
        promoted.ClearLookup();
    }
    private void ClearCertificateFields()
    {
        academic.Clear();
        attendance.Clear();
        remarks.Clear();
        /* Finding 7 - back to the plain picker: the check box only appears for a Left student with no leaving date on file. */
        leaveDatePrefilled = false;
        leave.ShowCheckBox = false;
        leave.Value =
            DateTime.Today;
    }
    private void ClearCertificateSelection()
    {
        selectedLeaveRecordId = 0;
        selectedIssueId = 0;
        selectedCertificateAdmissionId = 0;
        selectedIssueCollected = false;
    }
    private void ResetToStart()
    {
        suppressStudentChanged = true;
        try
        {
            student.Clear();
        }
        finally
        {
            suppressStudentChanged = false;
        }
        ClearStudentDetails();
        ClearCertificateFields();
        ClearCertificateSelection();
        certificateResults =
            new DataTable();
        Bind(certificateResults);
        FormatCertificateGrid();
        ApplyWorkflowState();
    }
    private void ClearForm()
    {
        if(!ConfirmDraftDiscard())return;
        suppressStudentChanged = true;
        try
        {
            student.Clear();
            certificateResults =
                new DataTable();
            ClearStudentDetails();
            ClearCertificateFields();
            ClearCertificateSelection();
            Bind(certificateResults);
            FormatCertificateGrid();
        }
        finally
        {
            suppressStudentChanged = false;
        }
        ApplyWorkflowState();
    }
    private void LoadStudentDates(int admissionId)
    {
        SetStudentDates(null);
        try
        {
            var details = DatabaseHelper.QueryUser("GetStudentDetails", DatabaseHelper.Int("@AdmissionId", admissionId));
            SetStudentDates(details.Rows.Count > 0 ? details.Rows[0] : null);
        }
        finally { ApplyWorkflowState(); }
    }
    private void SetStudentDates(DataRow? row)
    {
        currentAdmissionDate = row is null ? null : GetDate(row, "AdmissionDate")?.Date;
        currentDateOfBirth = row is null ? null : GetDate(row, "DateOfBirth")?.Date;
        admissionDateDisplay.Text = currentAdmissionDate?.ToString("dd MMM yyyy") ?? (student.SelectedId > 0 ? "Not on record" : string.Empty);
        dateOfBirthDisplay.Text = currentDateOfBirth?.ToString("dd MMM yyyy") ?? (student.SelectedId > 0 ? "Not on record" : string.Empty);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Grid.CellMouseClick -=
                Grid_CellMouseClick;
        }
        base.Dispose(disposing);
    }
}


