using FMSC_SMS.Core;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class NoObjectionCertificatePage : V2Page, IUnsavedChanges, IBusyPage
{
    public bool HasUnsavedChanges => HasCertificateEdits;
    public string UnsavedChangesMessage => "Discard the unsaved certificate changes?";
    public bool IsBusy => creatingCertificate;
    public string BusyMessage => "Please wait for the certificate to finish saving.";
    private bool creatingCertificate;
    private readonly StudentPicker student = new();
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    private string? savedEditorSnapshot;
    private string EditorSnapshot() => string.Join("\u001f", certificateDate.Value.Date.ToString("yyyy-MM-dd"));
    private bool HasCertificateEdits => selectedNoObjectionCertificateId > 0 && savedEditorSnapshot is not null && savedEditorSnapshot != EditorSnapshot();
    /* Finding 13 - a search debounce tick that lands while a modal (print preview, confirmation) is open runs inside that modal's message loop and re-binds the picker underneath it. */
    private bool suppressStudentChanged;
    private int selectedNoObjectionCertificateId;
    private readonly ModernButton createCertificateButton = new() {
        Text = UiText.Certificates.Create,
        Width = AppScale.Scale(170)
    };
    private readonly ModernButton updateCertificateButton = new() {
        Text = UiText.Certificates.Update,
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
    public NoObjectionCertificatePage()
        : base("No Objection Certificate", "Record and print No Objection Certificates (NOC) for students.")
    {
        /* dbo.CreateNoObjectionCertificate has no duplicate guard, and the certificate table has no unique constraint on (AdmissionId, ...). */
        createCertificateButton.Click += async (_, _) => {
            if (!createCertificateButton.Enabled)
                return;
            /* Finding 10 (D-H3, ported from the Bank page) - the write owns the page until it settles, so the student picker and the records grid are held too. */
            var heldDuringCreate = new List<(Control Control, bool WasEnabled)>
            {
                (student, student.Enabled),
                (Grid, Grid.Enabled)
            };
            creatingCertificate = true;
            createCertificateButton.Enabled = false;
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
            }
        };
        updateCertificateButton.Click +=
            (_, _) => UpdateCertificate();
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
        StudentSearchCache.Warm();
        student.SelectionChangeGuard = _ => !creatingCertificate &&
            (!HasCertificateEdits || Confirm("Discard the unsaved certificate changes and choose another student?", UiText.Dialogs.UnsavedChanges));
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
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "Find the student and set the certificate date, then create the NOC. Student Name and Father Name come from the student's record; Certificate Date can be changed later by selecting a row below.");
        var fields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 1; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
        fields.Controls.Add(
            CertificateUi.Field("Student", student, true), 0, 0);
        fields.Controls.Add(
            CertificateUi.Field(UiText.Certificates.Date, certificateDate, true), 1, 0);
        details.Content.Controls.Add(fields);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, updateCertificateButton, newButton, clearButton);
        certButtonRow.Dock =
            DockStyle.Bottom;
        details.Content.Controls.Add(certButtonRow);
        var records =
            HumanUi.Card("No Objection Certificates", "A student may have more than one NOC. Select a row to edit or print it.");
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
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)
                + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(records, 0, 1);
        SetContent(
            root,
            UiText.Certificates.StudentGuidance);
        certificateDate.ValueChanged += (_, _) => ApplyWorkflowState();
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
            LoadCertificates(admissionId);
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
                    "GetNoObjectionCertificateRecords",
                    DatabaseHelper.Int("@AdmissionId", admissionId));
            certificateResults = CertificateRecordOrder.NewestFirst(table, "NoObjectionCertificateId");
            Bind(certificateResults);
            FormatCertificateGrid();
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException63)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException63, "NoObjectionCertificatePage: LoadCertificates fallback");
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
    }
    private void SelectSavedCertificate(int id)
    {
        
        CertificateRecordOrder.Select(Grid, "NoObjectionCertificateId", id,
            index => Grid_CellMouseClick(Grid, new DataGridViewCellMouseEventArgs(0,index,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))));
    }
    private void FormatCertificateGrid()
    {
        CertificateRecordOrder.ArrangeColumns(Grid, "NoObjectionCertificateId");
        /* The click handlers below read the row from certificateResults by the clicked row INDEX, which only matches the grid while it is in the table's own order. */
        LockGridOrder();
        SetGridWidth("NoObjectionCertificateId", 110);
        if (Grid.Columns.Contains("NoObjectionCertificateId"))
        {
            Grid.Columns["NoObjectionCertificateId"]!.HeaderText =
                "Certificate No.";
        }
        SetGridWidth("AdmissionNumber", 80);
        SetGridWidth("StudentName", 170);
        SetGridWidth("FatherName", 150);
        SetGridWidth("CertificateDate", 120);
        SetGridWidth("CreatedAt", 145);
        SetGridWidth("UpdatedAt", 145);
        HideGridColumn("AdmissionId");
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
            int noObjectionCertificateId =
                GetInt(row, "NoObjectionCertificateId");
            if (noObjectionCertificateId <= 0)
            {
                ClearCertificateSelection();
                SetStatus("The selected NOC contains invalid data.", true);
                return;
            }
            if (!ConfirmCertificateSelection(FindForm(), Grid, "NoObjectionCertificateId",
                selectedNoObjectionCertificateId, noObjectionCertificateId, HasCertificateEdits)) return;
            selectedNoObjectionCertificateId =
                noObjectionCertificateId;
            DateTime? certDate =
                GetDate(row, "CertificateDate");
            certificateDate.Value =
                certDate ?? DateTime.Today;
            savedEditorSnapshot = EditorSnapshot();
            ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            ClearCertificateSelection();
            SetStatus($"Unable to read the selected NOC: {DbMessage.UserMessage(ex)}", true);
        }
    }
    private void ApplyWorkflowState()
    {
        bool hasStudent =
            student.SelectedId > 0;
        bool hasSelection =
            selectedNoObjectionCertificateId > 0;
        student.Enabled = true;
        certificateDate.Enabled = hasStudent;
        createCertificateButton.Enabled =
            hasStudent &&
            !hasSelection;
        updateCertificateButton.Enabled =
            hasStudent &&
            hasSelection && HasCertificateEdits;
        newButton.Enabled =
            hasStudent &&
            hasSelection;
        clearButton.Enabled = true;
        refreshButton.Enabled =
            hasStudent;
        printButton.Enabled =
            hasSelection && !HasCertificateEdits && CurrentUser.HasPermission("Certificates.Print");
        if (!hasStudent)
        {
            SetStatus(UiText.Prompts.SearchForStudent);
            return;
        }
        if (hasSelection)
        {
            SetStatus(
                "Editing an existing NOC. Save your changes with Update Certificate, or click New to create another one for this student.");
            return;
        }
        SetStatus("Confirm the certificate date, then create the NOC. Select any certificate below to edit or print it.");
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
            if (!ValidateCertificateFields())
                return;
            int admissionId =
                student.SelectedId;
            DateTime certDate =
                certificateDate.Value.Date;
            createCertificateButton.Enabled = false;
            // awaited so the caller's disabled button covers the whole write. @UserId is passed explicitly and equals CurrentUser.UserId, so ExecuteUserAsync's automatic injection supplies the same value.
            await DatabaseHelper.ExecuteUserAsync(
                "CreateNoObjectionCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AdmissionId",
                    admissionId),
                DatabaseHelper.Date("@CertificateDate", certDate));
            successMessage =
                "NOC created successfully.";
            /* refresh only when the student this certificate was created for is still the selected one. The success message above stays: the write did happen. */
            if (IsDisposed || student.SelectedId != admissionId)
                return;
            ClearCertificateFields();
            ClearCertificateSelection();
            LoadCertificates(admissionId);
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
    private void UpdateCertificate()
    {
        int savedCertificateId = selectedNoObjectionCertificateId;
        // Finding 12 - see CreateCertificate: the confirmation is set last.
        string? successMessage =
            null;
        try
        {
            if (selectedNoObjectionCertificateId <= 0)
            {
                Warn("Select an NOC from the list first.");
                return;
            }
            if (!ValidateCertificateFields())
                return;
            int admissionId =
                student.SelectedId;
            DateTime certDate =
                certificateDate.Value.Date;
            updateCertificateButton.Enabled = false;
            DatabaseHelper.Execute(
                "UpdateNoObjectionCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@NoObjectionCertificateId",
                    selectedNoObjectionCertificateId),
                DatabaseHelper.Date("@CertificateDate", certDate));
            successMessage =
                "NOC updated successfully.";
            /* same rule as the create path: the student this certificate was updated for must still be the selected one, or the reload would show the previous student's certificates under the… */
            if (IsDisposed || student.SelectedId != admissionId)
                return;
            ClearCertificateFields();
            ClearCertificateSelection();
            LoadCertificates(admissionId);
            SelectSavedCertificate(savedCertificateId);
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
    private bool ValidateCertificateFields()
    {
        if (certificateDate.Value.Date > DateTime.Today)
        {
            Warn(UiText.Certificates.FutureDateRefused);
            return false;
        }
        return true;
    }
    private void StartNewCertificate()
    {
        if (HasCertificateEdits && !Confirm("Discard the unsaved changes and start a new certificate?", "New Certificate")) return;
        ClearCertificateSelection();
        certificateDate.Value =
            DateTime.Today;
        Grid.ClearSelection();
        try
        {
            Grid.CurrentCell = null;
        }
        catch (Exception nonFatalException64)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException64, "NoObjectionCertificatePage: StartNewCertificate fallback");
        }
        ApplyWorkflowState();
    }
    private void PrintSelected()
    {
        if (!printButton.Enabled) return;
        // Finding 13 - the preview is modal; park the search while it is open.
        try
        {
            if (selectedNoObjectionCertificateId <= 0)
            {
                Warn("Select an NOC from the list first.");
                return;
            }
            PrintCertificate(selectedNoObjectionCertificateId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
        }
    }
    private void PrintCertificate(int noObjectionCertificateId)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetNoObjectionCertificatePrintData",
                DatabaseHelper.Int("@NoObjectionCertificateId", noObjectionCertificateId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "NoObjectionCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("No Objection Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "No Objection Certificate", reportPath, "NoObjectionCertificateData", data);
    }
    private void RefreshCertificates()
    {
        if (HasCertificateEdits && !Confirm("Discard the unsaved changes and refresh certificates?", "Refresh Certificates")) return;
        if (student.SelectedId <= 0) return;
        int selectedId = selectedNoObjectionCertificateId;
        LoadCertificates(student.SelectedId);
        if (selectedId > 0) SelectSavedCertificate(selectedId);
    }
    private void ClearCertificateFields()
    {
        certificateDate.Value =
            DateTime.Today;
    }
    private void ClearCertificateSelection()
    {
        selectedNoObjectionCertificateId = 0;
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
        if (HasCertificateEdits && !Confirm("Discard the unsaved changes and choose another student?", "Next Student")) return;
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
            Grid.CellMouseClick -=
                Grid_CellMouseClick;
        }
        base.Dispose(disposing);
    }
}
