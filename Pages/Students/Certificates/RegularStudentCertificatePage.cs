using FMSC_SMS.Core;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class RegularStudentCertificatePage : V2Page, IUnsavedChanges, IBusyPage
{
    private const string MissingBirthDateMessage = "Date of birth is missing. Add it to the student's Admission record, then reselect the student to create this certificate.";
    private DateTime? newCertificateDateBaseline;
    private int newDraftStudent;
    private readonly Func<string,bool>? discardConfirmation;
    private readonly Func<int,DataTable> readCertificates;
    // Short certificate details can be retyped without a discard warning.
    public bool HasUnsavedChanges => reissue.Blocked;
    private bool ConfirmDraftDiscard(string message)=>!HasUnsavedChanges||
        (discardConfirmation?.Invoke(message)??Confirm(message,UiText.Dialogs.UnsavedChanges));
    private void CaptureNewCertificateBaseline()
    {
        newDraftStudent=student.SelectedId;
        newCertificateDateBaseline=newDraftStudent>0?certificateDate.Value.Date:null;
    }
    public string UnsavedChangesMessage => "Discard the unsaved certificate changes?";
    public bool IsBusy => creatingCertificate || reissuing;
    public string BusyMessage => "Please wait for the certificate to finish saving.";
    private bool restoringCertificateSelection;
    private bool HasCertificateEdits => selectedRegularStudentCertificateId > 0 &&
        (className.Text.Trim() != savedClassName.Trim() || certificateDate.Value.Date != savedCertificateDate);
    private readonly StudentPicker student = new();
    private readonly ModernTextBox dateOfBirth =
        HumanUi.TextBox(false, 150);
    private readonly ModernTextBox className =
        HumanUi.TextBox(false, 100);
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    /* Finding 13 - a search debounce tick that lands while a modal (print preview, confirmation) is open runs inside that modal's message loop and re-binds the picker underneath it. */
    private bool suppressStudentChanged;
    private int selectedRegularStudentCertificateId;
    private int selectedIssueId;
    private bool reissuing;
    private readonly CertificateReissueFlow reissue = new(MutationKind.RegularCertificateReissue);
    private readonly ModernButton checkReissue = new() { Text="Check Reissue",Width=AppScale.Scale(170) };
    private bool loadingCertificates;
    private bool creatingCertificate;
    private bool certificatesLoaded;
    private string savedClassName = "";
    private DateTime savedCertificateDate;
    private readonly ModernButton createCertificateButton = new() {
        Text = UiText.Certificates.Create,
        Width = AppScale.Scale(170)
    };
    private readonly ModernButton updateCertificateButton = new() {
        Text = UiText.Certificates.IssueAgainAction,
        Width = AppScale.Scale(170)
    };
    private readonly ModernButton newButton = new() {
        Text = UiText.Certificates.New,
        Width = AppScale.Scale(110)
    };
    private readonly ModernButton clearButton = new() {
        Text = UiText.Certificates.NextStudent,
        Width = AppScale.Scale(120)
    };
    private readonly ModernButton printButton = new() {
        Text = UiText.Actions.Print,
        Width = AppScale.Scale(120)
    };
    private readonly ModernButton refreshButton = new() {
        Text = UiText.Actions.Refresh,
        Width = AppScale.Scale(120)
    };
    public RegularStudentCertificatePage():this(true){ }
    internal RegularStudentCertificatePage(bool initialize,Func<string,bool>? discardConfirmation=null,Func<int,DataTable>? readCertificates=null)
        : base("Regular Student Certificate", "Record and print regular student (proof of placement) certificates.")
    {
        this.discardConfirmation=discardConfirmation;
        this.readCertificates=readCertificates??(id=>DatabaseHelper.QueryUser("GetRegularStudentCertificateRecords",DatabaseHelper.Int("@AdmissionId",id)));
        className.MaxLength = 100;
        className.ReadOnly = true;
        className.TabStop = false;
        dateOfBirth.ReadOnly = true;
        dateOfBirth.TabStop = false;
        // Duplicate classes are checked before offering Create and again before saving.
        createCertificateButton.Click += async (_, _) => {
            if (!createCertificateButton.Enabled)
                return;
            /* Finding 10 (D-H3, ported from the Bank page) - the write owns the page until it settles, so the student picker and the records grid are held too. */
            var heldDuringCreate = new List<(Control Control, bool WasEnabled)>
            {
                (student, student.Enabled),
                (Grid, Grid.Enabled)
            };
            createCertificateButton.Enabled = false;
            creatingCertificate = true;
            ApplyWorkflowState();
            foreach (var (control, _) in heldDuringCreate)
                control.Enabled = false;
            try
            {
                await CreateCertificate();
            }
            finally
            {
                creatingCertificate = false;
                foreach (var (control, wasEnabled) in heldDuringCreate)
                {
                    if (!control.IsDisposed)
                        control.Enabled = wasEnabled;
                }
                if (!IsDisposed) ApplyWorkflowState();
            }
        };
        updateCertificateButton.Click += async (_, _) => await ReissueCertificateAsync(false);
        checkReissue.Click += async (_, _) => await ReissueCertificateAsync(true);
        if(initialize)Defer(()=>{reissue.Load();ApplyWorkflowState();});
        newButton.Click +=
            (_, _) => StartNewCertificate();
        clearButton.Click +=
            (_, _) => ClearForm();
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
        // Show all students; explain missing certificate details after selection.
        student.SelectionChangeGuard = _ => !creatingCertificate &&
            ConfirmDraftDiscard("Discard the unsaved certificate changes and choose another student?");
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
        Grid.SelectionChanged += GridSelectionChanged;
        Grid.CurrentCellChanged += GridSelectionChanged;
        className.TextChanged += (_, _) => { if (!loadingCertificates) ApplyWorkflowState(); };
        certificateDate.ValueChanged += (_, _) => { if (!loadingCertificates) ApplyWorkflowState(); };
        if(initialize)Defer(
            () => {
                ResetToStart();
            });
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "Saved copies keep their original details. Issue Again uses the current school birth date and preserves earlier copies.");
        var fields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 2; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
        fields.Controls.Add(
            CertificateUi.Field("Student", student, true), 0, 0);
        fields.Controls.Add(
            CertificateUi.Field(UiText.Certificates.Date, certificateDate, true), 1, 0);
        fields.Controls.Add(
            CertificateUi.Field("Date of Birth", dateOfBirth, true), 0, 1);
        fields.Controls.Add(
            CertificateUi.Field("Class (School Record)", className, true), 1, 1);
        details.Content.Controls.Add(fields);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, updateCertificateButton, newButton, clearButton, checkReissue);
        certButtonRow.Dock =
            DockStyle.Bottom;
        details.Content.Controls.Add(certButtonRow);
        var records =
            HumanUi.Card(
                "Regular Student Certificates",
                "Select a saved copy to reprint its original details, or use Issue Again for a new copy with the corrected school birth date.");
        var certificateActions =
            CertificateButtonRow.Create(printButton, refreshButton);
        certificateActions.Dock =
            DockStyle.Bottom;
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
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(2)
                + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(records, 0, 1);
        CertificateUi.CompactFields(details,fields,root,new[]{40f,20f,20f,20f},new[]{(0,0,1),(1,0,1),(2,0,1),(3,0,1)},1320);
        SetContent(
            root,
            UiText.Certificates.StudentGuidance);
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
            if (matches.Length > 0)
            {
                DataRow row =
                    matches[0];
                DateTime? dob =
                    GetDate(row, "DateOfBirth");
                dateOfBirth.Text =
                    dob.HasValue
                        ? dob.Value.ToString("dd-MMM-yyyy")
                        : "Not on record";
                className.Text =
                    GetString(row, "ClassName");
            }
            LoadCertificates(admissionId);
            SelectCurrentClassCertificate();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally{if(selectedRegularStudentCertificateId==0)CaptureNewCertificateBaseline();}
    }
    private void LoadCertificates(int admissionId)
    {
        loadingCertificates = true;
        certificatesLoaded = false;
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
            DataTable table = readCertificates(admissionId);
            certificateResults =
                table.Copy();
            if (certificateResults.Columns.Contains("RegularStudentCertificateIssueId"))
                certificateResults=CertificateRecordOrder.NewestFirst(certificateResults,"RegularStudentCertificateIssueId");
            else if (certificateResults.Columns.Contains("CertificateDate") && certificateResults.Columns.Contains("RegularStudentCertificateId"))
            {
                certificateResults.DefaultView.Sort = "CertificateDate DESC, RegularStudentCertificateId DESC";
                certificateResults = certificateResults.DefaultView.ToTable();
            }
            Bind(certificateResults);
            FormatCertificateGrid();
            certificatesLoaded = true;
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException67)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException67, "RegularStudentCertificatePage: LoadCertificates fallback");
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
        finally { loadingCertificates = false; }
    }
    internal static int ExistingClassCertificateId(DataTable records, string className)
    {
        if (!records.Columns.Contains("ClassName") || !records.Columns.Contains("RegularStudentCertificateId"))
            return 0;
        string normalizedClass = className.Trim();
        if (normalizedClass.Length == 0) return 0;
        return records.AsEnumerable()
            .Where(row => string.Equals(GetString(row, "ClassName").Trim(), normalizedClass, StringComparison.OrdinalIgnoreCase))
            .Select(row => GetInt(row, "RegularStudentCertificateId"))
            .DefaultIfEmpty(0).Max();
    }
    private void SelectCurrentClassCertificate()
    {
        int id = ExistingClassCertificateId(certificateResults, className.Text);
        if (id > 0) SelectCertificate(id);
    }
    private void FormatCertificateGrid()
    {
        LockGridOrder();
        int displayIndex = 0;
        foreach (string name in new[] { "CertificateDate", "RegularStudentCertificateId", "RegularStudentCertificateIssueId", "AdmissionNumber",
            "StudentName", "FatherName", "ClassName", "DateOfBirth", "CreatedAt", "UpdatedAt" })
            if (Grid.Columns.Contains(name)) Grid.Columns[name].DisplayIndex = displayIndex++;
        SetGridWidth("RegularStudentCertificateId", 110);
        SetGridWidth("RegularStudentCertificateIssueId",100);
        if(Grid.Columns.Contains("RegularStudentCertificateIssueId"))Grid.Columns["RegularStudentCertificateIssueId"]!.HeaderText="Copy No.";
        if (Grid.Columns.Contains("RegularStudentCertificateId"))
        {
            Grid.Columns["RegularStudentCertificateId"]!.HeaderText =
                "Certificate No.";
        }
        SetGridWidth("AdmissionNumber", 80);
        SetGridWidth("StudentName", 150);
        SetGridWidth("FatherName", 130);
        SetGridWidth("DateOfBirth", 110);
        SetGridWidth("ClassName", 100);
        SetGridWidth("CertificateDate", 110);
        SetGridWidth("CreatedAt", 145);
        SetGridWidth("UpdatedAt", 145);
        HideGridColumn("AdmissionId");
        HideGridColumn("IssuedByUserId");
        HideGridColumn("PreviousIssueId");
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
        if (restoringCertificateSelection || loadingCertificates || Grid.CurrentRow is null || Grid.SelectedRows.Count == 0)
        {
            return;
        }
        try
        {
            DataGridViewRow gridRow =
                Grid.CurrentRow;
            if (gridRow.IsNewRow)
                return;
            if (gridRow.DataBoundItem is not DataRowView view) return;
            DataRow row = view.Row;
            int regularStudentCertificateId =
                GetInt(row, "RegularStudentCertificateId");
            if (regularStudentCertificateId <= 0)
            {
                ClearCertificateSelection();
                SetStatus("The selected regular student certificate contains invalid data.", true);
                return;
            }
            restoringCertificateSelection = true;
            try
            {
                if (!ConfirmCertificateSelection(FindForm(), Grid, "RegularStudentCertificateId",
                    selectedRegularStudentCertificateId, regularStudentCertificateId, HasUnsavedChanges,
                    ()=>ConfirmDraftDiscard("Discard the unsaved certificate changes and select another certificate?"))) return;
            }
            finally { restoringCertificateSelection = false; }
            selectedRegularStudentCertificateId =
                regularStudentCertificateId;
            selectedIssueId=GetInt(row,"RegularStudentCertificateIssueId");
            savedClassName = GetString(row,"ClassName");
            savedCertificateDate = GetDate(row,"CertificateDate")?.Date ?? DateTime.Today;
            DateTime? dob =
                GetDate(row, "DateOfBirth");
            dateOfBirth.Text =
                dob.HasValue
                    ? dob.Value.ToString("dd-MMM-yyyy")
                    : "Not on record";
            className.Text =
                GetString(row, "ClassName");
            DateTime? certDate =
                GetDate(row, "CertificateDate");
            certificateDate.Value =
                certDate ?? DateTime.Today;
            ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            ClearCertificateSelection();
            SetStatus($"Unable to read the selected regular student certificate: {DbMessage.UserMessage(ex)}", true);
        }
    }
    private void ApplyWorkflowState()
    {
        bool hasStudent =
            student.SelectedId > 0;
        bool hasSelection =
            selectedRegularStudentCertificateId > 0;
        bool dirty = HasCertificateEdits;
        student.Enabled = !IsBusy;
        dateOfBirth.Enabled = false;
        dateOfBirth.ReadOnly = true;
        certificateDate.Enabled = hasStudent && !hasSelection && !IsBusy;
        className.Enabled = hasStudent && !creatingCertificate;
        createCertificateButton.Enabled =
            hasStudent && HasRecordedBirthDate() &&
            !hasSelection && certificatesLoaded && ExistingClassCertificateId(certificateResults, className.Text) == 0 && !IsBusy && !reissue.Blocked;
        updateCertificateButton.Enabled =
            hasStudent && hasSelection && selectedIssueId>0 && !IsBusy && !reissue.Blocked;
        checkReissue.Visible=reissue.Blocked;
        checkReissue.Enabled=reissue.Blocked&&!IsBusy;
        newButton.Enabled = hasStudent && !creatingCertificate;
        clearButton.Enabled = !creatingCertificate;
        refreshButton.Enabled =
            hasStudent && !creatingCertificate;
        printButton.Enabled =
            hasSelection && selectedIssueId>0 && !dirty && !IsBusy && CurrentUser.HasPermission("Certificates.Print");
        if(reissue.Blocked){SetStatus("A certificate reissue needs checking. Use Check Reissue before issuing another copy.",true);return;}
        if (!hasStudent)
        {
            SetStatus(
                UiText.Prompts.SearchForStudent);
            return;
        }
        if (hasSelection)
        {
            SetStatus(
                dirty ? "Save your changes before printing this certificate."
                    : "Certificate ready to print.");
            return;
        }
        if (!HasRecordedBirthDate())
        {
            SetStatus(MissingBirthDateMessage, true);
            return;
        }
        /* Finding 8 - say so up front when the student is not active: the certificate is proof of enrolment, and Create asks for a confirmation. */
        string status =
            GetSelectedStudentStatus();
        SetStatus(
            !string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                ? $"Student status: {status}. Confirm the class; creation requires confirmation. Select a saved certificate to edit or print."
                : "Confirm the class to create. Select a saved certificate to edit or print.");
    }
    private bool HasRecordedBirthDate() =>
        student.SelectedRow is DataRow row && GetDate(row, "DateOfBirth").HasValue;

    /// <summary>Finding 8 - the status of the selected student, from the row the picker was bound from. Empty when it is not known; no query is made.</summary>
    private string GetSelectedStudentStatus()
    {
        if (student.SelectedId <= 0)
            return string.Empty;
        DataRow[] matches =
            studentResults.Select($"AdmissionId = {student.SelectedId}");
        return matches.Length == 0
            ? string.Empty
            : GetString(matches[0], "Status");
    }
    private async Task CreateCertificate()
    {
        if (!ValidateRequiredInputs())
            return;
        /* Finding 12 - Info writes the status line, but the reload below calls Bind ("N record(s)") and ApplyWorkflowState after it. */
        string? successMessage =
            null;
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (!HasRecordedBirthDate())
            {
                Warn(MissingBirthDateMessage);
                return;
            }
            if (!ValidateCertificateFields())
                return;
            DataTable existing = await DatabaseHelper.QueryUserAsync(
                "GetRegularStudentCertificateRecords",
                DatabaseHelper.Int("@AdmissionId", student.SelectedId));
            if (IsDisposed) return;
            int existingId = ExistingClassCertificateId(existing, className.Text);
            if (existingId > 0)
            {
                LoadCertificates(student.SelectedId);
                SelectCertificate(existingId);
                successMessage = "A certificate already exists for this class. You can update its date or print it again.";
                return;
            }
            /* Finding 8 - this certificate says the student IS enrolled, so issuing one for a student marked Left (or any other non-active status) is a mismatch. dbo.CreateRegularStudentCertificate allows it. */
            string studentStatus =
                GetSelectedStudentStatus();
            if (!string.IsNullOrWhiteSpace(studentStatus) &&
                !string.Equals(studentStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                // Finding 13 - the confirmation is modal; park the search while it is open.
                bool proceed;
                try
                {
                    proceed =
                        Confirm(
                            $"This student is marked {studentStatus}. Issue a Regular Student certificate anyway?",
                            "Regular Student Certificate");
                }
                finally
                {
                }
                if (!proceed)
                {
                    Warn($"No certificate was created. The student is marked {studentStatus}.");
                    return;
                }
            }
            int admissionId =
                student.SelectedId;
            DateTime certDate =
                certificateDate.Value.Date;
            createCertificateButton.Enabled = false;
            // awaited so the caller's disabled button covers the whole write. @UserId is passed explicitly and equals CurrentUser.UserId, so ExecuteUserAsync's automatic injection supplies the same value.
            await DatabaseHelper.ExecuteUserAsync(
                "CreateRegularStudentCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AdmissionId",
                    admissionId),
                DatabaseHelper.Date(
                    "@CertificateDate",
                    certDate),
                DatabaseHelper.P(
                    "@ClassName",
                    DatabaseHelper.OptionalText(className.Text)));
            successMessage =
                "Regular student certificate created successfully.";
            /* refresh only when the student this certificate was created for is still the selected one. The success message above stays: the write did happen. */
            if (IsDisposed || student.SelectedId != admissionId)
                return;
            ClearCertificateFields();
            ClearCertificateSelection();
            LoadCertificates(admissionId);
            SelectCertificate(0);
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
    private async Task ReissueCertificateAsync(bool checkOnly)
    {
        if(IsBusy)return;
        int certificateId=selectedRegularStudentCertificateId;
        if(!checkOnly&&certificateId<=0){Warn("Select a saved certificate to issue again.");return;}
        reissuing=true;Enabled=false;
        try
        {
            MutationResult? saved=checkOnly?await reissue.CheckAsync():await reissue.IssueAsync(FindForm(),certificateId);
            if(IsDisposed)return;
            if(saved is not null)
            {
                if(student.SelectedId==saved.SubjectId)
                {
                    LoadCertificates(saved.SubjectId);
                    SelectCertificate(saved.OriginId,saved.RecordId);
                }
                Info("The new certificate copy is saved. Earlier copies keep their original birth dates and details.");
            }
            else if(checkOnly)Info("The earlier reissue was not saved. You may review and issue again.");
        }
        catch(Exception ex){Fail(ex);}
        finally{reissuing=false;if(!IsDisposed){Enabled=true;ApplyWorkflowState();}}
    }
    private bool ValidateCertificateFields()
    {
        if (string.IsNullOrWhiteSpace(className.Text))
        {
            Warn("No class was found for this student. Check the admission record before creating the certificate.");
            return false;
        }
        if (certificateDate.Value.Date > DateTime.Today)
        {
            Warn(UiText.Certificates.FutureDateRefused);
            return false;
        }
        return true;
    }
    private void SelectCertificate(int id,int issueId=0)
    {
        if (id == 0 && certificateResults.Rows.Count > 0)
            id = certificateResults.AsEnumerable().Max(row => GetInt(row,"RegularStudentCertificateId"));
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.DataBoundItem is not DataRowView view ||
                (id > 0 && GetInt(view.Row,"RegularStudentCertificateId") != id) ||
                (issueId>0&&GetInt(view.Row,"RegularStudentCertificateIssueId")!=issueId)) continue;
            Grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().First(cell => cell.Visible);
            row.Selected = true;
            GridSelectionChanged(Grid,EventArgs.Empty);
            return;
        }
    }
    private void StartNewCertificate()
    {
        if (creatingCertificate) return;
        ClearForm();
    }
    private void PrintSelected()
    {
        if (!printButton.Enabled) return;
        // Finding 13 - the preview is modal; park the search while it is open.
        try
        {
            if (selectedRegularStudentCertificateId <= 0)
            {
                Warn("Select a regular student certificate from the list first.");
                return;
            }
            PrintCertificate(selectedRegularStudentCertificateId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
        }
    }
    private void PrintCertificate(int regularStudentCertificateId)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetRegularStudentCertificatePrintData",
                DatabaseHelper.Int("@RegularStudentCertificateId", regularStudentCertificateId),
                DatabaseHelper.Int("@RegularStudentCertificateIssueId",selectedIssueId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "RegularStudentCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Regular Student Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "Regular Student Certificate", reportPath, "RegularStudentCertificateData", data);
    }
    private void RefreshCertificates()
    {
        if (creatingCertificate||student.SelectedId<=0)return;
        if(!ConfirmDraftDiscard("Discard the unsaved changes and refresh certificates?"))return;
        if (selectedRegularStudentCertificateId > 0)
        {
            int id = selectedRegularStudentCertificateId;
            LoadCertificates(student.SelectedId);
            SelectCertificate(id);
            return;
        }
        StudentChanged();
    }
    private void ClearCertificateFields()
    {
        dateOfBirth.Clear();
        className.Clear();
        certificateDate.Value =
            DateTime.Today;
        CaptureNewCertificateBaseline();
    }
    private void ClearCertificateSelection()
    {
        selectedRegularStudentCertificateId = 0;
        selectedIssueId=0;
        newCertificateDateBaseline=null;
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
    private void ClearForm(bool confirmDiscard = true)
    {
        if (creatingCertificate) return;
        if(confirmDiscard&&!ConfirmDraftDiscard("Discard the unsaved changes and choose another student?"))return;
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
        student.Focus();
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
