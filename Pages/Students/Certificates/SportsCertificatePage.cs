using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class SportsCertificatePage : V2Page, IUnsavedChanges, IBusyPage
{
    public bool HasUnsavedChanges => HasCertificateEdits;
    public string UnsavedChangesMessage => "Discard the unsaved certificate changes?";
    public bool IsBusy => creatingCertificate;
    public string BusyMessage => "Please wait for the certificate to finish saving.";
    private bool creatingCertificate;
    private readonly Func<SqlParameter[], Task> writeCertificate;
    private readonly Func<int, DataTable> readCertificates;
    private readonly StudentPicker student = new();
    private readonly ModernTextBox sportName =
        HumanUi.TextBox(false, 200);
    private readonly ModernTextBox performance =
        HumanUi.TextBox(false, 1000);
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    private string? savedEditorSnapshot;
    private string EditorSnapshot() => string.Join("\u001f", sportName.Text, performance.Text, certificateDate.Value.Date.ToString("yyyy-MM-dd"));
    private bool HasCertificateEdits => student.SelectedId > 0 &&
        (selectedSportsCertificateId > 0
            ? savedEditorSnapshot is not null && savedEditorSnapshot != EditorSnapshot()
            : !string.IsNullOrWhiteSpace(sportName.Text) || !string.IsNullOrWhiteSpace(performance.Text));
    /* Finding 13 - a search debounce tick that lands while a modal (print preview, confirmation) is open runs inside that modal's message loop and re-binds the picker underneath it. */
    private bool suppressStudentChanged;
    private int selectedSportsCertificateId;
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
    public SportsCertificatePage() : this(true) { }
    internal SportsCertificatePage(bool initialize, Func<SqlParameter[], Task>? writeCertificate = null,
        Func<int, DataTable>? readCertificates = null)
        : base("Sports Certificate", "Record and print sports certificates for students.")
    {
        this.writeCertificate = writeCertificate ?? (args => DatabaseHelper.ExecuteUserAsync("CreateSportsCertificate", args));
        this.readCertificates = readCertificates ?? (id => DatabaseHelper.QueryUser("GetSportsCertificateRecords", DatabaseHelper.Int("@AdmissionId", id)));
        sportName.MaxLength = 200;
        performance.MaxLength = 1000;
        ConfigureFieldSuggestions();
        /* dbo.CreateSportsCertificate has no duplicate guard, and the certificate table has no unique constraint on (AdmissionId, ...). */
        createCertificateButton.Click += async (_, _) => await RunCreateCertificateAsync();
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
        if (initialize) StudentSearchCache.Warm();
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
        if (initialize) Defer(
            () => {
                ResetToStart();
            });
        if (initialize) Defer(LoadFieldSuggestions);
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "Select student; enter sport and performance to create. Select a saved row to edit any field.");
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
            CertificateUi.Field("Sport Name", sportName, true), 0, 1);
        fields.Controls.Add(
            CertificateUi.Field("Performance", performance, true), 1, 1);
        details.Content.Controls.Add(fields);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, updateCertificateButton, newButton, clearButton);
        certButtonRow.Dock =
            DockStyle.Bottom;
        details.Content.Controls.Add(certButtonRow);
        var records =
            HumanUi.Card("Sports Certificates", "A student may have more than one sports certificate. Select a row to edit or print it.");
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
        SetContent(
            root,
            UiText.Certificates.StudentGuidance);
        sportName.TextChanged += (_, _) => ApplyWorkflowState();
        performance.TextChanged += (_, _) => ApplyWorkflowState();
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
            DataTable table = readCertificates(admissionId);
            certificateResults = CertificateRecordOrder.NewestFirst(table, "SportsCertificateId");
            Bind(certificateResults);
            FormatCertificateGrid();
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException69)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException69, "SportsCertificatePage: LoadCertificates fallback");
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
        
        CertificateRecordOrder.Select(Grid, "SportsCertificateId", id,
            index => Grid_CellMouseClick(Grid, new DataGridViewCellMouseEventArgs(0,index,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))));
    }
    private void FormatCertificateGrid()
    {
        CertificateRecordOrder.ArrangeColumns(Grid, "SportsCertificateId");
        /* The click handlers below read the row from certificateResults by the clicked row INDEX, which only matches the grid while it is in the table's own order. */
        LockGridOrder();
        SetGridWidth("SportsCertificateId", 110);
        if (Grid.Columns.Contains("SportsCertificateId"))
        {
            Grid.Columns["SportsCertificateId"]!.HeaderText =
                "Certificate No.";
        }
        SetGridWidth("AdmissionNumber", 80);
        SetGridWidth("StudentName", 150);
        SetGridWidth("FatherName", 130);
        SetGridWidth("SportName", 140);
        SetGridWidth("Performance", 220);
        SetGridWidth("CertificateDate", 110);
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
            int sportsCertificateId =
                GetInt(row, "SportsCertificateId");
            if (sportsCertificateId <= 0)
            {
                ClearCertificateSelection();
                SetStatus("The selected sports certificate contains invalid data.", true);
                return;
            }
            if (!ConfirmCertificateSelection(FindForm(), Grid, "SportsCertificateId",
                selectedSportsCertificateId, sportsCertificateId, HasCertificateEdits)) return;
            selectedSportsCertificateId =
                sportsCertificateId;
            sportName.Text =
                GetString(row, "SportName");
            performance.Text =
                GetString(row, "Performance");
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
            SetStatus($"Unable to read the selected sports certificate: {DbMessage.UserMessage(ex)}", true);
        }
    }
    private void ApplyWorkflowState() => ApplyWorkflowStateAndStatus(true);
    private void ApplyWorkflowStateAndStatus(bool showStatus)
    {
        student.Enabled = Grid.Enabled = !creatingCertificate;
        if (creatingCertificate)
        {
            createCertificateButton.Enabled = updateCertificateButton.Enabled = newButton.Enabled =
                clearButton.Enabled = refreshButton.Enabled = printButton.Enabled = false;
            return;
        }
        bool hasStudent =
            student.SelectedId > 0;
        bool hasSelection =
            selectedSportsCertificateId > 0;
        student.Enabled = true;
        certificateDate.Enabled = hasStudent;
        sportName.Enabled = hasStudent;
        performance.Enabled = hasStudent;
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
        if (!showStatus) return;
        if (!hasStudent)
        {
            SetStatus(UiText.Prompts.SearchForStudent);
            return;
        }
        if (hasSelection)
        {
            SetStatus(
                "Editing sports certificate. Use Update Certificate to save, or New for another.");
            return;
        }
        SetStatus("Enter sport and performance to create. Select a saved certificate to edit or print.");
    }
    private async Task RunCreateCertificateAsync()
    {
        if (creatingCertificate || IsDisposed || !createCertificateButton.Enabled) return;
        creatingCertificate = true;
        ApplyWorkflowState();
        try
        {
            await CreateCertificate();
        }
        finally
        {
            creatingCertificate = false;
            if (!IsDisposed) ApplyWorkflowStateAndStatus(false);
        }
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
            using var hold = BeginBusy(BusyMessage, this);
            await writeCertificate(new[] {
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
                    "@SportName",
                    DatabaseHelper.OptionalText(
                        sportName.Text)),
                DatabaseHelper.P(
                    "@Performance",
                    DatabaseHelper.OptionalText(performance.Text)) });
            successMessage =
                "Sports certificate created successfully.";
            AddSuggestionIfNew(sportName, sportName.Text);
            AddSuggestionIfNew(performance, performance.Text);
            /* refresh only when the student this certificate was created for is still the selected one. */
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
        if (creatingCertificate) return;
        int savedCertificateId = selectedSportsCertificateId;
        // Finding 12 - see CreateCertificate: the confirmation is set last.
        string? successMessage =
            null;
        try
        {
            if (selectedSportsCertificateId <= 0)
            {
                Warn("Select a sports certificate from the list first.");
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
                "UpdateSportsCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@SportsCertificateId",
                    selectedSportsCertificateId),
                DatabaseHelper.Date(
                    "@CertificateDate",
                    certDate),
                DatabaseHelper.P(
                    "@SportName",
                    DatabaseHelper.OptionalText(
                        sportName.Text)),
                DatabaseHelper.P(
                    "@Performance",
                    DatabaseHelper.OptionalText(performance.Text)));
            successMessage =
                "Sports certificate updated successfully.";
            AddSuggestionIfNew(sportName, sportName.Text);
            AddSuggestionIfNew(performance, performance.Text);
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
        if (string.IsNullOrWhiteSpace(sportName.Text))
        {
            Warn("Enter the Sport Name.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(performance.Text))
        {
            Warn("Enter the Performance.");
            return false;
        }
        if (certificateDate.Value.Date > DateTime.Today)
        {
            Warn(UiText.Certificates.FutureDateRefused);
            return false;
        }
        return true;
    }
    private void StartNewCertificate()
    {
        if (creatingCertificate) return;
        ClearCertificateFields();
        ClearCertificateSelection();
        Grid.ClearSelection();
        try
        {
            Grid.CurrentCell = null;
        }
        catch (Exception nonFatalException70)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException70, "SportsCertificatePage: StartNewCertificate fallback");
        }
        ApplyWorkflowState();
    }
    private void PrintSelected()
    {
        if (creatingCertificate) return;
        if (!printButton.Enabled) return;
        // Finding 13 - the preview is modal; park the search while it is open.
        try
        {
            if (selectedSportsCertificateId <= 0)
            {
                Warn("Select a sports certificate from the list first.");
                return;
            }
            PrintCertificate(selectedSportsCertificateId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
        }
    }
    private void PrintCertificate(int sportsCertificateId)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetSportsCertificatePrintData",
                DatabaseHelper.Int("@SportsCertificateId", sportsCertificateId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "SportsCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Sports Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "Sports Certificate", reportPath, "SportsCertificateData", data);
    }
    private void ConfigureFieldSuggestions()
    {
        sportName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        sportName.AutoCompleteSource = AutoCompleteSource.CustomSource;
        sportName.AutoCompleteCustomSource = new AutoCompleteStringCollection();
        performance.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        performance.AutoCompleteSource = AutoCompleteSource.CustomSource;
        performance.AutoCompleteCustomSource = new AutoCompleteStringCollection();
    }
    private void LoadFieldSuggestions()
    {
        LoadFieldSuggestions(sportName, "SportName");
        LoadFieldSuggestions(performance, "Performance");
    }
    private void LoadFieldSuggestions(ModernTextBox field, string columnField)
    {
        try
        {
            DataTable table = DatabaseHelper.QueryUser("GetSportsCertificateFieldSuggestions", DatabaseHelper.P("@Field", columnField));
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
    private void RefreshCertificates()
    {
        if (creatingCertificate) return;
        if (HasCertificateEdits && !Confirm("Discard the unsaved changes and refresh certificates?", "Refresh Certificates")) return;
        if (student.SelectedId <= 0) return;
        int selectedId = selectedSportsCertificateId;
        LoadCertificates(student.SelectedId);
        if (selectedId > 0) SelectSavedCertificate(selectedId);
    }
    private void ClearCertificateFields()
    {
        sportName.Clear();
        performance.Clear();
        certificateDate.Value =
            DateTime.Today;
    }
    private void ClearCertificateSelection()
    {
        selectedSportsCertificateId = 0;
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
        if (creatingCertificate) return;
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
