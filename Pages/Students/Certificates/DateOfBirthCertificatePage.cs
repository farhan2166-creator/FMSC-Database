using FMSC_SMS.Core;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Students.Certificates;
public sealed class DateOfBirthCertificatePage : V2Page
{
    private readonly StudentPicker student = new();
    private readonly ModernTextBox dateOfBirth =
        HumanUi.TextBox(false, 150);
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable studentResults => student.Source;
    private DataTable certificateResults = new();
    private DateTime? currentDateOfBirth;
    /* Finding 13 - a search debounce tick that lands while a modal (confirmation, collector prompt, print preview) is open runs inside that modal's message loop and re-binds the picker underneath… */
    /* Students hidden from the picker because they have no date of birth on record. */
    private bool suppressStudentChanged;
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
    public DateOfBirthCertificatePage() : this(true) { }
    internal DateOfBirthCertificatePage(bool initialize)
        : base("Date of Birth Certificate", "Create, issue, collect and print student date of birth certificates.")
    {
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
        dateOfBirth.ReadOnly = true;
        dateOfBirth.TabStop = false;
        TrackStudentSearchStatus(student);
        /* The shared student finder (StudentPicker): the list narrows on every keystroke and nothing is chosen until the operator clicks a row, presses Enter on a highlighted row, or types an… */
        if(initialize)StudentSearchCache.Warm();
        /* dbo.Create...Certificate refuses a student with no date of birth (58505), so such students are not offered. */
        student.RowFilter = row => !row.IsNull("DateOfBirth");
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
                "Create the student’s certificate. The first issue is recorded automatically.");
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
            CertificateUi.Field("Date of Birth (School Record)", dateOfBirth, true), 0, 1);
        details.Content.Controls.Add(fields);
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
            CertificateButtonRow.Create(issueAgainButton, collectButton, printButton, refreshButton);
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
        CertificateUi.CompactFields(details,fields,root,new[]{25f,25f,25f,25f},new[]{(0,0,2),(2,0,1),(3,0,1)},1100);
        SetContent(
            root,
            UiText.Certificates.IssueGuidance);
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
            currentDateOfBirth =
                GetDate(row, "DateOfBirth");
            dateOfBirth.Text =
                currentDateOfBirth.HasValue
                    ? currentDateOfBirth.Value.ToString("dd-MM-yyyy")
                    : "Not on record";
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
                    "GetDateOfBirthCertificateRecords",
                    DatabaseHelper.Int("@AdmissionId", admissionId));
            certificateResults = CertificateRecordOrder.NewestFirst(table, "DateOfBirthCertificateIssueId");
            Bind(certificateResults);
            FormatCertificateGrid();
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException60)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException60, "DateOfBirthCertificatePage: LoadCertificates fallback");
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
        
        CertificateRecordOrder.Select(Grid, "DateOfBirthCertificateIssueId", id,
            index => Grid_CellMouseClick(Grid, new DataGridViewCellMouseEventArgs(0,index,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))));
    }
    private void FormatCertificateGrid()
    {
        CertificateRecordOrder.ArrangeColumns(Grid, "DateOfBirthCertificateIssueId");
        /* The click handlers below read the row from certificateResults by the clicked row INDEX, which only matches the grid while it is in the table's own order. */
        LockGridOrder();
        SetGridWidth("CertificateRecordId", 80);
        SetGridWidth("DateOfBirthCertificateIssueId", 105);
        SetGridWidth("AdmissionNumber", 80);
        SetGridWidth("StudentName", 150);
        SetGridWidth("FatherName", 130);
        SetGridWidth("DateOfBirth", 110);
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
            int certificateRecordId =
                GetInt(row, "CertificateRecordId");
            int issueId =
                GetInt(row, "DateOfBirthCertificateIssueId");
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
            selectedIssueId > 0;
        bool latestIssueCollected =
            IsLatestIssueCollected();
        student.Enabled = true;
        dateOfBirth.Enabled = false;
        dateOfBirth.ReadOnly = true;
        bool canCreate =
            hasStudent &&
            !hasCertificate &&
            currentDateOfBirth.HasValue;
        certificateDate.Enabled = canCreate;
        createCertificateButton.Enabled =
            canCreate;
        clearButton.Enabled = true;
        refreshButton.Enabled =
            hasStudent;
        issueAgainButton.Enabled =
            hasCertificate &&
            latestIssueCollected;
        collectButton.Enabled =
            hasSelectedIssue &&
            !selectedIssueCollected;
        printButton.Enabled =
            hasSelectedIssue &&
            selectedIssueCollected;
        if (!hasStudent)
        {
            SetStatus(
                UiText.Prompts.SearchForStudent + " Students without a date of birth are not listed - add the date of birth on the student's record first.");
            return;
        }
        if (hasStudent && !currentDateOfBirth.HasValue && !hasCertificate)
        {
            SetStatus(
                "Add the student's date of birth to their record before creating a certificate.",
                true);
            return;
        }
        if (!hasCertificate)
        {
            SetStatus("Set the certificate date and create the date of birth certificate.");
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
        if (latestIssueCollected)
        {
            SetStatus("Latest copy collected. Select a copy to print, or use Issue Again.");
            return;
        }
        SetStatus("Current copy not collected.");
    }
    private bool IsLatestIssueCollected()
    {
        if (certificateResults.Rows.Count == 0)
            return false;
        DataRow latest =
            certificateResults.Rows[0];
        return string.Equals(
            GetString(latest, "CollectionStatus"), "Collected", StringComparison.OrdinalIgnoreCase);
    }
    private bool IsLatestSelectedIssue()
    {
        if (selectedIssueId <= 0 ||
            certificateResults.Rows.Count == 0)
        {
            return false;
        }
        int latestIssueId =
            GetInt(certificateResults.Rows[0], "DateOfBirthCertificateIssueId");
        return latestIssueId ==
               selectedIssueId;
    }
    private void CreateCertificate()
    {
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
            if (certificateResults.Rows.Count > 0)
            {
                Warn("This student already has a date of birth certificate.");
                return;
            }
            DataRow[] matches =
                studentResults.Select($"AdmissionId = {student.SelectedId}");
            if (matches.Length == 0)
            {
                Alert("The selected student record could not be found.");
                return;
            }
            if (!currentDateOfBirth.HasValue)
            {
                Warn("The selected student does not have a date of birth on record. Update the student's record first.");
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
                    "Creating this date of birth certificate will:\n\n" +
                    "• save the date of birth certificate record\n" +
                    "• record the original certificate issuance\n\n" +
                    UiText.Dialogs.Continue,
                    "Create Date of Birth Certificate",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;
            int admissionId =
                student.SelectedId;
            createCertificateButton.Enabled = false;
            DatabaseHelper.Execute(
                "CreateDateOfBirthCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AdmissionId",
                    admissionId),
                DatabaseHelper.Date("@CertificateDate", certDate));
            successMessage =
                "Date of birth certificate created successfully. " +
                "The original issuance has also been recorded.";
            ClearCertificateFields();
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
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (certificateResults.Rows.Count == 0)
            {
                Warn("The student does not have an existing date of birth certificate.");
                return;
            }
            if (!IsLatestIssueCollected())
            {
                Warn("The current certificate issuance must be collected before another issuance can be created.");
                return;
            }
            int certificateRecordId =
                GetExistingCertificateRecordId();
            if (certificateRecordId <= 0)
            {
                Alert("The student's date of birth certificate record could not be found.");
                return;
            }
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
            issueAgainButton.Enabled = false;
            DatabaseHelper.Execute(
                "IssueDateOfBirthCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int("@CertificateRecordId", certificateRecordId));
            successMessage =
                "Certificate issued again successfully. The new issuance has been added to the history.";
            LoadCertificates(student.SelectedId);
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
    private int GetExistingCertificateRecordId()
    {
        if (certificateResults.Rows.Count == 0)
            return 0;
        return GetInt(certificateResults.Rows[0], "CertificateRecordId");
    }
    private void MarkCollected()
    {
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
                "MarkDateOfBirthCertificateCollected",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@DateOfBirthCertificateIssueId",
                    selectedIssueId),
                DatabaseHelper.P("@CollectedBy", collectedBy));
            successMessage =
                "Certificate issuance marked as collected.";
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
            PrintCertificate(selectedCertificateRecordId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
        }
    }
    private void PrintCertificate(int certificateRecordId)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetDateOfBirthCertificatePrintData",
                DatabaseHelper.Int("@CertificateRecordId", certificateRecordId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "DateOfBirthCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Date of Birth Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "Date of Birth Certificate", reportPath, "DateOfBirthCertificateData", data);
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
    private void RefreshCertificates()
    {
        if (student.SelectedId <= 0) return;
        int selectedId = selectedIssueId;
        LoadCertificates(student.SelectedId);
        if (selectedId > 0) SelectSavedCertificate(selectedId);
    }
    private void ClearStudentDetails()
    {
        currentDateOfBirth = null;
        dateOfBirth.Clear();
    }
    private void ClearCertificateFields()
    {
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

