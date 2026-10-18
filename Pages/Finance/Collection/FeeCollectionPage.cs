using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Collection;
public sealed class FeeCollectionPage : V2Page, IBusyPage
{
    public bool IsBusy => transactionBusy || billBusy;
    public string BusyMessage => billBusy ? "Finish the bill, return, or fee-review workflow before leaving." : "Please wait for the fee collection to finish recording.";
    private bool billBusy;
    private readonly StudentPicker student = new();
    private readonly LookupBox enrollment = new();
    private readonly DataGridView charges =
        HumanUi.Grid(true);
    /* Reverse acts on one charge; assignment changes belong in Fee Assignments. */
    private readonly ContextMenuStrip chargeMenu = new StyledContextMenu();
    private readonly ContextMenuStrip studentFeesMenu = new StyledContextMenu();
    private readonly DataGridView recentPayments =
        HumanUi.Grid();
    /* Show one proposal row per child only for family collections. */
    private readonly DataGridView familyProposal =
        HumanUi.Grid(true);
    private bool suppressProposalEvents;
    private RowStyle? proposalRow;
    /* Collect selected siblings' fees in one workflow without a separate family screen. */
    private readonly CheckedListBox siblings = new FamilyStudentList() {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        BackColor = AppTheme.Surface,
        ForeColor = AppTheme.Text
    };
    private readonly Label primaryPayer = new() { Dock=DockStyle.Fill, Font=AppTheme.NormalFont, TextAlign=ContentAlignment.MiddleLeft,
        BackColor=AppTheme.SurfaceSecondary, Padding=new Padding(8), AutoEllipsis=true };
    private ModernButton selectAllSiblings = null!;
    private Action? updateSiblingLayout;
    /// <summary>Ignore programmatic sibling ticks while rebuilding the list.</summary>
    private bool suppressSiblingEvents;
    /// <summary>Prevent overlapping sibling charge loads.</summary>
    private bool siblingBusy;
    private bool focusAmountAfterSiblingLoad;
    /// <summary>Report multiple receipts after refreshes finish updating status.</summary>
    private string? multiStudentReceiptNote;
    /// <summary>Retain the shared family receipt number for reprinting.</summary>
    private string? lastGroupReceiptNumber;
    private readonly Label siblingHint = new() {
        Dock = DockStyle.Bottom,
        AutoSize = false,
        Height = AppScale.Scale(30, AppScale.ControlSizeScale),
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft
    };
    /// <summary>Map selected siblings to their enrollment and display name for allocation.</summary>
    private readonly Dictionary<int, (int EnrollmentId, string Name)> payingFor = new();
    private readonly ModernComboBox method =
        HumanUi.Combo();
    private readonly ModernTextBox discountAmount =
        HumanUi.TextBox(false, 100);
    /* Allocate the entered payment across charges in settlement order. */
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
    /* Preserve a payment date explicitly chosen by the operator. */
    private bool paymentDateEdited;
    private bool suppressPaymentDateEvents;
    private readonly Label totalDue =
        new();
    private readonly Label balance =
        new();
    /* Derived payable amount after discount, before collection. */
    private readonly Label amountToPay =
        new();
    private int admissionId;
    private int enrollmentId;
    private bool suppressStudentEvents;
    private bool suppressSearch;
    private bool suppressDiscountEvents;
    private bool suppressPaymentEvents;
    /// <summary>Last displayed payable amount after discount allocation.</summary>
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
    private ModernButton? studentCreditButton;
    private readonly Dictionary<int, decimal> availableCreditByStudent = new();
    private readonly CheckBox useAvailableCredit = new() { Text = "Use available credit", Checked = true, AutoSize = true };
    private readonly Label availableCredit = new();
    private readonly Label remainingCredit = new();
    private Control? outstandingSummary;
    private Control? remainingBalanceSummary;
    private Control? creditSummary;
    private Control? remainingCreditSummary;
    /// <summary>Payment History receipt-reprint action.</summary>
    private ModernButton? reprintReceiptButton;
    /// <summary>Off-screen parent for the payment list.</summary>
    private Panel? paymentHistoryHost;
    private bool transactionBusy;
    // Retain the last committed receipt with its actual post-discount amounts.
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
    private readonly bool initialize;
    private readonly int preloadFocusAdmissionId;
    internal override bool FocusEntryField()
    {
        if (preloadFocusAdmissionId > 0 && admissionId == preloadFocusAdmissionId &&
            HasOutstandingCharges() && paymentAmount.CanFocus)
        {
            bool focused = paymentAmount.Focus();
            if (focused) paymentAmount.SelectAll();
            return focused;
        }
        return base.FocusEntryField();
    }
    public FeeCollectionPage() : this(true) { }
    internal FeeCollectionPage(bool initialize)
        : base(
            "Fee Collection",
            "Record student and family fee payments.")
    {
        this.initialize=initialize;
        BuildStudentSearch();
    }
    public FeeCollectionPage(int preloadAdmissionId)
        : this()
    {
        if (preloadAdmissionId > 0)
        {
            preloadFocusAdmissionId = preloadAdmissionId;
            Defer(() => LoadStudentByAdmissionId(preloadAdmissionId));
        }
    }
    private void BuildStudentSearch()
    {
        discountAmount.ZeroAsPlaceholder = paymentAmount.ZeroAsPlaceholder = true;
        ConfigureDecimalInput(discountAmount);
        paymentAmount.KeyPress += WholeAmountKeyPress;
        /* This workspace records collections only. */
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
        /* Guard the async collection handler against duplicate submissions. */
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
        // Open fee assignments for the selected enrollment.
        feeAssignmentsButton =
            AddAction(
                "Fee Assignments",
                (_, _) => OpenFeeAssignments(),
                "Secondary");
        feeAssignmentsButton.Enabled = false;
        addChargeButton = AddAction(
            "Add Charge", (_, _) => OpenAdditionalFee(), "Secondary");
        addChargeButton.Enabled = false;
        studentCreditButton = AddAction("Student Credit", async (_, _) => {
            if (admissionId <= 0) { Warn("Select a student first."); return; }
            using var dialog = new StudentCreditDialog(admissionId);
            dialog.ShowDialog(FindForm());
            await LoadChargesAsync(loadVersion);
            await LoadRecentPaymentsAsync(loadVersion);
        }, "Secondary");
        // Reuse existing projection and statement views for Future Fees and History.
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
        /* Open payment history on demand. */
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
        /* Delegate menu actions to the existing buttons so permissions and enabled states remain authoritative. */
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
        studentFeesMenu.Items.Add(new ToolStripSeparator());
        var printBillItem=new ToolStripMenuItem("Print Outstanding Bill") {
            ToolTipText="Selected child and ticked siblings: all posted unpaid charges, including older years. This is not a payment receipt."
        };
        printBillItem.Click+=async(_,_)=>await PrintSelectedFamilyBillAsync();
        studentFeesMenu.Items.Add(printBillItem);
        var familyReturnsItem=new ToolStripMenuItem("Family / Returns");
        familyReturnsItem.Click+=async(_,_)=>await OpenFamilyReturnsAsync();
        studentFeesMenu.Items.Add(familyReturnsItem);
        studentFeesMenu.Opening += (_, _) => {
            if (studentFeesMenu.Items.Count < 5) return;
            studentFeesMenu.Items[0].Enabled = addChargeButton?.Enabled == true;
            studentFeesMenu.Items[1].Enabled = futureFeesButton?.Enabled == true;
            studentFeesMenu.Items[3].Enabled = historyButton?.Enabled == true;
            studentFeesMenu.Items[4].Enabled = paymentHistoryButton?.Enabled == true;
            printBillItem.Available=CurrentUser.HasPermission("Fees.View");
            printBillItem.Enabled=admissionId>0 && !IsBusy && !siblingBusy && CurrentUser.HasPermission("Fees.View");
            familyReturnsItem.Available=CurrentUser.HasPermission("Students.Family");
            familyReturnsItem.Enabled=admissionId>0 && !IsBusy && !siblingBusy && CurrentUser.HasPermission("Students.Family") && CurrentUser.HasPermission("Students.View");
            familyReturnsItem.ToolTipText="Review family readiness and individual returns. Requires family and student-view access.";
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
        if(initialize)StudentSearchCache.Warm();
        /* Clear the previous student's data without changing the search text. */
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
        /* Load only a committed student selection. */
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
            /* Clear sibling charges, discounts and payment amounts when enrollment changes. */
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
            /* Reallocate payment after discount changes so the grid and payment field agree. */
            if (PaidIsUntouchedDefault())
                RefreshEnteredPaymentAllocation();
            else
                ApplyPaymentAcrossCharges();
        };
        /* The payment field drives allocation; the grid displays the result. */
        paymentAmount.TextChanged += (_, _) => {
            if (suppressPaymentEvents)
                return;
            ApplyPaymentAcrossCharges();
        };
        /* Only an operator-selected date suppresses automatic date refresh. */
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
                "Choose the student once; the current academic placement and outstanding charges load automatically.");
        /* Keep method, reference and remarks beside the amount being collected. */
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
                "Academic Placement",
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
        var reverseChargeButton = new ModernButton {
            Text = "Reverse Charge",
            ThemeRole = "Danger",
            Width = AppScale.Scale(150),
            Height = AppMetrics.ButtonHeight,
            Enabled = false
        };
        void UpdateReverseChargeButton() => reverseChargeButton.Enabled =
            !transactionBusy && charges.Enabled && charges.CurrentRow is { } row &&
            GetLong(row.Cells["BillingChargeId"].Value) > 0;
        charges.SelectionChanged += (_, _) => UpdateReverseChargeButton();
        charges.EnabledChanged += (_, _) => UpdateReverseChargeButton();
        charges.DataBindingComplete += (_, _) => UpdateReverseChargeButton();
        reverseChargeButton.Click += async (_, _) => {
            if (transactionBusy || charges.CurrentRow is not { } row) return;
            await ReverseChargeAsync(row.Index);
            UpdateReverseChargeButton();
        };
        var chargeActions = new FlowLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty
        };
        allocationButton.Margin = new Padding(0,0,AppMetrics.SpacingSm,0);
        reverseChargeButton.Margin = Padding.Empty;
        chargeActions.Controls.Add(allocationButton);
        chargeActions.Controls.Add(reverseChargeButton);
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
                "Select a charge to reverse it. Use Adjust Allocation to pay specific charges.", AppTheme.ModuleAccent("Finance"), chargeActions);
        chargeCard.Header.Controls.SetChildIndex(chargeActions, chargeCard.Header.Controls.Count - 1);
        chargeCard.Content.Controls.Add(
            charges);
        /* Payment history opens separately; preserve space for collection. */
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
        /* Ticking a sibling adds their outstanding charges to this collection. */
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
        siblingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(78)));
        siblingPanel.Controls.Add(primaryPayer,0,0);
        siblingPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                55f));
        siblingPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppScale.Scale(48)));
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
        /* Attach the panel after its button exists. */
        siblingPanel.Controls.Add(
            selectAllSiblings,
            0,
            3);
        /* Show the per-child proposal only during family allocation. */
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
        siblings.ItemCheck += (_, _) => {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) SetSiblingHint(""); }));
        };
        enrollment.SelectedIndexChanged += (_, _) => {
            if (admissionId > 0) primaryPayer.Text = $"☑ {AdmissionNumbers.ForId(admissionId)} · {StudentNameFor(admissionId)}\n    {enrollment.Text} · Always included";
        };
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
        int valueHeight = AppScale.Scale(40, AppScale.ControlSizeScale);
        foreach (int height in new[] { valueHeight, AppMetrics.FieldRowHeight + 2, 0, valueHeight,
            AppMetrics.FieldRowHeight + 2, 0, valueHeight, 0, AppMetrics.FieldRowHeight + 2,
            AppMetrics.FieldRowHeight + 2, AppMetrics.ButtonHeight + 6, AppMetrics.ButtonHeight + 6, AppMetrics.ButtonHeight + 6 })
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        summary.SizeChanged += (_, _) => collectionResult.MaximumSize = new Size(Math.Max(1, summary.ClientSize.Width - 8), 0);
        totalDue.Text = balance.Text = "0.00";
        suppressPaymentEvents = true;
        try { paymentAmount.Text = "0"; }
        finally { suppressPaymentEvents = false; }

        outstandingSummary = SummaryField("Outstanding (Rs.)", totalDue, AppTheme.HeadingText);
        summary.Controls.Add(outstandingSummary, 0, 0);
        summary.Controls.Add(HumanUi.Field("Discount (Rs.)", discountAmount), 0, 1);
        summary.Controls.Add(SummaryField("Amount to Pay (Rs.)", amountToPay, AppTheme.HeadingText), 0, 3);
        summary.Controls.Add(HumanUi.Field("Amount Received (Rs.)", paymentAmount, true), 0, 4);
        var amountError = new Label {
            Dock = DockStyle.Fill, Margin = new Padding(8, 0, 8, 0),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.Danger,
            TextAlign = ContentAlignment.TopLeft, AccessibleRole = AccessibleRole.Alert,
            Visible = false
        };
        summary.Controls.Add(amountError, 0, 5);
        amountError.VisibleChanged += (_, _) =>
            summary.RowStyles[5].Height = amountError.Visible
                ? AppMetrics.TextHeight(AppTheme.SmallFont) * 2 + 4 : 0;
        (collectionValidator ??= CreateValidator()).BindMessageLabel(paymentAmount, amountError);
        remainingBalanceSummary = SummaryField("Remaining Balance (Rs.)", balance, AppTheme.Text);
        summary.Controls.Add(remainingBalanceSummary, 0, 6);
        var paymentDetails = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0)
        };
        paymentDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        paymentDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        paymentDetails.Controls.Add(HumanUi.Field("Method", method), 0, 0);
        paymentDetails.Controls.Add(HumanUi.Field("Reference No.", reference), 1, 0);
        summary.Controls.Add(paymentDetails, 0, 8);
        summary.Controls.Add(HumanUi.Field("Remarks", remarks), 0, 9);
        fullAmountButton = AddAction("Use Full Amount", (_, _) => UseFullAmount(), "Secondary");
        var receiptActions = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0)
        };
        receiptActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        receiptActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        receiptActions.Controls.Add(fullAmountButton, 0, 0);
        receiptActions.Controls.Add(printReceiptButton!, 1, 0);

        summary.Controls.Add(executeButton!, 0, 10);
        summary.Controls.Add(receiptActions, 0, 11);
        summary.Controls.Add(clear, 0, 12);
        creditSummary = SummaryField("Available credit (Rs.)", availableCredit, Color.ForestGreen);
        remainingCreditSummary = SummaryField("Remaining credit (Rs.)", remainingCredit, Color.ForestGreen);
        ((TableLayoutPanel)remainingCreditSummary).ColumnStyles[0].Width = 70;
        ((TableLayoutPanel)remainingCreditSummary).ColumnStyles[1].Width = 30;
        // Keep credit with the balance figures, above the amount received.
        summary.Controls.Add(creditSummary, 0, 2);
        summary.Controls.Add(remainingCreditSummary, 0, 7);
        var creditField = (TableLayoutPanel)creditSummary;
        creditField.ColumnStyles[0].Width = 70;
        creditField.ColumnStyles[1].Width = 30;
        var oldCaption = creditField.GetControlFromPosition(0, 0)!;
        creditField.Controls.Remove(oldCaption); oldCaption.Dispose();
        useAvailableCredit.Dock = DockStyle.Fill;
        useAvailableCredit.AutoSize = false;
        useAvailableCredit.Font = new Font(AppTheme.SmallFont, FontStyle.Bold);
        useAvailableCredit.ForeColor = Color.ForestGreen;
        creditField.Controls.Add(useAvailableCredit, 0, 0);
        useAvailableCredit.CheckedChanged += (_, _) => {
            if (!suppressPaymentEvents) RefreshEnteredPaymentAllocation();
        };
        // Keep transaction feedback in the page status, not below Next Student.
        Disposed += (_, _) => collectionResult.Dispose();
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
            Dock = DockStyle.Right, WrapContents = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        paymentHistoryButton!.Text = "Recent Payments";
        foreach (Control button in new Control[] { studentFeesMenuButton!, studentCreditButton!, paymentHistoryButton })
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
        // Owner-approved layout: retain the 78/22 split unless a reproduced usability issue justifies change.
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        work.Padding = new Padding(0);
        work.Margin = new Padding(0, 0, 10, 0);
        workspace.Controls.Add(work, 0, 0);
        workspace.Controls.Add(paymentSummary, 1, 0);
        context.SetHeaderText("Student", "Search by admission number, student or father name.");
        context.Header.Controls.Add(actions);
        context.Header.Controls.SetChildIndex(actions, context.Header.Controls.Count - 1);
        chargeCard.SetHeaderText("Outstanding Charges", "Review the charges included in this payment.");
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(0), Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(context, 0, 0);
        root.Controls.Add(workspace, 0, 1);
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
                HeaderText = UiText.Fees.FeeType,
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
                HeaderText = UiText.Fees.Due,
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
                HeaderText = UiText.Fees.PayNow,
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
                HeaderText = UiText.Fees.Amount,
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
        /* Keep positional row values aligned with the column order. */
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
            Name = "AppliedDiscount", HeaderText = UiText.Fees.Discount, ReadOnly = true, Visible = false,
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
        // Retained for later use: charge remarks column.
        // charges.Columns.Add(new DataGridViewTextBoxColumn {
        //     Name = "ChargeRemarks", HeaderText = "Remarks", ReadOnly = true,
        //     MinimumWidth = AppScale.Scale(160), Width = AppScale.Scale(220)
        // });
        charges.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        charges.Columns["Pay"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        foreach (string columnName in new[] { "GrossAmount", "AppliedDiscount", "Balance", "Amount" })
        {
            charges.Columns[columnName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            charges.Columns[columnName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        /* Shared grid helpers own column fitting. */
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
            if (!InputParse.TryWholeAmount(Convert.ToString(e.FormattedValue), out decimal value) || value<0m || value>maximum)
            {
                e.Cancel=true;
                row.ErrorText=$"Enter a whole-rupee discount between 0 and {maximum:0.##}.";
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
        /* Use the same row menu for mouse and keyboard gestures. */
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
        var terms=chargeMenu.Items.Add("Review bill terms...");
        terms.Enabled=!IsBusy&&CurrentUser.HasPermission("Fees.Adjust");
        terms.Click+=async(_,_)=>await ReviewBillTermsAsync(chargeId);
        var reverse =
            chargeMenu.Items.Add(
                "Reverse this charge...");
        /* Hold all charge inputs while their allocation is being submitted. */
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
                HeaderText = "Amount (Rs.)",
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
                Name = name, HeaderText = name == "Adjusted" ? "Adjustments" : "Net Amount",
                ReadOnly = true, MinimumWidth = AppScale.Scale(85),
                DefaultCellStyle = new DataGridViewCellStyle { Format = "0.##", Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        recentPayments.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "ReceiptTotal", HeaderText = "Family Receipt Total", ReadOnly = true,
            MinimumWidth = AppScale.Scale(100),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "#,0.##" }
        });
        recentPayments.Columns["Adjusted"].DisplayIndex = 5;
        // Keep the database receipt key for reprinting/recovery; format credit
        // applications for display using their permanent payment identity.
        recentPayments.CellFormatting += (_, e) => {
            if (e.RowIndex < 0) return;
            string column = recentPayments.Columns[e.ColumnIndex].Name;
            if (column == "TransactionType")
            {
                e.Value = Convert.ToString(e.Value) switch {
                    "Collected" => "Fee payment", "Family collection" => "Family fee payment",
                    "Credit deposit" => "Advance deposited", "Credit refund" => "Advance refunded",
                    "Credit Application" => "Credit used for fees", _ => e.Value
                };
                e.FormattingApplied = true;
            }
            if (column == "PaymentMethod" && Convert.ToString(e.Value) == "Account Credit")
            { e.Value = "Credit balance"; e.FormattingApplied = true; }
            if (e.RowIndex >= 0 && recentPayments.Columns[e.ColumnIndex].Name == "ReceiptNumber" &&
                recentPayments.Rows[e.RowIndex].Cells["ReceiptNumber"].Tag is string reference)
            { e.Value = reference; e.FormattingApplied = true; }
        };
        recentPayments.Columns["NetPaid"].DisplayIndex = 6;
        recentPayments.Columns["Adjusted"].Visible = recentPayments.Columns["NetPaid"].Visible = false;
        recentPayments.Columns["ReceiptNumber"].HeaderText = "Reference";
        recentPayments.Columns["TransactionType"].HeaderText = "Activity";
        recentPayments.Columns["ChargeType"].HeaderText = "Fee / Details";
        recentPayments.Columns["Amount"].HeaderText = "Student Amount";
        int order = 0;
        foreach (string name in new[] { "PaymentDate", "ReceiptNumber", "TransactionType", "ChargeType", "Amount", "PaymentMethod", "ReceiptTotal" })
            recentPayments.Columns[name].DisplayIndex = order++;
        foreach (string name in new[] { "Amount", "Adjusted", "NetPaid", "ReceiptTotal" })
            recentPayments.Columns[name].DefaultCellStyle.Format = "#,0.##";
        recentPayments.Columns["ReceiptTotal"].DefaultCellStyle.NullValue = "—";
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
    /// <summary>Keep collection mode explicit.</summary>
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
        // Payment history is read-only; modify payments through Fee Adjustments.
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
            // Navigation may focus the search before the preloaded bills finish loading.
            // Advance that initial focus, while preserving any input the operator moved to.
            if (!IsDisposed && ContainsFocus && student.ContainsFocus &&
                this.admissionId == admissionId && student.SelectedId == admissionId &&
                HasOutstandingCharges() && paymentAmount.CanFocus)
            {
                paymentAmount.Focus();
                paymentAmount.SelectAll();
            }
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
        primaryPayer.Text=selectedAdmissionId>0 ? $"☑ {AdmissionNumbers.ForId(selectedAdmissionId)} · {StudentNameFor(selectedAdmissionId)}\n    Current student · Always included" : "";
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
        /* Reset to today for a new student unless the operator chooses another date. */
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
        /* Clearing enrollment triggers loads that advance loadVersion. */
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
            /* Recheck the student before binding enrollment and enabling money actions. */
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
            /* Awaiting-return enrollments are unbillable; earlier enrollments may still have outstanding fees. */
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
                    ? "Current placement loaded."
                    : "All outstanding placements loaded. Each bill retains its original academic year.");
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
        availableCreditByStudent.Clear();
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
    /// <summary>Tuition first; other fees share chronological priority.</summary>
    private static int SettlementRank(DataRow row) =>
        string.Equals(GetText(row, "FeeTypeCode"), "TUITION", StringComparison.OrdinalIgnoreCase) ||
        GetText(row, "FeeTypeName").Contains("tuition", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    private async Task<int> AppendChargesAsync(
        int forAdmissionId,
        int forEnrollmentId,
        int version = -1,
        int expectedPrimaryAdmissionId = 0,
        int expectedPrimaryEnrollmentId = 0)
    {
        if (forAdmissionId <= 0 || forEnrollmentId <= 0)
            return 0;
        var chargesTask = DatabaseHelper.QueryUserAsync(
            "GetOutstandingCharges",
            DatabaseHelper.Int("@AdmissionId", forAdmissionId),
            DatabaseHelper.Int("@EnrollmentId", 0),
            DatabaseHelper.Date("@AsOfDate", DateTime.Today));
        // Same Fees.View permission as the former direct query; stable catalog
        // data can use the existing user-scoped cache across sibling accounts.
        var feeOrderTask = DatabaseHelper.QueryUserAsync("GetFeeTypes");
        var creditTask = DatabaseHelper.QueryUserAsync("GetStudentCredit", DatabaseHelper.Int("@AdmissionId", forAdmissionId));
        await Task.WhenAll(chargesTask, feeOrderTask, creditTask);
        DataTable raw = await chargesTask;
        var feeOrderRows = await feeOrderTask;
        var feeOrder=feeOrderRows.AsEnumerable().ToDictionary(row=>GetInt(row,"FeeTypeId"),row=>GetInt(row,"DisplayOrder"));
        /* Version -1 identifies explicit sibling ticks that append charges on demand. */
        if (version >= 0 &&
            (IsDisposed || version != loadVersion))
        {
            return 0;
        }
        /* Do not append charges after the selected student changes. */
        if (IsDisposed ||
            (expectedPrimaryAdmissionId > 0 &&
             (admissionId != expectedPrimaryAdmissionId ||
              enrollmentId != expectedPrimaryEnrollmentId)))
        {
            return 0;
        }
        var creditRows = await creditTask;
        availableCreditByStudent[forAdmissionId] = creditRows.Rows.Count > 0 ? GetDecimal(creditRows.Rows[0], "AvailableCredit") : 0m;
        /* Oldest tuition first, then all other charges in date order. */
        List<DataRow> ordered =
            raw.Rows
                .Cast<DataRow>()
                .OrderBy(
                    row => SettlementRank(row))
                .ToList();
        int added = 0;
        var listedCharges = charges.Rows.Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => GetLong(row.Cells["BillingChargeId"].Value)).ToHashSet();
        foreach (DataRow row in ordered)
        {
            long chargeId = GetLong(row, "BillingChargeId");
            if (chargeId <= 0)
                continue;
            // Deduplicate charges when siblings are removed and reselected.
            if (!listedCharges.Add(chargeId))
                continue;
            decimal outstanding = GetDecimal(row, "Balance");
            bool additional = string.Equals(GetText(row, "ChargeKind"),
                "Manual", StringComparison.OrdinalIgnoreCase);
            string chargeLabel = GetText(row, "FeeTypeName").Replace(" Fee", "");
            string chargeReason = GetText(row, "Remarks");
            string periodLabel = additional
                ? FormatBillingMonth(row["ChargeDate"])
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
            // Restore with the charge remarks column above.
            // charges.Rows[index].Cells["ChargeRemarks"].Value = chargeReason;
            // charges.Rows[index].Cells["ChargeRemarks"].ToolTipText = chargeReason;
            charges.Rows[index].Cells["AppliedDiscount"].Value =
                GetDecimal(row, "GrossAmount") - GetDecimal(row, "NetAmount");
            charges.Rows[index].Cells["AppliedDiscount"].Tag = charges.Rows[index].Cells["AppliedDiscount"].Value;
            charges.Rows[index].Cells["GrossAmount"].Tag = charges.Rows[index].Cells["AppliedDiscount"].Value;
            charges.Rows[index].Cells["Balance"].Tag = outstanding;
            charges.Rows[index].Cells["Pay"].Value = "+";
            charges.Rows[index].Cells["BillingChargeId"].Value = chargeId;
            charges.Rows[index].Cells["BillingChargeId"].Tag = SettlementRank(row);
            object period = additional ? row["ChargeDate"] : row["BillingMonth"];
            charges.Rows[index].Cells["BillingMonth"].Tag = period == DBNull.Value
                ? DateTime.MinValue : Convert.ToDateTime(period);
            charges.Rows[index].Cells["AdmissionId"].Value = forAdmissionId;
            charges.Rows[index].Cells["EnrollmentId"].Value = GetInt(row, "EnrollmentId");
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
            int priority = Convert.ToInt32(left.Cells["BillingChargeId"].Tag)
                .CompareTo(Convert.ToInt32(right.Cells["BillingChargeId"].Tag));
            if (priority != 0) return priority;
            int period=ChargePeriod(left).CompareTo(ChargePeriod(right));
            return period!=0 ? period : Convert.ToInt64(left.Cells["BillingChargeId"].Value)
                .CompareTo(Convert.ToInt64(right.Cells["BillingChargeId"].Value));
        }
    }
    private static DateTime ChargePeriod(DataGridViewRow row) =>
        row.Cells["BillingMonth"].Tag is DateTime date ? date : DateTime.MinValue;
    /* Show field-specific refusals inline and focus the invalid input. */
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
    /// <summary>Allocate across open charges in settlement order.</summary>
    private decimal FillFromReceivedAmount(decimal received)
    {
        if (received <= 0m)
        {
            /* The grid follows the Paid field. */
            ClearPayNowAmounts();
            if (GetCreditAllocations().Count == 0) Warn("Enter an amount greater than zero.");
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
        var discounts = GetDiscountAllocations();
        return SettlementRows().Sum(row => GetRowNetDue(row, discounts));
    }
    private Dictionary<long, decimal> GetCreditAllocations(Dictionary<long, decimal>? discounts = null)
    {
        var result = new Dictionary<long, decimal>();
        if (!useAvailableCredit.Checked) return result;
        discounts ??= GetDiscountAllocations();
        var remaining = availableCreditByStudent.ToDictionary(pair => pair.Key, pair => Math.Max(0m, pair.Value));
        foreach (var row in SettlementRows())
        {
            int owner = GetInt(row.Cells["AdmissionId"].Value);
            long id = GetLong(row.Cells["BillingChargeId"].Value);
            if (id <= 0 || !remaining.TryGetValue(owner, out decimal credit) || credit <= 0) continue;
            decimal due = Math.Max(0m, GetDecimal(row.Cells["Balance"].Value) - discounts.GetValueOrDefault(id));
            decimal take = Math.Min(due, credit);
            if (take > 0) { result[id] = take; remaining[owner] -= take; }
        }
        return result;
    }
    private decimal SelectedAvailableCredit() => SelectedBillStudents().Sum(id => availableCreditByStudent.GetValueOrDefault(id));
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
    /// <summary>Allocate the received amount in settlement order; totals report any excess.</summary>
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
            var eligible = await DatabaseHelper.QuerySqlAsync("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT S.AdmissionId,
                    CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active') THEN 1 ELSE 0 END AS bit) IsActive,
                    (SELECT TOP(1) B.EnrollmentId FROM dbo.BillingCharges B
                     WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
                     ORDER BY B.EnrollmentId DESC) PendingEnrollmentId
                FROM dbo.Students S WHERE S.AdmissionId IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids));
                """, DatabaseHelper.Int("@UserId",CurrentUser.UserId), DatabaseHelper.P("@Ids",
                    System.Text.Json.JsonSerializer.Serialize(members.AsEnumerable().Select(row=>GetInt(row,"AdmissionId")).ToArray())));
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
                string placement = GetText(row, "ClassName");
                if (!string.IsNullOrWhiteSpace(GetText(row, "SectionName"))) placement += $" - {GetText(row, "SectionName")}";
                // The family read includes EnrollmentId. Without an enrollment there is nothing to collect for that child, so they are shown as unavailable rather than silently omitted.
                string label = $"{AdmissionNumbers.ForId(memberAdmissionId)} · {name}\n" +
                    (memberEnrollmentId > 0 ? $"{placement} · {(active ? "Sibling" : "Pending fees")}" : "No active placement");
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
        siblingHint.Text = siblingBusy ? "Updating selected students…" :
            admissionId > 0 ? $"{1 + siblings.CheckedItems.Count} student(s) included" : message;
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
                Warn($"{info.Name} has no active placement, so there are no fees to collect for them.");
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
                SetSiblingHint("");
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
            SetSiblingHint("" );
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
            var summaries = await DatabaseHelper.QuerySqlAsync("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT P.FeePaymentId,P.TotalAmount,P.Status,P.PaymentMethod,
                    (SELECT SUM(R.TotalAmount) FROM dbo.FeePayments R WHERE R.ReceiptNumber=P.ReceiptNumber AND R.RelatedFeePaymentId IS NULL) ReceiptTotal,
                    (SELECT COUNT(DISTINCT R.AdmissionId) FROM dbo.FeePayments R WHERE R.ReceiptNumber=P.ReceiptNumber AND R.RelatedFeePaymentId IS NULL) ReceiptStudents,
                    C.EntryKind AS CreditKind,
                    ISNULL(A.Refunded,0) Refunded,ISNULL(A.Corrected,0) Corrected
                FROM dbo.FeePayments P
                OUTER APPLY (SELECT TOP(1) EntryKind FROM dbo.StudentCreditEntries
                    WHERE FeePaymentId=P.FeePaymentId ORDER BY CreditEntryId) C
                OUTER APPLY (SELECT
                    -SUM(CASE WHEN X.PaymentMethod=N'Refund' THEN X.TotalAmount ELSE 0 END) Refunded,
                    -SUM(CASE WHEN X.PaymentMethod=N'Correction' THEN X.TotalAmount ELSE 0 END) Corrected
                    FROM dbo.FeePayments X WHERE X.RelatedFeePaymentId=P.FeePaymentId
                    AND X.Status=N'Completed' AND X.TotalAmount<0) A
                WHERE P.EnrollmentId=@EnrollmentId AND P.RelatedFeePaymentId IS NULL;
                """,DatabaseHelper.Int("@UserId",userId),DatabaseHelper.Int("@EnrollmentId",selectedEnrollment));
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
                decimal net = reversed ? 0m : original < 0m ? original : Math.Max(0m,original-refunded-corrected);
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
                string creditKind = GetText(summary, "CreditKind");
                string chargeType = GetText(row, "ChargeType");
                if (!reversed && creditKind.Length > 0)
                {
                    displayTransaction = creditKind switch {
                        "Deposit" => "Credit deposit",
                        "Refund" => "Credit refund",
                        _ => "Credit " + creditKind
                    };
                    chargeType = "Student credit";
                }
                else if (!reversed && original < 0m)
                    displayTransaction = GetText(summary, "PaymentMethod") == "Refund" ? "Refunded" : "Corrected";
                int index = recentPayments.Rows.Add(
                    FormatDate(row, "TransactionDate"),
                    GetText(row, "ReceiptNumber"),
                    chargeType,
                    displayTransaction,
                    original,
                    GetText(row, "PaymentMethod"),
                    GetInt(row, "FeePaymentId"));
                recentPayments.Rows[index].Cells["Adjusted"].Value = original-net;
                recentPayments.Rows[index].Cells["NetPaid"].Value = net;
                if (string.Equals(creditKind, "Application", StringComparison.OrdinalIgnoreCase))
                {
                    var cell = recentPayments.Rows[index].Cells["ReceiptNumber"];
                    cell.Tag = $"FMSC-CR-{Convert.ToDateTime(row["TransactionDate"]):yyyy}-{GetInt(row, "FeePaymentId"):D4}";
                    cell.ToolTipText = "Credit application reference. Existing credit was used; no new money was received.";
                }
                recentPayments.Rows[index].Cells["ReceiptTotal"].Value = GetInt(summary, "ReceiptStudents") > 1 ? summary["ReceiptTotal"] : null;
                if (!reversed && creditKind.Length == 0 && original >= 0 && GetInt(summary, "ReceiptStudents") > 1)
                {
                    recentPayments.Rows[index].Cells["TransactionType"].Value = "Family collection";
                    recentPayments.Rows[index].Cells["Amount"].ToolTipText = "This student's share of the family receipt.";
                    recentPayments.Rows[index].Cells["ReceiptTotal"].ToolTipText = "Full receipt total across all included students.";
                }
                recentPayments.Rows[index].Cells["Adjusted"].ToolTipText = reversed
                    ? $"Reversed: Rs. {original:0.##}"
                    : $"Refunds: Rs. {refunded:0.##}; Corrections: Rs. {corrected:0.##}";
                recentPayments.Rows[index].Cells["Amount"].ToolTipText =
                    $"Student amount: Rs. {original:#,0.##}\nAdjustments: Rs. {original-net:#,0.##}\nNet amount: Rs. {net:#,0.##}\n" +
                    recentPayments.Rows[index].Cells["Adjusted"].ToolTipText;
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
                // Discounts settle the largest remaining fee balance first,
                // across all included students. Equal balances retain the
                // existing settlement order; payment allocation is unchanged.
                .OrderByDescending(x => x.Due)
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
        return Math.Max(0m, due - GetCreditAllocations(discounts).GetValueOrDefault(id));
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
        decimal credit = SelectedAvailableCredit();
        decimal usedCredit = GetCreditAllocations(discountMap).Values.Sum();
        availableCredit.Text = credit.ToString("N0");
        remainingCredit.Text = (credit - usedCredit + Math.Max(0m, GetAmount(paymentAmount) - payment)).ToString("N0");
        if (creditSummary is not null && remainingCreditSummary is not null)
        {
            var layout = (TableLayoutPanel)creditSummary.Parent!;
            creditSummary.Visible = credit > 0;
            layout.RowStyles[2].Height = credit > 0 ? AppScale.Scale(40, AppScale.ControlSizeScale) : 0;
            remainingCreditSummary.Visible = credit > 0 || GetAmount(paymentAmount) > payment;
            layout.RowStyles[7].Height = credit > 0 || GetAmount(paymentAmount) > payment ? AppScale.Scale(40, AppScale.ControlSizeScale) : 0;
            outstandingSummary!.Visible = due > 0 || credit <= 0;
            layout.RowStyles[0].Height = due > 0 || credit <= 0 ? AppScale.Scale(40, AppScale.ControlSizeScale) : 0;
            remainingBalanceSummary!.Visible = remaining > 0 || credit <= 0;
            layout.RowStyles[6].Height = remaining > 0 || credit <= 0 ? AppScale.Scale(40, AppScale.ControlSizeScale) : 0;
        }
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
    private BusyScope HoldSubmittedInputs() => BeginBusy(string.Empty, this);
    private async Task ProcessTransaction()
    {
        if (transactionBusy)
            return;
        try
        {
            var pending = CollectionRecoveryJournal.ReadPending();
            if (pending is not null)
            {
                if (Confirm(
                    "A previous collection was interrupted and may already have been recorded.\n\n" +
                    pending.Summary + $"\nStarted: {pending.StartedUtc.ToLocalTime():dd-MMM-yyyy HH:mm}\n\n" +
                    "Check Payment History and receipts for every student listed above. " +
                    "Have you checked those records and established whether this collection was recorded?\n\n" +
                    "Yes clears this recovery notice and the current entries. It does not record a payment.",
                    "Review Interrupted Collection", defaultYes: false))
                {
                    CollectionRecoveryJournal.ClearReviewed(pending.Id);
                    ClearPayment();
                    Warn("Recovery review acknowledged. Select the student again to load current balances before entering a new collection.");
                }
                return;
            }
        }
        catch (Exception ex) { Fail(ex); return; }
        bool collectionCommitted = false;
        string confirmedReceipt = string.Empty;
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
            if (!string.IsNullOrWhiteSpace(paymentAmount.Text) && !TryParseWholeCollectionAmount(paymentAmount.Text, out _))
            {
                MarkCollectionField(paymentAmount, "Enter a whole amount using digits only; decimal points and fractions are not allowed.");
                return;
            }
            foreach (var input in new[] { discountAmount })
            {
                string raw = input.Text.Trim();
                if (raw.Length > 0 &&
                    (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal entered) || entered < 0m || !WholeRupees.IsWhole(entered)))
                {
                    MarkCollectionField(input, "Enter a whole-rupee amount of zero or more");
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
            /* Allocations grouped by student AND enrolment, not one flat list. dbo.ProcessFeeTransaction is scoped to a single (AdmissionId, EnrollmentId) and writes one FeePayments row per call. */
            var allocationsByStudent =
                new Dictionary<(int AdmissionId, int EnrollmentId), (int EnrollmentId, List<(long chargeId, decimal amount)> Rows)>();
            var receiptLines =
                new List<(
                    string feeType,
                    string billingMonth,
                    decimal nominalAmount,
                    decimal actualAmount)>();
            decimal amount;
            var creditMap = GetCreditAllocations();
            var creditEdits = charges.Rows.Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow && creditMap.ContainsKey(GetLong(row.Cells["BillingChargeId"].Value)))
                .Select(row => new CollectionDiscountWriter.CreditEdit(GetLong(row.Cells["BillingChargeId"].Value),
                    GetInt(row.Cells["AdmissionId"].Value), GetInt(row.Cells["EnrollmentId"].Value),
                    creditMap[GetLong(row.Cells["BillingChargeId"].Value)], GetDecimal(row.Cells["Balance"].Value),
                    availableCreditByStudent.GetValueOrDefault(GetInt(row.Cells["AdmissionId"].Value)))).ToArray();
            decimal creditUsed = creditEdits.Sum(edit => edit.Amount);
            decimal surplusCredit = 0m;
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
                            rowDiscount - creditMap.GetValueOrDefault(chargeId));
                    if (pay < 0m ||
                        pay > netDue)
                    {
                        Warn(
                            "Pay Now cannot exceed the amount remaining after discount and credit.");
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
                                (rowAdmissionId, rowEnrollmentId),
                                out var group))
                        {
                            group = (rowEnrollmentId, new List<(long chargeId, decimal amount)>());
                            allocationsByStudent[(rowAdmissionId, rowEnrollmentId)] = group;
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
                                (discountAdmissionId, discountEnrollmentId),
                                out var discountGroup))
                        {
                            discountGroup = (discountEnrollmentId, new List<(long chargeId, decimal amount)>());
                            allocationsByStudent[(discountAdmissionId, discountEnrollmentId)] = discountGroup;
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
                if (GetAmount(paymentAmount) < amount)
                {
                    MarkCollectionField(paymentAmount, "Received amount cannot be less than the allocated payment.");
                    return;
                }
                surplusCredit = GetAmount(paymentAmount) - amount;
                amount += surplusCredit;
                if (amount <= 0m &&
                    discount <= 0m && creditUsed <= 0m)
                {
                    Warn(
                        "Enter a payment, discount or use available credit.");
                    return;
                }
            }
            /* Those three now run on Finance > Fee Adjustments, so this branch is gone rather than left dormant. */
            /* Money-in only. */
            string description =
                discount > 0m
                    ? $"Collect Rs. {amount:0.##} and apply a discount of Rs. {discount:0.##}."
                    : $"Collect Rs. {amount:0.##}.";
            if (creditUsed > 0m) description += $"\r\nExisting credit applied: Rs. {creditUsed:N0}.";
            if (surplusCredit > 0m)
                description += $"\r\nRs. {surplusCredit:N0} is retained as credit for {receiptStudentName}.";
            /* The payment date is stamped on the receipt and the ledger. */
            description +=
                $"\r\nPayment date: {paymentDate.Value:dd-MMM-yyyy hh:mm tt}   Method: {method.Text}.";
            var confirmationFacts = new List<(string, string)> {
                ("Student", student.Text.Trim()),
                ("Cash / bank received", $"Rs. {amount:#,##0.##}"),
                ("Discount", $"Rs. {discount:#,##0.##}"),
                ("Payment date", paymentDate.Value.ToString("dd-MMM-yyyy hh:mm tt")),
                ("Method", method.Text)
            };
            if (creditUsed > 0m)
            {
                confirmationFacts.Add(("Credit applied", $"Rs. {creditUsed:N0}"));
                confirmationFacts.Add(("Remaining credit", $"Rs. {SelectedAvailableCredit() - creditUsed + surplusCredit:N0}"));
            }
            foreach (DataGridViewRow row in charges.Rows)
            {
                if (row.Cells["GrossAmount"].Tag is decimal oldDiscount &&
                    oldDiscount != GetDecimal(row.Cells["AppliedDiscount"].Value))
                {
                    description += $"\r\n{GetText(row.Cells["FeeTypeName"].Value)} ({GetText(row.Cells["BillingMonth"].Value)}): " +
                        $"bill discount Rs. {oldDiscount:0.##} to Rs. {GetDecimal(row.Cells["AppliedDiscount"].Value):0.##}.";
                    confirmationFacts.Add(($"{GetText(row.Cells["FeeTypeName"].Value)} · {GetText(row.Cells["BillingMonth"].Value)}",
                        $"Bill discount: Rs. {oldDiscount:0.##} → Rs. {GetDecimal(row.Cells["AppliedDiscount"].Value):0.##}"));
                }
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
            var settlingStudents = allocationLines.Select(group => group.Key.AdmissionId)
                .Concat(creditEdits.Select(edit => edit.AdmissionId)).Distinct().OrderBy(id => id).ToArray();
            int payingStudentCount = settlingStudents.Length;
            using var confirmationRows = new DataTable();
            confirmationRows.Columns.Add("Admission No.");
            confirmationRows.Columns.Add("Student");
            confirmationRows.Columns.Add("Payment (Rs.)", typeof(decimal));
            confirmationRows.Columns.Add("Discount (Rs.)", typeof(decimal));
            if (creditUsed > 0m) confirmationRows.Columns.Add("Credit (Rs.)", typeof(decimal));
            if (payingStudentCount > 1)
            {
                description +=
                    $"\r\n\r\nThis is ONE FAMILY COLLECTION: {payingStudentCount} students " +
                    "collected from in a single transaction, under ONE family receipt number " +
                    "that covers every child. Each child's fees stay attributed to that child.";
                foreach (int settlingStudent in settlingStudents)
                {
                    var studentRows = allocationLines.Where(group => group.Key.AdmissionId == settlingStudent).SelectMany(group => group.Value.Rows).ToArray();
                    decimal studentTotal = studentRows
                        .Where(r => r.amount > 0m)
                        .Sum(r => r.amount);
                    decimal studentDiscount = -studentRows
                        .Where(r => r.amount < 0m)
                        .Sum(r => r.amount);
                    string who = WhoIs(settlingStudent);
                    var facts = new List<object> { AdmissionNumbers.ForId(settlingStudent), who, studentTotal, studentDiscount };
                    if (creditUsed > 0m) facts.Add(creditEdits.Where(edit => edit.AdmissionId == settlingStudent).Sum(edit => edit.Amount));
                    confirmationRows.Rows.Add(facts.ToArray());
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
            var confirmMessage = new MessageDialogContent(
                payingStudentCount > 1 ? $"Record payment for {payingStudentCount} students?" : "Record this payment?",
                Explanation: amount == 0m && creditUsed > 0m ? "Uses existing student credit. No new money is received." : payingStudentCount > 1 ? "One family receipt covers this payment. Each child's fees stay under their own record." : null,
                Facts: confirmationFacts,
                Records: payingStudentCount > 1 ? confirmationRows : null,
                Notice: "This records the payment immediately and is final. Check every amount before choosing Yes.");
            if (ModernMessageDialog.Show(
                    FindForm(),
                    confirmMessage,
                    payingStudentCount > 1 ? "Confirm Family Collection" : "Confirm Fee Transaction",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    // BUSINESS RULE: Yes is intentionally the default for fast counter entry after
                    // the operator has reviewed the student, amount and allocation above.
                    MessageBoxDefaultButton.Button1)
                != DialogResult.Yes)
            {
                return;
            }
            using var submittedInputs = HoldSubmittedInputs();
            /* A failure on the third child left the first two children's money committed while the operator was told the collection had failed. */
            DataTable result;
            string recoverySummary = description + "\r\nStudents: " + string.Join(", ",
                allocationLines.Select(group => $"{WhoIs(group.Key.AdmissionId)} [AdmissionId {group.Key}]"));
            var billDiscountEdits = charges.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow && r.Cells["GrossAmount"].Tag is decimal original &&
                    original != GetDecimal(r.Cells["AppliedDiscount"].Value))
                .Select(r => new CollectionDiscountWriter.Edit(GetLong(r.Cells["BillingChargeId"].Value),
                    GetInt(r.Cells["AdmissionId"].Value),GetInt(r.Cells["EnrollmentId"].Value),
                    (decimal)r.Cells["GrossAmount"].Tag!,GetDecimal(r.Cells["AppliedDiscount"].Value),
                    GetDecimal(r.Cells["Balance"].Tag))).ToArray();
            if (allocationLines.Count == 0 && amount == 0m)
            {
                result = await CollectionDiscountWriter.CollectWithCreditAsync(null, billDiscountEdits, creditEdits,
                    paymentDate.Value, recoverySummary);
            }
            else if (allocationLines.Count == 1 && surplusCredit == 0m && SelectedBillStudents().Length == 1)
            {
                var single = allocationLines[0];
                decimal singleAmount = single.Value.Rows
                    .Where(r => r.amount > 0m)
                    .Sum(r => r.amount);
                result =
                    await CollectionDiscountWriter.CollectWithCreditAsync(
                        "ProcessFeeTransaction",
                        billDiscountEdits, creditEdits, paymentDate.Value,
                        recoverySummary,
                        DatabaseHelper.Int("@AdmissionId", single.Key.AdmissionId),
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
                    await CollectionDiscountWriter.CollectWithCreditAsync(
                        "ProcessFamilyFeeTransaction",
                        billDiscountEdits, creditEdits, paymentDate.Value,
                        recoverySummary,
                        DatabaseHelper.P("@ReceivedAmount", amount),
                        DatabaseHelper.Int("@CreditAdmissionId", admissionId),
                        DatabaseHelper.P("@CreditRequestId", Guid.NewGuid()),
                        DatabaseHelper.P("@IncludedStudentsJson", System.Text.Json.JsonSerializer.Serialize(SelectedBillStudents())),
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
                                            g.Key.AdmissionId,
                                            g.Value.EnrollmentId > 0
                                                ? g.Value.EnrollmentId
                                                : enrollmentId,
                                            (IEnumerable<(long chargeId, decimal amount)>)
                                                g.Value.Rows)))
                        });
            }
            collectionCommitted = true;
            totalDueBeforeThisTransaction -= creditUsed;
            lastCollectionReceipt = null;
            lastGroupReceiptNumber = null;
            if (printReceiptButton is not null) printReceiptButton.Enabled = false;
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
            int posted = result.Columns.Contains("AdmissionId") ? result.AsEnumerable().Select(row => GetInt(row,"AdmissionId")).Distinct().Count() : result.Rows.Count;
            /* made a family collection carry ONE receipt number shared by every child, and the procedure returns that same number on every row. The distinct list is the real answer. */
            List<string> distinctReceipts = receipts
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string singleReceipt = distinctReceipts.Count == 1
                ? distinctReceipts[0]
                : string.Empty;
            confirmedReceipt = string.Join(", ", distinctReceipts);
            bool isFamilyCollection = posted > 1;
            bool receiptNumberMissing =
                isFamilyCollection && amount > 0m && singleReceipt.Length == 0;
            if (receiptNumberMissing)
            {
                /* The money is taken and the rows above prove it, so this is not a failure of the collection. */
                Warn(
                    $"Collection recorded for {payingStudentCount} students. Check Recent Payments: " +
                    $"{distinctReceipts.Count} receipt numbers returned; no single family receipt confirmed.");
            }
            else
            {
                Info(
                    receipts.Count == 0
                        ? $"{transactionType} recorded."
                        : isFamilyCollection
                            ? $"{transactionType} recorded for {payingStudentCount} students. Family receipt: {singleReceipt}."
                            : $"{transactionType} recorded. Receipt: {singleReceipt}.");
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
                if (posted > 1 || allocationLines.Count > 1 || surplusCredit > 0m || SelectedBillStudents().Length > 1)
                {
                    lastCollectionReceipt = null;
                    lastGroupReceiptNumber = singleReceipt;
                    if (printReceiptButton is not null)
                        printReceiptButton.Enabled = true;
                    /* Held and re-asserted after the reloads below, because those reload the charges and set their own status text - without this the operator would never see which receipt was issued. */
                    multiStudentReceiptNote =
                        $"Collected Rs. {amount:0.##} across {payingStudentCount} students under family " +
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
            if (collectionCommitted)
            {
                AppExceptionHandler.LogNonFatal(ex, "Refreshing a confirmed fee collection");
                // The old allocation must not remain collectible if only the display
                // failed after commit. Preserve a prepared receipt, but drop stale bills.
                charges.Rows.Clear();
                familyProposal.Rows.Clear();
                string message = "The payment was recorded" +
                    (confirmedReceipt.Length > 0 ? $". Receipt: {confirmedReceipt}" : string.Empty) +
                    ", but the screen could not finish refreshing. Check Payment History before another collection; do not repeat this payment.";
                collectionResult.Text = message; collectionResult.Visible = true;
                Warn(message);
            }
            else if (ex is CollectionOutcomeUnknownException)
            {
                AppExceptionHandler.LogNonFatal(ex, "Unconfirmed fee collection outcome");
                collectionResult.Text = ex.Message; collectionResult.Visible = true;
                Warn(ex.Message);
            }
            else Fail(ex);
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
                Text = $"Payment History — {StudentNameFor(admissionId)} · Admission {AdmissionNumbers.ForId(admissionId)}",
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(AppScale.Scale(1180), AppScale.Scale(520)),
                MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(420)),
                BackColor = AppTheme.PageBackground,
                Font = AppTheme.NormalFont,
                ShowInTaskbar = false
            };
        var card =
            HumanUi.Card(
                $"Payment History · {StudentNameFor(admissionId)} · Admission {AdmissionNumbers.ForId(admissionId)}",
                "Student amounts are shown below. Family totals appear only for shared receipts. Hover over an amount for adjustments; negative amounts are money returned.");
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
    /// <summary>Reverse one charge through dbo.ReverseBillingCharge; SQL decides eligibility, then dependent views reload.</summary>
    private async Task ReviewBillTermsAsync(long chargeId)
    {
        if(IsBusy||!CurrentUser.HasPermission("Fees.Adjust"))return;
        if(!charges.EndEdit())return;
        if(HasDraftBillDiscounts()||HasEnteredAmount(paymentAmount.Text)||!string.IsNullOrWhiteSpace(reference.Text)||!string.IsNullOrWhiteSpace(remarks.Text))
        {Warn("Finish or clear the current payment draft before reviewing bill terms. Your entered payment has been retained.");return;}
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Reviewing bill terms…",this);
            var context=FeeDiscountPeriod.BillContext(chargeId);
            using var form=new ChangeFeeDiscountForm(Convert.ToInt32(context["StudentFeeAssignmentId"]),Convert.ToString(context["FeeTypeName"])??"Fee",
                Convert.ToDecimal(context["StandardAmount"]),Convert.ToDecimal(context["DiscountAmount"]),context);
            form.ShowDialog(FindForm());
            if(!form.Changed)return;
            int version=++loadVersion;
            await Task.WhenAll(LoadChargesAsync(version),LoadSiblingsAsync(version));
            SyncPaymentFieldToGrid();UpdateCollectionTotals();SetStatus("Reviewed bill terms saved. Balances and available credit have been refreshed.");
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
    }
    private async Task ReverseChargeAsync(int rowIndex)
    {
        if (transactionBusy) return;
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
        // Closed enrollment does not make an erroneous unpaid charge permanent.
        // The audited procedure checks payment state and requires a reason.
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
        availableCreditByStudent.Clear();
        useAvailableCredit.Checked = true;
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
    private int[] SelectedBillStudents() => new[] { admissionId }.Concat(siblings.CheckedIndices.Cast<int>()
        .Select(SiblingAdmissionIdAt)).Distinct().OrderBy(id=>id).ToArray();

    private static bool HasEnteredAmount(string text) => !string.IsNullOrWhiteSpace(text)
        && (!decimal.TryParse(text,out var amount) || amount!=0m);

    private bool HasDraftBillDiscounts() => HasEnteredAmount(discountAmount.Text)
        || charges.Rows.Cast<DataGridViewRow>().Where(row=>!row.IsNewRow).Any(row=>
            GetDecimal(row.Cells["AppliedDiscount"].Value)!=GetDecimal(row.Cells["GrossAmount"].Tag));

    private async Task OpenFamilyReturnsAsync()
    {
        if(IsBusy || siblingBusy || admissionId<=0 || !CurrentUser.HasPermission("Students.Family") || !CurrentUser.HasPermission("Students.View"))return;
        if(!charges.EndEdit())return;
        if(HasDraftBillDiscounts() || !string.IsNullOrWhiteSpace(reference.Text) || !string.IsNullOrWhiteSpace(remarks.Text)
            || HasEnteredAmount(paymentAmount.Text))
        {
            Warn("Finish or clear the current payment draft before managing family returns. Your entered payment has been retained.");
            return;
        }
        int anchor=admissionId;
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Reviewing family returns…",this);
            using(var dialog=new ApplicationWindowForm{Text="Family / Returns",StartPosition=FormStartPosition.CenterParent,
                Size=new Size(AppScale.Scale(1150),AppScale.Scale(740)),ShowInTaskbar=false})
            {
                var family=new FMSC_SMS.Pages.Students.Profile.StudentFamilyPage(anchor){Dock=DockStyle.Fill};
                dialog.Controls.Add(family);ModalPageCloseGuard.Attach(dialog,family);
                dialog.ShowDialog(FindForm());
            }
            if(!IsDisposed && admissionId==anchor)await LoadStudentByAdmissionId(anchor);
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
    }

    private async Task PrintSelectedFamilyBillAsync()
    {
        if(IsBusy || siblingBusy || admissionId<=0 || !CurrentUser.HasPermission("Fees.View"))return;
        if(!charges.EndEdit())return;
        if(HasDraftBillDiscounts())
        {
            Warn("The bill uses saved charges. Review the unsaved discount changes before printing; they have not been included.");
            return;
        }
        int anchor=admissionId, placement=enrollmentId, version=loadVersion;
        int[] selected=SelectedBillStudents();
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Preparing outstanding bill…",this);
            await FamilyBillPreview.ShowAsync(FindForm(),anchor,selected,()=>!IsDisposed && admissionId==anchor
                && enrollmentId==placement && loadVersion==version && selected.SequenceEqual(SelectedBillStudents()));
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
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
                "Print the receipt before changing the student or placement.");
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









