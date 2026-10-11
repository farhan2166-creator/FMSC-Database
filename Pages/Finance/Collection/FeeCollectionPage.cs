using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Collection;
public sealed class FeeCollectionPage : V2Page, IBusyPage
{
    public bool IsBusy => transactionBusy;
    public string BusyMessage => "Please wait for the fee collection to finish recording.";
    private readonly StudentPicker student = new();
    private readonly LookupBox enrollment = new();
    private readonly DataGridView charges =
        HumanUi.Grid(true);
    /* Reverse is the one verb that belongs to a single charge - the fee-level operations act on the assignment and live in Fee Assignments. */
    private readonly ContextMenuStrip chargeMenu = new StyledContextMenu();
    private readonly ContextMenuStrip studentFeesMenu = new StyledContextMenu();
    private readonly DataGridView recentPayments =
        HumanUi.Grid();
    /* One row per child with open charges, shown only when more than one child is being paid for. */
    private readonly DataGridView familyProposal =
        HumanUi.Grid(true);
    private bool suppressProposalEvents;
    private RowStyle? proposalRow;
    /* This page collects for whoever is actually paying, so a parent paying for two or three children does not need a separate screen or an up-front decision about whether this… */
    private readonly CheckedListBox siblings = new() {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        BackColor = AppTheme.Surface,
        ForeColor = AppTheme.Text
    };
    private readonly Label primaryPayer = new() { Dock=DockStyle.Fill, Font=AppTheme.NormalFont, TextAlign=ContentAlignment.MiddleLeft };
    private ModernButton selectAllSiblings = null!;
    private Action? updateSiblingLayout;
    /// <summary>Set while the sibling list is being rebuilt, because ItemCheck fires for programmatic SetItemChecked too and would otherwise load charges for siblings nobody ticked.</summary>
    private bool suppressSiblingEvents;
    /// <summary>True while a tick is still loading that sibling's charges from the database, so a second tick cannot interleave with it at the await point.</summary>
    private bool siblingBusy;
    private bool focusAmountAfterSiblingLoad;
    /// <summary>Set when a collection produced more than one receipt, so the note can be shown after the post-transaction reloads have finished overwriting the status line.</summary>
    private string? multiStudentReceiptNote;
    /// <summary>Set when the collection just completed covered a family, so the one receipt number shared by every child can be reprinted as a single document.</summary>
    private string? lastGroupReceiptNumber;
    private readonly Label siblingHint = new() {
        Dock = DockStyle.Bottom,
        AutoSize = false,
        Height = AppScale.Scale(30, AppScale.ControlSizeScale),
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft
    };
    /// <summary>admissionId -> (enrollmentId, display name) for every sibling ticked, so their charges can be loaded and their charges attributed on posting.</summary>
    private readonly Dictionary<int, (int EnrollmentId, string Name)> payingFor = new();
    private readonly ModernComboBox method =
        HumanUi.Combo();
    private readonly ModernTextBox discountAmount =
        HumanUi.TextBox(false, 100);
    /* This is the input the screen is driven from - the grid shows how it settles each charge, in the settlement order (tuition, one-time, yearly, then the rest). */
    private readonly ModernTextBox paymentAmount =
        HumanUi.TextBox(false, 100);
    private readonly ModernTextBox reference =
        HumanUi.TextBox(false, 100);
    private readonly ModernTextBox remarks =
        HumanUi.TextBox(false, 500);
    private readonly ModernDatePicker paymentDate =
        new ModernDatePicker {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd-MMM-yyyy HH:mm",
            ShowUpDown = true,
            Value = DateTime.Now
        };
    /* True once the OPERATOR has moved the payment date for the student now on screen. */
    private bool paymentDateEdited;
    private bool suppressPaymentDateEvents;
    private readonly Label totalDue =
        new();
    private readonly Label balance =
        new();
    /* The figure the operator acts on: what is owed after the discount and before any money. Read-only - it is derived, not entered. */
    private readonly Label amountToPay =
        new();
    private int admissionId;
    private int enrollmentId;
    private bool suppressStudentEvents;
    private bool suppressSearch;
    private bool suppressDiscountEvents;
    private bool suppressPaymentEvents;
    /// <summary>The payable figure the summary last displayed (= Total Due less the discount as spread).</summary>
    private decimal lastPayableShown;
    private int loadVersion;
    private ModernButton? executeButton;
    private ModernButton? fullAmountButton;
    private ModernButton? printReceiptButton;
    private ModernButton? nextStudentButton;
    private readonly Label collectionResult = new() {
        Dock = DockStyle.Top, AutoSize = true, Visible = false,
        Font = AppTheme.NormalFont, ForeColor = AppTheme.Text,
        Margin = new Padding(4, 8, 4, 4)
    };
    private ModernButton? feeAssignmentsButton;
    private ModernButton? addChargeButton;
    private ModernButton? futureFeesButton;
    private ModernButton? historyButton;
    private ModernButton? paymentHistoryButton;
    private ModernButton? studentFeesMenuButton;
    /// <summary>The Payment History dialog's Reprint Receipt button. See ReprintReceiptAsync for why a receipt can now be reprinted at all.</summary>
    private ModernButton? reprintReceiptButton;
    /// <summary>Off-screen parent for the payment list.</summary>
    private Panel? paymentHistoryHost;
    private bool transactionBusy;
    // Snapshot of the most recently completed Collection transaction, kept only in memory so "Print Receipt" can reprint it with the real (post-discount) figures.
    private sealed class LastCollectionReceipt
    {
        public required string StudentName;
        public required int AdmissionId;
        public required int EnrollmentId;
        public required string ReceiptNumber;
        public required string PaymentMethod;
        public required DateTime TransactionDate;
        public required string ClassName;
        public required string SectionName;
        public required string CampusName;
        public required decimal DiscountApplied;
        public required decimal BalanceRemaining;
        public required List<(string FeeType, string BillingMonth, decimal NominalAmount, decimal ActualAmount)> Lines;
    }
    private sealed class FamilyReceiptLine
    {
        public int AdmissionId { get; init; }
        public int EnrollmentId { get; init; }
        public required string StudentName { get; init; }
        public string FatherName { get; init; } = string.Empty;
        public string ClassName { get; init; } = string.Empty;
        public string SectionName { get; init; } = string.Empty;
        public string CampusName { get; init; } = string.Empty;
        public required string FeeName { get; init; }
        public string BillingMonth { get; init; } = string.Empty;
        public decimal Amount { get; init; }
    }
    private LastCollectionReceipt? lastCollectionReceipt;
    private bool manualAllocation;
    private static bool TryParseWholeCollectionAmount(string text, out decimal amount)
    {
        string value = text.Trim();
        amount = 0m;
        return value.Length > 0 && value.All(c => c >= '0' && c <= '9') &&
            decimal.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out amount);
    }
    private static void WholeAmountKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !(e.KeyChar >= '0' && e.KeyChar <= '9'))
            e.Handled = true;
    }
    private void ConfigureWholeAmountColumn(DataGridView grid, string column)
    {
        grid.EditingControlShowing += (_, e) => {
            e.Control.KeyPress -= WholeAmountKeyPress;
            if (grid.CurrentCell?.OwningColumn.Name == column)
                e.Control.KeyPress += WholeAmountKeyPress;
        };
        grid.CellValidating += (_, e) => {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != column || !grid.IsCurrentCellInEditMode) return;
            bool valid = TryParseWholeCollectionAmount(Convert.ToString(e.FormattedValue) ?? string.Empty, out decimal entered);
            e.Cancel = !valid;
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = valid ? string.Empty : "Enter a whole amount using digits only; fractions are not collected.";
        };
    }
    private static void ConfigureDecimalInput(ModernTextBox textBox)
    {
        textBox.KeyPress += (_, e) => {
            if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar))
                return;
            if (e.KeyChar == '.' && !textBox.Text.Contains('.'))
                return;
            e.Handled = true;
        };
    }
    public FeeCollectionPage()
        : base(
            "Fee Collection",
            "Record student and family fee payments.")
    {
        BuildStudentSearch();
    }
    public FeeCollectionPage(int preloadAdmissionId)
        : this()
    {
        if (preloadAdmissionId > 0)
        {
            Defer(() => LoadStudentByAdmissionId(preloadAdmissionId));
        }
    }
    private void BuildStudentSearch()
    {
        ConfigureDecimalInput(discountAmount);
        paymentAmount.KeyPress += WholeAmountKeyPress;
        /* The Operation dropdown is gone: this page is collection-only. See the Operation label in the layout for why. */
        method.Items.AddRange(
            new object[]
            {
                "Cash",
                "Bank",
                "Card",
                "Online",
                "Other"
            });
        method.SelectedIndex = 0;
        /* It is an async void handler underneath, so it had no protection against a second click while the first posting was in flight. */
        executeButton =
            AddBusyAction(
                "Collect Payment",
                ProcessTransaction,
                "Success");
        /* Process Collection is enabled only when there is something outstanding to collect. */
        executeButton.EnabledChanged +=
            (_, _) => {
                if (executeButton.Enabled &&
                    (transactionBusy || !HasOutstandingCharges()))
                {
                    executeButton.Enabled = false;
                }
            };
        /* The page opens with no student, so there is nothing to collect. */
        executeButton.Enabled = false;
        printReceiptButton =
            AddBusyAction(
                "Print Receipt",
                PrintReceiptAsync,
                "Secondary");
        printReceiptButton.Enabled = false;
        printReceiptButton.EnabledChanged += (_, _) => {
            if (printReceiptButton.Enabled && (transactionBusy ||
                (lastCollectionReceipt is null && lastGroupReceiptNumber is null)))
                printReceiptButton.Enabled = false;
        };
        // (Fee Assignments UI): contextual entry point, ("Add the Fee Assignments panel to the existing FeeCollectionPage as a first visible increment - no retirement, no navigation change").
        feeAssignmentsButton =
            AddAction(
                "Fee Assignments",
                (_, _) => OpenFeeAssignments(),
                "Secondary");
        feeAssignmentsButton.Enabled = false;
        addChargeButton = AddAction(
            "Add Charge", (_, _) => OpenAdditionalFee(), "Secondary");
        addChargeButton.Enabled = false;
        // (Fee Operations, scoped down): rather than build a whole new consolidated screen, Future Fees ('s GetStudentFeeProjection) and History (the existing, unmodified StudentAccountStatementPage) are added here as two more…
        futureFeesButton =
            AddAction(
                "Future Fees",
                (_, _) => OpenFutureFees(),
                "Secondary");
        futureFeesButton.Enabled = false;
        historyButton =
            AddAction(
                "Account Statement",
                (_, _) => OpenHistory(),
                "Secondary");
        historyButton.Enabled = false;
        /* The payment list is no longer a permanent column on this page, so it opens from here instead, the same way History and Fee Assignments do. */
        paymentHistoryButton =
            AddAction(
                "Payment History",
                (_, _) => OpenPaymentHistory(),
                "Secondary");
        paymentHistoryButton.Enabled = false;
        historyButton.EnabledChanged +=
            (_, _) => {
                if (paymentHistoryButton is not null &&
                    historyButton is not null)
                {
                    paymentHistoryButton.Enabled = historyButton.Enabled;
                }
            };
        /* Keep the collection workflow visually dominant. The existing buttons stay as the single source of enable-state and behaviour; this compact menu simply invokes them. */
        studentFeesMenuButton = new ModernButton {
            Text = "Student Fees ▾",
            ThemeRole = "Secondary",
            AutoSize = true
        };
        studentFeesMenu.Items.Add("Add One-Off Charge", null, (_, _) => addChargeButton?.PerformClick());
        studentFeesMenu.Items.Add("Future Fees", null, (_, _) => futureFeesButton?.PerformClick());
        studentFeesMenu.Items.Add(new ToolStripSeparator());
        studentFeesMenu.Items.Add("Account Statement", null, (_, _) => historyButton?.PerformClick());
        studentFeesMenu.Items.Add("Payment History", null, (_, _) => paymentHistoryButton?.PerformClick());
        studentFeesMenu.Opening += (_, _) => {
            if (studentFeesMenu.Items.Count < 5) return;
            studentFeesMenu.Items[0].Enabled = addChargeButton?.Enabled == true;
            studentFeesMenu.Items[1].Enabled = futureFeesButton?.Enabled == true;
            studentFeesMenu.Items[3].Enabled = historyButton?.Enabled == true;
            studentFeesMenu.Items[4].Enabled = paymentHistoryButton?.Enabled == true;
        };
        studentFeesMenuButton.Click += (_, _) =>
            studentFeesMenu.Show(studentFeesMenuButton, new Point(0, studentFeesMenuButton.Height));
        var clear =
            AddAction(
                UiText.Certificates.NextStudent,
                (_, _) => { if (transactionBusy) return; ClearPayment(); student.Focus(); },
                "Secondary");
        nextStudentButton = clear;
        RelocateFromActions(
            executeButton,
            printReceiptButton,
            feeAssignmentsButton,
            addChargeButton,
            futureFeesButton,
            historyButton,
            paymentHistoryButton,
            clear);
        /* Start reading the student list now so the first keystroke filters at once. */
        StudentSearchCache.Warm();
        /* The operator edited the box away from the student this page holds. The typed text is left exactly as typed. */
        student.SelectionCleared += (_, _) => {
            if (suppressStudentEvents ||
                suppressSearch)
            {
                return;
            }
            if (admissionId > 0 || enrollmentId > 0)
                ClearPayment(false);
        };
        TrackStudentSearchStatus(student);
        /* A student was chosen: clicked, highlighted and Enter, or an exact admission number typed and Enter. */
        student.UserSelectionChanged += async (_, _) => {
            if (suppressStudentEvents ||
                suppressSearch)
            {
                return;
            }
            if (student.SelectedId <= 0)
                return;
            await SelectStudent();
            // Only advance a deliberate picker selection; an async load must not
            // take focus back from an operator who has already moved elsewhere.
            if (!IsDisposed && student.ContainsFocus &&
                admissionId == student.SelectedId && HasOutstandingCharges())
            {
                paymentAmount.Focus();
                paymentAmount.SelectAll();
            }
        };
        enrollment.SelectedIndexChanged += async (_, _) => {
            enrollmentId =
                enrollment.SelectedId;
            charges.Rows.Clear();
            recentPayments.Rows.Clear();
            lastCollectionReceipt = null;
            lastGroupReceiptNumber = null;
            collectionResult.Text = ""; collectionResult.Visible = false;
            if (printReceiptButton is not null)
                printReceiptButton.Enabled = false;
            if (feeAssignmentsButton is not null)
                feeAssignmentsButton.Enabled = admissionId > 0 && enrollmentId > 0;
            if (addChargeButton is not null)
                addChargeButton.Enabled = admissionId > 0 && enrollmentId > 0;
            if (futureFeesButton is not null)
                futureFeesButton.Enabled = admissionId > 0 && enrollmentId > 0;
            if (historyButton is not null)
                historyButton.Enabled = admissionId > 0 && enrollmentId > 0;
            /* The rows above were just cleared, and with them every sibling's charges, the discount and the Paid figure belonged to the previous enrollment. */
            suppressSiblingEvents = true;
            try
            {
                for (int i = 0; i < siblings.Items.Count; i++)
                    siblings.SetItemChecked(i, false);
            }
            finally
            {
                suppressSiblingEvents = false;
            }
            suppressDiscountEvents = true;
            suppressPaymentEvents = true;
            try
            {
                discountAmount.Text = "0";
                paymentAmount.Text = "0";
            }
            finally
            {
                suppressDiscountEvents = false;
                suppressPaymentEvents = false;
            }
            int enrollmentLoadVersion = ++loadVersion;
            await Task.WhenAll(
                LoadChargesAsync(enrollmentLoadVersion),
                LoadRecentPaymentsAsync(enrollmentLoadVersion));
            if (enrollmentLoadVersion != loadVersion)
                return;
            UpdateOperationMode();
            /* Keep the received amount independent of the outstanding total. */
            RefreshEnteredPaymentAllocation();
        };
        discountAmount.TextChanged += (_, _) => {
            if (suppressDiscountEvents)
                return;
            /* The discount changes what each charge can take, so the allocation is recomputed from the payment rather than merely capped - otherwise the Payment field and the grid disagree. */
            if (PaidIsUntouchedDefault())
                RefreshEnteredPaymentAllocation();
            else
                ApplyPaymentAcrossCharges();
        };
        /* entering the amount received settles the charges. The grid is the result of that allocation, not the input to it. */
        paymentAmount.TextChanged += (_, _) => {
            if (suppressPaymentEvents)
                return;
            ApplyPaymentAcrossCharges();
        };
        /* The operator setting the date himself is the one thing that stops this page refreshing it; a date this page wrote is not that. */
        paymentDate.ValueChanged += (_, _) => {
            if (suppressPaymentDateEvents)
                return;
            paymentDateEdited = true;
        };
        ConfigureChargesGrid();
        ConfigurePaymentsGrid();
        var context =
            HumanUi.Card(
                "Student & Collection Date",
                "Choose the student once; the current academic enrolment and outstanding charges load automatically.");
        /* Keep the transaction header compact. Payment details belong beside the amount being collected, not beside the student identity, so method / reference / remarks live in the checkout card below. */
        var contextGrid =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(2, 3, 2, 0),
                Margin = new Padding(0)
            };
        contextGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        contextGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
        contextGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        contextGrid.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        contextGrid.Controls.Add(
            HumanUi.Field(
                "Student (Admission Number / Name)",
                student,
                true),
            0,
            0);
        contextGrid.Controls.Add(
            HumanUi.Field(
                "Academic Enrolment",
                enrollment,
                true),
            1,
            0);
        contextGrid.Controls.Add(
            HumanUi.Field(
                "Collection Date",
                paymentDate,
                true),
            2,
            0);
        context.Content.Controls.Add(contextGrid);
        var allocationButton = new ModernButton {
            Text = "Adjust Allocation",
            ThemeRole = "Secondary",
            Width = AppScale.Scale(205),
            Height = AppMetrics.ButtonHeight
        };
        manualAllocation = false;
        charges.Columns["Pay"].Visible = false;
        charges.Columns["Amount"].ReadOnly = true;
        allocationButton.Click += (_, _) => {
            manualAllocation = !manualAllocation;
            charges.Columns["Pay"].Visible = manualAllocation;
            UpdateOperationMode();
            allocationButton.Text = manualAllocation ? "Automatic Allocation" : "Adjust Allocation";
            UiHelper.FitColumnsToWidth(charges);
            if (!manualAllocation)
                ApplyPaymentAcrossCharges();
        };
        var chargeCard =
            HumanUi.Card(
                "Outstanding Charges",
                "Payments are allocated automatically. Adjust Allocation is available when you need to pay specific charges.", AppTheme.ModuleAccent("Finance"), allocationButton);
        chargeCard.Header.Controls.SetChildIndex(allocationButton, chargeCard.Header.Controls.Count - 1);
        chargeCard.Content.Controls.Add(
            charges);
        /* It now opens from the Payment History button, so both of those keep the width. */
        paymentHistoryHost =
            new Panel {
                Dock = DockStyle.Fill,
                Visible = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
        paymentHistoryHost.Controls.Add(
            recentPayments);
        Controls.Add(
            paymentHistoryHost);
        /* Paying for a sibling. Ticking one loads that child's outstanding charges into the same grid on the left, so one payment can cover several children without leaving this screen. */
        var siblingsCard =
            HumanUi.Card(
                "Paying For",
                "Select children to pay for.");
        var siblingPanel =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
        siblingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(42)));
        siblingPanel.Controls.Add(primaryPayer,0,0);
        siblingPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                55f));
        siblingPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppScale.Scale(25)));
        siblingPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppMetrics.ButtonHeight + AppScale.Scale(8)));
        siblingPanel.Controls.Add(
            siblings,
            0,
            1);
        siblingPanel.Controls.Add(
            siblingHint,
            0,
            2);
        selectAllSiblings =
            new ModernButton {
                Text = UiText.Actions.SelectAll,
                Dock = DockStyle.Left,
                Width = AppScale.Scale(108),
                Height = AppTheme.CompactButtonHeight,
                ThemeRole = "Outline",
                Enabled = false
            };
        selectAllSiblings.SetOutlineColors(
            AppTheme.Border,
            AppTheme.Text,
            AppTheme.Surface);
        selectAllSiblings.Click +=
            (_, _) => ToggleAllSiblings();
        selectAllSiblings.TabStop = false;
        siblings.PreviewKeyDown += (_, e) => { if(e.KeyCode==Keys.Tab) e.IsInputKey=true; };
        siblings.KeyDown += (_, e) => {
            if(e.KeyCode==Keys.Tab && !e.Shift) {
                if(siblingBusy) focusAmountAfterSiblingLoad=true;
                else { paymentAmount.Focus(); paymentAmount.SelectAll(); }
                e.Handled=true; e.SuppressKeyPress=true;
            }
        };
        /* Added to the panel HERE, after the button exists. */
        siblingPanel.Controls.Add(
            selectAllSiblings,
            0,
            3);
        /* The per-child proposal, collapsed to nothing until a family payment is actually in progress - see ProposeFamilyAllocation. */
        proposalRow =
            new RowStyle(
                SizeType.Absolute,
                0);
        siblingPanel.RowStyles.Add(
            proposalRow);
        ConfigureProposalGrid();
        siblingPanel.Controls.Add(
            familyProposal,
            0,
            4);
        siblings.ItemCheck +=
            async (_, e) => await SiblingCheckedAsync(e);
        siblingsCard.Content.Controls.Add(
            siblingPanel);
        var work =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 6, 0, 0),
                Margin = new Padding(0)
            };
        // Keep the family context visible; reserve most of the space for charges.
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
        siblingsCard.Margin = new Padding(0, 0, 8, 0);
        chargeCard.Margin = new Padding(0);
        work.Controls.Add(chargeCard, 1, 0);
        work.Controls.Add(siblingsCard, 0, 0);
        siblingsCard.Visible = true;
        updateSiblingLayout = () => {
            siblingsCard.Visible = true;
            work.PerformLayout();
        };
        selectAllSiblings.EnabledChanged += (_, _) => updateSiblingLayout?.Invoke();
        var paymentSummary = HumanUi.Card(
            "Payment", "Enter the amount received.");
        var summary = new TableLayoutPanel {
            Dock = DockStyle.Top, AutoSize = true,
            ColumnCount = 1, RowCount = 13,
            Padding = new Padding(0), Margin = new Padding(0)
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(40, AppScale.ControlSizeScale)));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight + 2));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(40, AppScale.ControlSizeScale)));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight + 2));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(40, AppScale.ControlSizeScale)));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight + 2));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight + 2));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 6));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 6));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 6));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 6));
        summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        summary.SizeChanged += (_, _) => collectionResult.MaximumSize = new Size(Math.Max(1, summary.ClientSize.Width - 8), 0);
        totalDue.Text = balance.Text = "0.00";
        suppressPaymentEvents = true;
        try { paymentAmount.Text = "0"; }
        finally { suppressPaymentEvents = false; }

        summary.Controls.Add(SummaryField("Outstanding (Rs.)", totalDue, AppTheme.HeadingText), 0, 0);
        summary.Controls.Add(HumanUi.Field("Extra Discount (Rs.)", discountAmount), 0, 1);
        summary.Controls.Add(SummaryField("Amount to Pay (Rs.)", amountToPay, AppTheme.HeadingText), 0, 2);
        summary.Controls.Add(HumanUi.Field("Amount Received (Rs.)", paymentAmount, true), 0, 3);
        var amountError = new Label {
            Dock = DockStyle.Fill, Margin = new Padding(8, 0, 8, 0),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.Danger,
            TextAlign = ContentAlignment.TopLeft, AccessibleRole = AccessibleRole.Alert,
            Visible = false
        };
        summary.Controls.Add(amountError, 0, 4);
        amountError.VisibleChanged += (_, _) =>
            summary.RowStyles[4].Height = amountError.Visible
                ? AppMetrics.TextHeight(AppTheme.SmallFont) * 2 + 4 : 0;
        (collectionValidator ??= CreateValidator()).BindMessageLabel(paymentAmount, amountError);
        summary.Controls.Add(SummaryField("Remaining Balance (Rs.)", balance, AppTheme.Text), 0, 5);
        var paymentDetails = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0)
        };
        paymentDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        paymentDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        paymentDetails.Controls.Add(HumanUi.Field("Method", method), 0, 0);
        paymentDetails.Controls.Add(HumanUi.Field("Reference No.", reference), 1, 0);
        summary.Controls.Add(paymentDetails, 0, 6);
        summary.Controls.Add(HumanUi.Field("Remarks", remarks), 0, 7);
        fullAmountButton = AddAction("Use Full Amount", (_, _) => UseFullAmount(), "Secondary");
        var receiptActions = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            Margin = new Padding(0)
        };
        receiptActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        receiptActions.Controls.Add(fullAmountButton, 0, 0);

        summary.Controls.Add(receiptActions, 0, 8);
        summary.Controls.Add(executeButton!, 0, 9);
        summary.Controls.Add(printReceiptButton!, 0, 10);
        summary.Controls.Add(clear, 0, 11);
        summary.Controls.Add(collectionResult, 0, 12);
        foreach (ModernButton button in new[] { fullAmountButton!, printReceiptButton!, executeButton!, clear })
        {
            button.AutoSize = false;
            button.MinimumSize = Size.Empty;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 2, 4, 2);
            button.Height = AppMetrics.ButtonHeight;
        }
        executeButton!.ThemeRole = "Primary";
        foreach (TableLayoutPanel field in summary.Controls.OfType<TableLayoutPanel>())
            if (field.ColumnStyles.Count == 0)
                field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (TableLayoutPanel field in paymentDetails.Controls)
            if (field.ColumnStyles.Count == 0)
                field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var paymentScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        paymentScroll.Controls.Add(summary);
        paymentSummary.Content.Controls.Add(paymentScroll);

        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false,
            Padding = new Padding(0, 6, 0, 0), Margin = new Padding(0)
        };
        paymentHistoryButton!.Text = "Recent Payments";
        foreach (Control button in new Control[] { studentFeesMenuButton!, paymentHistoryButton })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.None;
            button.Height = AppMetrics.ButtonHeight;
            button.Width = TextRenderer.MeasureText(button.Text, button.Font).Width + AppScale.Scale(24);
            button.Margin = new Padding(0, 0, 8, 0);
            actions.Controls.Add(button);
        }
        var workspace = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(0, 8, 0, 0), Margin = new Padding(0)
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        work.Padding = new Padding(0);
        work.Margin = new Padding(0, 0, 10, 0);
        workspace.Controls.Add(work, 0, 0);
        workspace.Controls.Add(paymentSummary, 1, 0);
        context.SetHeaderText("Student", "Search by admission number, student or father name.");
        chargeCard.SetHeaderText("Outstanding Charges", "Review the charges included in this payment.");
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(0), Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 16));
        root.Controls.Add(context, 0, 0);
        root.Controls.Add(workspace, 0, 1);
        root.Controls.Add(actions, 0, 2);
        SetContent(root, null);
        FormFlow.EnterMovesToNext(this);
        FormFlow.OrderTableFields(root);
        ClearPayment();
    }
    private void UseFullAmount()
    {
        if (transactionBusy || admissionId <= 0 || enrollmentId <= 0) return;
        if (!decimal.TryParse((string.IsNullOrWhiteSpace(discountAmount.Text) ? "0" : discountAmount.Text.Trim()), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out decimal discount) || discount < 0m)
        {
            MarkCollectionField(discountAmount, "Enter a valid discount of zero or more");
            return;
        }
        decimal payable = CurrentPayableAfterDiscount();
        if (payable != decimal.Truncate(payable))
        {
            MarkCollectionField(paymentAmount, "Balance contains a fraction. Enter whole rupees; it will not be rounded.");
            return;
        }
        paymentAmount.Text = payable.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        paymentAmount.Focus();
        paymentAmount.SelectAll();
    }
    private static Control SummaryField(string caption, Label value, Color valueColor)
    {
        var panel = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = AppTheme.SurfaceSecondary,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label {
            Text = caption, Dock = DockStyle.Fill, ForeColor = AppTheme.MutedText,
            Font = AppTheme.SmallFont, TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.Margin = new Padding(0);
        value.Padding = new Padding(0);
        value.Font = new Font(AppTheme.HeadingFont.FontFamily, 16f, FontStyle.Bold);
        value.ForeColor = valueColor;
        value.TextAlign = ContentAlignment.MiddleRight;
        value.AutoEllipsis = true;
        panel.Controls.Add(value, 1, 0);
        return panel;
    }
    private void ConfigureChargesGrid()
    {
        charges.Columns.Clear();
        charges.AutoGenerateColumns = false;
        charges.Dock = DockStyle.Fill;
        charges.AllowUserToResizeColumns = true;
        charges.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        charges.MultiSelect = false;
        charges.AllowUserToAddRows = false;
        charges.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;
        charges.ScrollBars = ScrollBars.Both;
        var payColumn =
            new DataGridViewButtonColumn {
                Name = "Pay",
                HeaderText = "",
                ReadOnly = false,
                MinimumWidth = AppScale.Scale(48),
                Width = AppScale.Scale(48),
                UseColumnTextForButtonValue = false,
                DefaultCellStyle =
                    new DataGridViewCellStyle {
                        Alignment =
                            DataGridViewContentAlignment.MiddleCenter,
                        Font =
                            AppTheme.HeadingFont
                    }
            };
        charges.Columns.Add(payColumn);
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "FeeTypeName",
                HeaderText = "Charge",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(80)
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "BillingMonth",
                HeaderText = "Period",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(80)
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "DueDate",
                HeaderText = "Due Date",
                Visible = false,
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(80)
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Balance",
                HeaderText = "Due",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(70),
                DefaultCellStyle =
                    new DataGridViewCellStyle {
                        Format = "0.##",
                        Alignment =
                            DataGridViewContentAlignment.MiddleRight
                    }
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Amount",
                HeaderText = "Pay Now",
                ReadOnly = false,
                MinimumWidth = AppScale.Scale(75),
                DefaultCellStyle =
                    new DataGridViewCellStyle {
                        Format = "0.##",
                        Alignment =
                            DataGridViewContentAlignment.MiddleRight
                    }
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "BillingChargeId",
                Visible = false,
                ReadOnly = true
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "GrossAmount",
                HeaderText = "Fee",
                Visible = true,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "0.##", Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "NetAmount",
                Visible = false,
                ReadOnly = true
            });
        /* The rows are added positionally against the columns above, so inserting a column in the middle would silently shift every value. */
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "AdmissionId",
                Visible = false,
                ReadOnly = true
            });
        charges.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "EnrollmentId",
                Visible = false,
                ReadOnly = true
            });
        charges.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "AppliedDiscount", HeaderText = "Discount", ReadOnly = !CurrentUser.HasPermission("Fees.Adjust"),
            DefaultCellStyle = new DataGridViewCellStyle {
                Format = "0.##", Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });
        charges.Columns["GrossAmount"].DisplayIndex = 4;
        charges.Columns["AppliedDiscount"].DisplayIndex = 5;
        charges.Columns.Add(new DataGridViewTextBoxColumn {
            Name="StudentName", HeaderText="Student", ReadOnly=true,
            MinimumWidth=AppScale.Scale(95)
        });
        charges.Columns["StudentName"].DisplayIndex=1;
        charges.Columns.Add(new DataGridViewTextBoxColumn { Name="FeeDisplayOrder", Visible=false, ReadOnly=true });
        charges.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        charges.Columns["Pay"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        foreach (string columnName in new[] { "GrossAmount", "AppliedDiscount", "Balance", "Amount" })
        {
            charges.Columns[columnName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            charges.Columns[columnName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        /* Column fitting is no longer wired here. It now lives in UiHelper.FitColumnsToWidth and HumanUi.Grid routes every grid to it. */
        charges.CellClick += (_, e) => {
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0)
            {
                return;
            }
            if (charges.Columns[e.ColumnIndex].Name !=
                "Pay")
            {
                return;
            }
            var row =
                charges.Rows[e.RowIndex];
            bool selected =
                row.Tag is bool value && value;
            selected =
                !selected;
            row.Tag =
                selected;
            decimal due =
                GetRowNetDue(
                    row,
                    GetDiscountAllocations());
            row.Cells["Amount"].Value =
                selected
                    ? due
                    : 0m;
            row.Cells["Pay"].Value =
                selected
                    ? "−"
                    : "+";
            UpdateCollectionTotals();
            SyncPaymentFieldToGrid();
        };
        charges.CellValidating += (_, e) => {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || charges.Columns[e.ColumnIndex].Name != "AppliedDiscount") return;
            var row = charges.Rows[e.RowIndex];
            decimal previous = row.Cells["AppliedDiscount"].Tag is decimal old ? old : 0m;
            decimal maximum = GetDecimal(row.Cells["Balance"].Value)+previous;
            if (!InputParse.TryAmount(Convert.ToString(e.FormattedValue), out decimal value) || value<0m || value>maximum)
            {
                e.Cancel=true;
                row.ErrorText=$"Enter a discount between 0 and {maximum:0.##}.";
            }
            else row.ErrorText=string.Empty;
        };
        charges.CellEndEdit += (_, e) => {
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0)
            {
                return;
            }
            if (charges.Columns[e.ColumnIndex].Name == "AppliedDiscount")
            {
                var edited = charges.Rows[e.RowIndex];
                decimal previous = edited.Cells["AppliedDiscount"].Tag is decimal old ? old : 0m;
                decimal gross = GetDecimal(edited.Cells["GrossAmount"].Value);
                decimal paid = gross - previous - GetDecimal(edited.Cells["Balance"].Value);
                decimal proposed = Math.Clamp(GetDecimal(edited.Cells["AppliedDiscount"].Value), 0m, Math.Max(0m, gross-paid));
                edited.Cells["AppliedDiscount"].Value = proposed;
                edited.Cells["AppliedDiscount"].Tag = proposed;
                edited.Cells["Balance"].Value = Math.Max(0m,gross-paid-proposed);
                edited.Cells["NetAmount"].Value = gross-proposed;
                if (edited.Tag is true) edited.Cells["Amount"].Value = GetRowNetDue(edited,GetDiscountAllocations());
                UpdateCollectionTotals();
                SyncPaymentFieldToGrid();
                return;
            }
            if (charges.Columns[e.ColumnIndex].Name !=
                "Amount")
            {
                return;
            }
            DataGridViewRow row =
                charges.Rows[e.RowIndex];
            decimal max =
                GetRowNetDue(
                    row,
                    GetDiscountAllocations());
            decimal amount =
                GetDecimal(
                    row.Cells["Amount"].Value);
            amount =
                Math.Max(
                    0m,
                    Math.Min(
                        amount,
                        max));
            row.Cells["Amount"].Value =
                amount;
            bool selected =
                amount > 0m;
            row.Tag =
                selected;
            row.Cells["Pay"].Value =
                selected
                    ? "−"
                    : "+";
            UpdateCollectionTotals();
            SyncPaymentFieldToGrid();
        };
        ConfigureWholeAmountColumn(charges, "Amount");
        /* Payment and discount allocation retain their fee priority even if rows move. */
        foreach (DataGridViewColumn chargeColumn in charges.Columns)
            chargeColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        charges.ContextMenuStrip = chargeMenu;
        /* Both gestures land here: a right-click over a row, and the keyboard's Menu key / Shift+F10 on the current row. */
        chargeMenu.Opening += (_, e) => {
            Point pointer = charges.PointToClient(Cursor.Position);
            var hit = charges.HitTest(pointer.X, pointer.Y);
            int rowIndex = hit.RowIndex >= 0
                ? hit.RowIndex
                : charges.CurrentRow?.Index ?? -1;
            e.Cancel = !BuildChargeMenu(rowIndex);
            if (e.Cancel || rowIndex < 0 || rowIndex >= charges.Rows.Count)
                return;
            charges.ClearSelection();
            charges.Rows[rowIndex].Selected = true;
            charges.CurrentCell = charges.Rows[rowIndex].Cells["FeeTypeName"];
        };
    }
    /// <summary>Fills the charges grid's row menu for one row and says whether it should open at all.</summary>
    private bool BuildChargeMenu(int rowIndex)
    {
        chargeMenu.Items.Clear();
        if (rowIndex < 0 || rowIndex >= charges.Rows.Count)
            return false;
        DataGridViewRow row =
            charges.Rows[rowIndex];
        long chargeId =
            GetLong(
                row.Cells["BillingChargeId"].Value);
        if (chargeId <= 0)
            return false;
        var header =
            chargeMenu.Items.Add(
                $"{GetText(row.Cells["FeeTypeName"].Value)}  |  "
                + $"{GetText(row.Cells["BillingMonth"].Value)}  |  "
                + $"Rs. {GetDecimal(row.Cells["Balance"].Value):N0} due");
        header.Enabled = false;
        chargeMenu.Items.Add(
            new ToolStripSeparator());
        var reverse =
            chargeMenu.Items.Add(
                "Reverse this charge...");
        /* A collection in flight owns the whole charger list - the allocation it is about to post was built from these rows. */
        reverse.Enabled = !transactionBusy;
        reverse.Click += async (_, _) => await ReverseChargeAsync(rowIndex);
        return true;
    }
    private void ConfigurePaymentsGrid()
    {
        recentPayments.Columns.Clear();
        recentPayments.AutoGenerateColumns = false;
        recentPayments.Dock = DockStyle.Fill;
        recentPayments.AllowUserToResizeColumns = true;
        recentPayments.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        recentPayments.MultiSelect = false;
        recentPayments.AllowUserToAddRows = false;
        recentPayments.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;
        recentPayments.ScrollBars =
            ScrollBars.Both;
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "PaymentDate",
                HeaderText = "Date",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(85)
            });
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "ReceiptNumber",
                HeaderText = "Receipt",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(110)
            });
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "ChargeType",
                HeaderText = "Charge Type",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(110)
            });
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "TransactionType",
                HeaderText = "Transaction",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(95)
            });
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Amount",
                HeaderText = "Original Paid",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(85),
                DefaultCellStyle =
                    new DataGridViewCellStyle {
                        Format = "0.##",
                        Alignment =
                            DataGridViewContentAlignment.MiddleRight
                    }
            });
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "PaymentMethod",
                HeaderText = "Method",
                ReadOnly = true,
                MinimumWidth = AppScale.Scale(80)
            });
        recentPayments.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "PaymentId",
                Visible = false,
                ReadOnly = true
            });
        foreach (string name in new[] { "Adjusted", "NetPaid" })
            recentPayments.Columns.Add(new DataGridViewTextBoxColumn {
                Name = name, HeaderText = name == "Adjusted" ? "Adjusted" : "Net Paid",
                ReadOnly = true, MinimumWidth = AppScale.Scale(85),
                DefaultCellStyle = new DataGridViewCellStyle { Format = "0.##", Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        recentPayments.Columns["Adjusted"].DisplayIndex = 5;
        recentPayments.Columns["NetPaid"].DisplayIndex = 6;
        foreach (string name in new[] { "Amount", "Adjusted", "NetPaid" })
        {
            recentPayments.Columns[name].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            recentPayments.Columns[name].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        recentPayments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        recentPayments.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var sizing in new[] { ("PaymentDate",85f), ("ReceiptNumber",130f),
            ("ChargeType",200f), ("TransactionType",115f), ("Amount",100f),
            ("Adjusted",85f), ("NetPaid",85f), ("PaymentMethod",75f) })
            recentPayments.Columns[sizing.Item1].FillWeight = sizing.Item2;
        recentPayments.Columns["ChargeType"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        recentPayments.Columns["TransactionType"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        /* Payment history is read-only context on this page. */
        recentPayments.SelectionChanged -= OnPaymentHistorySelectionChanged;
        recentPayments.SelectionChanged += OnPaymentHistorySelectionChanged;
    }
    /// <summary>One subscription for the page's lifetime.</summary>
    private void OnPaymentHistorySelectionChanged(
        object? sender,
        EventArgs e) =>
        UpdateReprintAvailability();
    /// <summary>Only collected payment entries can be reprinted as receipts.</summary>
    private void UpdateReprintAvailability()
    {
        if (reprintReceiptButton is null ||
            reprintReceiptButton.IsDisposed)
        {
            return;
        }
        reprintReceiptButton.Enabled =
            recentPayments.CurrentRow is not null &&
            recentPayments.CurrentRow.Tag is true &&
            !string.IsNullOrWhiteSpace(
                GetText(
                    recentPayments.CurrentRow.Cells["ReceiptNumber"].Value));
    }
    /// <summary>Reprints the receipt of the payment selected in the Payment History list.</summary>
    private async Task ReprintSelectedReceiptAsync()
    {
        if (recentPayments.CurrentRow?.Tag is not true)
        {
            Warn("Select a collected payment to print its receipt. Adjustment and reversed entries have no printable receipt.");
            return;
        }
        string receiptNumber =
            recentPayments.CurrentRow is null
                ? string.Empty
                : GetText(
                    recentPayments.CurrentRow.Cells["ReceiptNumber"].Value);
        if (string.IsNullOrWhiteSpace(receiptNumber))
        {
            Warn("Select a recorded payment in the list first, so the receipt number is known.");
            return;
        }
        await ReprintReceiptAsync(receiptNumber);
    }
    /// <summary>Prints any receipt again, by its number - the parent who comes back a week later.</summary>
    private async Task ReprintReceiptAsync(
        string receiptNumber)
    {
        var institution =
            await Core.InstitutionProfile.GetPrintIdentityAsync();
        if (string.IsNullOrWhiteSpace(institution.Name))
        {
            Alert(
                "The school profile could not be read, so nothing was printed. " +
                "Check School Profile, then try again.");
            return;
        }
        await PrintGroupReceiptAsync(institution, receiptNumber);
    }
    /// <summary>Puts the page into its one and only mode: Collection. Keeping the method (rather than deleting its call sites) keeps the collection state explicit and in one place.</summary>
    private void UpdateOperationMode()
    {
        charges.Columns["Pay"].Visible = manualAllocation;
        charges.Columns["Pay"].ReadOnly = !manualAllocation;
        charges.Columns["Amount"].ReadOnly = !manualAllocation;
        foreach (DataGridViewRow row in charges.Rows)
        {
            if (row.IsNewRow)
                continue;
            row.Cells["Pay"].ReadOnly = !manualAllocation;
            row.Cells["Amount"].ReadOnly = !manualAllocation;
        }
        charges.Enabled = true;
        method.Enabled = true;
        reference.Enabled = true;
        discountAmount.Enabled = true;
        paymentAmount.Enabled = true;
        // Payment history is read-only context here. Selecting a payment to act on is done on Fee Adjustments, not on this page.
        recentPayments.Enabled = false;
        recentPayments.ClearSelection();
        UpdateCollectionTotals();
    }
    private async Task LoadStudentByAdmissionId(
        int admissionId)
    {
        int version = ++loadVersion;
        try
        {
            SetStatus("Loading student...");
            DataRow? found = FindCachedStudent(
                await StudentSearchCache.GetAllAsync(),
                admissionId);
            if (found is null)
            {
                /* Admitted since the list was read. */
                StudentSearchCache.Invalidate();
                found = FindCachedStudent(
                    await StudentSearchCache.GetAllAsync(),
                    admissionId);
            }
            if (IsDisposed || version != loadVersion)
                return;
            if (found is null)
            {
                SetStatus("That student could not be found.", true);
                return;
            }
            suppressStudentEvents = true;
            try
            {
                student.SetStudent(found);
            }
            finally
            {
                suppressStudentEvents = false;
            }
            await SelectStudentAsync(version);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == loadVersion)
                Fail(ex);
        }
    }
    private static DataRow? FindCachedStudent(
        DataTable all,
        int admissionId)
    {
        foreach (DataRow row in all.Rows)
        {
            if (GetInt(row, "AdmissionId") == admissionId)
                return row;
        }
        return null;
    }
    private async Task SelectStudent() => await SelectStudentAsync(++loadVersion);
    private async Task SelectStudentAsync(int version)
    {
        int selectedAdmissionId =
            student.SelectedId;
        admissionId =
            selectedAdmissionId;
        primaryPayer.Text=selectedAdmissionId>0 ? $"{StudentNameFor(selectedAdmissionId)} · #{AdmissionNumbers.ForId(selectedAdmissionId)} (included)" : "";
        enrollmentId = 0;
        enrollment.ClearLookup();
        charges.Rows.Clear();
        recentPayments.Rows.Clear();
        /* The previous student's money must not follow the next one. */
        suppressDiscountEvents = true;
        try
        {
            discountAmount.Text = "0";
        }
        finally
        {
            suppressDiscountEvents = false;
        }
        suppressPaymentEvents = true;
        try
        {
            paymentAmount.Text = "0";
        }
        finally
        {
            suppressPaymentEvents = false;
        }
        /* A new student is a new collection, so the date is today's again unless this student's own date is set deliberately afterwards. */
        ResetPaymentDateToNow(true);
        enrollment.Enabled = false;
        if (feeAssignmentsButton is not null)
            feeAssignmentsButton.Enabled = false;
        if (addChargeButton is not null)
            addChargeButton.Enabled = false;
        if (futureFeesButton is not null)
            futureFeesButton.Enabled = false;
        if (historyButton is not null)
            historyButton.Enabled = false;
        if (admissionId <= 0)
        {
            UpdateCollectionTotals();
            return;
        }
        /* The reset above (enrollment.ClearLookup) raises the enrollment change handler at 476, which runs LoadCharges/LoadRecentPayments and advances loadVersion on the way. */
        version = loadVersion;
        try
        {
            // Family and enrollment data are independent, so both can start as soon as the student is known.
            Task siblingsTask = LoadSiblingsAsync(version);
            var placementTask = ReportClassSectionLookup.QueryAsync();
            Task<DataTable> enrollmentsTask =
                DatabaseHelper.QueryUserAsync(
                    "GetStudentEnrollments",
                    DatabaseHelper.Int(
                        "@AdmissionId",
                        admissionId));
            await Task.WhenAll(siblingsTask, enrollmentsTask, placementTask);
            var raw = await enrollmentsTask;
            var placements = (await placementTask).AsEnumerable()
                .ToDictionary(row => GetInt(row,"ClassSectionId"),row => GetText(row,"CampusName"));
            /* this reply re-binds the Enrollment dropdown and writes enrollmentId, the pair every money action then uses, and it had no post-await check at all. */
            if (IsDisposed ||
                version != loadVersion ||
                student.SelectedId != selectedAdmissionId)
            {
                return;
            }
            var dt =
                new DataTable();
            dt.Columns.Add(
                "EnrollmentId",
                typeof(int));
            dt.Columns.Add(
                "Display",
                typeof(string));
            dt.Columns.Add(
                "Status",
                typeof(string));
            dt.Columns.Add(
                "OutstandingAmount",
                typeof(decimal));
            dt.Columns.Add(
                "ClassName",
                typeof(string));
            dt.Columns.Add(
                "SectionName",
                typeof(string));
            dt.Columns.Add("CampusName",typeof(string));
            foreach (DataRow row in raw.Rows)
            {
                int id =
                    GetInt(
                        row,
                        "EnrollmentId");
                if (id <= 0)
                    continue;
                string status =
                    GetText(
                        row,
                        "Status");
                decimal outstanding =
                    GetDecimal(
                        row,
                        "OutstandingAmount");
                string className =
                    GetText(row, "ClassName");
                string sectionName =
                    GetText(row, "SectionName");
                string display =
                    $"{GetText(row, "YearName")}, " +
                    $"{className} — " +
                    $"{sectionName} " +
                    $"({status})";
                dt.Rows.Add(
                    id,
                    display,
                    status,
                    outstanding,
                    className,
                    sectionName,
                    placements.GetValueOrDefault(GetInt(row,"ClassSectionId"),string.Empty));
            }
            enrollment.Bind(
                dt,
                "EnrollmentId",
                "Display");
            if (dt.Rows.Count == 0)
            {
                enrollmentId = 0;
                enrollment.Enabled = false;
                UpdateCollectionTotals();
                SetStatus(
                    "The student has no outstanding charges.");
                return;
            }
            int activeEnrollmentId = 0;
            foreach (DataRow row in dt.Rows)
            {
                if (string.Equals(
                        GetText(
                            row,
                            "Status"),
                        "Active",
                        StringComparison.OrdinalIgnoreCase))
                {
                    activeEnrollmentId =
                        GetInt(
                            row,
                            "EnrollmentId");
                    break;
                }
            }
            enrollment.Enabled =
                dt.Rows.Count > 1;
            /* A promoted student who has not returned yet has a new Active enrollment with no charges (BillingReady = 0) beside last year's enrollment, which may still owe money. */
            int owingEnrollmentId = 0;
            foreach (DataRow owingRow in dt.Rows)
            {
                if (GetDecimal(owingRow, "OutstandingAmount") > 0)
                {
                    owingEnrollmentId = GetInt(owingRow, "EnrollmentId");
                    break;
                }
            }
            int selectedEnrollmentId =
                owingEnrollmentId > 0
                    ? owingEnrollmentId
                    : activeEnrollmentId > 0
                        ? activeEnrollmentId
                        : GetInt(dt.Rows[0], "EnrollmentId");
            suppressSearch = true;
            try
            {
                enrollment.SelectedValue =
                    selectedEnrollmentId;
            }
            finally
            {
                suppressSearch = false;
            }
            enrollmentId =
                selectedEnrollmentId;
            charges.Rows.Clear();
            recentPayments.Rows.Clear();
            if (feeAssignmentsButton is not null)
                feeAssignmentsButton.Enabled = admissionId > 0 && enrollmentId > 0;
            if (addChargeButton is not null)
                addChargeButton.Enabled = admissionId > 0 && enrollmentId > 0;
            if (futureFeesButton is not null)
                futureFeesButton.Enabled = admissionId > 0 && enrollmentId > 0;
            if (historyButton is not null)
                historyButton.Enabled = admissionId > 0 && enrollmentId > 0;
            await Task.WhenAll(
                LoadChargesAsync(version),
                LoadRecentPaymentsAsync(version));
            UpdateOperationMode();
            /* Loading fees does not imply that the parent is paying the full balance. */
            RefreshEnteredPaymentAllocation();
            SetStatus(
                dt.Rows.Count == 1
                    ? "Current enrollment loaded."
                    : "Current enrollment loaded. Other outstanding enrollments are available.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private async Task LoadCharges() => await LoadChargesAsync(++loadVersion);
    private async Task LoadChargesAsync(int version)
    {
        charges.Rows.Clear();
        if (admissionId <= 0 || enrollmentId <= 0)
            return;
        try
        {
            await AppendChargesAsync(admissionId, enrollmentId, version);
            if (IsDisposed || version != loadVersion)
                return;
            UpdateCollectionTotals();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == loadVersion)
                Fail(ex);
        }
    }
    /// <summary>Appends one student's outstanding charges to the charges grid WITHOUT clearing it.</summary>
    /// <summary>Monthly charges first, with tuition then transport within each frequency group.</summary>
    private static int SettlementRank(
        DataRow row,
        Dictionary<int, string> frequencyByAssignment)
    {
        string code =
            GetText(row, "FeeTypeCode");
        string name =
            GetText(row, "FeeTypeName");
        int assignmentId =
            GetInt(row, "StudentFeeAssignmentId");
        string frequency =
            frequencyByAssignment.TryGetValue(assignmentId, out string? billedAs) && billedAs is not null
                ? billedAs
                : string.Empty;
        string chargeKind = GetText(row, "ChargeKind");
        bool monthly = string.Equals(chargeKind, "Monthly", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(chargeKind) && string.Equals(frequency, "Monthly", StringComparison.OrdinalIgnoreCase);
        int feeRank = string.Equals(code, "TUITION", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tuition", StringComparison.OrdinalIgnoreCase) ? 0 :
            string.Equals(code, "TRANSPORT", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("transport", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        return (monthly ? 0 : 10) + feeRank;
    }
    /// <summary>StudentFeeAssignmentId -> Frequency for one enrollment, so a charge can be ranked by what it was billed under rather than by a guess from its fee type.</summary>
    private static async Task<Dictionary<int, string>> LoadAssignmentFrequenciesAsync(
        int forEnrollmentId)
    {
        var frequencies =
            new Dictionary<int, string>();
        if (forEnrollmentId <= 0)
            return frequencies;
        try
        {
            DataTable assignments =
                await DatabaseHelper.QueryUserAsync(
                    "GetStudentFeeAssignments",
                    DatabaseHelper.Int("@EnrollmentId", forEnrollmentId));
            foreach (DataRow row in assignments.Rows)
            {
                int id =
                    GetInt(row, "StudentFeeAssignmentId");
                if (id > 0)
                    frequencies[id] = GetText(row, "Frequency");
            }
        }
        catch (Exception nonFatalException30)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException30, "FeeCollectionPage: LoadAssignmentFrequenciesAsync fallback");
            /* Ranking only - see the summary above. */
        }
        return frequencies;
    }
    private async Task<int> AppendChargesAsync(
        int forAdmissionId,
        int forEnrollmentId,
        int version = -1,
        int expectedPrimaryAdmissionId = 0,
        int expectedPrimaryEnrollmentId = 0)
    {
        if (forAdmissionId <= 0 || forEnrollmentId <= 0)
            return 0;
        DataTable raw = await DatabaseHelper.QueryUserAsync(
            "GetOutstandingCharges",
            DatabaseHelper.Int("@AdmissionId", forAdmissionId),
            DatabaseHelper.Int("@EnrollmentId", forEnrollmentId),
            DatabaseHelper.Date("@AsOfDate", DateTime.Today));
        Dictionary<int, string> frequencyByAssignment =
            await LoadAssignmentFrequenciesAsync(forEnrollmentId);
        var feeOrderRows = await Task.Run(() => DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT FeeTypeId,DisplayOrder FROM dbo.FeeTypes;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId)));
        var feeOrder=feeOrderRows.AsEnumerable().ToDictionary(row=>GetInt(row,"FeeTypeId"),row=>GetInt(row,"DisplayOrder"));
        /* Callers that pass no version (-1) are the explicit tick paths, which append on demand rather than on a load. */
        if (version >= 0 &&
            (IsDisposed || version != loadVersion))
        {
            return 0;
        }
        /* Those callers pass the student they started for; if the page has moved on, append nothing. */
        if (IsDisposed ||
            (expectedPrimaryAdmissionId > 0 &&
             (admissionId != expectedPrimaryAdmissionId ||
              enrollmentId != expectedPrimaryEnrollmentId)))
        {
            return 0;
        }
        /* Monthly fees precede other charges; tuition then transport within that group. */
        List<DataRow> ordered =
            raw.Rows
                .Cast<DataRow>()
                .OrderBy(
                    row => SettlementRank(
                        row,
                        frequencyByAssignment))
                .ToList();
        int added = 0;
        foreach (DataRow row in ordered)
        {
            long chargeId = GetLong(row, "BillingChargeId");
            if (chargeId <= 0)
                continue;
            // Never list the same charge twice - ticking a sibling, unticking and re-ticking would otherwise duplicate every one of their rows.
            if (ChargeAlreadyListed(chargeId))
                continue;
            decimal outstanding = GetDecimal(row, "Balance");
            bool additional = string.Equals(GetText(row, "ChargeKind"),
                "Manual", StringComparison.OrdinalIgnoreCase);
            string chargeLabel = GetText(row, "FeeTypeName").Replace(" Fee", "");
            string chargeReason = GetText(row, "Remarks");
            if (additional && !string.IsNullOrWhiteSpace(chargeReason))
                chargeLabel += " — " + chargeReason;
            string periodLabel = additional
                ? FormatDueDate(row["ChargeDate"])
                : FormatBillingMonth(row["BillingMonth"]);
            int index = charges.Rows.Add(
                "+",
                chargeLabel,
                periodLabel,
                FormatDueDate(row["DueDate"]),
                outstanding,
                0m,
                chargeId,
                GetDecimal(row, "GrossAmount"),
                GetDecimal(row, "NetAmount"),
                forAdmissionId,
                forEnrollmentId);
            charges.Rows[index].Tag = false;
            charges.Rows[index].Cells["AppliedDiscount"].Value =
                GetDecimal(row, "GrossAmount") - GetDecimal(row, "NetAmount");
            charges.Rows[index].Cells["AppliedDiscount"].Tag = charges.Rows[index].Cells["AppliedDiscount"].Value;
            charges.Rows[index].Cells["GrossAmount"].Tag = charges.Rows[index].Cells["AppliedDiscount"].Value;
            charges.Rows[index].Cells["Balance"].Tag = outstanding;
            charges.Rows[index].Cells["Pay"].Value = "+";
            charges.Rows[index].Cells["BillingChargeId"].Value = chargeId;
            charges.Rows[index].Cells["BillingChargeId"].Tag = SettlementRank(row, frequencyByAssignment);
            object period = additional ? row["ChargeDate"] : row["BillingMonth"];
            charges.Rows[index].Cells["BillingMonth"].Tag = period == DBNull.Value
                ? DateTime.MinValue : Convert.ToDateTime(period);
            charges.Rows[index].Cells["AdmissionId"].Value = forAdmissionId;
            charges.Rows[index].Cells["EnrollmentId"].Value = forEnrollmentId;
            charges.Rows[index].Cells["StudentName"].Value=StudentNameFor(forAdmissionId);
            charges.Rows[index].Cells["FeeDisplayOrder"].Value=feeOrder.GetValueOrDefault(GetInt(row,"FeeTypeId"),int.MaxValue);
            charges.Rows[index].Cells["StudentName"].ToolTipText=$"{StudentNameFor(forAdmissionId)} · Admission {AdmissionNumbers.ForId(forAdmissionId)}";
            charges.Rows[index].Cells["FeeTypeName"].ToolTipText=$"{GetText(row,"FeeTypeName")} · Due {FormatDueDate(row["DueDate"])}";
            added++;
        }
        if (added > 0) charges.Sort(new StudentChargeComparer(admissionId));
        return added;
    }
    private sealed class StudentChargeComparer(int primaryAdmissionId) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var left = (DataGridViewRow)x!;
            var right = (DataGridViewRow)y!;
            int leftStudent=Convert.ToInt32(left.Cells["AdmissionId"].Value);
            int rightStudent=Convert.ToInt32(right.Cells["AdmissionId"].Value);
            if(leftStudent!=rightStudent) {
                if(leftStudent==primaryAdmissionId) return -1;
                if(rightStudent==primaryAdmissionId) return 1;
                int name=StringComparer.CurrentCultureIgnoreCase.Compare(
                    Convert.ToString(left.Cells["StudentName"].Value),Convert.ToString(right.Cells["StudentName"].Value));
                return name!=0 ? name : leftStudent.CompareTo(rightStudent);
            }
            int feeOrder=Convert.ToInt32(left.Cells["FeeDisplayOrder"].Value)
                .CompareTo(Convert.ToInt32(right.Cells["FeeDisplayOrder"].Value));
            if(feeOrder!=0) return feeOrder;
            int period=ChargePeriod(left).CompareTo(ChargePeriod(right));
            return period!=0 ? period : Convert.ToInt64(left.Cells["BillingChargeId"].Value)
                .CompareTo(Convert.ToInt64(right.Cells["BillingChargeId"].Value));
        }
    }
    private static DateTime ChargePeriod(DataGridViewRow row) =>
        row.Cells["BillingMonth"].Tag is DateTime date ? date : DateTime.MinValue;
    /* UX - a refusal that belongs to one field is marked on that field (border + a few words on its caption) and focused, with the same words in the… */
    private FormValidator? collectionValidator;
    private void MarkCollectionField(Control field, string message)
    {
        FormValidator v = collectionValidator ??= CreateValidator();
        v.Begin();
        v.Fail(field, message);
        v.Finish();
    }
    private IEnumerable<DataGridViewRow> SettlementRows() => charges.Rows.Cast<DataGridViewRow>()
        .Where(row => !row.IsNewRow)
        .OrderBy(row => row.Cells["BillingChargeId"].Tag is int rank ? rank : 12)
        .ThenBy(ChargePeriod)
        .ThenBy(row => Convert.ToInt64(row.Cells["BillingChargeId"].Value));
    /// <summary>Spreads an amount across EVERY open charge, in the desk's settlement order, and writes the result into the grid.</summary>
    private decimal FillFromReceivedAmount(decimal received)
    {
        if (received <= 0m)
        {
            /* The grid follows the Paid field. */
            ClearPayNowAmounts();
            Warn("Enter an amount greater than zero.");
            return 0m;
        }
        var discountMap = GetDiscountAllocations();
        var open = SettlementRows()
            .Select(r => new {
                Row = r,
                ChargeId = GetLong(r.Cells["BillingChargeId"].Value),
                Due = GetDecimal(r.Cells["Balance"].Value)
            })
            .Where(x => x.ChargeId > 0 && x.Due > 0m)
            .ToList();
        if (open.Count == 0)
        {
            Warn("There is nothing outstanding to settle for this student.");
            return 0m;
        }
        decimal remaining = received;
        decimal placed = 0m;
        suppressPaymentEvents = true;
        try
        {
            foreach (var item in open)
            {
                decimal rowDiscount = discountMap.TryGetValue(item.ChargeId, out decimal d) ? d : 0m;
                decimal payable = Math.Max(0m, item.Due - rowDiscount);
                decimal give = Math.Min(remaining, payable);
                item.Row.Cells["Amount"].Value = give;
                /* Every row is visited now, so the tick and the marker are always consistent with the amount - no stale "+" left on a row from a previous, larger payment. */
                item.Row.Tag = give > 0m;
                item.Row.Cells["Pay"].Value = give > 0m ? "−" : "+";
                remaining -= give;
                placed += give;
            }
        }
        finally
        {
            suppressPaymentEvents = false;
        }
        return placed;
    }
    /// <summary>Sets every listed charge's Pay Now to zero and its marker to "+" (not included).</summary>
    private void ClearPayNowAmounts()
    {
        suppressPaymentEvents = true;
        try
        {
            foreach (DataGridViewRow row in charges.Rows)
            {
                if (row.IsNewRow)
                    continue;
                row.Cells["Amount"].Value = 0m;
                row.Tag = false;
                row.Cells["Pay"].Value = "+";
            }
        }
        finally
        {
            suppressPaymentEvents = false;
        }
    }
    /// <summary>What the listed charges owe after the discount entered in the summary - the figure the "Amount to Pay" field shows.</summary>
    private decimal CurrentPayableAfterDiscount()
    {
        var discountMap = GetDiscountAllocations();
        decimal payable = 0m;
        foreach (DataGridViewRow row in charges.Rows)
        {
            if (row.IsNewRow)
                continue;
            long chargeId =
                GetLong(
                    row.Cells["BillingChargeId"].Value);
            if (chargeId <= 0)
                continue;
            decimal due =
                GetDecimal(
                    row.Cells["Balance"].Value);
            decimal rowDiscount =
                discountMap.TryGetValue(
                    chargeId,
                    out decimal d)
                        ? d
                        : 0m;
            payable +=
                Math.Max(
                    0m,
                    due -
                    rowDiscount);
        }
        return payable;
    }
    /// <summary>Refresh allocation from the entered amount without assuming full payment.</summary>
    private void RefreshEnteredPaymentAllocation()
    {
        FillFromReceivedAmount(GetAmount(paymentAmount));
        UpdateCollectionTotals();
    }
    /// <summary>True while the Paid field still holds the figure the summary last offered - the untouched default.</summary>
    private bool PaidIsUntouchedDefault() =>
        Math.Abs(
            GetAmount(paymentAmount) -
            lastPayableShown) <= 0.005m;
    /// <summary>Reads the amount received, applies it in the grid's settlement order, and reports the result. An amount larger than the debt is reported by UpdateCollectionTotals, which already knows the payable figure.</summary>
    private void ApplyPaymentAcrossCharges()
    {
        decimal typed = GetAmount(paymentAmount);
        decimal placed = FillFromReceivedAmount(typed);
        UpdateCollectionTotals();
        if (typed > 0m && placed >= typed - 0.005m)
            Info($"Applied the full Rs. {placed:N0} across the open charges, in settlement order.");
    }
    /// <summary>Keeps the Payment field and the grid in step when the operator edits the grid directly, which is the finer of the two override levels.</summary>
    private void SyncPaymentFieldToGrid()
    {
        decimal applied = charges.Rows
            .Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Where(r => r.Tag is bool selected && selected)
            .Sum(r => GetDecimal(r.Cells["Amount"].Value));
        suppressPaymentEvents = true;
        try
        {
            paymentAmount.Text = applied.ToString("0.##");
        }
        finally
        {
            suppressPaymentEvents = false;
        }
    }
    private bool ChargeAlreadyListed(long chargeId)
    {
        foreach (DataGridViewRow row in charges.Rows)
        {
            if (row.IsNewRow)
                continue;
            if (GetLong(row.Cells["BillingChargeId"].Value) == chargeId)
                return true;
        }
        return false;
    }
    /// <summary>Loads the selected student's siblings into the Paying For list, all unticked.</summary>
    private async Task LoadSiblingsAsync(int version)
    {
        suppressSiblingEvents = true;
        try
        {
            siblings.Items.Clear();
            payingFor.Clear();
            selectAllSiblings.Enabled = false;
            if (admissionId <= 0)
                return;
            DataTable family = await DatabaseHelper.QueryUserAsync(
                "GetStudentFamily",
                DatabaseHelper.Int("@AdmissionId", admissionId));
            // The operator can pick another student while the family read is in flight.
            if (IsDisposed || version != loadVersion)
                return;
            if (family.Rows.Count == 0 || !family.Columns.Contains("FamilyId") ||
                family.Rows[0]["FamilyId"] == DBNull.Value)
            {
                SetSiblingHint("Not part of a family, so there are no siblings to add.");
                return;
            }
            DataTable members = family;
            var eligible = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT S.AdmissionId,
                    CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active') THEN 1 ELSE 0 END AS bit) IsActive,
                    (SELECT TOP(1) B.EnrollmentId FROM dbo.BillingCharges B
                     WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
                     ORDER BY B.EnrollmentId DESC) PendingEnrollmentId
                FROM dbo.Students S WHERE S.AdmissionId IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids));
                """, DatabaseHelper.Int("@UserId",CurrentUser.UserId), DatabaseHelper.P("@Ids",
                    System.Text.Json.JsonSerializer.Serialize(members.AsEnumerable().Select(row=>GetInt(row,"AdmissionId")).ToArray()))));
            if (IsDisposed || version != loadVersion)
                return;
            foreach (DataRow row in members.Rows)
            {
                int memberAdmissionId = GetInt(row, "AdmissionId");
                // Never list the student being collected from as their own sibling.
                if (memberAdmissionId <= 0 || memberAdmissionId == admissionId)
                    continue;
                var eligibility=eligible.AsEnumerable().FirstOrDefault(item=>Convert.ToInt32(item["AdmissionId"])==memberAdmissionId);
                if(eligibility==null) continue;
                bool active=Convert.ToBoolean(eligibility["IsActive"]);
                int pendingEnrollment=eligibility["PendingEnrollmentId"]==DBNull.Value ? 0 : Convert.ToInt32(eligibility["PendingEnrollmentId"]);
                if(!active && pendingEnrollment<=0) continue;
                int memberEnrollmentId = GetInt(row, "EnrollmentId");
                if(!active && pendingEnrollment>0) memberEnrollmentId=pendingEnrollment;
                string name = GetText(row, "StudentName");
                if (!string.IsNullOrWhiteSpace(GetText(row, "ClassName")))
                    name += $"  ({GetText(row, "ClassName")})";
                // The family read includes EnrollmentId. Without an enrollment there is nothing to collect for that child, so they are shown as unavailable rather than silently omitted.
                if(!active) name += " — pending fees";
                string label = memberEnrollmentId > 0
                    ? name
                    : $"{name}  - no active enrollment";
                payingFor[memberAdmissionId] = (memberEnrollmentId, GetText(row, "StudentName"));
                /* A CheckedListBox sorts its items by default, so Items[i] is NOT the i-th thing added. Storing the id on the item removes the assumption entirely. */
                siblings.Items.Add(new SiblingChoice(memberAdmissionId, label), false);
            }
            selectAllSiblings.Enabled = siblings.Items.Count > 0;
            SetSiblingHint(siblings.Items.Count == 0
                ? "This student's family has no other members."
                : $"{siblings.Items.Count} eligible sibling(s).");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            suppressSiblingEvents = false;
            updateSiblingLayout?.Invoke();
        }
    }
    private void SetSiblingHint(string message)
    {
        siblingHint.Text = message;
    }
    /// <summary>Ticking a sibling adds their outstanding charges to the grid; unticking removes them again and clears any amount entered against them.</summary>
    private async Task SiblingCheckedAsync(ItemCheckEventArgs e)
    {
        if (suppressSiblingEvents)
            return;
        if (siblingBusy)
        {
            // Re-assert the list against reality rather than acting on a tick that arrived mid-update.
            SetSiblingHint("Still updating the previous change - try again in a moment.");
            /* ItemCheck fires before the box changes, so returning without this left the box showing a state (ticked / unticked) that the grid never followed. */
            e.NewValue = e.CurrentValue;
            return;
        }
        // ItemCheck fires BEFORE the check state changes, so the new state has to come from e.NewValue rather than from the item itself.
        bool nowTicked = e.NewValue == CheckState.Checked;
        int index = e.Index;
        if (index < 0 || index >= siblings.Items.Count)
            return;
        int memberAdmissionId = SiblingAdmissionIdAt(index);
        if (memberAdmissionId <= 0)
            return;
        if (!payingFor.TryGetValue(memberAdmissionId, out var info))
            return;
        if (nowTicked)
        {
            if (info.EnrollmentId <= 0)
            {
                Warn($"{info.Name} has no active enrollment, so there are no fees to collect for them.");
                return;
            }
            int before = CountChargesFor(memberAdmissionId);
            int primaryAdmission = admissionId;
            int primaryEnrollment = enrollmentId;
            siblingBusy = true;
            try
            {
                int added = await AppendChargesAsync(
                    memberAdmissionId,
                    info.EnrollmentId,
                    expectedPrimaryAdmissionId: primaryAdmission,
                    expectedPrimaryEnrollmentId: primaryEnrollment);
                // The operator moved to another student while this loaded; the reply described the previous family and was not appended.
                if (primaryAdmission != admissionId || primaryEnrollment != enrollmentId)
                    return;
                if (added > 0)
                {
                    /* Include the new charges in allocation of the amount already entered. */
                    RefreshEnteredPaymentAllocation();
                }
                else
                {
                    UpdateCollectionTotals();
                }
                SetStatus(added == 0 && before == 0
                    ? $"{info.Name} has no outstanding charges, so nothing was added."
                    : $"{info.Name}: {added} charge(s) added to this payment.");
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                siblingBusy = false;
                if(focusAmountAfterSiblingLoad) {
                    focusAmountAfterSiblingLoad=false;
                    if(primaryAdmission==admissionId && primaryEnrollment==enrollmentId) {
                        paymentAmount.Focus(); paymentAmount.SelectAll();
                    }
                }
            }
        }
        else
        {
            RemoveChargesFor(memberAdmissionId);
            /* The Paid figure follows the grid here too, so "one figure for the whole group" stays true after a child is taken out. */
            SyncPaymentFieldToGrid();
            UpdateCollectionTotals();
            SetStatus($"{info.Name} removed from this payment.");
        }
    }
    /// <summary>A row in the Paying For list. ToString is what the CheckedListBox renders.</summary>
    private sealed record SiblingChoice(int AdmissionId, string Display)
    {
        public override string ToString() => Display;
    }
    /// <summary>The admission id behind a Paying For row, taken from the item itself rather than from a position in a parallel list - see SiblingChoice.</summary>
    private int SiblingAdmissionIdAt(int index) =>
        index >= 0 && index < siblings.Items.Count &&
        siblings.Items[index] is SiblingChoice choice
            ? choice.AdmissionId
            : 0;
    private int CountChargesFor(int forAdmissionId)
    {
        int count = 0;
        foreach (DataGridViewRow row in charges.Rows)
        {
            if (row.IsNewRow)
                continue;
            if (GetInt(row.Cells["AdmissionId"].Value) == forAdmissionId)
                count++;
        }
        return count;
    }
    private void RemoveChargesFor(int forAdmissionId)
    {
        for (int i = charges.Rows.Count - 1; i >= 0; i--)
        {
            DataGridViewRow row = charges.Rows[i];
            if (row.IsNewRow)
                continue;
            if (GetInt(row.Cells["AdmissionId"].Value) != forAdmissionId)
                continue;
            charges.Rows.RemoveAt(i);
        }
    }
    private async void ToggleAllSiblings()
    {
        // One sibling change at a time, the same rule as a single tick: two overlapping rebuilds interleave their appends.
        if (siblingBusy)
        {
            SetSiblingHint("Still updating the previous change - try again in a moment.");
            return;
        }
        siblingBusy = true;
        try
        {
            bool tickAll = siblings.CheckedItems.Count < siblings.Items.Count;
            int primaryAdmission = admissionId;
            int primaryEnrollment = enrollmentId;
            suppressSiblingEvents = true;
            try
            {
                for (int i = 0; i < siblings.Items.Count; i++)
                    siblings.SetItemChecked(i, tickAll);
            }
            finally
            {
                suppressSiblingEvents = false;
            }
            // Rebuild by loading every ticked sibling at once, which is much cheaper than one refresh per item and avoids repeated grid churn.
            charges.Rows.Clear();
            await AppendChargesAsync(
                admissionId,
                enrollmentId,
                expectedPrimaryAdmissionId: primaryAdmission,
                expectedPrimaryEnrollmentId: primaryEnrollment);
            foreach (int index in siblings.CheckedIndices.Cast<int>().ToList())
            {
                if (primaryAdmission != admissionId || primaryEnrollment != enrollmentId)
                    return;
                int memberAdmissionId = SiblingAdmissionIdAt(index);
                if (memberAdmissionId > 0 &&
                    payingFor.TryGetValue(memberAdmissionId, out var info) &&
                    info.EnrollmentId > 0)
                {
                    await AppendChargesAsync(
                        memberAdmissionId,
                        info.EnrollmentId,
                        expectedPrimaryAdmissionId: primaryAdmission,
                        expectedPrimaryEnrollmentId: primaryEnrollment);
                }
            }
            if (primaryAdmission != admissionId || primaryEnrollment != enrollmentId)
                return;
            /* Recompute allocation while preserving the amount entered. */
            RefreshEnteredPaymentAllocation();
            UpdateCollectionTotals();
            SetStatus(tickAll
                ? $"All {siblings.CheckedItems.Count} sibling(s) included in this payment."
                : "Siblings cleared from this payment.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            siblingBusy = false;
        }
    }
    private async Task LoadRecentPayments() => await LoadRecentPaymentsAsync(++loadVersion);
    private async Task LoadRecentPaymentsAsync(int version)
    {
        recentPayments.Rows.Clear();
        if (admissionId <= 0 || enrollmentId <= 0)
            return;
        try
        {
            var raw = await DatabaseHelper.QueryUserAsync(
                "GetFeeStatement",
                DatabaseHelper.Int("@AdmissionId", admissionId),
                DatabaseHelper.P("@FromDate", null),
                DatabaseHelper.P("@ToDate", null));
            if (IsDisposed || version != loadVersion)
                return;
            int userId = CurrentUser.UserId;
            int selectedEnrollment = enrollmentId;
            var summaries = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT P.FeePaymentId,P.TotalAmount,P.Status,
                    ISNULL(A.Refunded,0) Refunded,ISNULL(A.Corrected,0) Corrected
                FROM dbo.FeePayments P
                OUTER APPLY (SELECT
                    -SUM(CASE WHEN X.PaymentMethod=N'Refund' THEN X.TotalAmount ELSE 0 END) Refunded,
                    -SUM(CASE WHEN X.PaymentMethod=N'Correction' THEN X.TotalAmount ELSE 0 END) Corrected
                    FROM dbo.FeePayments X WHERE X.RelatedFeePaymentId=P.FeePaymentId
                    AND X.Status=N'Completed' AND X.TotalAmount<0) A
                WHERE P.EnrollmentId=@EnrollmentId AND P.RelatedFeePaymentId IS NULL;
                """,DatabaseHelper.Int("@UserId",userId),DatabaseHelper.Int("@EnrollmentId",selectedEnrollment)));
            if (IsDisposed || version != loadVersion) return;
            var byPayment = summaries.AsEnumerable().ToDictionary(row => GetInt(row,"FeePaymentId"));
            var rows = raw.AsEnumerable()
                .Where(row => GetInt(row, "EnrollmentId") == enrollmentId)
                .Where(row => !string.Equals(GetText(row, "TransactionType"), "REVERSAL", StringComparison.OrdinalIgnoreCase))
                .Where(row => GetInt(row, "FeePaymentId") > 0)
                .Where(row => byPayment.ContainsKey(GetInt(row,"FeePaymentId")))
                .GroupBy(row => GetInt(row,"FeePaymentId"))
                .Select(group => group.First())
                .OrderByDescending(row => row.Table.Columns.Contains("TransactionDate") && row["TransactionDate"] != DBNull.Value && DateTime.TryParse(Convert.ToString(row["TransactionDate"]), out DateTime date) ? date : DateTime.MinValue)
                .ThenByDescending(row => GetInt(row, "FeePaymentId"))
                .ToList();
            foreach (DataRow row in rows)
            {
                var summary = byPayment[GetInt(row,"FeePaymentId")];
                decimal original = GetDecimal(summary,"TotalAmount");
                decimal refunded = GetDecimal(summary,"Refunded");
                decimal corrected = GetDecimal(summary,"Corrected");
                bool reversed = GetText(summary,"Status") == "Reversed";
                decimal net = reversed ? 0m : Math.Max(0m,original-refunded-corrected);
                string transactionType = GetText(row, "TransactionType");
                string displayTransaction = transactionType.ToUpperInvariant() switch {
                    "PAYMENT" => "Collected",
                    "REFUND" => "Refunded",
                    "CORRECTION" => "Corrected",
                    "PAYMENT-REVERSED" => "Reversed",
                    _ => transactionType
                };
                displayTransaction = reversed ? "Reversed" : refunded > 0m && corrected > 0m ? "Refunded / Corrected"
                    : refunded > 0m ? "Refunded" : corrected > 0m ? "Corrected" : "Collected";
                int index = recentPayments.Rows.Add(
                    FormatDate(row, "TransactionDate"),
                    GetText(row, "ReceiptNumber"),
                    GetText(row, "ChargeType"),
                    displayTransaction,
                    original,
                    GetText(row, "PaymentMethod"),
                    GetInt(row, "FeePaymentId"));
                recentPayments.Rows[index].Cells["Adjusted"].Value = original-net;
                recentPayments.Rows[index].Cells["NetPaid"].Value = net;
                recentPayments.Rows[index].Cells["Adjusted"].ToolTipText = reversed
                    ? $"Reversed: Rs. {original:0.##}"
                    : $"Refunds: Rs. {refunded:0.##}; Corrections: Rs. {corrected:0.##}";
                recentPayments.Rows[index].Tag = !reversed && IsPrintablePayment(row);
            }
            UpdateReprintAvailability();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == loadVersion)
                Fail(ex);
        }
    }
    private static bool IsPrintablePayment(DataRow row) =>
        string.Equals(GetText(row,"TransactionType"),"PAYMENT",StringComparison.OrdinalIgnoreCase)
        && !string.Equals(GetText(row,"PaymentMethod"),"Correction",StringComparison.OrdinalIgnoreCase)
        && !string.Equals(GetText(row,"PaymentMethod"),"Refund",StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(GetText(row,"ReceiptNumber"));

    private Dictionary<long, decimal>
        GetDiscountAllocations()
    {
        var result =
            new Dictionary<long, decimal>();
        decimal discount =
            GetAmount(
                discountAmount);
        if (discount <= 0m)
            return result;
        var rows =
            SettlementRows()
                .Select(
                    r => new {
                        Row = r,
                        Id =
                            GetLong(
                                r.Cells[
                                    "BillingChargeId"]
                                    .Value),
                        Due =
                            GetDecimal(
                                r.Cells[
                                    "Balance"]
                                    .Value)
                    })
                .Where(
                    x =>
                        x.Id > 0 &&
                        x.Due > 0m)
                /* Discounts and payments use the same priority, including sibling charges. */
                .ToList();
        foreach (var item in rows)
        {
            if (discount <= 0m)
                break;
            decimal take =
                Math.Min(
                    item.Due,
                    discount);
            result[item.Id] =
                take;
            discount -= take;
        }
        return result;
    }
    private decimal GetRowNetDue(
        DataGridViewRow row,
        Dictionary<long, decimal> discounts)
    {
        long id =
            GetLong(
                row.Cells[
                    "BillingChargeId"]
                    .Value);
        decimal due =
            GetDecimal(
                row.Cells[
                    "Balance"]
                    .Value);
        if (id > 0 &&
            discounts.TryGetValue(
                id,
                out decimal discount))
        {
            due -= discount;
        }
        return Math.Max(
            0m,
            due);
    }
    /// <summary>. The per-child columns: the child, what they owe after their share of the discount, and the amount the allocation proposes for them.</summary>
    private void ConfigureProposalGrid()
    {
        familyProposal.Columns.Clear();
        familyProposal.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "AdmissionId",
                Visible = false
            });
        familyProposal.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Child",
                HeaderText = "Student",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });
        familyProposal.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Outstanding",
                HeaderText = "Owed",
                ReadOnly = true,
                Width = AppScale.Scale(90),
                DefaultCellStyle = new DataGridViewCellStyle {
                    Format = "N0",
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            });
        /* The only editable column on this grid: the per-child override. */
        familyProposal.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Proposed",
                HeaderText = "Proposed",
                ReadOnly = false,
                Width = AppScale.Scale(90),
                DefaultCellStyle = new DataGridViewCellStyle {
                    Format = "N0",
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            });
        familyProposal.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        familyProposal.Columns["Child"].FillWeight=140;
        familyProposal.Columns["Outstanding"].FillWeight=90;
        familyProposal.Columns["Proposed"].FillWeight=90;
        familyProposal.Columns["Proposed"].HeaderText="Pay";
        familyProposal.Columns["Child"].DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        familyProposal.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        familyProposal.CellEndEdit += (_, e) => {
            if (suppressProposalEvents)
                return;
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0)
            {
                return;
            }
            if (familyProposal.Columns[e.ColumnIndex].Name != "Proposed")
                return;
            DataGridViewRow row =
                familyProposal.Rows[e.RowIndex];
            int child =
                GetInt(
                    row.Cells["AdmissionId"].Value);
            decimal amount =
                Math.Max(
                    0m,
                    GetDecimal(
                        row.Cells["Proposed"].Value));
            ApplyProposedAmountForChild(
                child,
                amount);
        };
        ConfigureWholeAmountColumn(familyProposal, "Proposed");
    }
    /// <summary>Re-spreads one child's amount across their charges using the shared fee priority.</summary>
    private void ApplyProposedAmountForChild(
        int forAdmissionId,
        decimal amount)
    {
        var discountMap =
            GetDiscountAllocations();
        decimal remaining =
            Math.Max(
                0m,
                amount);
        suppressPaymentEvents = true;
        try
        {
            foreach (DataGridViewRow row in SettlementRows())
            {
                if (row.IsNewRow)
                    continue;
                if (GetInt(row.Cells["AdmissionId"].Value) != forAdmissionId)
                    continue;
                decimal payable =
                    GetRowNetDue(
                        row,
                        discountMap);
                decimal give =
                    Math.Min(
                        remaining,
                        payable);
                row.Cells["Amount"].Value =
                    give;
                row.Tag =
                    give > 0m;
                row.Cells["Pay"].Value =
                    give > 0m
                        ? "−"
                        : "+";
                remaining -=
                    give;
            }
        }
        finally
        {
            suppressPaymentEvents = false;
        }
        UpdateCollectionTotals();
        SyncPaymentFieldToGrid();
    }
    /// <summary>The per-child view of the allocation: each child with open charges, what they owe after their share of the discount, and what this payment currently gives them.</summary>
    private void ProposeFamilyAllocation()
    {
        if (proposalRow is null)
            return;
        suppressProposalEvents = true;
        try
        {
            var children = new List<int>();
            var outstanding = new Dictionary<int, decimal>();
            var allocated = new Dictionary<int, decimal>();
            var discountMap =
                GetDiscountAllocations();
            foreach (DataGridViewRow row in charges.Rows)
            {
                if (row.IsNewRow)
                    continue;
                int child =
                    GetInt(
                        row.Cells["AdmissionId"].Value);
                if (child <= 0)
                    continue;
                if (!outstanding.ContainsKey(child))
                {
                    children.Add(child);
                    outstanding[child] = 0m;
                    allocated[child] = 0m;
                }
                outstanding[child] +=
                    GetRowNetDue(
                        row,
                        discountMap);
                if (row.Tag is bool selected && selected)
                {
                    allocated[child] +=
                        GetDecimal(
                            row.Cells["Amount"].Value);
                }
            }
            /* One child is the ordinary case, and the Payment field already says everything about it. */
            bool isFamily =
                children.Count > 1;
            proposalRow.SizeType=SizeType.Percent;
            proposalRow.Height=isFamily ? 45 : 0;
            familyProposal.Visible =
                isFamily;
            if (!isFamily)
            {
                familyProposal.Rows.Clear();
                return;
            }
            /* Update in place when the same children are already listed rather than clearing and re-adding. */
            bool sameShape =
                familyProposal.Rows.Count == children.Count;
            if (sameShape)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (GetInt(familyProposal.Rows[i].Cells["AdmissionId"].Value) != children[i])
                    {
                        sameShape = false;
                        break;
                    }
                }
            }
            if (!sameShape)
                familyProposal.Rows.Clear();
            for (int i = 0; i < children.Count; i++)
            {
                int child =
                    children[i];
                decimal due =
                    outstanding[child];
                decimal proposed =
                    allocated[child];
                string who = $"{StudentNameFor(child)}\r\n#{AdmissionNumbers.ForId(child)}";
                if (sameShape)
                {
                    DataGridViewRow row =
                        familyProposal.Rows[i];
                    row.Cells["Child"].Value = who;
                    row.Cells["Outstanding"].Value = due;
                    row.Cells["Proposed"].Value = proposed;
                }
                else
                {
                    familyProposal.Rows.Add(
                        child,
                        who,
                        due,
                        proposed);
                }
            }
        }
        finally
        {
            suppressProposalEvents = false;
        }
    }
    /// <summary>Process Collection is the button that takes money, so it is enabled only while the charges grid holds a charge that still has a balance.</summary>
    private void UpdateProcessCollectionAvailability()
    {
        if (fullAmountButton is not null)
            fullAmountButton.Enabled = !transactionBusy && HasOutstandingCharges();
        if (executeButton is null)
        {
            return;
        }
        executeButton.Enabled =
            !transactionBusy &&
            HasOutstandingCharges();
    }
    /// <summary>True when the grid holds a real charge with a balance left to collect.</summary>
    private bool HasOutstandingCharges()
    {
        foreach (DataGridViewRow row in charges.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }
            if (GetLong(
                    row.Cells["BillingChargeId"]
                        .Value) > 0 &&
                (GetDecimal(
                    row.Cells["Balance"]
                        .Value) > 0m ||
                 (row.Cells["GrossAmount"].Tag is decimal originalDiscount &&
                  GetDecimal(row.Cells["AppliedDiscount"].Value)>originalDiscount)))
            {
                return true;
            }
        }
        return false;
    }
    private void UpdateCollectionTotals()
    {
        decimal due = 0m;
        decimal payment = 0m;
        /* The discount is read and spread inside CurrentPayableAfterDiscount, so there is no second copy of the figure here to drift from it. */
        var discountMap =
            GetDiscountAllocations();
        foreach (DataGridViewRow row in charges.Rows)
        {
            if (row.IsNewRow)
                continue;
            decimal rowDue =
                GetDecimal(
                    row.Cells["Balance"].Value);
            due +=
                rowDue;
            bool selected =
                row.Tag is bool value && value;
            if (!selected)
                continue;
            decimal amount =
                GetDecimal(
                    row.Cells["Amount"].Value);
            decimal max =
                GetRowNetDue(
                    row,
                    discountMap);
            payment +=
                Math.Min(
                    amount,
                    max);
        }
        /* ONE definition of the payable figure, shared with the default allocation: the charges' dues less the discount as it is actually spread across them. */
        decimal payableAfterDiscount =
            CurrentPayableAfterDiscount();
        decimal remaining =
            Math.Max(
                0m,
                payableAfterDiscount -
                payment);
        totalDue.Text =
            due.ToString("N0");
        amountToPay.Text =
            payableAfterDiscount.ToString("N0");
        balance.Text =
            remaining.ToString("N0");
        balance.ForeColor = remaining > 0 ? AppTheme.Danger : AppTheme.Text;
        /* Remembered so the next change can tell the untouched default from a figure the operator entered - see lastPayableShown. */
        lastPayableShown =
            payableAfterDiscount;
        /* if more was handed over than could be applied, say so rather than quietly collecting less than the operator typed. */
        decimal typed = GetAmount(paymentAmount);
        if (typed > payment + 0.005m)
        {
            decimal payable =
                remaining +
                payment;
            SetStatus(
                $"Only Rs. {payment:N0} of Rs. {typed:N0} can be applied to the "
                + $"Rs. {payable:N0} owed after the discount. "
                + $"Rs. {typed - payment:N0} would be unapplied.",
                true);
        }
        /* The per-child view is refreshed here, so it is always in step with the grid no matter which path changed the allocation. */
        ProposeFamilyAllocation();
        /* The grid is the only thing that knows whether there is anything to collect, and this method runs on every path that can change it. */
        UpdateProcessCollectionAvailability();
    }
    /* This is a business operation (it posts a payment/refund/reversal), not an event boundary, so it returns Task and can be awaited, sequenced and tested. */
    private async Task ProcessTransaction()
    {
        if (transactionBusy)
            return;
        transactionBusy = true;
        if (fullAmountButton is not null) fullAmountButton.Enabled = false;
        if (executeButton is not null)
            executeButton.Enabled = false;
        /* The receipt snapshot at the end of this method is built AFTER the await, from the Student box, admissionId, Payment Method and Payment Date. */
        string receiptStudentName = student.Text.Trim();
        int receiptAdmissionId = admissionId;
        int receiptEnrollmentId = enrollmentId;
        string receiptPaymentMethod = method.Text;
        DateTime receiptTransactionDate = paymentDate.Value;
        /* The student, enrollment and sibling pickers stay live during the write, so choosing another student while it ran let the post-commit block… */
        bool studentWasEnabled = student.Enabled;
        bool enrollmentWasEnabled = enrollment.Enabled;
        bool siblingsWasEnabled = siblings.Enabled;
        student.Enabled = false;
        enrollment.Enabled = false;
        siblings.Enabled = false;
        if (printReceiptButton is not null) printReceiptButton.Enabled = false;
        if (nextStudentButton is not null) nextStudentButton.Enabled = false;
        try
        {
            if (admissionId <= 0 ||
                enrollmentId <= 0)
            {
                Warn(
                    UiText.Prompts.SelectStudentAndEnrollment);
                return;
            }
            if (!charges.EndEdit() || !familyProposal.EndEdit()) return;
            foreach (DataGridViewRow row in charges.Rows)
            {
                if (row.Cells["GrossAmount"].Tag is decimal original &&
                    original > GetDecimal(row.Cells["AppliedDiscount"].Value) &&
                    (row.Tag is not true || GetDecimal(row.Cells["Amount"].Value)<=0m))
                {
                    row.ErrorText="Include this bill in the payment to save its reduced discount.";
                    return;
                }
            }
            if (!TryParseWholeCollectionAmount(paymentAmount.Text, out _))
            {
                MarkCollectionField(paymentAmount, "Enter a whole amount using digits only; decimal points and fractions are not allowed.");
                return;
            }
            foreach (var input in new[] { discountAmount })
            {
                string raw = input.Text.Trim();
                if (raw.Length > 0 &&
                    (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal entered) || entered < 0m))
                {
                    MarkCollectionField(input, "Enter a valid amount of zero or more");
                    return;
                }
            }
            /* This page collects. */
            const string transactionType = "Collection";
            if (paymentDate.Value.Date > DateTime.Today)
            {
                /* UX - marked on the date field itself; the summary line carries the same words. */
                MarkCollectionField(paymentDate, "Not in the future");
                return;
            }
            /* Allocations grouped by STUDENT, not one flat list. dbo.ProcessFeeTransaction is scoped to a single (AdmissionId, EnrollmentId) and writes one FeePayments row per call. */
            var allocationsByStudent =
                new Dictionary<int, (int EnrollmentId, List<(long chargeId, decimal amount)> Rows)>();
            var receiptLines =
                new List<(
                    string feeType,
                    string billingMonth,
                    decimal nominalAmount,
                    decimal actualAmount)>();
            decimal amount;
            decimal discount = 0m;
            // Populated inside the Collection branch below; used afterwards to work out the balance remaining for the receipt.
            decimal totalDueBeforeThisTransaction = 0m;
            if (transactionType ==
                "Collection")
            {
                var discountMap =
                    GetDiscountAllocations();
                discount =
                    GetAmount(
                        discountAmount);
                if (discount < 0m)
                {
                    MarkCollectionField(discountAmount, "Cannot be negative");
                    return;
                }
                decimal totalDueValue =
                    charges.Rows
                        .Cast<DataGridViewRow>()
                        .Where(
                            r => !r.IsNewRow)
                        .Sum(
                            r =>
                                GetDecimal(
                                    r.Cells["Balance"]
                                        .Value));
                totalDueBeforeThisTransaction =
                    totalDueValue;
                if (discount >
                    totalDueValue)
                {
                    MarkCollectionField(discountAmount, "More than is outstanding");
                    return;
                }
                foreach (
                    DataGridViewRow row
                    in charges.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    long chargeId =
                        GetLong(
                            row.Cells[
                                "BillingChargeId"]
                                .Value);
                    if (chargeId <= 0)
                        continue;
                    decimal pay =
                        GetDecimal(
                            row.Cells["Amount"]
                                .Value);
                    decimal rowDiscount =
                        discountMap.TryGetValue(
                            chargeId,
                            out decimal d)
                                ? d
                                : 0m;
                    decimal due =
                        GetDecimal(
                            row.Cells["Balance"]
                                .Value);
                    decimal netDue =
                        Math.Max(
                            0m,
                            due -
                            rowDiscount);
                    if (pay < 0m ||
                        pay > netDue)
                    {
                        Warn(
                            "Pay Now cannot exceed the amount remaining after discount.");
                        return;
                    }
                    if (row.Cells["GrossAmount"].Tag is decimal originalDiscount)
                    {
                        decimal increase = Math.Max(0m,GetDecimal(row.Cells["AppliedDiscount"].Value)-originalDiscount);
                        rowDiscount += increase;
                        discount += increase;
                        totalDueBeforeThisTransaction += increase;
                    }
                    if (row.Tag is bool selected &&
        selected &&
        pay > 0m)
                    {
                        // Which child this charge belongs to. Falls back to the primary student for any row that predates the column.
                        int rowAdmissionId =
                            GetInt(
                                row.Cells["AdmissionId"]
                                    .Value);
                        int rowEnrollmentId =
                            GetInt(
                                row.Cells["EnrollmentId"]
                                    .Value);
                        if (rowAdmissionId <= 0)
                            rowAdmissionId = admissionId;
                        if (rowEnrollmentId <= 0)
                            rowEnrollmentId = enrollmentId;
                        if (!allocationsByStudent.TryGetValue(
                                rowAdmissionId,
                                out var group))
                        {
                            group = (rowEnrollmentId, new List<(long chargeId, decimal amount)>());
                            allocationsByStudent[rowAdmissionId] = group;
                        }
                        group.Rows.Add(
                            (
                                chargeId,
                                pay
                            ));
                        decimal chargeGross =
                            GetDecimal(
                                row.Cells["GrossAmount"]
                                    .Value);
                        decimal chargeNet =
                            GetDecimal(
                                row.Cells["NetAmount"]
                                    .Value);
                        decimal nominalPay =
                            chargeNet > 0m
                                ? pay * (chargeGross / chargeNet)
                                : pay;
                        receiptLines.Add(
                            (
                                GetText(
                                    row.Cells["FeeTypeName"]
                                        .Value),
                                GetText(
                                    row.Cells["BillingMonth"]
                                        .Value),
                                nominalPay,
                                pay
                            ));
                    }
                    if (rowDiscount > 0m)
                    {
                        /* A discount is not money received; it raises the charge's DiscountAmount. It must be attributed to the same student that owns the charge, not lumped onto the primary one. */
                        int discountAdmissionId =
                            GetInt(
                                row.Cells["AdmissionId"]
                                    .Value);
                        int discountEnrollmentId =
                            GetInt(
                                row.Cells["EnrollmentId"]
                                    .Value);
                        if (discountAdmissionId <= 0)
                            discountAdmissionId = admissionId;
                        if (discountEnrollmentId <= 0)
                            discountEnrollmentId = enrollmentId;
                        if (!allocationsByStudent.TryGetValue(
                                discountAdmissionId,
                                out var discountGroup))
                        {
                            discountGroup = (discountEnrollmentId, new List<(long chargeId, decimal amount)>());
                            allocationsByStudent[discountAdmissionId] = discountGroup;
                        }
                        discountGroup.Rows.Add(
                            (
                                chargeId,
                                -rowDiscount
                            ));
                    }
                }
                amount =
                    allocationsByStudent
                        .SelectMany(
                            g => g.Value.Rows)
                        .Where(
                            x => x.amount > 0m)
                        .Sum(
                            x => x.amount);
                if (Math.Abs(GetAmount(paymentAmount) - amount) > 0.005m)
                {
                    MarkCollectionField(paymentAmount, "Received amount must match the allocated payment.");
                    return;
                }
                if (amount <= 0m &&
                    discount <= 0m)
                {
                    Warn(
                        "Enter a payment or a discount first.");
                    return;
                }
            }
            /* Those three now run on Finance > Fee Adjustments, so this branch is gone rather than left dormant. */
            /* Money-in only. */
            string description =
                discount > 0m
                    ? $"Collect Rs. {amount:0.##} and apply a discount of Rs. {discount:0.##}."
                    : $"Collect Rs. {amount:0.##}.";
            /* The payment date is stamped on the receipt and the ledger. */
            description +=
                $"\r\nPayment date: {paymentDate.Value:dd-MMM-yyyy hh:mm tt}   Method: {method.Text}.";
            foreach (DataGridViewRow row in charges.Rows)
            {
                if (row.Cells["GrossAmount"].Tag is decimal oldDiscount &&
                    oldDiscount != GetDecimal(row.Cells["AppliedDiscount"].Value))
                    description += $"\r\n{GetText(row.Cells["FeeTypeName"].Value)} ({GetText(row.Cells["BillingMonth"].Value)}): " +
                        $"bill discount Rs. {oldDiscount:0.##} to Rs. {GetDecimal(row.Cells["AppliedDiscount"].Value):0.##}.";
            }
            /* When siblings are included, say so up front, and say it in the terms the system now uses: ONE family collection under ONE receipt number. */
            /* Every student with an allocation row goes to the database, DISCOUNTS INCLUDED. */
            var allocationLines = allocationsByStudent
                .Where(g => g.Value.Rows.Count > 0)
                .OrderBy(g => g.Key)
                .ToList();
            /* The student behind one group of allocation rows, named the way the operator knows them. */
            string WhoIs(
                int groupAdmissionId) =>
                groupAdmissionId == admissionId
                    ? $"{student.Text.Trim()} (this student)"
                    : payingFor.TryGetValue(groupAdmissionId, out var whoInfo)
                        ? whoInfo.Name
                        : $"Admission {AdmissionNumbers.ForId(groupAdmissionId)}";
            /* A student whose charges are covered by a discount alone is refused by the database. */
            var discountOnlyStudents = allocationLines
                .Where(g => !g.Value.Rows.Any(r => r.amount > 0m))
                .Select(g => WhoIs(g.Key))
                .ToList();
            if (discountOnlyStudents.Count > 0)
            {
                Alert(
                    $"{string.Join(", ", discountOnlyStudents)}: the charges ticked for " +
                    (discountOnlyStudents.Count == 1 ? "this student" : "these students") +
                    " would be covered by the discount only. A receipt needs a payment amount for " +
                    "every student on it, so this collection cannot be posted as it stands.\r\n\r\n" +
                    "Either enter a payment for that student - even part of the amount - or take the " +
                    "discount off and untick that student, and collect from the others.\r\n" +
                    "To waive a charge completely instead, cancel the charge itself: right-click its " +
                    "row and choose Reverse Charge.");
                return;
            }
            if (allocationLines.Count > 1)
            {
                description +=
                    $"\r\n\r\nThis is ONE FAMILY COLLECTION: {allocationLines.Count} students " +
                    "collected from in a single transaction, under ONE family receipt number " +
                    "that covers every child. Each child's fees stay attributed to that child.";
                foreach (var group in allocationLines)
                {
                    decimal studentTotal = group.Value.Rows
                        .Where(r => r.amount > 0m)
                        .Sum(r => r.amount);
                    decimal studentDiscount = -group.Value.Rows
                        .Where(r => r.amount < 0m)
                        .Sum(r => r.amount);
                    string who = WhoIs(group.Key);
                    /* What is left is each child's own payment, with the discount that goes with it so the figures on the receipt are recognisable. */
                    description += studentDiscount > 0m
                        ? $"\r\n    - {who}: Rs. {studentTotal:0.##} (discount Rs. {studentDiscount:0.##})"
                        : $"\r\n    - {who}: Rs. {studentTotal:0.##}";
                }
            }
            if (allocationsByStudent.Values.Any(group =>
                group.Rows.Where(row => row.amount > 0m).Sum(row => row.amount) is decimal total &&
                total != decimal.Truncate(total)))
            {
                MarkCollectionField(paymentAmount, "Each student's collected amount must be a whole number. Adjust the family payment allocation.");
                return;
            }
            string confirmMessage =
                description +
                "\r\n\r\n" +
                "This operation will be completed immediately and is final.";
            if (ModernMessageDialog.Show(
                    FindForm(),
                    confirmMessage,
                    allocationLines.Count > 1 ? "Confirm Family Collection" : "Confirm Fee Transaction",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    // BUSINESS RULE: Yes is intentionally the default for fast counter entry after
                    // the operator has reviewed the student, amount and allocation above.
                    MessageBoxDefaultButton.Button1)
                != DialogResult.Yes)
            {
                return;
            }
            /* A failure on the third child left the first two children's money committed while the operator was told the collection had failed. */
            DataTable result;
            var billDiscountEdits = charges.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow && r.Cells["GrossAmount"].Tag is decimal original &&
                    original != GetDecimal(r.Cells["AppliedDiscount"].Value))
                .Select(r => new CollectionDiscountWriter.Edit(GetLong(r.Cells["BillingChargeId"].Value),
                    GetInt(r.Cells["AdmissionId"].Value),GetInt(r.Cells["EnrollmentId"].Value),
                    (decimal)r.Cells["GrossAmount"].Tag!,GetDecimal(r.Cells["AppliedDiscount"].Value),
                    GetDecimal(r.Cells["Balance"].Tag))).ToArray();
            if (allocationLines.Count == 1)
            {
                var single = allocationLines[0];
                decimal singleAmount = single.Value.Rows
                    .Where(r => r.amount > 0m)
                    .Sum(r => r.amount);
                result =
                    await CollectionDiscountWriter.CollectAsync(
                        "ProcessFeeTransaction",
                        billDiscountEdits,
                        DatabaseHelper.Int("@AdmissionId", single.Key),
                        DatabaseHelper.Int(
                            "@EnrollmentId",
                            single.Value.EnrollmentId > 0
                                ? single.Value.EnrollmentId
                                : enrollmentId),
                        DatabaseHelper.P("@TransactionType", transactionType),
                        DatabaseHelper.P("@Amount", singleAmount),
                        DatabaseHelper.Date("@TransactionDate", paymentDate.Value),
                        DatabaseHelper.P("@PaymentMethod", method.Text),
                        DatabaseHelper.P("@ReferenceNumber", reference.Text.Trim()),
                        DatabaseHelper.P("@Remarks", remarks.Text.Trim()),
                        DatabaseHelper.NullableInt("@FeePaymentId", null),
                        new SqlParameter("@BillingChargeId", SqlDbType.BigInt) {
                            Value = DBNull.Value
                        },
                        new SqlParameter("@Allocations", SqlDbType.Structured) {
                            TypeName = "dbo.FeePaymentAllocationType",
                            Value = Tvp.FeeAllocations(single.Value.Rows)
                        });
            }
            else
            {
                result =
                    await CollectionDiscountWriter.CollectAsync(
                        "ProcessFamilyFeeTransaction",
                        billDiscountEdits,
                        DatabaseHelper.P(
                            "@TransactionType",
                            transactionType),
                        DatabaseHelper.Date(
                            "@TransactionDate",
                            paymentDate.Value),
                        DatabaseHelper.P(
                            "@PaymentMethod",
                            method.Text),
                        DatabaseHelper.P(
                            "@ReferenceNumber",
                            reference.Text.Trim()),
                        DatabaseHelper.P(
                            "@Remarks",
                            remarks.Text.Trim()),
                        new SqlParameter(
                            "@Allocations",
                            SqlDbType.Structured) {
                            TypeName =
                                "dbo.FamilyFeeAllocationType",
                            /* The enrollment fallback is kept from the loop this replaces: a row that predates the EnrollmentId column belongs to the enrollment currently selected. */
                            Value =
                                Tvp.FamilyAllocations(
                                    allocationLines.Select(
                                        g => (
                                            g.Key,
                                            g.Value.EnrollmentId > 0
                                                ? g.Value.EnrollmentId
                                                : enrollmentId,
                                            (IEnumerable<(long chargeId, decimal amount)>)
                                                g.Value.Rows)))
                        });
            }
            var receipts = new List<string>();
            foreach (DataRow row in result.Rows)
            {
                string thisReceipt =
                    GetText(
                        row,
                        "ReceiptNumber");
                if (!string.IsNullOrWhiteSpace(thisReceipt))
                    receipts.Add(thisReceipt);
            }
            /* One row per child, guaranteed by the procedure before it commits, so this is the number of children whose fees were collected. */
            int posted = result.Rows.Count;
            /* made a family collection carry ONE receipt number shared by every child, and the procedure returns that same number on every row. The distinct list is the real answer. */
            List<string> distinctReceipts = receipts
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string singleReceipt = distinctReceipts.Count == 1
                ? distinctReceipts[0]
                : string.Empty;
            bool isFamilyCollection = posted > 1;
            bool receiptNumberMissing =
                isFamilyCollection && singleReceipt.Length == 0;
            if (receiptNumberMissing)
            {
                /* The money is taken and the rows above prove it, so this is not a failure of the collection. */
                Warn(
                    $"The collection completed for {posted} students, but " +
                    $"{distinctReceipts.Count} distinct receipt numbers came back, so " +
                    "a single family receipt number could not be confirmed. " +
                    "Check Recent Payments for receipt details.");
            }
            else
            {
                Info(
                    receipts.Count == 0
                        ? $"{transactionType} completed successfully."
                        : isFamilyCollection
                            ? $"{transactionType} completed for {posted} students under one " +
                              $"family receipt: {singleReceipt}."
                            : $"{transactionType} completed successfully. Receipt: {singleReceipt}.");
            }
            // A confirmed new payment replaces the previous payment's print context.
            lastCollectionReceipt = null;
            lastGroupReceiptNumber = null;
            multiStudentReceiptNote = null;
            /* Receipt handling depends on whether this payment covered one student or several. */
            if (transactionType == "Collection" &&
                !string.IsNullOrWhiteSpace(singleReceipt) &&
                receiptLines.Count > 0)
            {
                if (posted > 1)
                {
                    lastCollectionReceipt = null;
                    lastGroupReceiptNumber = singleReceipt;
                    if (printReceiptButton is not null)
                        printReceiptButton.Enabled = true;
                    /* Held and re-asserted after the reloads below, because those reload the charges and set their own status text - without this the operator would never see which receipt was issued. */
                    multiStudentReceiptNote =
                        $"Collected Rs. {amount:0.##} across {posted} students under family " +
                        $"receipt {singleReceipt}. One receipt covers every child; " +
                        "Print Receipt prints it.";
                }
                else
                {
                    lastGroupReceiptNumber = null;
                    var (placementClassName, placementSectionName, placementCampusName) =
                        GetSelectedEnrollmentPlacement();
                    decimal balanceRemaining =
                        Math.Max(
                            0m,
                            totalDueBeforeThisTransaction -
                            discount -
                            amount);
                    lastCollectionReceipt = new LastCollectionReceipt {
                        StudentName = receiptStudentName,
                        AdmissionId = receiptAdmissionId,
                        EnrollmentId = receiptEnrollmentId,
                        ReceiptNumber = singleReceipt,
                        PaymentMethod = receiptPaymentMethod,
                        TransactionDate = receiptTransactionDate,
                        ClassName = placementClassName,
                        SectionName = placementSectionName,
                        CampusName = placementCampusName,
                        DiscountApplied = discount,
                        BalanceRemaining = balanceRemaining,
                        Lines = new List<(string FeeType, string BillingMonth, decimal NominalAmount, decimal ActualAmount)>(
                            receiptLines.Select(l => (l.feeType, l.billingMonth, l.nominalAmount, l.actualAmount)))
                    };
                    if (printReceiptButton is not null)
                        printReceiptButton.Enabled = true;
                    /* The automatic report opening was removed on the operator's instruction, so this flow only prepares the reprint. */
                }
            }
            collectionResult.Text = string.IsNullOrWhiteSpace(singleReceipt)
                ? "Payment recorded. Check Recent Payments for receipt details."
                : $"Payment recorded. Receipt: {singleReceipt}. " +
                  (lastCollectionReceipt is not null || lastGroupReceiptNumber is not null
                    ? "Print Receipt, or choose Next Student." : "Check Recent Payments to print, or choose Next Student.");
            collectionResult.Visible = true;
            int refreshVersion = ++loadVersion;
            // These refreshes read independent views of the same committed transaction.
            await Task.WhenAll(
                LoadChargesAsync(refreshVersion),
                LoadRecentPaymentsAsync(refreshVersion),
                LoadSiblingsAsync(refreshVersion));
            charges.ClearSelection();
            recentPayments.ClearSelection();
            suppressDiscountEvents = true;
            try
            {
                discountAmount.Text =
                    "0";
            }
            finally
            {
                suppressDiscountEvents = false;
            }
            suppressPaymentEvents = true;
            try
            {
                /* Leaving the Paid figure standing against a grid that now holds nothing selected read as though it were still owed. */
                paymentAmount.Text =
                    "0";
            }
            finally
            {
                suppressPaymentEvents = false;
            }
            reference.Clear();
            remarks.Clear();
            /* The next collection for this student is a new one and must not inherit the moment this page was opened. */
            ResetPaymentDateToNow(false);
            UpdateOperationMode();
            // Re-assert the multi-student receipt note: the reloads above reset the status text.
            if (!string.IsNullOrWhiteSpace(multiStudentReceiptNote))
            {
                SetStatus(multiStudentReceiptNote!, false);
                multiStudentReceiptNote = null;
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            transactionBusy = false;
            if (!IsDisposed)
            {
                student.Enabled = studentWasEnabled;
                enrollment.Enabled = enrollmentWasEnabled;
                siblings.Enabled = siblingsWasEnabled;
                if (nextStudentButton is not null) nextStudentButton.Enabled = true;
                if (printReceiptButton is not null)
                    printReceiptButton.Enabled = lastCollectionReceipt is not null || lastGroupReceiptNumber is not null;
            }
            /* After a collection that settled the student there is nothing left to collect, so the button goes quiet instead of inviting a second empty click. */
            UpdateProcessCollectionAvailability();
        }
    }
    // A contextual dialog on the currently selected enrollment - see FeeAssignmentsDialog.cs.
    private void OpenFeeAssignments()
    {
        if (admissionId <= 0 || enrollmentId <= 0)
        {
            Warn(UiText.Prompts.SelectStudentAndEnrollment);
            return;
        }
        using var dialog = new FeeAssignmentsDialog(
            enrollmentId,
            admissionId,
            student.Text.Trim());
        dialog.ShowDialog(FindForm());
        if (dialog.ChangesMade)
        {
            int refreshVersion = ++loadVersion;
            _ = LoadChargesAsync(refreshVersion);
        }
    }
    private void OpenAdditionalFee()
    {
        if (admissionId <= 0 || enrollmentId <= 0)
        {
            Warn(UiText.Prompts.SelectStudentAndEnrollment);
            return;
        }
        using var dialog = new AddAdditionalFeeDialog(
            enrollmentId, student.Text.Trim());
        dialog.ShowDialog(FindForm());
        if (dialog.Posted)
            _ = LoadChargesAsync(++loadVersion);
    }
    // (Fee Operations, scoped down). Read-only projection dialog - see FutureFeesDialog.cs. Never writes anything, so there is no "ChangesMade"/refresh step here unlike Fee Assignments.
    private void OpenFutureFees()
    {
        if (admissionId <= 0 || enrollmentId <= 0)
        {
            Warn(UiText.Prompts.SelectStudentAndEnrollment);
            return;
        }
        using var dialog = new FutureFeesDialog(
            enrollmentId,
            student.Text.Trim());
        dialog.ShowDialog(FindForm());
    }
    // Carries the selected student into fee-history views.
    /// <summary>It is the same grid with the same loader, moved into a dialog, so nothing about what it shows or how it loads has changed - only where it lives.</summary>
    private void OpenPaymentHistory()
    {
        if (admissionId <= 0 || enrollmentId <= 0)
        {
            Warn(UiText.Prompts.SelectStudentAndEnrollment);
            return;
        }
        Panel? host = paymentHistoryHost;
        if (host is not null)
        {
            host.Controls.Remove(
                recentPayments);
        }
        using var dialog =
            new ApplicationWindowForm {
                Text = "Payment History",
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(AppScale.Scale(1180), AppScale.Scale(520)),
                MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(420)),
                BackColor = AppTheme.PageBackground,
                Font = AppTheme.NormalFont,
                ShowInTaskbar = false
            };
        var card =
            HumanUi.Card(
                "Payment History",
                $"Payments for this enrollment, newest first. Use {PageTitles.StudentFeeCorrections} to correct, refund or reverse.");
        recentPayments.Dock =
            DockStyle.Fill;
        card.Content.Controls.Add(
            recentPayments);
        var closeButton =
            new ModernButton {
                Text = UiText.Actions.Close,
                Width = AppScale.Scale(105),
                Height = AppMetrics.ButtonHeight,
                ThemeRole = "Secondary",
                DialogResult = DialogResult.OK
            };
        /* Anything in this list can be printed again, not just the collection made in this session: the receipt number is the key, and it is already on the row. */
        var reprintButton =
            new ModernButton {
                Text = "Reprint Receipt",
                Width = AppScale.Scale(150),
                Height = AppMetrics.ButtonHeight,
                ThemeRole = "Secondary"
            };
        reprintReceiptButton = reprintButton;
        reprintButton.Click += async (_, _) => await ReprintSelectedReceiptAsync();
        var buttons =
            HumanUi.ButtonRow(reprintButton, closeButton);
        buttons.Dock = DockStyle.Bottom;
        buttons.Margin = new Padding(0, 10, 0, 0);
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(12)
            };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        root.Controls.Add(
            card,
            0,
            0);
        root.Controls.Add(
            buttons,
            0,
            1);
        dialog.Controls.Add(
            root);
        dialog.CancelButton =
            closeButton;
        // The hidden grid can receive its size before it has a handle, when the
        // shared resize fitter cannot run. Fit once after the dialog is laid out,
        // including when the payment history has no rows.
        dialog.Shown += (_, _) => UiHelper.FitColumnsToWidth(recentPayments);
        /* It is switched on for the dialog and back off after. */
        recentPayments.Enabled = true;
        /* The list is on screen before the button is offered, so its state is derived once here rather than waiting for the first click to set it. */
        UpdateReprintAvailability();
        try
        {
            dialog.ShowDialog(
                FindForm());
        }
        finally
        {
            /* Take the grid back out BEFORE the dialog is disposed: a Form disposes the controls it owns, and this grid belongs to the page, not to the dialog. */
            card.Content.Controls.Remove(
                recentPayments);
            if (host is not null)
            {
                host.Controls.Add(
                    recentPayments);
            }
            /* The button, unlike the grid, IS the dialog's - it is disposed with it, so the page must stop pointing at it. */
            reprintReceiptButton = null;
            recentPayments.Enabled = false;
        }
    }
    private void OpenHistory()
    {
        if (admissionId <= 0 || enrollmentId <= 0)
        {
            Warn(UiText.Prompts.SelectStudentAndEnrollment);
            return;
        }
        using var dialog = new FeeHistoryDialog(admissionId);
        dialog.ShowDialog(FindForm());
    }
    /// <summary>Reverses exactly one charge, through dbo.ReverseBillingCharge (FMSC_FIX_082), then reloads what the reversal invalidated. The procedure itself decides whether the reversal is allowed - this method never guesses.</summary>
    private async Task ReverseChargeAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= charges.Rows.Count)
            return;
        DataGridViewRow row =
            charges.Rows[rowIndex];
        long chargeId =
            GetLong(
                row.Cells["BillingChargeId"].Value);
        if (chargeId <= 0)
        {
            Warn("That row is not a posted charge, so there is nothing to reverse.");
            return;
        }
        var placement=await Task.Run(()=>DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
            SELECT E.Status FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
            WHERE B.BillingChargeId=@ChargeId;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@ChargeId",chargeId)));
        if(placement.Rows.Count==0 || Convert.ToString(placement.Rows[0]["Status"])!="Active") {
            Warn("This charge belongs to a closed enrollment. It stays in history and can still be collected, but cannot be reversed here.");
            return;
        }
        string chargeName =
            GetText(
                row.Cells["FeeTypeName"].Value);
        string billingMonth =
            GetText(
                row.Cells["BillingMonth"].Value);
        decimal due =
            GetDecimal(
                row.Cells["Balance"].Value);
        /* A sibling's row is reversed for that sibling - this action is scoped per student exactly as the collection is. */
        int rowAdmissionId =
            GetInt(
                row.Cells["AdmissionId"].Value);
        string forStudent =
            rowAdmissionId == admissionId || admissionId <= 0
                ? (student.Text ?? string.Empty).Trim()
                : payingFor.TryGetValue(rowAdmissionId, out var sibling)
                    ? sibling.Name
                    : $"Admission {AdmissionNumbers.ForId(rowAdmissionId)}";
        using var dialog =
            new ReverseChargeDialog(
                chargeId,
                chargeName,
                billingMonth,
                due,
                forStudent);
        dialog.ShowDialog(FindForm());
        if (!dialog.Reversed)
            return;
        int refreshVersion = ++loadVersion;
        await Task.WhenAll(
            LoadChargesAsync(refreshVersion),
            LoadSiblingsAsync(refreshVersion));
        SyncPaymentFieldToGrid();
        UpdateCollectionTotals();
        SetStatus(
            $"Reversed: {chargeName} ({billingMonth}). It is cancelled on the bill and kept as history.");
    }
    private void ClearPayment(bool clearStudentBox = true)
    {
        primaryPayer.Text="";
        focusAmountAfterSiblingLoad=false;
        ++loadVersion;
        suppressSiblingEvents = true;
        try {
            siblings.Items.Clear();
            payingFor.Clear();
            familyProposal.Rows.Clear();
            if (proposalRow is not null) proposalRow.Height = 0;
            selectAllSiblings.Enabled = false;
            SetSiblingHint("Select a student to see their siblings.");
        }
        finally {
            suppressSiblingEvents = false;
            updateSiblingLayout?.Invoke();
        }
        collectionValidator?.ClearAll();
        admissionId = 0;
        enrollmentId = 0;
        lastCollectionReceipt = null;
        lastGroupReceiptNumber = null;
        multiStudentReceiptNote = null;
        collectionResult.Text = ""; collectionResult.Visible = false;
        if (printReceiptButton is not null)
            printReceiptButton.Enabled = false;
        if (feeAssignmentsButton is not null)
            feeAssignmentsButton.Enabled = false;
        if (addChargeButton is not null)
            addChargeButton.Enabled = false;
        if (futureFeesButton is not null)
            futureFeesButton.Enabled = false;
        if (historyButton is not null)
            historyButton.Enabled = false;
        suppressStudentEvents = true;
        try
        {
            if (clearStudentBox)
                student.Clear();
        }
        finally
        {
            suppressStudentEvents = false;
        }
        enrollment.ClearLookup();
        charges.Rows.Clear();
        recentPayments.Rows.Clear();
        method.SelectedIndex =
            0;
        suppressDiscountEvents = true;
        try
        {
            discountAmount.Text =
                "0";
        }
        finally
        {
            suppressDiscountEvents = false;
        }
        suppressPaymentEvents = true;
        try
        {
            paymentAmount.Text =
                "0";
        }
        finally
        {
            suppressPaymentEvents = false;
        }
        reference.Clear();
        remarks.Clear();
        /* Clear starts the form afresh, so a back-dated date set for the student being cleared goes with it. */
        ResetPaymentDateToNow(true);
        UpdateOperationMode();
        UpdateCollectionTotals();
        SetStatus(
            "Ready. Select a student to load the current fees.");
    }
    /// <summary>The payment date is stamped on the receipt and the ledger, and it was set once, when the page opened.</summary>
    private void ResetPaymentDateToNow(
        bool forNewStudent)
    {
        if (forNewStudent)
            paymentDateEdited = false;
        else if (paymentDateEdited)
            return;
        suppressPaymentDateEvents = true;
        try
        {
            paymentDate.Value =
                DateTime.Now;
        }
        finally
        {
            suppressPaymentDateEvents = false;
        }
    }
    private static decimal GetAmount(
        Control control)
    {
        string text =
            control.Text?.Trim()
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return 0m;
        }
        return decimal.TryParse(
            text.Replace(
                ",",
                string.Empty),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out decimal value)
                ? value
                : 0m;
    }
    private static DataTable FilterAdmissionId(
        DataTable source,
        int admissionId)
    {
        if (!source.Columns.Contains(
                "AdmissionId"))
        {
            return source.Clone();
        }
        var result =
            source.Clone();
        foreach (DataRow row in source.Rows)
        {
            if (row["AdmissionId"] ==
                DBNull.Value)
            {
                continue;
            }
            if (!int.TryParse(
                    Convert.ToString(
                        row["AdmissionId"]),
                    out int id))
            {
                continue;
            }
            if (id ==
                admissionId)
            {
                result.ImportRow(row);
            }
        }
        return result;
    }
    private static int GetInt(
        DataRow row,
        string column)
    {
        if (!row.Table.Columns.Contains(
                column) ||
            row[column] ==
                DBNull.Value)
        {
            return 0;
        }
        return int.TryParse(
            Convert.ToString(
                row[column]),
            out int value)
                ? value
                : 0;
    }
    private static int GetInt(
        object? value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return 0;
        }
        return int.TryParse(
            Convert.ToString(
                value),
            out int result)
                ? result
                : 0;
    }
    private static long GetLong(
        DataRow row,
        string column)
    {
        if (!row.Table.Columns.Contains(
                column) ||
            row[column] ==
                DBNull.Value)
        {
            return 0L;
        }
        return long.TryParse(
            Convert.ToString(
                row[column]),
            out long value)
                ? value
                : 0L;
    }
    private static long GetLong(
        object? value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return 0L;
        }
        return long.TryParse(
            Convert.ToString(
                value),
            out long result)
                ? result
                : 0L;
    }
    private static decimal GetDecimal(
        DataRow row,
        string column)
    {
        if (!row.Table.Columns.Contains(
                column) ||
            row[column] ==
                DBNull.Value)
        {
            return 0m;
        }
        return decimal.TryParse(
            Convert.ToString(
                row[column]),
            out decimal value)
                ? value
                : 0m;
    }
    private static decimal GetDecimal(
        object? value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return 0m;
        }
        return decimal.TryParse(
            Convert.ToString(
                value),
            out decimal result)
                ? result
                : 0m;
    }
    private static string GetText(
        DataRow row,
        string column)
    {
        if (!row.Table.Columns.Contains(
                column) ||
            row[column] ==
                DBNull.Value)
        {
            return string.Empty;
        }
        return Convert.ToString(
            row[column])
            ?? string.Empty;
    }
    private static string GetText(
        object? value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return string.Empty;
        }
        return Convert.ToString(value)
            ?? string.Empty;
    }
    private static string FormatDate(
        DataRow row,
        string column)
    {
        if (!row.Table.Columns.Contains(
                column) ||
            row[column] ==
                DBNull.Value)
        {
            return string.Empty;
        }
        return DateTime.TryParse(
            Convert.ToString(
                row[column]),
            out DateTime value)
                ? value.ToString(
                    "dd-MMM-yyyy")
                : Convert.ToString(
                    row[column])
                    ?? string.Empty;
    }
    private static string FormatBillingMonth(
        object value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return string.Empty;
        }
        return DateTime.TryParse(
            Convert.ToString(
                value),
            out DateTime parsed)
                ? parsed.ToString(
                    "MMM yyyy")
                : Convert.ToString(
                    value)
                    ?? string.Empty;
    }
    private string StudentNameFor(int id)
    {
        if(id==admissionId && student.SelectedRow is DataRow selected) {
            string name=GetText(selected,"StudentName");
            if(!string.IsNullOrWhiteSpace(name)) return name;
        }
        if(payingFor.TryGetValue(id,out var info) && !string.IsNullOrWhiteSpace(info.Name)) return info.Name;
        return $"Admission {AdmissionNumbers.ForId(id)}";
    }
    private static string FormatDueDate(
        object value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return string.Empty;
        }
        return DateTime.TryParse(
            Convert.ToString(
                value),
            out DateTime parsed)
                ? parsed.ToString(
                    "dd-MMM-yyyy")
                : Convert.ToString(
                    value)
                    ?? string.Empty;
    }
    // Reprints the most recently completed Collection using the true, post-discount figures. See PrintPaymentReceipt's showActualAmount parameter, which still carries the parent path for when it is wanted again.
    private async Task PrintReceiptAsync()
    {
        if (transactionBusy) return;
        if (lastCollectionReceipt is null && lastGroupReceiptNumber is null)
        {
            Warn(
                "Process a Collection first - Print Receipt reprints " +
                "the transaction just completed, with its real amounts.");
            return;
        }
        /* both values are captured BEFORE the await below. */
        var receiptSnapshot = lastCollectionReceipt;
        string? groupReceiptNumber = lastGroupReceiptNumber;
        /* The receipt's institution name is read from School Profile. Nothing is printed when it cannot be read. */
        var institution =
            await Core.InstitutionProfile.GetPrintIdentityAsync();
        if (string.IsNullOrWhiteSpace(institution.Name))
        {
            Alert(
                "The school profile could not be read, so nothing was printed. " +
                "Check School Profile, then try again.");
            return;
        }
        /* A family collection carries ONE receipt number shared by every child, so it is reprinted as a single document listing each child, rather than as one receipt per student. */
        if (groupReceiptNumber is not null)
        {
            await PrintGroupReceiptAsync(institution, groupReceiptNumber);
            return;
        }
        /* assigned from the value captured before the await, and checked, because the field may have been cleared while the institution name was being read. */
        if (receiptSnapshot is null)
        {
            Warn(
                "The collection being reprinted is no longer on screen. " +
                "Print the receipt before changing the student or enrollment.");
            return;
        }
        var snapshot = receiptSnapshot;
        var identity = await FeeReceiptPlacement.QueryStudentAsync(snapshot.AdmissionId);
        if (identity.Rows.Count == 0) { Alert("The student's details could not be read. Try printing again."); return; }
        await PrintPaymentReceipt(
            institution,
            GetText(identity.Rows[0],"StudentName"),
            snapshot.AdmissionId,
            snapshot.ReceiptNumber,
            snapshot.PaymentMethod,
            snapshot.TransactionDate,
            snapshot.ClassName,
            snapshot.SectionName,
            snapshot.CampusName,
            snapshot.BalanceRemaining,
            /* showActualAmount */ true,
            snapshot.DiscountApplied,
            snapshot.Lines
                .Select(l => (l.FeeType, l.BillingMonth, l.NominalAmount, l.ActualAmount))
                .ToList(), GetText(identity.Rows[0],"FatherName"), snapshot.EnrollmentId);
    }
    private (string ClassName, string SectionName, string CampusName) GetSelectedEnrollmentPlacement()
    {
        if (enrollment.SelectedItem is DataRowView rowView &&
            rowView.Row.Table.Columns.Contains("ClassName") &&
            rowView.Row.Table.Columns.Contains("SectionName"))
        {
            return (
                Convert.ToString(rowView.Row["ClassName"]) ?? string.Empty,
                Convert.ToString(rowView.Row["SectionName"]) ?? string.Empty,
                GetText(rowView.Row,"CampusName"));
        }
        return (string.Empty, string.Empty, string.Empty);
    }
    /* A family collection keeps its one database receipt number, while printing one complete physical receipt page per child. */
    private async Task PrintGroupReceiptAsync(
        InstitutionProfile.PrintIdentity institution,
        string receiptNumber)
    {
        DataSet result =
            await DatabaseHelper.QueryUserSetAsync(
                "GetFeeReceiptGroup",
                DatabaseHelper.P("@ReceiptNumber", receiptNumber));
        DataTable header =
            result.Tables.Count > 0 ? result.Tables[0] : new DataTable();
        DataTable lines = await FeeReceiptPlacement.QueryLinesAsync(receiptNumber);
        if (header.Rows.Count == 0)
        {
            /* Alert, not Warn: the operator asked for a print and the print did not happen. */
            Alert(
                $"Receipt {receiptNumber} could not be read back, so nothing " +
                "was printed. Check the receipt number and try again.");
            return;
        }
        DataRow head = header.Rows[0];
        var rows = new List<FamilyReceiptLine>();
        foreach (DataRow row in lines.Rows)
        {
            rows.Add(new FamilyReceiptLine {
                AdmissionId = GetInt(row, "AdmissionId"),
                EnrollmentId = GetInt(row, "EnrollmentId"),
                StudentName = GetText(row, "StudentName"),
                FatherName = GetText(row,"FatherName"),
                ClassName = GetText(row, "ClassName"),
                SectionName = GetText(row, "SectionName"),
                CampusName = GetText(row, "CampusName"),
                FeeName = GetText(row, "FeeTypeName"),
                BillingMonth = row.Table.Columns.Contains("BillingMonth")
                    ? FormatBillingMonth(row["BillingMonth"])
                    : string.Empty,
                Amount = GetDecimal(row, "Amount")
            });
        }
        await PrintGroupReceipt(
            institution,
            receiptNumber,
            FormatDate(head, "PaymentDate"),
            GetText(head, "PaymentMethod"),
            GetText(head, "ReceiptStatus"),
            rows);
    }
    private async Task PrintGroupReceipt(
        InstitutionProfile.PrintIdentity institution,
        string receiptNumber,
        string paymentDate,
        string paymentMethod,
        string status,
        List<FamilyReceiptLine> rows)
    {
        var children = rows
            .GroupBy(
                row => row.AdmissionId > 0
                    ? $"id:{row.AdmissionId}:enrollment:{row.EnrollmentId}"
                    : $"name:{row.StudentName.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToList())
            .ToList();
        if (children.Count == 0)
        {
            Alert(
                $"Receipt {receiptNumber} has no printable student lines, so nothing was printed.");
            return;
        }
        DataTable reportData = CreateReceiptReportTable();
        for (int childIndex = 0; childIndex < children.Count; childIndex++)
        {
            List<FamilyReceiptLine> child = children[childIndex];
            FamilyReceiptLine first = child[0];
            decimal total = child.Sum(line => line.Amount);
            foreach (FamilyReceiptLine line in child)
            {
                AddReceiptReportRow(
                    reportData,
                    receiptPageKey: childIndex + 1,
                    institution,
                    documentTitle: "CASH RECEIPT",
                    copyLabel: "STUDENT COPY",
                    receiptNumber,
                    studentName: first.StudentName,
                    admissionNumberText: first.AdmissionId > 0
                        ? AdmissionNumbers.ForId(first.AdmissionId)
                        : string.Empty,
                    classSection: FormatClassSection(first.ClassName, first.SectionName),
                    paymentDateText: paymentDate,
                    familyBatch: children.Count > 1
                        ? $"Receipt {childIndex + 1} of {children.Count}"
                        : string.Empty,
                    feeDescription: line.FeeName,
                    amount: line.Amount,
                    discountApplied: 0m,
                    totalPaid: total,
                    balanceRemaining: 0m,
                    paymentMethod,
                    receiptStatus: string.Equals(status, "Issued", StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : status,
                    showDiscount: false,
                    showBalance: false,
                    campusName: first.CampusName,
                    fatherName: first.FatherName,
                    feePeriod: line.BillingMonth,
                    paymentEnrollmentId: first.EnrollmentId);
            }
        }
        await FeeReceiptPlacement.ApplyCurrentBalanceAsync(reportData);
        ShowReceiptReport(reportData);
    }
    private async Task PrintPaymentReceipt(
        InstitutionProfile.PrintIdentity institution,
        string studentName,
        int admissionId,
        string receiptNumber,
        string paymentMethod,
        DateTime transactionDate,
        string className,
        string sectionName,
        string campusName,
        decimal balanceRemaining,
        bool showActualAmount,
        decimal discountApplied,
        List<(string feeType, string billingMonth, decimal nominalAmount, decimal actualAmount)> rows,
        string fatherName = "", int paymentEnrollmentId = 0)
    {
        DataTable reportData = CreateReceiptReportTable();
        decimal total = rows.Sum(row => showActualAmount ? row.actualAmount : row.nominalAmount);
        IEnumerable<(string feeType, string billingMonth, decimal nominalAmount, decimal actualAmount)> printableRows =
            rows.Count == 0
                ? new[] { ("Payment", string.Empty, total, total) }
                : rows;
        foreach (var row in printableRows)
        {
            AddReceiptReportRow(
                reportData,
                receiptPageKey: 1,
                institution,
                documentTitle: showActualAmount ? "CASH RECEIPT" : "FEE BILL",
                copyLabel: showActualAmount ? "STUDENT COPY" : "CUSTOMER COPY",
                receiptNumber,
                studentName,
                admissionNumberText: admissionId > 0 ? AdmissionNumbers.ForId(admissionId) : string.Empty,
                classSection: FormatClassSection(className, sectionName),
                paymentDateText: transactionDate.ToString("dd-MMM-yyyy hh:mm tt"),
                familyBatch: string.Empty,
                feeDescription: row.feeType,
                amount: showActualAmount ? row.actualAmount : row.nominalAmount,
                discountApplied,
                totalPaid: total,
                balanceRemaining,
                paymentMethod,
                receiptStatus: string.Empty,
                showDiscount: showActualAmount && discountApplied > 0m,
                showBalance: true,
                campusName: campusName,
                fatherName: fatherName,
                feePeriod: row.billingMonth,
                paymentEnrollmentId: paymentEnrollmentId);
        }
        if (showActualAmount) await FeeReceiptPlacement.ApplyCurrentBalanceAsync(reportData);
        ShowReceiptReport(reportData);
    }

    private static DataTable CreateReceiptReportTable() => ReceiptReportData.Create();

    private static void AddReceiptReportRow(
        DataTable table,
        int receiptPageKey,
        InstitutionProfile.PrintIdentity institution,
        string documentTitle,
        string copyLabel,
        string receiptNumber,
        string studentName,
        string admissionNumberText,
        string classSection,
        string paymentDateText,
        string familyBatch,
        string feeDescription,
        decimal amount,
        decimal discountApplied,
        decimal totalPaid,
        decimal balanceRemaining,
        string paymentMethod,
        string receiptStatus,
        bool showDiscount,
        bool showBalance,
        string campusName = "", string fatherName = "", string feePeriod = "", int paymentEnrollmentId = 0)
    {
        var reportRow = table.Rows.Add(
            receiptPageKey,
            institution.Name,
            documentTitle,
            copyLabel,
            receiptNumber,
            studentName,
            admissionNumberText,
            classSection,
            paymentDateText,
            familyBatch,
            feeDescription,
            amount,
            discountApplied,
            totalPaid,
            balanceRemaining,
            paymentMethod,
            receiptStatus,
            showDiscount,
            showBalance,
            institution.ContactNumber,
            institution.Address,
            campusName,
            fatherName,
            feePeriod);
        reportRow["PaymentEnrollmentId"] = paymentEnrollmentId;
    }

    private static string FormatClassSection(string className, string sectionName) =>
        string.IsNullOrWhiteSpace(className)
            ? string.Empty
            : string.IsNullOrWhiteSpace(sectionName)
                ? className
                : $"{className} - {sectionName}";

    private void ShowReceiptReport(DataTable reportData)
    {
        ReceiptReportData.GroupPeriods(reportData);
        string reportPath = Path.Combine(
            AppContext.BaseDirectory,
            "Reports",
            "FeePaymentReceipt.rdlc");
        if (!File.Exists(reportPath))
        {
            Alert("Fee receipt RDLC was not found, so nothing was printed.");
            return;
        }
        RdlcReportPreview.Show(
            FindForm(),
            "Fee Receipt",
            reportPath,
            "FeeReceiptData",
            reportData,
            fitPageWidth: false,
            reportDefinition: ReceiptReportLayout.Definition(reportPath, reportData));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            chargeMenu.Dispose();
            studentFeesMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}









