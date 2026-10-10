using FMSC_SMS.Core;
using static FMSC_SMS.Core.CertificateUi;
namespace FMSC_SMS.Pages.Staff.Certificates;
public sealed class BankAccountOpeningCertificatePage : V2Page, IUnsavedChanges
{
    private readonly StaffPicker staff = new();
    private string? _baseline;
    private string Snapshot() => string.Join("\u001f", bankName.Text, branchName.Text, designation.Text, monthlyIncome.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), purpose.Text, certificateDate.Value.Date.ToString("yyyy-MM-dd"));
    public bool HasUnsavedChanges => _baseline is not null && _baseline != Snapshot();
    public string UnsavedChangesMessage => "This staff certificate has unsaved changes. Leaving will discard them.";
    private bool ConfirmDiscard() => ModernMessageDialog.ConfirmDiscardIfDirty(FindForm(), () => HasUnsavedChanges, UnsavedChangesMessage);
    private void CaptureBaseline() => _baseline = Snapshot();

    private readonly ModernTextBox bankName =
        HumanUi.TextBox(false, 100);
    private readonly ModernTextBox branchName =
        HumanUi.TextBox(false, 150);
    private readonly ModernTextBox designation =
        HumanUi.TextBox(false, 100);
    private readonly ModernNumericTextBox monthlyIncome =
        HumanUi.Number(10000000);
    private readonly ModernTextBox purpose =
        HumanUi.TextBox(false, 200);
    private readonly ModernDatePicker certificateDate =
        HumanUi.Date();
    private DataTable certificateResults = new();
    private string? savedEditorSnapshot;
    private string EditorSnapshot() => string.Join("\u001f", bankName.Text, branchName.Text, designation.Text, monthlyIncome.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), purpose.Text, certificateDate.Value.Date.ToString("yyyy-MM-dd"));
    private bool HasCertificateEdits => selectedBankAccountOpeningCertificateId > 0 && savedEditorSnapshot is not null && savedEditorSnapshot != EditorSnapshot();
    private bool suppressStaffChanged;
    private int selectedBankAccountOpeningCertificateId;
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
        Text = "Next Staff Member",
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
    public BankAccountOpeningCertificatePage()
        : base("Bank Account Opening Certificate", "Record and print bank account opening request letters for staff.")
    {
        bankName.PlaceholderText =
            "e.g. Meezan Bank, Allied Bank";
        bankName.MaxLength = 100;
        branchName.PlaceholderText =
            "e.g. Ring Road Peshawar";
        branchName.MaxLength = 150;
        designation.PlaceholderText =
            "e.g. Computer Operator, Account Assistant";
        designation.MaxLength = 100;
        purpose.PlaceholderText =
            "e.g. for his/her salary purpose";
        purpose.MaxLength = 200;
        monthlyIncome.DecimalPlaces = 0;
        monthlyIncome.ThousandsSeparator = true;
        /* dbo.CreateBankAccountOpeningCertificate has no duplicate guard, and the certificate table has no unique constraint on (AdmissionId, ...). */
        createCertificateButton.Click += async (_, _) => {
            if (!createCertificateButton.Enabled)
                return;
            /* (D-H3) - the write owns the page until it settles, so the two controls that can move the context out from under it are held as well. */
            var heldDuringCreate = new List<(Control Control, bool WasEnabled)>
            {
                (staff, staff.Enabled),
                (Grid, Grid.Enabled)
            };
            createCertificateButton.Enabled = false;
            foreach (var (control, _) in heldDuringCreate)
                control.Enabled = false;
            try
            {
                await CreateCertificate();
            }
            finally
            {
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
        staff.SelectionChangeGuard = _ => ConfirmDiscard();
        staff.SelectionCleared += (_, _) => { if (!suppressStaffChanged) StaffChanged(); };
        staff.SearchFailed += ex => Fail(ex);
        staff.UserSelectionChanged +=
            (_, _) => {
                if (suppressStaffChanged)
                    return;
                StaffChanged();
            };
        Grid.CellMouseClick +=
            Grid_CellMouseClick;
        Defer(
            () => {
                /* The ORDER here was the defect. Resetting first and binding second leaves the list populated with nothing selected, which is what opening this page means. */
                ResetToStart();
                BindStaff();
            });
        var details =
            HumanUi.Card(
                UiText.Certificates.Details,
                "Select the staff member, fill in the bank details and designation, then create the certificate. Staff Name, Father Name and CNIC come from the staff record; every other field can be changed later by selecting a row below.");
        var fields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
        fields.Controls.Add(
            CertificateUi.Field("Staff", staff, true), 0, 0);
        fields.Controls.Add(
            CertificateUi.Field(UiText.Certificates.Date, certificateDate, true), 1, 0);
        fields.Controls.Add(
            CertificateUi.Field("Bank Name", bankName, true), 0, 1);
        fields.Controls.Add(
            CertificateUi.Field("Branch Name", branchName, true), 1, 1);
        fields.Controls.Add(
            CertificateUi.Field("Designation", designation, true), 0, 2);
        fields.Controls.Add(
            CertificateUi.Field("Monthly Income (optional)", monthlyIncome, false), 1, 2);
        fields.Controls.Add(
            CertificateUi.Field("Purpose", purpose, true), 0, 3);
        details.Content.Controls.Add(fields);
        var certButtonRow =
            CertificateButtonRow.Create(createCertificateButton, updateCertificateButton, newButton, clearButton);
        certButtonRow.Dock =
            DockStyle.Bottom;
        details.Content.Controls.Add(certButtonRow);
        var records =
            HumanUi.Card(
                "Bank Account Opening Certificates",
                "A staff member may have more than one of these. Select a row to edit or print it.");
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
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(4)
                + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(records, 0, 1);
        SetContent(
            root,
            UiText.Certificates.StaffGuidance);
        bankName.TextChanged += (_, _) => ApplyWorkflowState();
        branchName.TextChanged += (_, _) => ApplyWorkflowState();
        designation.TextChanged += (_, _) => ApplyWorkflowState();
        purpose.TextChanged += (_, _) => ApplyWorkflowState();
        certificateDate.ValueChanged += (_, _) => ApplyWorkflowState();
        monthlyIncome.ValueChanged += (_, _) => ApplyWorkflowState();
        ApplyWorkflowState();
    }
    private void BindStaff()
    {
        try
        {
            suppressStaffChanged = true;
            DataTable dt =
                DatabaseHelper.QueryUser(
                    "GetStaff",
                    DatabaseHelper.Int(
                        "@UserId",
                        CurrentUser.UserId));
            /* @StaffId is a filter whose default (NULL) means "everyone"; the earlier explicit 0 matched no staff row, so this picker came back empty. */
            staff.SetSource(dt);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            suppressStaffChanged = false;
        }
    }
    private void StaffChanged()
    {
        if (suppressStaffChanged)
            return;
        try
        {
            ClearCertificateSelection();
            ClearCertificateFields();
            int staffId =
                staff.SelectedId;
            if (staffId <= 0)
            {
                certificateResults =
                    new DataTable();
                Bind(certificateResults);
                FormatCertificateGrid();
                ApplyWorkflowState();
                return;
            }
            LoadCertificates(staffId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void LoadCertificates(int staffId)
    {
        try
        {
            ClearCertificateSelection();
            certificateResults =
                new DataTable();
            if (staffId <= 0)
            {
                Bind(certificateResults);
                FormatCertificateGrid();
                ApplyWorkflowState();
                return;
            }
            DataTable table =
                DatabaseHelper.QueryUser(
                    "GetBankAccountOpeningCertificateRecords",
                    DatabaseHelper.Int(
                        "@UserId",
                        CurrentUser.UserId),
                    DatabaseHelper.Int("@StaffId", staffId));
            certificateResults = CertificateRecordOrder.NewestFirst(table, "BankAccountOpeningCertificateId");
            Bind(certificateResults);
            FormatCertificateGrid();
            Grid.ClearSelection();
            try
            {
                Grid.CurrentCell = null;
            }
            catch (Exception nonFatalException43)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException43, "BankAccountOpeningCertificatePage: LoadCertificates fallback");
            }
            ApplyWorkflowState();
        }
        catch (Exception ex)
        {
            Fail(ex);
            /* Finding 11 - the load failed, so the grid must not keep showing the previous staff member's certificates, and the buttons must be recomputed for the empty list. */
            certificateResults =
                new DataTable();
            Bind(certificateResults);
            FormatCertificateGrid();
            ApplyWorkflowState();
        }
    }
    private void SelectSavedCertificate(int id)
    {
        CaptureBaseline();
        CertificateRecordOrder.Select(Grid, "BankAccountOpeningCertificateId", id,
            index => Grid_CellMouseClick(Grid, new DataGridViewCellMouseEventArgs(0,index,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))));
    }
    private void FormatCertificateGrid()
    {
        CertificateRecordOrder.ArrangeColumns(Grid, "BankAccountOpeningCertificateId");
        /* The click handlers below read the row from certificateResults by the clicked row INDEX, which only matches the grid while it is in the table's own order. */
        LockGridOrder();
        SetGridWidth("BankAccountOpeningCertificateId", 110);
        if (Grid.Columns.Contains("BankAccountOpeningCertificateId"))
        {
            Grid.Columns["BankAccountOpeningCertificateId"]!.HeaderText =
                "Certificate No.";
        }
        SetGridWidth("StaffId", 70);
        SetGridWidth("StaffName", 170);
        SetGridWidth("FatherName", 150);
        SetGridWidth("Designation", 140);
        SetGridWidth("BankName", 130);
        SetGridWidth("BranchName", 150);
        SetGridWidth("MonthlyIncome", 110);
        SetGridWidth("Purpose", 170);
        SetGridWidth("CertificateDate", 120);
        SetGridWidth("CreatedAt", 145);
        SetGridWidth("UpdatedAt", 145);
        HideGridColumn("StaffId");
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
        if (!ConfirmDiscard()) return;
        try
        {
            DataGridViewRow gridRow =
                Grid.Rows[e.RowIndex];
            if (gridRow.IsNewRow)
                return;
            Grid.ClearSelection();
            gridRow.Selected = true;
            /* The field population below moved into ApplyCertificateRow so that Refresh can put the same record back into the editor after reloading the list. */
            if (!ApplyCertificateRow(
                    certificateResults.Rows[
                        e.RowIndex]))
            {
                ClearCertificateSelection();
                SetStatus("The selected certificate contains invalid data.", true);
            }
        }
        catch (Exception ex)
        {
            ClearCertificateSelection();
            SetStatus($"Unable to read the selected certificate: {DbMessage.UserMessage(ex)}", true);
        }
    }
    /// <summary>Loads one certificate row into the editor and sets the selection to it.</summary>
    private bool ApplyCertificateRow(DataRow row)
    {
        int bankAccountOpeningCertificateId =
            GetInt(row, "BankAccountOpeningCertificateId");
        if (bankAccountOpeningCertificateId <= 0)
            return false;
        selectedBankAccountOpeningCertificateId =
            bankAccountOpeningCertificateId;
        bankName.Text =
            GetString(row, "BankName");
        branchName.Text =
            GetString(row, "BranchName");
        designation.Text =
            GetString(row, "Designation");
        purpose.Text =
            GetString(row, "Purpose");
        monthlyIncome.Value =
            GetDecimal(row, "MonthlyIncome");
        DateTime? certDate =
            GetDate(row, "CertificateDate");
        certificateDate.Value =
            certDate ?? DateTime.Today;
        savedEditorSnapshot = EditorSnapshot();
        ApplyWorkflowState();
        CaptureBaseline();
        return true;
    }
    private void ApplyWorkflowState()
    {
        bool hasStaff =
            staff.SelectedId > 0;
        bool hasSelection =
            selectedBankAccountOpeningCertificateId > 0;
        staff.Enabled = true;
        bankName.Enabled = hasStaff;
        branchName.Enabled = hasStaff;
        designation.Enabled = hasStaff;
        monthlyIncome.Enabled = hasStaff;
        purpose.Enabled = hasStaff;
        certificateDate.Enabled = hasStaff;
        createCertificateButton.Enabled =
            hasStaff &&
            !hasSelection;
        updateCertificateButton.Enabled =
            hasStaff &&
            hasSelection && HasCertificateEdits;
        newButton.Enabled =
            hasStaff &&
            hasSelection;
        clearButton.Enabled = true;
        refreshButton.Enabled =
            hasStaff;
        printButton.Enabled =
            hasSelection && !HasCertificateEdits && CurrentUser.HasPermission("Certificates.Print");
        if (!hasStaff)
        {
            SetStatus("Select a staff member.");
            return;
        }
        if (hasSelection)
        {
            SetStatus(
                "Editing an existing certificate. Save your changes with Update Certificate, or click New to create another one for this staff member.");
            return;
        }
        SetStatus(
            "Fill in the bank, branch, designation and purpose, then create the certificate. Select any certificate below to edit or print it.");
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
            if (staff.SelectedId <= 0)
            {
                Warn("Select a staff member first.");
                return;
            }
            if (!ValidateCertificateFields())
                return;
            int staffId =
                staff.SelectedId;
            createCertificateButton.Enabled = false;
            // awaited so the caller's disabled button covers the whole write. @UserId is passed explicitly and equals CurrentUser.UserId, so ExecuteUserAsync's automatic injection supplies the same value.
            await DatabaseHelper.ExecuteUserAsync(
                "CreateBankAccountOpeningCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@StaffId",
                    staffId),
                DatabaseHelper.P(
                    "@Designation",
                    designation.Text.Trim()),
                DatabaseHelper.P(
                    "@BankName",
                    bankName.Text.Trim()),
                DatabaseHelper.P(
                    "@BranchName",
                    branchName.Text.Trim()),
                DatabaseHelper.P(
                    "@MonthlyIncome",
                    monthlyIncome.Value),
                DatabaseHelper.P(
                    "@Purpose",
                    purpose.Text.Trim()),
                DatabaseHelper.Date("@CertificateDate", certificateDate.Value.Date));
            successMessage =
                "Certificate created successfully.";
            /* (D-H2) - reload only while the staff member this certificate was created for is still the selected one, or the list would be shown under a different member's name and… */
            if (IsDisposed ||
                staff.SelectedId != staffId)
            {
                return;
            }
            ClearCertificateFields();
            ClearCertificateSelection();
            LoadCertificates(staffId);
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
        int savedCertificateId = selectedBankAccountOpeningCertificateId;
        // Finding 12 - see CreateCertificate: the confirmation is set last.
        string? successMessage =
            null;
        try
        {
            if (selectedBankAccountOpeningCertificateId <= 0)
            {
                Warn("Select a certificate from the list first.");
                return;
            }
            if (!ValidateCertificateFields())
                return;
            int staffId =
                staff.SelectedId;
            updateCertificateButton.Enabled = false;
            DatabaseHelper.Execute(
                "UpdateBankAccountOpeningCertificate",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@BankAccountOpeningCertificateId",
                    selectedBankAccountOpeningCertificateId),
                DatabaseHelper.P(
                    "@Designation",
                    designation.Text.Trim()),
                DatabaseHelper.P(
                    "@BankName",
                    bankName.Text.Trim()),
                DatabaseHelper.P(
                    "@BranchName",
                    branchName.Text.Trim()),
                DatabaseHelper.P(
                    "@MonthlyIncome",
                    monthlyIncome.Value),
                DatabaseHelper.P(
                    "@Purpose",
                    purpose.Text.Trim()),
                DatabaseHelper.Date("@CertificateDate", certificateDate.Value.Date));
            successMessage =
                "Certificate updated successfully.";
            ClearCertificateFields();
            ClearCertificateSelection();
            LoadCertificates(staffId);
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
        if (string.IsNullOrWhiteSpace(bankName.Text))
        {
            Warn("Enter the bank name.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(branchName.Text))
        {
            Warn("Enter the branch name.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(designation.Text))
        {
            Warn("Enter the staff member's designation.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(purpose.Text))
        {
            Warn("Enter the purpose of opening the account.");
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
        if (!ConfirmDiscard()) return;
        ClearCertificateSelection();
        ClearCertificateFields();
        Grid.ClearSelection();
        try
        {
            Grid.CurrentCell = null;
        }
        catch (Exception nonFatalException44)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException44, "BankAccountOpeningCertificatePage: StartNewCertificate fallback");
        }
        ApplyWorkflowState();
    }
    private void PrintSelected()
    {
        if (!printButton.Enabled) return;
        try
        {
            if (selectedBankAccountOpeningCertificateId <= 0)
            {
                Warn("Select a certificate from the list first.");
                return;
            }
            PrintCertificate(selectedBankAccountOpeningCertificateId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void PrintCertificate(int bankAccountOpeningCertificateId)
    {
        DataTable data =
            DatabaseHelper.QueryUser(
                "GetBankAccountOpeningCertificatePrintData",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int("@BankAccountOpeningCertificateId", bankAccountOpeningCertificateId));
        if (data.Rows.Count == 0)
        {
            Warn(UiText.Certificates.PrintDataMissing);
            return;
        }
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "Reports", "BankAccountOpeningCertificate.rdlc");
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Bank Account Opening Certificate RDLC report was not found.", reportPath);
        }
        ShowReport(FindForm(), "Bank Account Opening Certificate", reportPath, "BankAccountOpeningCertificateData", data);
    }
    private void RefreshCertificates()
    {
        if (!ConfirmDiscard()) return;
        int staffId =
            staff.SelectedId;
        if (staffId <= 0)
            return;
        /* Refresh reloads the list, and LoadCertificates always clears the row selection, but nothing cleared the editor. */
        int previouslySelected =
            selectedBankAccountOpeningCertificateId;
        LoadCertificates(staffId);
        if (previouslySelected <= 0)
        {
            ClearCertificateFields();
            ApplyWorkflowState();
            return;
        }
        SelectCertificateById(previouslySelected);
    }
    /// <summary>re-selects a certificate by id after the list is reloaded, so the editor and the workflow state still describe the row the operator was working on.</summary>
    private void SelectCertificateById(int certificateId)
    {
        for (int index = 0;
             index < certificateResults.Rows.Count;
             index++)
        {
            DataRow row =
                certificateResults.Rows[index];
            if (GetInt(row, "BankAccountOpeningCertificateId") != certificateId)
            {
                continue;
            }
            if (index < Grid.Rows.Count)
            {
                Grid.ClearSelection();
                Grid.Rows[index].Selected = true;
            }
            ApplyCertificateRow(row);
            return;
        }
        ClearCertificateSelection();
        ClearCertificateFields();
        ApplyWorkflowState();
    }
    private void ClearCertificateFields()
    {
        bankName.Clear();
        branchName.Clear();
        designation.Clear();
        monthlyIncome.Value = 0;
        purpose.Text =
            "for his/her salary purpose";
        certificateDate.Value =
            DateTime.Today;
        CaptureBaseline();
    }
    private void ClearCertificateSelection()
    {
        selectedBankAccountOpeningCertificateId = 0;
    }
    private void ResetToStart()
    {
        suppressStaffChanged = true;
        try
        {
            staff.Clear();
        }
        finally
        {
            suppressStaffChanged = false;
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
        if (!ConfirmDiscard()) return;
        suppressStaffChanged = true;
        try
        {
            staff.Clear();
            certificateResults =
                new DataTable();
            ClearCertificateFields();
            ClearCertificateSelection();
            Bind(certificateResults);
            FormatCertificateGrid();
        }
        finally
        {
            suppressStaffChanged = false;
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
