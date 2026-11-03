using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class CharacterCertificatePage : V2Page, IUnsavedChanges, IBusyPage, IStudentCertificateWorkspaceEditor
{
    internal override bool FocusEntryField() => CertificateUi.FocusEntry(student, Grid, certificateDate);
    private readonly StudentPicker student = new();
    private readonly CertificateCollectionFlow copyFlow = new("Character");
    private string? draftBaseline;
    private string DraftSnapshot()=>System.Text.Json.JsonSerializer.Serialize(new {Exam=examType.SelectedIndex,Year=academicYear.SelectedId,Roll=rollNumber.Text.Trim(),Remarks=remarks.Text.Trim(),Date=certificateDate.Value.Date});
    private void CaptureDraftBaseline()=>draftBaseline=DraftSnapshot();
    public bool IsBusy => saving;
    public string BusyMessage => "Please wait for the certificate save to finish.";
    public bool HasUnsavedChanges => recoveryBlocked;
    public string UnsavedChangesMessage=>"A certificate save needs outcome checking. Its recovery record will be kept when you leave.";
    private bool ConfirmDraftDiscard()=>!HasUnsavedChanges||Confirm(UnsavedChangesMessage,UiText.Dialogs.UnsavedChanges);

    private readonly ModernComboBox examType =
        HumanUi.Combo();
    private readonly ModernTextBox rollNumber =
        HumanUi.TextBox(false, 50);
    private readonly ModernComboBox remarks = HumanUi.Combo();
    private readonly LookupBox academicYear = new();
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    /* Finding 13 - a search debounce tick that lands while a modal (confirmation, collector prompt, print preview) is open runs inside that modal's message loop and re-binds the picker underneath… */
    private bool suppressStudentChanged;
    private bool loadingCertificates;
    private bool saving;
    private bool recoveryBlocked;
    private string selectedStoredExamType = "";
    private readonly ModernButton checkSaveButton = new() { Text = "Check Save", Width = AppScale.Scale(140) };
    private int selectedCertificateRecordId;
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
    // Qualification is a saved identity. Reprints use the selected issue's snapshot.
    private static readonly string[] ExamTypeLabels =
    {
        "S.S.C (Matric)",
        "H.S.S.C (Intermediate)"
    };
    public CharacterCertificatePage() : this(true) { }
    internal CharacterCertificatePage(bool initialize)
        : base("Character Certificate", "Create, issue, collect and print student character certificates.")
    {
        examType.DropDownStyle =
            ComboBoxStyle.DropDownList;
        examType.Items.AddRange(ExamTypeLabels);
        remarks.DropDownStyle=ComboBoxStyle.DropDownList;
        remarks.Items.AddRange(CharacterCertificateOptions.Conduct);
        certificateDate.ValueChanged+=(_,_)=>ApplyWorkflowState();
        rollNumber.TextChanged+=(_,_)=>ApplyWorkflowState();
        remarks.SelectedIndexChanged+=(_,_)=>ApplyWorkflowState();
        academicYear.SelectedIndexChanged+=(_,_)=>ApplyWorkflowState();
        copyFlow.Wire(this,()=>selectedIssueId,id=>{LoadCertificates(student.SelectedId);SelectSavedCertificate(id);},()=>draftBaseline!=DraftSnapshot());
        createCertificateButton.Click += async (_, _) => await CreateCertificate();
        clearButton.Click +=
            (_, _) => ClearForm();
        issueAgainButton.Click += async (_, _) => await IssueAgain();
        checkSaveButton.Click += async (_, _) => await CheckSave();
        collectButton.Click +=
            (_, _) => MarkCollected();
        printButton.Click +=
            (_, _) => PrintSelected();
        /* printing calls a Get*PrintData procedure, which requires Certificates.Print, while this page is reached with Certificates.Create. Without this gate a create-only user was offered a Print button the database would refuse. */
        GateAction(printButton, "Certificates.Print");
        refreshButton.Click +=
            (_, _) => RefreshCertificates();
        RelocateFromActions(createCertificateButton, clearButton);
        TrackStudentSearchStatus(student);
        /* The shared student finder (StudentPicker): the list narrows on every keystroke and nothing is chosen until the operator clicks a row, presses Enter on a highlighted row, or types an… */
        if(initialize)StudentSearchCache.Warm();
        student.SelectionChangeGuard=_=>ConfirmDraftDiscard();
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
        examType.SelectedIndexChanged +=
            (_, _) => ApplyWorkflowState();
        Grid.SelectionChanged += GridSelectionChanged;
        Grid.CurrentCellChanged += GridSelectionChanged;
        Defer(
            () => {
                ResetToStart();
            });
        if(initialize) { Defer(LoadAcademicYears); Defer(LoadRecovery); }
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "Select a saved certificate to edit its details and Save Changes. Use Issue Again for a fresh copy.");
        var fields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Tag=CertificateUi.FieldGridTag,
                TabIndex = 0,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
        var studentField=CertificateUi.Field("Student", student, true);
        var dateField=CertificateUi.Field(UiText.Certificates.Date, certificateDate, true);
        var examField=CertificateUi.Field("Exam Type", examType, true);
        var rollField=CertificateUi.Field("BISE Roll No.", rollNumber, true);
        fields.Controls.Add(studentField,0,0);
        fields.Controls.Add(dateField,1,0);
        fields.Controls.Add(examField,0,1);
        fields.Controls.Add(rollField,1,1);
        var remarksField = CertificateUi.Field("Conduct", remarks, true);
        var yearField = CertificateUi.Field("Academic Year", academicYear, true);
        fields.Controls.Add(remarksField, 0, 2);
        fields.Controls.Add(yearField, 1, 2);
        foreach(var field in fields.Controls.OfType<TableLayoutPanel>())
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        certificateDate.MaximumSize=Size.Empty;
        examType.MaximumSize=Size.Empty;
        foreach(Control input in new Control[]{student,certificateDate,examType,rollNumber,remarks,academicYear})
            input.Dock=DockStyle.Top;
        // Row-major keyboard flow follows the visible certificate fields.
        foreach (Control field in fields.Controls)
            field.TabIndex = fields.GetRow(field) * fields.ColumnCount + fields.GetColumn(field);
        details.Content.Controls.Add(fields);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, clearButton, checkSaveButton);
        certButtonRow.Dock =
            DockStyle.Bottom;
        certButtonRow.TabIndex = 1;
        createCertificateButton.TabIndex = 0;
        clearButton.TabIndex = 1;
        details.Content.Controls.Add(certButtonRow);
        var records =
            HumanUi.Card(
                UiText.Certificates.IssuanceHistory,
                UiText.Certificates.IssueHistoryGuidance);
        var certificateActions =
            CertificateButtonRow.Create(printButton, collectButton, issueAgainButton, refreshButton);
        certificateActions.Dock =
            DockStyle.Bottom;
        Grid.TabIndex = 0;
        certificateActions.TabIndex = 1;
        issueAgainButton.TabIndex = 0;
        collectButton.TabIndex = 1;
        printButton.TabIndex = 2;
        refreshButton.TabIndex = 3;
        records.Content.Controls.Add(certificateActions);
        records.Content.Controls.Add(Grid);
        CertificateUi.WireNavigation(this, student, Grid, clearButton, fields, certButtonRow, certificateActions);
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
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(3)
                + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(records, 0, 1);
        bool? wideFields=null;
        void ArrangeFields()
        {
            bool wide=details.Content.ClientSize.Width>=AppScale.Scale(1050);
            if(wideFields==wide)return;
            wideFields=wide;
            fields.SuspendLayout();root.SuspendLayout();
            try
            {
                fields.SetColumnSpan(remarksField,1);
                fields.SetColumnSpan(yearField,1);
                fields.ColumnCount=wide?4:2;fields.RowCount=wide?2:3;
                fields.ColumnStyles.Clear();fields.RowStyles.Clear();
                foreach(float width in wide?new[]{40f,20f,20f,20f}:new[]{50f,50f})
                    fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
                for(int row=0;row<fields.RowCount;row++)fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
                fields.SetCellPosition(studentField,new TableLayoutPanelCellPosition(0,0));
                fields.SetCellPosition(dateField,new TableLayoutPanelCellPosition(1,0));
                fields.SetCellPosition(examField,new TableLayoutPanelCellPosition(wide?2:0,wide?0:1));
                fields.SetCellPosition(rollField,new TableLayoutPanelCellPosition(wide?3:1,wide?0:1));
                fields.SetCellPosition(remarksField,new TableLayoutPanelCellPosition(0,wide?1:2));
                fields.SetColumnSpan(remarksField,wide?2:1);
                fields.SetCellPosition(yearField,new TableLayoutPanelCellPosition(wide?2:1,wide?1:2));
                fields.SetColumnSpan(yearField,wide?2:1);
                foreach(Control field in fields.Controls)
                    field.TabIndex=fields.GetRow(field)*fields.ColumnCount+fields.GetColumn(field);
                root.RowStyles[0].Height=AppMetrics.FieldCardHeight(fields.RowCount)+AppMetrics.ButtonRowHeight;
            }
            finally{fields.ResumeLayout(true);root.ResumeLayout(true);}
        }
        details.Content.SizeChanged+=(_,_)=>ArrangeFields();
        ArrangeFields();
        details.TabIndex = 0;
        records.TabIndex = 1;
        SetContent(
            root,
            UiText.Certificates.IssueGuidance + " Passing year is the selected academic year's end year minus one.");
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
            LoadCertificates(admissionId);
            CertificateUi.FocusHistory(Grid, () => SelectSavedCertificate(0), certificateDate);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally{CaptureDraftBaseline();}
    }
    private void LoadCertificates(int admissionId)
    {
        loadingCertificates = true;
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
                    "GetCharacterCertificateRecords",
                    DatabaseHelper.Int("@AdmissionId", admissionId));
            certificateResults = CertificateRecordOrder.NewestFirst(table, "CharacterCertificateIssueId");
            Bind(certificateResults);
            FormatCertificateGrid();
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException57)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException57, "CharacterCertificatePage: LoadCertificates fallback");
            }
            ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            Fail(ex);
            /* Finding 11 - the load failed, so the grid must not keep showing the previous student's certificates, and the buttons must be recomputed for the empty list. */
            certificateResults =
                new DataTable();
            Bind(certificateResults);
            FormatCertificateGrid();
            ApplyWorkflowState();
        }
        finally { loadingCertificates = false; ApplyWorkflowState(); }
    }
    private void SelectSavedCertificate(int id)
    {

        CertificateRecordOrder.Select(Grid, "CharacterCertificateIssueId", id,
            index => LoadCertificateIssue(((DataRowView)Grid.Rows[index].DataBoundItem).Row));
    }
    private void FormatCertificateGrid()
    {
        int order = 0;
        foreach (string name in new[] { "CertificateDate", "AdmissionNumber", "StudentName", "FatherName",
            "ClassName", "ExamType", "LastExamYear", "RollNumber", "IssuedAt", "IssuedBy",
            "CollectionStatus", "CollectedAt", "CollectedBy", "Remarks", "CharacterCertificateIssueId" })
            if (Grid.Columns.Contains(name)) Grid.Columns[name].DisplayIndex = order++;
        /* The click handlers below read the row from certificateResults by the clicked row INDEX, which only matches the grid while it is in the table's own order. */
        LockGridOrder();
        if(Grid.Columns.Contains("Remarks"))Grid.Columns["Remarks"].HeaderText="Conduct";
        if(Grid.Columns.Contains("LastExamYear"))Grid.Columns["LastExamYear"].HeaderText="Passing Year";
        SetGridWidth("CertificateRecordId", 80);
        SetGridWidth("CharacterCertificateIssueId", 95);
        SetGridWidth("AdmissionNumber", 80);
        SetGridWidth("ExamType", 80);
        SetGridWidth("StudentName", 150);
        SetGridWidth("FatherName", 130);
        SetGridWidth("RollNumber", 110);
        SetGridWidth("LastExamYear", 95);
        SetGridWidth("CertificateDate", 110);
        SetGridWidth("IssuedAt", 145);
        SetGridWidth("IssuedByUserId", 90);
        SetGridWidth("IssuedBy", 130);
        SetGridWidth("CollectionStatus", 115);
        SetGridWidth("CollectedAt", 145);
        SetGridWidth("CollectedBy", 150);
        HideGridColumn("AdmissionId");
        HideGridColumn("IssuedByUserId");
        HideGridColumn("CertificateRecordId");
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
    private void GridSelectionChanged(object? sender, EventArgs e)
    {
        if (loadingCertificates) return;
        if (Grid.CurrentRow is { IsNewRow: false, Selected: true, DataBoundItem: DataRowView view })
        {
            LoadCertificateIssue(view.Row);
            return;
        }
        ClearCertificateSelection();
        ApplyWorkflowState();
    }
    private void LoadCertificateIssue(DataRow row)
    {
        try
        {
            int certificateRecordId =
                GetInt(row, "CertificateRecordId");
            int issueId =
                GetInt(row, "CharacterCertificateIssueId");
            int admissionId =
                GetInt(row, "AdmissionId");
            if (certificateRecordId <= 0 ||
                issueId <= 0 ||
                admissionId <= 0)
            {
                ClearCertificateSelection();
                SetStatus("The selected certificate issuance contains invalid data.", true);
                return;
            }
            selectedStoredExamType = GetString(row, "ExamType");
            examType.SelectedIndex = selectedStoredExamType switch { "SSC" => 0, "HSSC" => 1, _ => -1 };
            certificateDate.Value = row.Table.Columns.Contains("CertificateDate") && !row.IsNull("CertificateDate") ? Convert.ToDateTime(row["CertificateDate"]) : DateTime.Today;
            rollNumber.Text = GetString(row, "RollNumber");
            CharacterCertificateOptions.SelectConduct(remarks,GetString(row,"Remarks"));
            CharacterCertificateOptions.SelectYear(academicYear,GetInt(row,"LastExamYear"));
            selectedCertificateRecordId =
                certificateRecordId;
            selectedIssueId =
                issueId;
            selectedCertificateAdmissionId =
                admissionId;
            selectedIssueCollected =
                string.Equals(
                    GetString(row, "CollectionStatus"), "Collected", StringComparison.OrdinalIgnoreCase);
            CaptureDraftBaseline();ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            ClearCertificateSelection();
            SetStatus($"Unable to read certificate issuance: {DbMessage.UserMessage(ex)}", true);
        }
    }
    private bool HasCertificateForDraft()
    {
        string chosen = examType.SelectedIndex == 0 ? "SSC" : examType.SelectedIndex == 1 ? "HSSC" : "";
        return certificateResults.AsEnumerable().GroupBy(row=>GetInt(row,"CertificateRecordId"))
            .Select(group=>group.OrderByDescending(row=>GetInt(row,"CharacterCertificateIssueId")).First())
            .Any(row => string.IsNullOrWhiteSpace(GetString(row, "ExamType")) || GetString(row, "ExamType") == chosen);
    }
    private void ApplyWorkflowState()
    {
        bool hasStudent = student.SelectedId > 0;
        bool selected = selectedIssueId > 0 && selectedCertificateRecordId > 0;
        bool knownType = selectedStoredExamType is "SSC" or "HSSC";
        bool available = !saving && !recoveryBlocked;
        Grid.Enabled = !saving;
        student.Enabled = !saving;
        bool editable=available&&hasStudent&&(!selected||(IsLatestSelectedIssue()&&!selectedIssueCollected));
        examType.Enabled = editable;
        bool canCreate = available && hasStudent && !selected && examType.SelectedIndex >= 0 && !HasCertificateForDraft();
        certificateDate.Enabled = rollNumber.Enabled = remarks.Enabled = academicYear.Enabled = selected?editable:canCreate;
        createCertificateButton.Enabled = selected?editable:canCreate;
        createCertificateButton.Text=selected?"Save Changes":UiText.Certificates.Create;
        clearButton.Enabled = !saving;
        refreshButton.Enabled = hasStudent && !saving;
        issueAgainButton.Enabled = available && selected && knownType && IsLatestSelectedIssue() && IsLatestIssueCollectedFor(selectedCertificateRecordId);
        collectButton.Enabled = available && selected && knownType && !selectedIssueCollected && draftBaseline==DraftSnapshot();
        printButton.Enabled = !saving && selected && knownType && (selectedIssueCollected||draftBaseline==DraftSnapshot()) && CurrentUser.HasPermission("Certificates.Print");
        checkSaveButton.Visible = recoveryBlocked;
        checkSaveButton.Enabled = recoveryBlocked && !saving;
        copyFlow.Available=available;
        if(copyFlow.Apply(certificateResults,"CharacterCertificateIssueId",selectedIssueId,createCertificateButton,printButton,draftBaseline!=DraftSnapshot(),examType,certificateDate,rollNumber,remarks,academicYear))
        {
            collectButton.Enabled=false;
            SetStatus(copyFlow.Status);return;
        }
        if (recoveryBlocked) { SetStatus("An earlier save needs checking. Use Check Save before creating another copy.", true); return; }
        if (!hasStudent) { SetStatus(UiText.Prompts.SearchForStudent); return; }
        if (selected)
        {
            SetStatus(selectedIssueCollected?"Collected copy — details are locked. Print this saved copy or use Issue Again for a new copy.":!IsLatestSelectedIssue()?"This is an earlier copy. Select the latest copy to edit or issue again."
                :"Edit the entered details and Save Changes. Issue Again creates a fresh copy using current Admission details.");
            return;
        }
        SetStatus(examType.SelectedIndex < 0 ? "Choose SSC or HSSC for the new certificate."
            : HasCertificateForDraft() ? "This exam-type certificate already exists. Select it in history to issue again or correct its details."
            : "Complete the details to create the selected exam-type certificate.");
    }
    private bool IsLatestIssueCollectedFor(int certificateRecordId)
    {
        if (certificateRecordId <= 0)
            return false;
        foreach (DataRow row in certificateResults.Rows)
        {
            if (GetInt(
                    row,
                    "CertificateRecordId") !=
                certificateRecordId)
            {
                continue;
            }
            return string.Equals(
                GetString(row, "CollectionStatus"), "Collected", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    private bool IsLatestSelectedIssue()
    {
        if (selectedIssueId <= 0 ||
            selectedCertificateRecordId <= 0)
        {
            return false;
        }
        foreach (DataRow row in certificateResults.Rows)
        {
            if (GetInt(
                    row,
                    "CertificateRecordId") !=
                selectedCertificateRecordId)
            {
                continue;
            }
            return GetInt(
                    row,
                    "CharacterCertificateIssueId") ==
                selectedIssueId;
        }
        return false;
    }
    private bool ValidateWording()
    {
        if (examType.SelectedIndex >= 0 && examType.SelectedIndex < ExamTypeLabels.Length) return true;
        Warn("Choose SSC or HSSC wording for this certificate.");
        return false;
    }
    private void LoadRecovery()
    {
        recoveryBlocked = true;
        try { recoveryBlocked = MutationRecoveryJournal.ReadPending(MutationKind.CharacterCertificateSave) is not null; }
        finally { ApplyWorkflowState(); }
    }
    private async Task CheckSave()
    {
        if (saving) return;
        saving = true; ApplyWorkflowState();
        try
        {
            var pending = MutationRecoveryJournal.ReadPending(MutationKind.CharacterCertificateSave);
            if (pending is not null)
            {
                var saved = await RecoverableMutationWriter.CheckAsync(pending);
                MutationRecoveryJournal.ClearVerified(pending);
                if (saved is not null && student.SelectedId==saved.SubjectId) { LoadCertificates(saved.SubjectId); SelectSavedCertificate(saved.RecordId); }
                Info(saved is null ? "The earlier operation was not saved. You can review and try again." : "The earlier certificate save is confirmed.");
            }
            recoveryBlocked = false;
        }
        catch (Exception ex) { Fail(ex); }
        finally { saving = false; ApplyWorkflowState(); }
    }
    private async Task SaveOperation(SqlParameter[] args, string summary)
    {
        if (saving || recoveryBlocked) return;
        saving = true; ApplyWorkflowState();
        try
        {
            int user = CurrentUser.UserId;
            string scope = await RecoverableMutationWriter.GetScopeAsync();
            MutationIntent intent;
            MutationWriteResult outcome;
            using (var journal = MutationRecoveryJournal.Begin(MutationKind.CharacterCertificateSave, user, scope,
                RecoverableMutationWriter.PayloadHash(MutationKind.CharacterCertificateSave, user, args), summary))
            {
                intent = journal.Intent; recoveryBlocked = true;
                outcome = await RecoverableMutationWriter.SaveAsync(intent, args);
            }
            if (outcome.Outcome == MutationOutcome.Unconfirmed)
            { Warn("The save outcome is not confirmed. Use Check Save before trying again."); return; }
            MutationRecoveryJournal.ClearVerified(intent); recoveryBlocked = false;
            if (outcome.Outcome == MutationOutcome.Rejected) throw outcome.Error ?? new InvalidOperationException("The certificate was not saved.");
            var saved = outcome.Result!;
            LoadCertificates(saved.SubjectId); SelectSavedCertificate(saved.RecordId);
            Info("Certificate saved. The selected history row contains its saved exam type and details.");
        }
        catch (Exception ex)
        {
            try { recoveryBlocked=MutationRecoveryJournal.ReadPending(MutationKind.CharacterCertificateSave) is not null; }
            catch { recoveryBlocked=true; }
            Fail(ex);
        }
        finally { saving = false; ApplyWorkflowState(); }
    }
    private async Task CreateCertificate()
    {
        if (saving || recoveryBlocked || !ValidateWording() || !ValidateRequiredInputs()) return;
        if (student.SelectedId <= 0) { Warn(UiText.Prompts.SelectStudent); return; }
        bool editing=selectedCertificateRecordId>0;
        if(editing&&(!IsLatestSelectedIssue()||selectedIssueCollected))return;
        if (!editing&&HasCertificateForDraft()) { Warn("This exam-type certificate already exists. Select it to edit or issue again."); return; }
        if (string.IsNullOrWhiteSpace(rollNumber.Text) || !CharacterCertificateOptions.Conduct.Contains(remarks.Text))
        { Warn("Enter the BISE roll number and choose the conduct."); return; }
        int passingYear=CharacterCertificateOptions.PassingYear(academicYear);
        if(passingYear<=0||passingYear>certificateDate.Value.Year)
        { Warn("Choose an academic year with a passing year no later than the certificate date.");return; }
        if (certificateDate.Value.Date > DateTime.Today) { Warn(UiText.Certificates.FutureDateRefused); return; }
        string exam = examType.SelectedIndex == 0 ? "SSC" : "HSSC";
        await SaveOperation(new[] { DatabaseHelper.P("@Action", editing?"Update":"Create"), DatabaseHelper.Int("@AdmissionId", student.SelectedId),
            DatabaseHelper.Int("@CertificateRecordId",selectedCertificateRecordId),DatabaseHelper.Int("@ExpectedLatestIssueId",selectedIssueId),
            DatabaseHelper.P("@ExamType", exam), DatabaseHelper.Date("@CertificateDate", certificateDate.Value.Date),
            DatabaseHelper.P("@RollNumber", rollNumber.Text.Trim()), DatabaseHelper.P("@Remarks", remarks.Text.Trim()),DatabaseHelper.Int("@AcademicYearId",academicYear.SelectedId),CertificateCollectionFlow.VersionParameter(certificateResults,"CharacterCertificateIssueId",selectedIssueId) },
            $"{(editing?"Update":"Create")} {exam} character certificate for student {student.SelectedId}");
    }
    private async Task IssueAgain()
    {
        if (saving || recoveryBlocked || !IsLatestSelectedIssue() || !IsLatestIssueCollectedFor(selectedCertificateRecordId) || selectedStoredExamType is not ("SSC" or "HSSC")) return;
        if(draftBaseline!=DraftSnapshot()){Warn("Save your certificate changes before using Issue Again.");return;}
        try
        {
            using var preview=DatabaseHelper.QueryUser("GetCertificateReissuePreview",DatabaseHelper.P("@CertificateKind","Character"),DatabaseHelper.Int("@CertificateRecordId",selectedCertificateRecordId));
            if(preview.Rows.Count!=1||Convert.ToInt32(preview.Rows[0]["LatestIssueId"])!=selectedIssueId)
            { Warn("A newer copy exists. Refresh its history before issuing again."); return; }
            var row=preview.Rows[0];
            if (!Confirm($"Issue another {selectedStoredExamType} copy dated {DateTime.Today:dd-MMM-yyyy}?\n\nCurrent Admission name: {row["CurrentStudentName"]}\nFather name: {row["CurrentFatherName"]}\nGender: {row["CurrentGender"]}\n\nThe exam type, examination year, roll number and remarks stay as saved. Earlier copies retain their original details.", "Issue Certificate Again")) return;
            await SaveOperation(new[] { DatabaseHelper.P("@Action", "Issue"), DatabaseHelper.Int("@CertificateRecordId", selectedCertificateRecordId),
                DatabaseHelper.Int("@ExpectedLatestIssueId", selectedIssueId),DatabaseHelper.P("@ExpectedSourceHash",row["SourceHash"]) }, $"Reissue character certificate {selectedCertificateRecordId} using reviewed Admission details");
        }
        catch(Exception ex){Fail(ex);}
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
            collectButton.Enabled =
                false;
            DatabaseHelper.Execute(
                "MarkCharacterCertificateCollected",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@CharacterCertificateIssueId",
                    selectedIssueId),
                DatabaseHelper.P("@CollectedBy", collectedBy),CertificateCollectionFlow.VersionParameter(certificateResults,"CharacterCertificateIssueId",selectedIssueId));
            successMessage =
                "Certificate issuance marked as collected.";
            LoadCertificates(admissionId);
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
        try
        {
            if (selectedCertificateRecordId <= 0 || selectedIssueId <= 0 || selectedStoredExamType is not ("SSC" or "HSSC"))
            { Warn("Select a saved certificate with a recorded exam type."); return; }
            PrintCertificate(selectedCertificateRecordId, selectedIssueId);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void PrintCertificate(int certificateRecordId, int issueId)
    {
        DataTable data = DatabaseHelper.QueryUser("GetCharacterCertificatePrintData",
            DatabaseHelper.Int("@CertificateRecordId", certificateRecordId), DatabaseHelper.Int("@CharacterCertificateIssueId", issueId));
        if (data.Rows.Count == 0) { Warn(UiText.Certificates.PrintDataMissing); return; }
        string reportPath = Path.Combine(AppContext.BaseDirectory, "Reports", "CharacterCertificate.rdlc");
        if (!File.Exists(reportPath)) throw new FileNotFoundException("Character Certificate report was not found.", reportPath);
        ShowReport(FindForm(), "Character Certificate", reportPath, "CharacterCertificateData", data);
    }
    private string? PromptCollectedBy() => CertificateCollectionDialog.Show(this);
    private void LoadAcademicYears()
    {
        try
        {
            using var academicYears=CharacterCertificateOptions.ReadAcademicYears();
            CharacterCertificateOptions.BindYears(academicYear,academicYears);
            CharacterCertificateOptions.SelectYear(academicYear,0,chooseCurrent:true);
            CaptureDraftBaseline();
        }
        catch(Exception ex){Fail(ex);}
    }
    private void RefreshCertificates()
    {
        if(!ConfirmDraftDiscard())return;
        if (student.SelectedId <= 0) return;
        int selectedId = selectedIssueId;
        LoadCertificates(student.SelectedId);
        if (selectedId > 0) SelectSavedCertificate(selectedId);
    }
    private void ClearCertificateFields()
    {
        examType.SelectedIndex = -1;
        rollNumber.Clear();
        CharacterCertificateOptions.SelectConduct(remarks,"");
        CharacterCertificateOptions.SelectYear(academicYear,0,chooseCurrent:true);
        certificateDate.Value =
            DateTime.Today;
    }
    private void ClearCertificateSelection()
    {
        selectedCertificateRecordId = 0;
        selectedIssueId = 0; selectedStoredExamType = "";
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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Grid.SelectionChanged -= GridSelectionChanged;
            Grid.CurrentCellChanged -= GridSelectionChanged;
        }
        base.Dispose(disposing);
    }

    /// <summary>Opt-in workspace bridge: use the same selection and saved-copy logic as this editor.</summary>
    void IStudentCertificateWorkspaceEditor.LoadWorkspaceSelection(DataRow selectedStudent, int recordId, int issueId)
    {
        if (IsDisposed || selectedStudent.Table is null ||
            !selectedStudent.Table.Columns.Contains("AdmissionId") ||
            CertificateUi.GetInt(selectedStudent, "AdmissionId") <= 0)
            throw new ArgumentException("A valid selected student is required.", nameof(selectedStudent));
        student.SetStudent(selectedStudent);
        StudentChanged();
        if (recordId > 0)
        {
            if (!certificateResults.AsEnumerable().Any(row =>
                CertificateUi.GetInt(row, "CertificateRecordId") == recordId &&
                CertificateUi.GetInt(row, "CharacterCertificateIssueId") == issueId))
                throw new InvalidOperationException("The selected saved certificate is no longer available. Refresh the workspace.");
            SelectSavedCertificate(issueId);
        }
        else
        {
            // New preparation stays on the selected student; the ordinary New/Clear
            // action on the old form still keeps its original behavior.
            ClearCertificateSelection(); ClearCertificateFields(); CaptureDraftBaseline(); ApplyWorkflowState();
        }
    }

}

