using FMSC_SMS.Core;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class CharacterCertificatePage : V2Page, IUnsavedChanges
{
    private readonly StudentPicker student = new();
    private string? draftBaseline;
    private string DraftSnapshot()=>System.Text.Json.JsonSerializer.Serialize(new {Exam=examType.SelectedIndex,Roll=rollNumber.Text.Trim(),Remarks=remarks.Text.Trim(),Date=certificateDate.Value.Date});
    private void CaptureDraftBaseline()=>draftBaseline=DraftSnapshot();
    // Short certificate details can be retyped without a discard warning.
    public bool HasUnsavedChanges => false;
    public string UnsavedChangesMessage=>"The new certificate has unsaved details. Discard this draft?";
    private bool ConfirmDraftDiscard()=>!HasUnsavedChanges||Confirm(UnsavedChangesMessage,UiText.Dialogs.UnsavedChanges);

    private readonly ModernComboBox examType =
        HumanUi.Combo();
    private readonly ModernTextBox rollNumber =
        HumanUi.TextBox(false, 200);
    private readonly ModernTextBox remarks =
        HumanUi.TextBox(false, 500);
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    /* Finding 13 - a search debounce tick that lands while a modal (confirmation, collector prompt, print preview) is open runs inside that modal's message loop and re-binds the picker underneath… */
    private bool suppressStudentChanged;
    private bool loadingCertificates;
    private int selectedCertificateRecordId;
    private int selectedIssueId;
    private int selectedCertificateAdmissionId;
    private bool selectedIssueCollected;
    private readonly ModernButton createCertificateButton = new() {
        Text = UiText.Certificates.Create,
        Width = AppScale.Scale(170)
    };
    private readonly ModernButton clearButton = new() {
        Text = UiText.Certificates.NextStudent,
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
    /* Business rule (V20 audit F-01, confirmed by the owner): exam type is operator-selected
       wording for each print, not persisted certificate history. Keep this fixed combo;
       do not introduce an exam-type table/column or per-exam-type certificate uniqueness.
       The school chooses issuance; require wording without class or pass eligibility checks. */
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
        ConfigureFieldSuggestions();
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
        Defer(LoadFieldSuggestions);
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "The school decides whom to issue a certificate to. Choose SSC or HSSC wording for each print.");
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
        var examField=CertificateUi.Field("Certificate Wording", examType, true);
        var rollField=CertificateUi.Field("BISE Roll No.", rollNumber, true);
        fields.Controls.Add(studentField,0,0);
        fields.Controls.Add(dateField,1,0);
        fields.Controls.Add(examField,0,1);
        fields.Controls.Add(rollField,1,1);
        var remarksField = CertificateUi.Field("Remarks", remarks, true);
        fields.Controls.Add(remarksField, 0, 2);
        fields.SetColumnSpan(remarksField, 2);
        foreach(var field in fields.Controls.OfType<TableLayoutPanel>())
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        certificateDate.MaximumSize=Size.Empty;
        examType.MaximumSize=Size.Empty;
        foreach(Control input in new Control[]{student,certificateDate,examType,rollNumber,remarks})
            input.Dock=DockStyle.Top;
        // Row-major keyboard flow follows the visible certificate fields.
        foreach (Control field in fields.Controls)
            field.TabIndex = fields.GetRow(field) * fields.ColumnCount + fields.GetColumn(field);
        details.Content.Controls.Add(fields);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, clearButton);
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
            CertificateButtonRow.Create(issueAgainButton, collectButton, printButton, refreshButton);
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
                fields.SetColumnSpan(remarksField,fields.ColumnCount);
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
            UiText.Certificates.IssueGuidance + " Current academic year only.");
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
            SelectSavedCertificate(0);
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
            // StudentChanged clears wording on a student switch. Choosing this student's
            // history row must preserve a wording choice already made by the operator.
            if (student.SelectedId != admissionId)
                examType.SelectedIndex = -1;
            selectedCertificateRecordId =
                certificateRecordId;
            selectedIssueId =
                issueId;
            selectedCertificateAdmissionId =
                admissionId;
            selectedIssueCollected =
                string.Equals(
                    GetString(row, "CollectionStatus"), "Collected", StringComparison.OrdinalIgnoreCase);
            ApplyWorkflowState();
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
        bool hasSelectedIssue =
            selectedIssueId > 0 &&
            selectedCertificateRecordId > 0;
        bool examTypeChosen = examType.SelectedIndex >= 0;
        student.Enabled = true;
        examType.Enabled =
            hasStudent;
        /* One certificate on file means no new Create; another copy is Issue Again. */
        bool canCreate =
            hasStudent &&
            examTypeChosen &&
            !hasCertificate;
        certificateDate.Enabled =
            canCreate;
        rollNumber.Enabled =
            canCreate;
        remarks.Enabled =
            canCreate;
        createCertificateButton.Enabled =
            canCreate;
        clearButton.Enabled =
            true;
        refreshButton.Enabled =
            hasStudent;
        issueAgainButton.Enabled =
            hasSelectedIssue &&
            IsLatestIssueCollectedFor(selectedCertificateRecordId);
        collectButton.Enabled =
            hasSelectedIssue &&
            !selectedIssueCollected;
        printButton.Enabled =
            hasSelectedIssue &&
            selectedIssueCollected &&
            CurrentUser.HasPermission("Certificates.Print");
        if (!hasStudent)
        {
            SetStatus(UiText.Prompts.SearchForStudent);
            return;
        }
        if (!examTypeChosen)
        {
            SetStatus(
                hasCertificate
                    ? "Choose SSC/HSSC wording before printing. Select a collected copy to print or issue again; wording applies to the print only."
                    : "Choose SSC/HSSC and complete the details to create the current-year certificate.");
            return;
        }
        if (!hasCertificate)
        {
            SetStatus(
                $"Complete the details to create the current-year {ExamTypeLabels[examType.SelectedIndex]} certificate.");
            return;
        }
        if (hasSelectedIssue)
        {
            if (!selectedIssueCollected)
            {
                SetStatus("Not collected. Use Mark Collected before printing or issuing again.");
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
        /* Finding 6 - one per student, not one per exam type: say that plainly instead of naming the exam type the certificate does not record. */
        SetStatus(
            "Certificate already exists. Select it to print, or use Issue Again for another copy.");
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
    private void CreateCertificate()
    {
        int printExamType = examType.SelectedIndex;
        if (!ValidateWording()) return;
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
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (examType.SelectedIndex < 0)
            {
                Warn("Select the exam type (SSC or HSSC).");
                return;
            }
            /* Finding 6 - the database allows one character certificate per student (58005). Say so here rather than letting the confirmation run and the write fail. */
            if (certificateResults.Rows.Count > 0)
            {
                Warn("This student already has a character certificate. Print it, or use Issue Again for another copy.");
                return;
            }
            if (string.IsNullOrWhiteSpace(rollNumber.Text))
            {
                Warn("Enter the student's BISE Roll No.");
                return;
            }
            if (string.IsNullOrWhiteSpace(remarks.Text))
            {
                Warn("Enter remarks for the character certificate.");
                return;
            }
            DateTime certDate =
                certificateDate.Value.Date;
            if (certDate > DateTime.Today)
            {
                Warn(UiText.Certificates.FutureDateRefused);
                return;
            }
            DialogResult confirm =
                ModernMessageDialog.Show(
                    FindForm(),
                    $"Creating this {ExamTypeLabels[examType.SelectedIndex]} character certificate will:\n\n" +
                    "• save the character certificate for the current academic year\n" +
                    "• record the original certificate issuance\n\n" +
                    UiText.Dialogs.Continue,
                    "Create Character Certificate",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;
            int admissionId =
                student.SelectedId;
            createCertificateButton.Enabled =
                false;
            DatabaseHelper.Execute(
                "CreateCharacterCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AdmissionId",
                    admissionId),
                /* The exam type is deliberately NOT passed. dbo.CreateCharacterCertificate declares only @UserId, @AdmissionId, @CertificateDate, @RollNumber and @Remarks (verified against the live database, not the .dacpac), and dbo.CharacterCertificates has no ExamType column. */
                DatabaseHelper.Date(
                    "@CertificateDate",
                    certDate),
                DatabaseHelper.P(
                    "@RollNumber",
                    DatabaseHelper.OptionalText(
                        rollNumber.Text)),
                DatabaseHelper.P(
                    "@Remarks",
                    DatabaseHelper.OptionalText(remarks.Text)));
            successMessage =
                $"{ExamTypeLabels[examType.SelectedIndex]} character certificate created successfully for the current academic year. " +
                "The original issuance has also been recorded.";
            AddSuggestionIfNew(remarks, remarks.Text);
            ClearCertificateFields();
            LoadCertificates(admissionId);
            SelectSavedCertificate(0);
            if (printExamType >= 0) examType.SelectedIndex = printExamType;
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
        int printExamType = examType.SelectedIndex;
        // Finding 12 - see CreateCertificate: the confirmation is set last.
        string? successMessage =
            null;
        // Finding 13 - this path opens a modal confirmation; park the search.
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (selectedCertificateRecordId <= 0)
            {
                Warn("Select a certificate issuance from the history first.");
                return;
            }
            if (!IsLatestIssueCollectedFor(selectedCertificateRecordId))
            {
                Warn("The current certificate issuance must be collected before another issuance can be created.");
                return;
            }
            int certificateRecordId =
                selectedCertificateRecordId;
            DialogResult confirm =
                ModernMessageDialog.Show(
                    FindForm(),
                    "The certificate will be issued again and a new issuance history record will be created.\n\n" +
                    "The previous issuance history will remain unchanged.\n\n" +
                    "Continue?",
                    UiText.Certificates.IssueAgain,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;
            issueAgainButton.Enabled =
                false;
            DatabaseHelper.Execute(
                "IssueCharacterCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int("@CertificateRecordId", certificateRecordId));
            successMessage =
                "Certificate issued again successfully. The new issuance has been added to the history.";
            LoadCertificates(student.SelectedId);
            SelectSavedCertificate(0);
            if (printExamType >= 0) examType.SelectedIndex = printExamType;
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
    private void MarkCollected()
    {
        int savedIssueId = selectedIssueId;
        int printExamType = examType.SelectedIndex;
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
                DatabaseHelper.P("@CollectedBy", collectedBy));
            successMessage =
                "Certificate issuance marked as collected.";
            LoadCertificates(admissionId);
            SelectSavedCertificate(0);
            if (printExamType >= 0) examType.SelectedIndex = printExamType;
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
            if (selectedCertificateRecordId <= 0)
            {
                Warn("Select a certificate issuance from the history first.");
                return;
            }
            if (selectedIssueId <= 0)
            {
                Warn(UiText.Certificates.InvalidIssuance);
                return;
            }
            if (!selectedIssueCollected)
            {
                Warn("This certificate has not been collected yet. It cannot be printed.");
                return;
            }
            int examTypeIndex = examType.SelectedIndex;
            if (examTypeIndex < 0 || examTypeIndex >= ExamTypeLabels.Length)
            {
                Warn("Choose SSC or HSSC wording before printing this certificate.");
                examType.Focus();
                return;
            }
            // Use the operator's selected wording directly for this print.
            string examWording = ExamTypeLabels[examTypeIndex];
            int certificateRecordId = selectedCertificateRecordId;
            PrintCertificate(certificateRecordId, examWording);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
        }
    }
    private void PrintCertificate(int certificateRecordId, string examWording)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetCharacterCertificatePrintData",
                DatabaseHelper.Int("@CertificateRecordId", certificateRecordId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        if (!data.Columns.Contains("ExamTypeDisplay"))
            data.Columns.Add("ExamTypeDisplay", typeof(string));
        foreach (DataRow row in data.Rows)
            row["ExamTypeDisplay"] = examWording;
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "CharacterCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Character Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "Character Certificate", reportPath, "CharacterCertificateData", data);
    }
    private string? PromptCollectedBy()
    {
        using var form =
            new ApplicationWindowForm {
                Text =
                    UiText.Certificates.Collection,
                StartPosition =
                    FormStartPosition.CenterParent,
                FormBorderStyle =
                    FormBorderStyle.FixedDialog,
                MinimizeBox =
                    false,
                MaximizeBox =
                    false,
                ShowInTaskbar =
                    false,
                ClientSize =
                    new Size(AppScale.Scale(450), AppScale.Scale(205))
            };
        int labelHeight =
            Math.Max(
                45,
                AppMetrics.TextHeight(AppTheme.NormalFont) + 24);
        int inputHeight =
            Math.Max(
                36,
                AppMetrics.InputControlHeight);
        int buttonAreaHeight =
            Math.Max(
                68,
                AppMetrics.ButtonHeight + 28);
        int requiredClientHeight =
            labelHeight +
            inputHeight +
            buttonAreaHeight +
            32;
        form.ClientSize =
            new Size(
                450,
                Math.Max(205, requiredClientHeight));
        var label =
            new Label {
                Text =
                    "Name of person collecting the certificate:",
                AutoSize =
                    false,
                Dock =
                    DockStyle.Top,
                Height =
                    labelHeight,
                Padding =
                    new Padding(
                        10,
                        10,
                        10,
                        5),
                Font =
                    AppTheme.NormalFont
            };
        var text =
            new ModernTextBox {
                Dock =
                    DockStyle.Top,
                Height =
                    inputHeight,
                Margin =
                    new Padding(10),
                Font =
                    AppTheme.NormalFont,
                MaxLength =
                    400
            };
        var buttons =
            new FlowLayoutPanel {
                Dock =
                    DockStyle.Bottom,
                Height =
                    buttonAreaHeight,
                FlowDirection =
                    FlowDirection.RightToLeft,
                Padding =
                    new Padding(10)
            };
        var ok =
            new ModernButton {
                Text =
                    "Collect",
                DialogResult =
                    DialogResult.OK,
                ThemeRole =
                    UiDesignSystem.Actions.Success,
                Width =
                    AppScale.Scale(100),
                Height =
                    AppMetrics.ButtonHeight,
                Margin =
                    new Padding(4)
            };
        var cancel =
            new ModernButton {
                Text =
                    UiText.Actions.Cancel,
                ThemeRole =
                    UiDesignSystem.Actions.Secondary,
                Width =
                    AppScale.Scale(100),
                Height =
                    AppMetrics.ButtonHeight,
                Margin =
                    new Padding(4)
            };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        form.Controls.Add(buttons);
        form.Controls.Add(text);
        form.Controls.Add(label);
        form.AcceptButton =
            ok;
        form.CancelButton =
            cancel;
        cancel.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        form.Shown +=
            (_, _) =>
                text.Focus();
        if (form.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return null;
        }
        string value =
            text.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            Warn("The collector name is required.");
            return null;
        }
        return value;
    }
    private void ConfigureFieldSuggestions()
    {
        remarks.AutoCompleteMode =
            AutoCompleteMode.SuggestAppend;
        remarks.AutoCompleteSource =
            AutoCompleteSource.CustomSource;
        remarks.AutoCompleteCustomSource =
            new AutoCompleteStringCollection();
    }
    private void LoadFieldSuggestions()
    {
        LoadFieldSuggestions(remarks, "Remarks");
    }
    private void LoadFieldSuggestions(ModernTextBox field, string columnField)
    {
        try
        {
            DataTable table =
                DatabaseHelper.QueryUser(
                    "GetCharacterCertificateFieldSuggestions",
                    DatabaseHelper.P("@Field", columnField));
            var source =
                new AutoCompleteStringCollection();
            foreach (DataRow row in table.Rows)
            {
                string? value =
                    Convert.ToString(row["Value"]);
                if (!string.IsNullOrWhiteSpace(value))
                    source.Add(value);
            }
            field.AutoCompleteCustomSource =
                source;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(LoadFieldSuggestions)} failed: {ex.Message}");
        }
    }
    private static void AddSuggestionIfNew(ModernTextBox field, string? value)
    {
        value =
            value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (field.AutoCompleteCustomSource
                .Cast<string>()
                .Any(
                    existing =>
                        string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        field.AutoCompleteCustomSource.Add(value);
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
        remarks.Clear();
        certificateDate.Value =
            DateTime.Today;
    }
    private void ClearCertificateSelection()
    {
        selectedCertificateRecordId = 0;
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
}

