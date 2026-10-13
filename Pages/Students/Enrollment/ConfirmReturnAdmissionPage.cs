using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Enrollment;
/// <summary>Confirms that a promoted student has returned before new-year billing starts.</summary>
public sealed class ConfirmReturnAdmissionPage : V2Page
{
    /* A text box you can type into (name, admission or registration number), with a drop-down arrow that lists every promoted student still awaiting return. */
    private readonly StudentPicker pendingStudent = new() {
        IdColumn = "EnrollmentId",
        ShowDropDownButton = true,
        PlaceholderText = "Type Admission Number or student name - or open the list",
        NoMatchText = "No promoted student is awaiting return",
        /* Admission number, names, class / section and campus identify students in the list. Registration stays hidden by user request. */
        ShowCampusColumn = true
    };
    private readonly NextStepBar nextSteps;
    /* The student just confirmed, kept for the "Print Bill" step after the pending list has been reloaded without them. */
    private int lastAdmissionId;
    private int lastEnrollmentId;
    private string lastStudentName = string.Empty;
    private string lastClassSection = string.Empty;
    private readonly ModernDatePicker returnDate = HumanUi.Date();
    private readonly CheckBox transport = new() {
        Text = "Uses school transport",
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont
    };
    private readonly DataGridView fees = HumanUi.Grid(true);
    private readonly Label total = new() {
        AutoSize = true,
        Font = AppTheme.ButtonFont,
        ForeColor = AppTheme.Text,
        Text = "Select a returning student."
    };
    private readonly Label openingFeesTotal = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.HeadingText,
        TextAlign = ContentAlignment.MiddleRight,
        Text = "—"
    };
    private readonly Label openingDiscountTotal = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.Text,
        TextAlign = ContentAlignment.MiddleRight,
        Text = "—"
    };
    private readonly Label openingPayableTotal = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.HeadingText,
        TextAlign = ContentAlignment.MiddleRight,
        Text = "—"
    };
    private DataTable? feeRows;
    private DataTable? pendingRows;
    private int loadVersion;
    private bool confirmingReturn;
    private readonly bool compactDialog;
    private ModernButton? confirmButton;
    private readonly Label selectedStudentSummary = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = AppTheme.Text };
    public bool IsConfirmingReturn => confirmingReturn;
    public event EventHandler? ReturnConfirmed;
    /// <param name="navigate">The shell's navigate delegate, so the steps after confirming (Fee Collection) can open the next screen.</param>
    /// <param name="preselectAdmissionId">Opens with this student already chosen (0 = none), when they are awaiting return.</param>
    public ConfirmReturnAdmissionPage(
        Action<Func<Control>, string, string>? navigate = null,
        int preselectAdmissionId = 0, bool compactDialog = false)
        : base("Confirm Return Admission",
            "Review the class fees and confirm the returning student’s admission.")
    {
        this.compactDialog = compactDialog;
        var confirm = AddBusyAction(compactDialog ? "Confirm Return" : "Confirm Return & Create Charges", ConfirmAsync, "Success");
        confirmButton = confirm;
        GateAction(confirm, "Students.Administer");
        if (compactDialog) confirm.Enabled = false;
        RelocateFromActions(confirm);
        nextSteps = new NextStepBar(navigate, Fail);
        fees.ReadOnly = false;
        fees.AllowUserToAddRows = false;
        fees.AllowUserToDeleteRows = false;
        fees.AutoGenerateColumns = false;
        fees.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "FeeTypeName", HeaderText = UiText.Fees.FeeType, DataPropertyName = "FeeTypeName",
            ReadOnly = true, FillWeight = 42
        });
        fees.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "Frequency", HeaderText = UiText.Fees.Frequency, DataPropertyName = "Frequency",
            ReadOnly = true, FillWeight = 25
        });
        fees.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "Amount", HeaderText = UiText.Fees.Amount, DataPropertyName = "Amount",
            ReadOnly = false, FillWeight = 20
        });
        fees.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "DiscountAmount", HeaderText = UiText.Fees.Discount, DataPropertyName = "DiscountAmount",
            ReadOnly = false, FillWeight = 22
        });
        fees.CellValueChanged += (_, _) => UpdateTotal();
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        var studentField = HumanUi.Field("Promoted student awaiting return", pendingStudent, true);
        fields.Controls.Add(studentField, 0, 0);
        fields.SetColumnSpan(studentField, 2);
        fields.Controls.Add(HumanUi.Field("Billing starts from", returnDate, true, fillHeight: false), 0, 1);
        fields.Controls.Add(transport, 1, 1);
        var studentCard = HumanUi.Card(
            "Returning Student",
            "Select the promoted student, confirm the billing start date, and choose transport only if it applies.");
        studentCard.Content.Controls.Add(fields);
        var feeCard = HumanUi.Card(
            "Opening Fee Setup",
            "Edit the amounts and waivers before confirming return.");
        feeCard.Content.Controls.Add(fees);
        var totalsGrid = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(4)
        };
        for (int i = 0; i < 3; i++)
            totalsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        int amountFieldHeight = AppMetrics.TextHeight(AppTheme.NormalFont) +
            AppMetrics.TextHeight(AppTheme.HeadingFont) + 16;
        int totalsGuidanceHeight = AppMetrics.TextHeight(total.Font) + 4;
        totalsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, amountFieldHeight));
        totalsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, totalsGuidanceHeight));
        totalsGrid.Controls.Add(ReturnAmountField("Opening Fees", openingFeesTotal), 0, 0);
        totalsGrid.Controls.Add(ReturnAmountField("Discount / Waiver", openingDiscountTotal), 1, 0);
        totalsGrid.Controls.Add(ReturnAmountField("Payable", openingPayableTotal), 2, 0);
        total.Dock = DockStyle.Fill;
        total.AutoSize = false;
        total.Margin = Padding.Empty;
        total.TextAlign = ContentAlignment.MiddleLeft;
        total.ForeColor = AppTheme.MutedText;
        totalsGrid.Controls.Add(total, 0, 1);
        totalsGrid.SetColumnSpan(total, 3);
        var summaryCard = HumanUi.Card(
            "Opening Charges",
            "These are the charges that will be created when the return is confirmed.");
        summaryCard.Content.Controls.Add(totalsGrid);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Margin = Padding.Empty, Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(2)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, amountFieldHeight + totalsGuidanceHeight +
            totalsGrid.Padding.Vertical + summaryCard.Header.Height + summaryCard.Padding.Vertical +
            summaryCard.Content.Padding.Vertical));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.Controls.Add(studentCard, 0, 0);
        root.Controls.Add(nextSteps, 0, 1);
        root.Controls.Add(feeCard, 0, 2);
        root.Controls.Add(summaryCard, 0, 3);
        var buttonRow = HumanUi.ButtonRow(confirm);
        buttonRow.Dock = DockStyle.Fill;
        buttonRow.Margin = new Padding(0);
        root.Controls.Add(buttonRow, 0, 4);
        SetContent(root);
        if (compactDialog)
        {
            PrepareForEmbedding();
            pendingStudent.Enabled = false;
            studentField.Visible = false;
            fields.SetCellPosition(studentField, new TableLayoutPanelCellPosition(0, 2));
            fields.Controls.Add(selectedStudentSummary, 0, 0);
            fields.SetColumnSpan(selectedStudentSummary, 2);
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            studentCard.Header.Visible = false;
            selectedStudentSummary.AutoSize = false;
            selectedStudentSummary.Font = AppTheme.NormalFont;
            selectedStudentSummary.Margin = Padding.Empty;
            int identityHeight = AppMetrics.TextHeight(selectedStudentSummary.Font) * 2 + UiDesignSystem.Spacing.Sm;
            fields.RowStyles[0].Height = identityHeight;
            root.RowStyles[0].Height = identityHeight + AppMetrics.FieldRowHeight +
                studentCard.Padding.Vertical + studentCard.Content.Padding.Vertical;
            root.RowStyles[1].SizeType = SizeType.Absolute;
            root.RowStyles[1].Height = 0;
            nextSteps.Visible = false;
            summaryCard.Header.Visible = false;
            root.RowStyles[3].Height = amountFieldHeight + totalsGuidanceHeight + totalsGrid.Padding.Vertical +
                summaryCard.Padding.Vertical + summaryCard.Content.Padding.Vertical;
            root.Controls.Remove(buttonRow);
            var cancel = new ModernButton { Text = UiText.Actions.Cancel, ThemeRole = "Secondary" };
            cancel.Click += (_, _) => FindForm()?.Close();
            buttonRow.Controls.Add(cancel);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
            StatusLabel.AutoSize = true;
            StatusLabel.Dock = DockStyle.Fill;
            StatusLabel.MaximumSize = new Size(AppScale.Scale(700), 0);
            footer.Controls.Add(StatusLabel, 0, 0);
            footer.Controls.Add(buttonRow, 0, 1);
            root.RowStyles[4].SizeType = SizeType.AutoSize;
            root.Controls.Add(footer, 0, 4);
        }
        /* Choosing a student (click, or Enter on a highlighted row) loads their fees; editing the text away from the chosen student drops them. */
        pendingStudent.UserSelectionChanged += async (_, _) => {
            transport.Checked = false;
            nextSteps.Dismiss();
            await LoadFeesAsync();
        };
        pendingStudent.SelectionCleared += (_, _) =>
            ClearFees("Select a returning student.");
        pendingStudent.SearchFailed += ex => Fail(ex);
        Defer(() => LoadPendingAsync(preselectAdmissionId));
    }
    private static Control ReturnAmountField(string caption, Label value)
    {
        var panel = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(4, 0, 4, 0), Padding = new Padding(8, 4, 8, 4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont) + 4));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(value.Font) + 4));
        value.Margin = Padding.Empty;
        panel.Controls.Add(new Label {
            Text = caption, Dock = DockStyle.Fill, Font = AppTheme.NormalFont, Margin = Padding.Empty,
            ForeColor = AppTheme.MutedText, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }
    private void ResetOpeningTotals()
    {
        openingFeesTotal.Text = "—";
        openingDiscountTotal.Text = "—";
        openingPayableTotal.Text = "—";
    }
    private void ClearFees(string message)
    {
        transport.Checked = false;
        loadVersion++;
        feeRows = null;
        if (compactDialog && confirmButton is not null) confirmButton.Enabled = false;
        fees.DataSource = null;
        ResetOpeningTotals();
        ResetOpeningTotals();
        total.Text = message;
    }
    private async Task LoadPendingAsync(int selectAdmissionId = 0)
    {
        int version = ++loadVersion;
        try
        {
            var rows = await DatabaseHelper.QueryUserAsync("GetPendingReturnAdmissions");
            if (version != loadVersion || IsDisposed) return;
            pendingRows = rows;
            pendingStudent.SetSource(rows);
            pendingStudent.Clear();
            feeRows = null;
            fees.DataSource = null;
            ResetOpeningTotals();
            total.Text = rows.Rows.Count == 0
                ? "No promoted students are awaiting return admission."
                : "Select a returning student.";
            if (selectAdmissionId > 0)
                await SelectPendingAsync(selectAdmissionId);
        }
        catch (Exception ex) { Fail(ex); }
    }
    /// <summary>Chooses the pending student with this admission id (arriving from another screen) and loads their fees.</summary>
    private async Task SelectPendingAsync(int admissionId)
    {
        DataRow? row = pendingRows?.AsEnumerable()
            .FirstOrDefault(r => r["AdmissionId"] != DBNull.Value &&
                                 Convert.ToInt32(r["AdmissionId"]) == admissionId);
        if (row is null)
        {
            SetStatus(
                "That student is not awaiting return admission - the return may already be confirmed, or the student was not promoted.",
                true);
            return;
        }
        pendingStudent.SetStudent(row);
        selectedStudentSummary.Text = $"{Convert.ToString(row["StudentName"])} — {AdmissionNumbers.Read(row)}\n" +
            $"{Convert.ToString(row["ClassName"])} / {Convert.ToString(row["SectionName"])}";
        await LoadFeesAsync();
    }
    private async Task LoadFeesAsync()
    {
        int version = ++loadVersion;
        int enrollmentId = pendingStudent.SelectedId;
        feeRows = null;
        fees.DataSource = null;
        ResetOpeningTotals();
        total.Text = enrollmentId > 0 ? "Loading this student's fees..." : "Select a returning student.";
        if (enrollmentId <= 0) return;
        ApplyDefaultReturnDate(enrollmentId);
        try
        {
            var rows = await DatabaseHelper.QueryUserAsync(
                "GetReturnFeeChoices", DatabaseHelper.Int("@EnrollmentId", enrollmentId));
            if (version != loadVersion || IsDisposed || enrollmentId != pendingStudent.SelectedId)
                return;
            feeRows = rows;
            if (compactDialog && confirmButton is not null)
                confirmButton.Enabled = rows.Rows.Count > 0 && CurrentUser.HasPermission("Students.Administer");
            fees.DataSource = rows;
            UpdateTotal();
        }
        catch (Exception ex) { Fail(ex); }
    }
    /// <summary>The return date starts as the enrollment's own start date (the promotion effective date, normally 01-Apr), not today.</summary>
    private void ApplyDefaultReturnDate(int enrollmentId)
    {
        if (pendingRows is null || !pendingRows.Columns.Contains("EnrollmentDate"))
            return;
        DataRow? row = pendingRows.AsEnumerable()
            .FirstOrDefault(r => Convert.ToInt32(r["EnrollmentId"]) == enrollmentId);
        if (row is null || row["EnrollmentDate"] == DBNull.Value)
            return;
        DateTime start = Convert.ToDateTime(row["EnrollmentDate"]).Date;
        returnDate.Value = start > DateTime.Today ? DateTime.Today : start;
    }
    private void UpdateTotal()
    {
        if (feeRows is null) return;
        try
        {
            fees.EndEdit();
            decimal amount = feeRows.AsEnumerable().Sum(r => Convert.ToDecimal(r["Amount"]));
            decimal discount = feeRows.AsEnumerable().Sum(r => Convert.ToDecimal(r["DiscountAmount"]));
            openingFeesTotal.Text = $"Rs. {amount:N0}";
            openingDiscountTotal.Text = $"Rs. {discount:N0}";
            openingPayableTotal.Text = $"Rs. {amount - discount:N0}";
            total.Text = "Review the amounts above, then confirm the return admission.";
        }
        catch (Exception nonFatalException74)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException74, "ConfirmReturnAdmissionPage: UpdateTotal fallback");
            ResetOpeningTotals();
            total.Text = "Check the fee amounts and discounts before confirming.";
        }
    }
    private async Task ConfirmAsync()
    {
        if (confirmingReturn) return;
        confirmingReturn = true;
        try
        {
            using var busy = BeginBusy("Reviewing return admission...", pendingStudent, returnDate, transport, fees);
            await ConfirmReturnCoreAsync();
        }
        finally { confirmingReturn = false; }
    }
    private bool TryBuildFeeChoices(out List<(int feeTypeId, decimal amount, decimal discount)> choices)
    {
        choices = new();
        if (feeRows is null) return false;
        foreach (DataGridViewRow gridRow in fees.Rows) gridRow.ErrorText = "";
        foreach (DataRow row in feeRows.Rows)
        {
            if (!decimal.TryParse(Convert.ToString(row["Amount"]), out decimal amount) ||
                !decimal.TryParse(Convert.ToString(row["DiscountAmount"]), out decimal discount) ||
                amount < 0 || discount < 0 || discount > amount)
            {
                var invalid = fees.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => item.DataBoundItem is DataRowView view && ReferenceEquals(view.Row, row));
                if (invalid is not null)
                {
                    invalid.ErrorText = "Enter valid amounts; discount must be between zero and the fee amount.";
                    fees.CurrentCell = invalid.Cells["DiscountAmount"];
                }
                Warn($"Check {Convert.ToString(row["FeeTypeName"])}: enter valid amounts and a discount no greater than the fee.");
                return false;
            }
            choices.Add((Convert.ToInt32(row["FeeTypeId"]), amount, discount));
        }
        return true;
    }
    private async Task ConfirmReturnCoreAsync()
    {
        if (!ValidateRequiredInputs())
            return;
        int enrollmentId = pendingStudent.SelectedId;
        if (enrollmentId <= 0 || feeRows is null || feeRows.Rows.Count == 0)
        {
            Warn("Select a promoted student and review the class fees.");
            return;
        }
        fees.EndEdit();
        if (!TryBuildFeeChoices(out var choices)) return;
        /* Who is being confirmed, read now: after the confirmation the pending list is reloaded without them. */
        DataRow? pendingRow = pendingStudent.SelectedRow;
        int admissionId = pendingRow is not null && pendingRow["AdmissionId"] != DBNull.Value
            ? Convert.ToInt32(pendingRow["AdmissionId"])
            : 0;
        string studentName = pendingRow is not null && pendingRow.Table.Columns.Contains("StudentName")
            ? Convert.ToString(pendingRow["StudentName"]) ?? string.Empty
            : string.Empty;
        string classSection = string.Empty;
        if (pendingRow is not null && pendingRow.Table.Columns.Contains("ClassName"))
        {
            string cls = Convert.ToString(pendingRow["ClassName"]) ?? string.Empty;
            string section = pendingRow.Table.Columns.Contains("SectionName")
                ? Convert.ToString(pendingRow["SectionName"]) ?? string.Empty
                : string.Empty;
            classSection = section.Length > 0 ? $"{cls} / {section}" : cls;
        }
        decimal payable = choices.Sum(x => x.amount - x.discount);
        if (!Confirm(
            $"Confirm this student's return on {returnDate.Value:dd-MMM-yyyy}?\n\n" +
            $"Opening fees payable: Rs. {payable:N2}\n" +
            $"Transport: {(transport.Checked ? "Yes" : "No")}\n\n" +
            "This creates the opening charges. Books and other purchases can be added in Fee Collection.",
            "Confirm Return Admission"))
            return;
        try
        {
            var confirmed = await DatabaseHelper.QueryUserAsync(
                "ConfirmPromotedAdmission",
                DatabaseHelper.Int("@EnrollmentId", enrollmentId),
                DatabaseHelper.Date("@ReturnDate", returnDate.Value),
                DatabaseHelper.Bit("@UsesTransport", transport.Checked),
                new SqlParameter("@FeeChoices", SqlDbType.Structured) {
                    TypeName = "dbo.ReturnFeeChoiceType",
                    Value = Tvp.ReturnFeeChoices(choices)
                });
            string billingFrom = string.Empty;
            if (confirmed.Rows.Count > 0 &&
                confirmed.Columns.Contains("BillingStartDate") &&
                confirmed.Rows[0]["BillingStartDate"] != DBNull.Value)
            {
                billingFrom = $" Billing starts {Convert.ToDateTime(confirmed.Rows[0]["BillingStartDate"]):MMMM yyyy}.";
            }
            if (confirmed.Rows.Count > 0 &&
                confirmed.Columns.Contains("AdmissionId") &&
                confirmed.Rows[0]["AdmissionId"] != DBNull.Value)
            {
                admissionId = Convert.ToInt32(confirmed.Rows[0]["AdmissionId"]);
            }
            Info("Return admission confirmed. Opening charges are ready in Fee Collection." + billingFrom);
            StudentSearchCache.Invalidate();
            if (compactDialog)
            {
                ReturnConfirmed?.Invoke(this, EventArgs.Empty);
                return;
            }
            transport.Checked = false;
            lastAdmissionId = admissionId;
            lastEnrollmentId = enrollmentId;
            lastStudentName = studentName;
            lastClassSection = classSection;
            await LoadPendingAsync();
            /* The bill and the payment come next, for this same student. */
            if (!IsDisposed && admissionId > 0)
            {
                string who = studentName.Length > 0 ? studentName : "the student";
                nextSteps.Offer(
                    $"Return confirmed for {who}.{billingFrom} Print the bill, then take the payment in Fee Collection.",
                    admissionId,
                    FlowStep.Do("Print Bill", "Fees.View", PrintBillAsync),
                    StudentFlow.FeeCollection(primary: true));
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    /// <summary>Prints the just-confirmed student's bill (their posted, unpaid charges) on the thermal slip.</summary>
    private async Task PrintBillAsync(int admissionId)
    {
        if (lastEnrollmentId <= 0 || admissionId != lastAdmissionId)
        {
            Warn("Confirm a student's return first, then print the bill.");
            return;
        }
        string? problem = await ChargeBillPrinter.PrintAsync(
            lastAdmissionId, lastEnrollmentId, lastStudentName, lastClassSection);
        if (!IsDisposed && problem is not null)
            Warn(problem);
    }
}

