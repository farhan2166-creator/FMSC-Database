using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;
/// <summary>Complete student payment history with eligibility-aware adjustment controls.</summary>
public sealed class StudentFeeAdjustmentsPage : V2Page, IBusyPage, IUnsavedChanges
{
    private readonly LatestReadScope detailReads=new();
    private readonly Func<int,int,int,Task<DataTable>> readPaymentDetails;
    private readonly Action<DataTable>? showPaymentDetails;
    private bool recoveryBlocked;
    private MutationIntent? pendingAdjustment;
    private readonly ModernButton checkOutcome;
    private bool adjusting;
    public bool HasUnsavedChanges=>recoveryBlocked;
    public string UnsavedChangesMessage=>"A payment adjustment still needs verification. Its recovery record will be retained when you leave. Check Save Outcome when you return; do not repeat it.";
    public bool IsBusy => adjusting;
    public string BusyMessage => "Please wait for the payment adjustment to finish recording.";
    private readonly StudentPicker student = new();
    private int studentSearchVersion;
    /* invalidates an in-flight payments load. Deliberately separate from studentSearchVersion: a payments reload must not cancel a student search, and a search must not cancel the payments load. */
    private int paymentsLoadVersion;
    private readonly ModernDatePicker transactionDate = HumanUi.Date();
    private readonly ModernComboBox operation = new();
    // Keep persisted transaction codes stable; describe the financial effect to the operator.
    private string SelectedOperation => operation.SelectedItem as string == "Reduce Recorded Payment"
        ? "Correction" : operation.SelectedItem as string ?? string.Empty;
    private static string OperationLabel(string code) => code == "Correction" ? "Reduce Recorded Payment" : code;
    private readonly ModernTextBox actionAmount = new();
    private Label? actionAmountLabel;
    private readonly ModernTextBox reason = new();
    /* Built from the shared HumanUi.Grid and styled with HumanUi.ApplyGridStyle once its columns exist (ConfigurePaymentsGrid). */
    private readonly DataGridView payments = HumanUi.Grid(false);
    private int admissionId;
    private int enrollmentId;
    private int selectedPaymentId;
    private decimal selectedPaymentAmount;
    private (int Admission,int Payment) adjustmentDraftContext;
    private void ResetAdjustmentDraftForSelection()
    {
        var context=(admissionId,selectedPaymentId);
        if(context==adjustmentDraftContext)return;
        detailReads.Cancel();
        adjustmentDraftContext=context;
        actionAmount.Text="0";reason.Clear();transactionDate.Value=DateTime.Today;
    }
    private string selectedStudentName = string.Empty;
    /// <summary>The raw GetFeeStatement result, kept so the selected grid row can be traced back to its EnrollmentId.</summary>
    private DataTable paymentRows = new();
    private ModernButton executeButton = null!;
    private ModernButton detailsButton = null!;
    private bool updatingOperation;
    private readonly Label eligibilityNote=new() { AutoSize=true,Dock=DockStyle.Fill,
        Font=AppTheme.SmallFont,ForeColor=AppTheme.MutedText,Padding=new Padding(6) };
    public StudentFeeAdjustmentsPage() : this(true) { }
    internal StudentFeeAdjustmentsPage(bool initialize) : this(initialize,null,null) { }
    internal StudentFeeAdjustmentsPage(bool initialize,Func<int,int,int,Task<DataTable>>? readPaymentDetails,Action<DataTable>? showPaymentDetails)
        : base(
            PageTitles.StudentFeeCorrections,
            "Review payments, reduce recorded collections, or record refunds.")
    {
        this.readPaymentDetails=readPaymentDetails??ReadPaymentDetailsAsync;
        this.showPaymentDetails=showPaymentDetails;
        Disposed+=(_,_)=>detailReads.Dispose();
        transactionDate.Value = DateTime.Today;
        operation.Items.AddRange(new object[] { OperationLabel("Correction"), "Refund", "Reversal" });
        operation.SelectedIndex = 0;
        operation.SelectedIndexChanged += (_, _) => { if(!updatingOperation) UpdateActionMode(); };
        actionAmount.Width = AppScale.Scale(120);
        reason.Width = AppScale.Scale(300);
        actionAmount.ZeroAsPlaceholder = true;
        ConfigureDecimalInput(actionAmount);
        /* The shared student finder (StudentPicker): type in the box and the list narrows on every keystroke. */
        if(initialize)StudentSearchCache.Warm();
        /* The operator edited the box away from the student this page loaded, so that student is dropped before anything else runs. */
        student.SelectionCleared += async (_, _) => {
            ++studentSearchVersion;
            if (admissionId > 0 || selectedPaymentId > 0 || payments.Rows.Count > 0)
                await LoadSelectedStudentAsync();
            /* The load above can drop the student this page was pointed at (and with it the payment selection), so the action is re-derived once the settled state is on screen. */
            ApplyActionAvailability();
        };
        TrackStudentSearchStatus(student);
        student.UserSelectionChanged += async (_, _) => {
            ++studentSearchVersion;
            if (student.SelectedId <= 0)
                return;
            await LoadSelectedStudentAsync();
        };
        var search = AddAction("Search Students", (_, _) => student.ShowMatches(), "Primary");
        var load = AddBusyAction("Reload Payments", LoadPaymentsAsync, "Secondary");
        /* Correction / Refund / Reversal move money that has already been collected, so a duplicate submission is a duplicate financial transaction. */
        executeButton = AddBusyAction("Execute Adjustment", ExecuteAsync, "Danger");
        detailsButton=AddBusyAction("View Details",ViewPaymentDetailsAsync,"Secondary");
        checkOutcome=AddAction("Check Save Outcome",async(_,_)=>await CheckAdjustmentOutcomeAsync(),"Secondary");
        checkOutcome.Visible=false;
        RelocateFromActions(checkOutcome);
        /* ExecuteAsync checks its own preconditions at the top of its handler - a student must have been loaded and one recorded payment selected. */
        executeButton.EnabledChanged += (_, _) => {
            if (executeButton.Enabled && !ExecuteIsAvailable())
                executeButton.Enabled = false;
        };
        RelocateFromActions(search, load, executeButton, detailsButton);
        ConfigurePaymentsGrid();
        UpdateActionMode();
        // The page opens with no student and no payment chosen.
        ApplyActionAvailability();
        var studentBar = HumanUi.Field("Find Student",student);
        studentBar.Height=AppMetrics.FieldRowHeight;
        var studentCard = HumanUi.Card(
            "Student",
            "Select a student.",
            AppTheme.ModuleAccent("Finance"));
        var studentFields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        studentFields.RowStyles.Add(new RowStyle(SizeType.Absolute, studentBar.Height));
        studentFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        studentFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,AppScale.Scale(160)));
        studentBar.Dock = DockStyle.Fill;
        var searchHost=new Panel { Dock=DockStyle.Fill,Padding=new Padding(8,0,0,8) };
        search.Dock=DockStyle.Bottom;
        searchHost.Controls.Add(search);
        studentFields.Controls.Add(studentBar, 0, 0);
        studentFields.Controls.Add(searchHost,1,0);
        studentCard.Content.Controls.Add(studentFields);
        var paymentLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty };
        paymentLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        paymentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var adjustmentCard=HumanUi.Card("Payment Adjustment","Select a payment, then enter the operation and reason.",AppTheme.ModuleAccent("Finance"));
        var fields=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty };
        for(int i=0;i<4;i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        fields.Controls.Add(HumanUi.Field("Transaction Date",transactionDate),0,0);
        fields.Controls.Add(HumanUi.Field("Operation",operation),1,0);
        var amountField=HumanUi.Field("Amount",actionAmount);
        actionAmountLabel=amountField.Controls.OfType<Label>().Single();
        fields.Controls.Add(amountField,2,0);
        fields.Controls.Add(HumanUi.Field("Reason",reason,true),3,0);
        var adjustmentFields=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty };
        adjustmentFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        adjustmentFields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        adjustmentFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        adjustmentFields.Controls.Add(fields,0,0);
        var adjustmentButtons=HumanUi.ButtonRow(load,detailsButton,executeButton,checkOutcome);
        adjustmentButtons.Dock=DockStyle.Top;
        adjustmentFields.Controls.Add(adjustmentButtons,0,1);
        adjustmentCard.Content.Controls.Add(adjustmentFields);
        var historyCard=HumanUi.Card("Recorded Payments","Select a payment to review or adjust.",AppTheme.ModuleAccent("Finance"));
        payments.Dock=DockStyle.Fill;
        historyCard.Content.Controls.Add(payments);
        paymentLayout.RowCount=3;
        paymentLayout.RowStyles.Clear();
        paymentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,
            adjustmentCard.Padding.Vertical+adjustmentCard.Header.Height+adjustmentCard.Content.Padding.Vertical+
            AppMetrics.FieldRowHeight+AppMetrics.ButtonRowHeight+AppMetrics.SpacingSm*2));
        paymentLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        paymentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        paymentLayout.Controls.Add(adjustmentCard,0,0);
        paymentLayout.Controls.Add(historyCard,0,1);
        paymentLayout.Controls.Add(eligibilityNote,0,2);
        void FitAdjustmentCard()
        {
            int height=adjustmentCard.Padding.Vertical+adjustmentCard.Header.Height+
                adjustmentCard.Content.Padding.Vertical+AppMetrics.FieldRowHeight+
                Math.Max(AppMetrics.ButtonRowHeight,adjustmentButtons.Height+adjustmentButtons.Margin.Vertical)+
                AppMetrics.SpacingSm*2;
            if (Math.Abs(paymentLayout.RowStyles[0].Height-height)>1)
                paymentLayout.RowStyles[0].Height=height;
        }
        adjustmentButtons.SizeChanged+=(_,_)=>FitAdjustmentCard();
        adjustmentButtons.Layout+=(_,_)=>FitAdjustmentCard();
        FitAdjustmentCard();
        paymentLayout.SizeChanged+=(_,_)=>eligibilityNote.MaximumSize=new Size(Math.Max(1,paymentLayout.ClientSize.Width-12),0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            studentBar.Height + studentCard.Padding.Vertical + studentCard.Header.Height + studentCard.Content.Padding.Vertical));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(studentCard, 0, 0);
        root.Controls.Add(paymentLayout, 0, 1);
        SetContent(
            root,
            "Payment History");
        Defer(() => {
            // The page opens with nothing selected and no guidance otherwise, and its whole purpose is the reverse of the collection screen.
            SetStatus(
                "Type an Admission Number or student name. " +
                "Select the student from the results to see their payment history.");
            return Task.CompletedTask;
        });
        if(initialize){ReadAdjustmentRecovery();if(recoveryBlocked)Defer(CheckAdjustmentOutcomeAsync);}

    }
    private static void ConfigureDecimalInput(ModernTextBox box)
    {
        box.KeyPress += (_, e) => {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                e.Handled = true;
            if (e.KeyChar == '.' && box.Text.Contains('.'))
                e.Handled = true;
        };

    }
    private const string NoStudentGridMessage =
        "Select a student above to see the payments recorded for them.";
    private void ConfigurePaymentsGrid()
    {
        // The inherited report grid is not used by this page; the payments grid is the only one, and it is filled by LoadPaymentsAsync.
        Grid.Visible = false;
        payments.Columns.Clear();
        payments.Columns.Add("PaymentId", "PaymentId");
        payments.Columns.Add("Date", "Date");
        payments.Columns.Add("Receipt", "Receipt");
        payments.Columns.Add("Method", "Method");
        payments.Columns.Add("OriginalAmount", "Original (Rs.)");
        payments.Columns.Add("Amount", "Remaining (Rs.)");
        payments.Columns.Add("Status", "Status");
        payments.Columns.Add("AvailableActions","Available Actions");
        payments.Columns.Add("OriginalReceipt","Original Receipt");
        payments.Columns["PaymentId"].Visible = false;
        foreach(string name in new[]{"OriginalAmount","Amount"}) {
        payments.Columns[name].DefaultCellStyle = new DataGridViewCellStyle {
            Format = "#,##0.##",
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        payments.Columns[name].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter;
        }
        payments.AutoGenerateColumns = false;
        payments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        payments.Columns["Date"].FillWeight = 80;
        payments.Columns["Amount"].FillWeight = 80;
        payments.Columns["OriginalAmount"].FillWeight=80;
        payments.Columns["AvailableActions"].FillWeight=140;
        payments.Columns["Receipt"].FillWeight=120;
        payments.Columns["OriginalReceipt"].FillWeight=120;
        payments.Columns["Status"].FillWeight=120;
        foreach(string name in new[]{"Status","AvailableActions","Receipt","OriginalReceipt"})
            payments.Columns[name].DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        payments.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        HumanUi.ApplyGridStyle(payments, AppTheme.Primary);
        UiHelper.SetGridEmptyMessage(payments, NoStudentGridMessage);
        /* The selected payment is half of what ExecuteAsync requires, so every change of selection re-derives the action's state. */
        payments.SelectionChanged += (_, _) => {
            OnPaymentSelected();
            ApplyActionAvailability();
        };
    }
    /// <summary>Everything that follows from the student in the lookup box: the Assignments tab's list and the Payments tab's rows.</summary>
    private async Task LoadSelectedStudentAsync()
    {
        await LoadPaymentsAsync();
    }
    private Task LoadPaymentsAsync() => LoadPaymentsAsync(null, null);

    private async Task LoadPaymentsAsync(string? completionMessage, int? completedAdmissionId)
    {
        int version=++paymentsLoadVersion;
        detailReads.Cancel();
        payments.Rows.Clear();
        selectedPaymentId=0;selectedPaymentAmount=0m;enrollmentId=0;
        UpdateActionMode();
        int chosen=student.SelectedId;
        admissionId=chosen;
        ResetAdjustmentDraftForSelection();
        if(chosen<=0) { UiHelper.SetGridEmptyMessage(payments,NoStudentGridMessage);return; }
        selectedStudentName=student.Text;
        int userId=CurrentUser.UserId;
        try
        {
            var history=await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT P.FeePaymentId,P.EnrollmentId,P.PaymentDate TransactionDate,
                    P.ReceiptNumber,P.PaymentMethod,P.TotalAmount PaidAmount,P.Status,
                    P.RelatedFeePaymentId,O.ReceiptNumber OriginalReceipt,
                    ISNULL(R.Refunded,0) Refunded,ISNULL(R.Corrected,0) Corrected,
                    ISNULL(R.ReleasedToCredit,0) ReleasedToCredit,
                    CONVERT(bit,CASE WHEN P.IsCreditApplication=1 OR EXISTS(
                        SELECT 1 FROM dbo.StudentCreditEntries C WHERE C.FeePaymentId=P.FeePaymentId)
                        THEN 1 ELSE 0 END) IsCreditEntry
                FROM dbo.FeePayments P
                LEFT JOIN dbo.FeePayments O ON O.FeePaymentId=P.RelatedFeePaymentId
                OUTER APPLY (SELECT
                    -SUM(CASE WHEN A.PaymentMethod=N'Refund' THEN A.TotalAmount ELSE 0 END) Refunded,
                    -SUM(CASE WHEN A.PaymentMethod=N'Correction' THEN A.TotalAmount ELSE 0 END) Corrected,
                    -SUM(CASE WHEN A.IsCreditApplication=1 THEN A.TotalAmount ELSE 0 END) ReleasedToCredit
                    FROM dbo.FeePayments A WHERE A.RelatedFeePaymentId=P.FeePaymentId
                    AND A.Status=N'Completed' AND A.TotalAmount<0) R
                WHERE P.AdmissionId=@AdmissionId ORDER BY P.PaymentDate DESC,P.FeePaymentId DESC;
                """,DatabaseHelper.Int("@UserId",userId),DatabaseHelper.Int("@AdmissionId",chosen)));
            if(IsDisposed || version!=paymentsLoadVersion || student.SelectedId!=chosen) return;
            paymentRows=history;
            var orderedPayments=history.AsEnumerable()
                .Select(row=>(Row:row,State:PaymentAdjustmentState.Create(
                    GetText(row,"Status"),GetText(row,"PaymentMethod"),
                    GetDecimal(row,"PaidAmount"),GetInt(row,"RelatedFeePaymentId")>0,
                    GetDecimal(row,"Refunded"),GetDecimal(row,"Corrected"),
                    row["IsCreditEntry"] is bool credit && credit,GetDecimal(row,"ReleasedToCredit"))))
                .OrderBy(entry=>entry.State.Actions.Length==0)
                .ThenByDescending(entry=>GetDate(entry.Row,"TransactionDate"))
                .ThenByDescending(entry=>GetInt(entry.Row,"FeePaymentId"));
            foreach(var entry in orderedPayments)
            {
                DataRow row=entry.Row;
                var state=entry.State;
                int index=payments.Rows.Add(GetInt(row,"FeePaymentId"),
                    GetDate(row,"TransactionDate")?.ToString("dd-MMM-yyyy") ?? "",
                    GetText(row,"ReceiptNumber"),GetText(row,"PaymentMethod"),
                    GetDecimal(row,"PaidAmount"),state.Remaining,state.Status,
                    state.Actions.Length==0 ? "None" : string.Join(", ",state.Actions.Select(action=>
                        action=="Reversal" ? "Reverse" : OperationLabel(action))),
                    GetText(row,"OriginalReceipt"));
                payments.Rows[index].Tag=state;
                payments.Rows[index].Cells["AvailableActions"].ToolTipText=state.Explanation;
            }
            UiHelper.SetGridEmptyMessage(payments,"No payments were found for this student.");
            SetStatus($"{payments.Rows.Count} recorded entries. All payments and adjustments are shown.");
            OnPaymentSelected();
            if (completionMessage is not null && completedAdmissionId == chosen)
                Info(completionMessage);
        }
        catch(Exception ex)
        {
            if(!IsDisposed&&version==paymentsLoadVersion)
            {
                if(completionMessage is not null&&completedAdmissionId==chosen)
                    Warn(completionMessage+" Payment history could not be refreshed. The adjustment is recorded; reload payments before acting further. Do not repeat it because this reload failed. "+DbMessage.UserMessage(ex));
                else Fail(ex);
            }
        }
    }
    private void OnPaymentSelected()
    {
        if (payments.CurrentRow is null || payments.CurrentRow.Index < 0)
        { selectedPaymentId=0;selectedPaymentAmount=0m;enrollmentId=0;ResetAdjustmentDraftForSelection();UpdateActionMode();return; }
        DataGridViewRow row = payments.CurrentRow;
        selectedPaymentId = Convert.ToInt32(row.Cells["PaymentId"].Value ?? 0);
        selectedPaymentAmount = Convert.ToDecimal(row.Cells["Amount"].Value ?? 0m);
        // Resolve the enrollment for the selected payment. ProcessFeeTransaction is scoped to (AdmissionId, EnrollmentId), so posting with the wrong enrollment would either fail or act on the wrong placement.
        enrollmentId = paymentRows.AsEnumerable()
            .Where(r => GetInt(r, "FeePaymentId") == selectedPaymentId)
            .Select(r => GetInt(r, "EnrollmentId"))
            .FirstOrDefault();
        ResetAdjustmentDraftForSelection();
        UpdateActionMode();
    }
    /// <summary>Each operation needs a different amount rule. Reversal is the payment's full amount and is therefore locked; Correction and Refund are entered by the operator.</summary>
    private void UpdateActionMode()
    {
        var state=payments.CurrentRow?.Tag as PaymentAdjustmentState;
        string previous=SelectedOperation;
        updatingOperation=true;
        try {
            operation.Items.Clear();
            operation.Items.AddRange(state?.Actions.Select(action=>(object)OperationLabel(action)).ToArray() ?? Array.Empty<object>());
            if(operation.Items.Count>0) operation.SelectedIndex=Math.Max(0,operation.Items.IndexOf(OperationLabel(previous)));
        }
        finally { updatingOperation=false; }
        operation.Enabled=state?.Actions.Length>0;
        bool available=state?.Actions.Contains(SelectedOperation)==true;
        actionAmount.Enabled=reason.Enabled=transactionDate.Enabled=available;
        bool reversal = SelectedOperation == "Reversal";
        if(actionAmountLabel is not null)actionAmountLabel.Text=SelectedOperation switch
        {"Correction"=>"Amount to keep as paid","Refund"=>"Amount to refund","Reversal"=>"Whole payment to undo",_=>"Amount"};
        actionAmount.ReadOnly = reversal;
        if (reversal)
            actionAmount.Text = selectedPaymentAmount.ToString("0.##");
        else if (string.IsNullOrWhiteSpace(actionAmount.Text))
            actionAmount.Text = "0";
        string hint = SelectedOperation switch {
            "Correction" =>
                "Enter the amount that should remain recorded as collected. The reduction is added back to outstanding dues. " +
                "For example, reducing Rs. 1,000 to Rs. 800 increases outstanding by Rs. 200. Enter 0 to remove the whole collection.",
            "Refund" =>
                "Refund gives back part of the selected payment. It must be more than 0 " +
                "and less than the amount collected.",
            _ =>
                "Reversal undoes the entire selected payment and cannot be partial."
        };
        eligibilityNote.Text=state is null ? "Select a recorded payment." : available
            ? $"Remaining collected: Rs. {state.Remaining:0.##}. {hint} {state.Explanation}" : state.Explanation;
        var help=SelectedOperation switch {"Refund"=>FinanceHelp.Topic.RefundPayment,"Reversal"=>FinanceHelp.Topic.ReversePayment,_=>FinanceHelp.Topic.ReducePayment};
        FinanceHelp.Attach(this,operation,help);
        FinanceHelp.Attach(this,actionAmount,help);
        FinanceHelp.Attach(this,executeButton,help);
        ApplyActionAvailability();
    }
    /// <summary>The two preconditions ExecuteAsync checks at the top of its handler before it will do anything: a student must have been loaded (admissionId) and one recorded payment must be selected (selectedPaymentId).</summary>
    private bool ExecuteIsAvailable() =>
        !recoveryBlocked&&admissionId > 0 && selectedPaymentId > 0 && payments.CurrentRow?.Tag is PaymentAdjustmentState state &&
        state.Actions.Contains(SelectedOperation);
    /// <summary>derives the enabled state of Execute Adjustment from the current selection, so an unavailable action is not offered as a normal operation.</summary>
    private void ApplyActionAvailability()
    {
        if (executeButton is null)
            return;
        executeButton.Enabled = ExecuteIsAvailable();
        if(detailsButton is not null) detailsButton.Enabled=selectedPaymentId>0;
        if(checkOutcome is not null){checkOutcome.Visible=recoveryBlocked;checkOutcome.Enabled=recoveryBlocked&&!adjusting;}
    }
    private async Task ExecuteAsync()
    {
        if (adjusting||recoveryBlocked) return;
        ++paymentsLoadVersion;detailReads.Cancel();
        adjusting = true;
        using var hold = new BusyScope(string.Empty,
            new Control[] { student, payments, operation, actionAmount, reason, transactionDate, detailsButton },
            () => string.Empty, () => PageStatusKind.Info, (_, _) => { });
        try
        {
            if (admissionId <= 0)
            {
                Warn("Search for and select a student first.");
                return;
            }
            if (!ExecuteIsAvailable())
            {
                Warn("Select the recorded payment to adjust, then try again.");
                return;
            }
            if (reason.Text.Trim().Length == 0)
            {
                Warn("Enter a reason. These actions change money already collected, " +
                     "and the reason is recorded with the transaction.");
                reason.Focus();
                return;
            }
            string transactionType = SelectedOperation;
            decimal amount;
            if (transactionType == "Reversal")
            {
                DataRow? selected = paymentRows.AsEnumerable().FirstOrDefault(row => GetInt(row, "FeePaymentId") == selectedPaymentId);
                if (selected is not null && selectedPaymentAmount < GetDecimal(selected, "PaidAmount"))
                {
                    Warn("This payment has already been partially reduced. Use Reduce Recorded Payment with a final amount of 0 to undo the remaining collection.");
                    return;
                }
                amount = selectedPaymentAmount;
                if (amount <= 0m)
                {
                    Warn("The selected payment has no valid amount to reverse.");
                    return;
                }
            }
            else
            {
                if (!decimal.TryParse(actionAmount.Text.Trim(), out amount))
                {
                    Warn("Enter a valid amount.");
                    actionAmount.Focus();
                    return;
                }
                if (transactionType == "Refund" && amount <= 0m)
                {
                    Warn("A refund must be greater than zero.");
                    actionAmount.Focus();
                    return;
                }
                if (transactionType == "Refund" && amount >= selectedPaymentAmount)
                {
                    Warn($"A refund must be less than the Rs. {selectedPaymentAmount:N0} collected. " +
                         "To remove the remaining collection, use Reduce Recorded Payment with a final amount of 0.");
                    actionAmount.Focus();
                    return;
                }
                /* A correction states the FINAL collected amount, so it must strictly reduce what was collected. */
                if (transactionType == "Correction" && (amount<0m || amount >= selectedPaymentAmount))
                {
                    Warn($"Enter a final amount of Rs. 0 or more, below Rs. {selectedPaymentAmount:N0}. " +
                         "Use Reversal to undo the whole payment.");
                    actionAmount.Focus();
                    return;
                }
            }
            string question = transactionType switch {
                "Correction" => $"Reduce the recorded payment to Rs. {amount:0.##}?",
                "Refund" => $"Refund Rs. {amount:0.##}?",
                _ => $"Reverse the full payment of Rs. {amount:0.##}?"
            };
            var confirmation = new MessageDialogContent(
                question,
                Explanation: transactionType == "Correction"
                    ? $"Outstanding dues will increase by Rs. {selectedPaymentAmount-amount:0.##}. No cash refund is recorded by this action." : null,
                Facts: new (string, string)[] {
                    ("Student", selectedStudentName),
                    ("Payment", selectedPaymentId.ToString()),
                    ("Currently recorded", $"Rs. {selectedPaymentAmount:0.##}"),
                    ("Date", transactionDate.Value.ToString("d")),
                    ("Reason", reason.Text.Trim())
                },
                Notice: "This changes recorded money and cannot be undone. Check the payment and amount before choosing Yes.");
            if (ModernMessageDialog.Show(
                    FindForm(),
                    confirmation,
                    $"Confirm {OperationLabel(transactionType)}",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            var emptyAllocations = new SqlParameter("@Allocations", SqlDbType.Structured) {
                TypeName = "dbo.FeePaymentAllocationType",
                Value = Tvp.FeeAllocations(Array.Empty<(long, decimal)>())
            };
            DataTable? result = await SubmitAdjustmentAsync($"{OperationLabel(transactionType)} · Payment {selectedPaymentId} · Rs. {amount:N0} · {transactionDate.Value:dd MMM yyyy}",
                DatabaseHelper.Int("@AdmissionId", admissionId),
                DatabaseHelper.Int("@EnrollmentId", enrollmentId),
                DatabaseHelper.P("@TransactionType", transactionType),
                DatabaseHelper.P("@Amount", amount),
                DatabaseHelper.Date("@TransactionDate", transactionDate.Value),
                DatabaseHelper.P("@PaymentMethod", null),
                DatabaseHelper.P("@ReferenceNumber", null),
                DatabaseHelper.P("@Remarks", reason.Text.Trim()),
                DatabaseHelper.NullableInt("@FeePaymentId", selectedPaymentId),
                new SqlParameter("@BillingChargeId", SqlDbType.BigInt) { Value = DBNull.Value },
                emptyAllocations);
            if(result is null)return;
            string receipt = result.Rows.Count > 0 && result.Columns.Contains("ReceiptNumber")
                ? GetText(result.Rows[0], "ReceiptNumber")
                : string.Empty;
            string completionMessage = transactionType switch {
                "Correction" => $"Recorded payment reduced to Rs. {amount:0.##}. Outstanding increased by Rs. {selectedPaymentAmount-amount:0.##}.",
                "Refund" => $"Refund of Rs. {amount:0.##} recorded.",
                _ => $"Payment of Rs. {amount:0.##} reversed."
            };
            if (!string.IsNullOrWhiteSpace(receipt))
                completionMessage += $" Receipt: {receipt}.";
            // Clear the selection BEFORE reloading, so the amount box and the confirmation summary cannot still be describing the payment that was just adjusted.
            reason.Clear();
            selectedPaymentId = 0;
            selectedPaymentAmount = 0m;
            enrollmentId = 0;
            actionAmount.Text = "0";
            actionAmount.ReadOnly = false;
            await LoadPaymentsAsync(completionMessage, admissionId);
        }
        catch (Exception ex)
        {
            ReadAdjustmentRecovery();
            if(recoveryBlocked)Warn("The adjustment may already be recorded. Use Check Save Outcome before submitting another adjustment. "+DbMessage.UserMessage(ex));else Fail(ex);
        }
        finally { adjusting = false;if(!IsDisposed)ApplyActionAvailability(); }
    }
    private void ReadAdjustmentRecovery()
    {
        bool wasBlocked=recoveryBlocked;
        try
        {
            pendingAdjustment??=MutationRecoveryJournal.ReadPending(MutationKind.FeeAdjustment);recoveryBlocked=pendingAdjustment is not null;
            if(wasBlocked&&!recoveryBlocked)
            {
                selectedPaymentId=0;selectedPaymentAmount=0m;enrollmentId=0;payments.ClearSelection();payments.CurrentCell=null;
                SetStatus("Another window resolved the recovery record. Reload payments before choosing another adjustment.",PageStatusKind.Warning);
            }
        }
        catch(Exception ex){recoveryBlocked=true;SetStatus("Retain the adjustment recovery record for review. Do not repeat the adjustment. "+DbMessage.UserMessage(ex),PageStatusKind.Warning);}
        ApplyActionAvailability();
        if(pendingAdjustment is not null)SetStatus(pendingAdjustment.Summary+" · Outcome needs checking. Use Check Save Outcome.",PageStatusKind.Warning);
    }
    private async Task<DataTable?> SubmitAdjustmentAsync(string summary,params SqlParameter[] args)
    {
        int user=CurrentUser.UserId;string scope=await RecoverableMutationWriter.GetScopeAsync();
        if(IsDisposed||CurrentUser.UserId!=user)throw new InvalidOperationException("The adjustment page or signed-in operator changed before submission.");
        MutationWriteResult outcome;MutationIntent intent;
        using(var journal=MutationRecoveryJournal.Begin(MutationKind.FeeAdjustment,user,scope,RecoverableMutationWriter.PayloadHash(MutationKind.FeeAdjustment,user,args),summary))
        {
            intent=journal.Intent;pendingAdjustment=intent;recoveryBlocked=true;ApplyActionAvailability();
            outcome=await RecoverableMutationWriter.SaveAsync(intent,args);
        }
        if(outcome.Outcome==MutationOutcome.Unconfirmed){SetStatus("The adjustment outcome is unknown. Use Check Save Outcome; do not repeat it.",PageStatusKind.Warning);return null;}
        MutationRecoveryJournal.ClearVerified(intent);pendingAdjustment=null;recoveryBlocked=false;ApplyActionAvailability();
        if(outcome.Outcome==MutationOutcome.Rejected){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(outcome.Error!).Throw();}
        LookupDataCache.OperationCompleted("ProcessFeeTransaction");
        return outcome.Result!.ToTable();
    }
    private async Task CheckAdjustmentOutcomeAsync()
    {
        if(adjusting||IsDisposed)return;ReadAdjustmentRecovery();if(pendingAdjustment is null)return;
        adjusting=true;ApplyActionAvailability();
        MutationResult? verified=null;bool checkedOutcome=false;
        try
        {
            var intent=pendingAdjustment;var saved=await RecoverableMutationWriter.CheckAsync(intent);verified=saved;checkedOutcome=true;
            MutationRecoveryJournal.ClearVerified(intent);pendingAdjustment=null;recoveryBlocked=false;
            selectedPaymentId=0;selectedPaymentAmount=0m;enrollmentId=0;payments.ClearSelection();payments.CurrentCell=null;
            if(saved is null){SetStatus("The earlier adjustment was not recorded. Reload payments and deliberately submit it again only if still needed.",PageStatusKind.Warning);return;}
            LookupDataCache.OperationCompleted("ProcessFeeTransaction");
            reason.Clear();actionAmount.Text="0";
            var found=await StudentSearchCache.FindByAdmissionIdAsync(saved.SubjectId);
            if(IsDisposed)return;
            if(found is not null)student.SetStudent(found);else student.SetStudent(saved.SubjectId,"Recovered student");
            string message=$"Earlier {saved.TransactionType.ToLowerInvariant()} recorded."+(string.IsNullOrWhiteSpace(saved.Receipt)?"":$" Receipt: {saved.Receipt}.");
            await LoadPaymentsAsync(message,saved.SubjectId);
        }
        catch(Exception ex){if(!IsDisposed)Warn((checkedOutcome?(verified is null?"The earlier adjustment was not recorded, but recovery cleanup failed. Reopen this page before submitting again. ":"The earlier adjustment is recorded, but the screen or recovery file could not be refreshed. Do not repeat it. Reopen this page or reload payments. "):"The adjustment outcome is still unresolved. Keep recovery and do not submit it again. ")+DbMessage.UserMessage(ex));}
        finally{adjusting=false;if(!IsDisposed)ApplyActionAvailability();}
    }
    private async Task ViewPaymentDetailsAsync()
    {
        int paymentId=selectedPaymentId, studentId=admissionId, userId=CurrentUser.UserId;
        if(paymentId<=0) return;
        var selected=paymentRows.AsEnumerable().FirstOrDefault(row=>GetInt(row,"FeePaymentId")==paymentId);
        if(selected is null) return;
        string receipt=GetText(selected,"ReceiptNumber");
        var state=payments.CurrentRow?.Tag as PaymentAdjustmentState;
        var request=detailReads.Begin();
        DataTable data;
        try{data=await readPaymentDetails(userId,paymentId,studentId);}
        catch(Exception ex){if(!IsDisposed&&detailReads.IsCurrent(request)&&admissionId==studentId&&selectedPaymentId==paymentId&&CurrentUser.UserId==userId)Fail(ex);return;}
        if(IsDisposed||!detailReads.IsCurrent(request)||admissionId!=studentId||selectedPaymentId!=paymentId||CurrentUser.UserId!=userId)return;
        if(showPaymentDetails is not null){showPaymentDetails(data);return;}
        using var dialog=new ApplicationWindowForm {
            Text="Payment Details",StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(AppScale.Scale(900),AppScale.Scale(420)),ShowInTaskbar=false
        };
        var grid=CreatePaymentDetailsGrid(data);
        var card=HumanUi.Card(receipt,$"{state?.Status}. {state?.Explanation}",AppTheme.ModuleAccent("Finance"));
        card.Dock=DockStyle.Fill;card.Content.Controls.Add(grid);
        var close=new ModernButton { Text=UiText.Actions.Close,DialogResult=DialogResult.OK,ThemeRole="Secondary" };
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(card,0,0);layout.Controls.Add(HumanUi.ButtonRow(close),0,1);
        dialog.Controls.Add(layout);dialog.CancelButton=close;
        dialog.ShowDialog(FindForm());
    }
    private static DataGridView CreatePaymentDetailsGrid(DataTable data)
    {
        var grid=HumanUi.Grid(false);
        grid.Dock=DockStyle.Fill;
        grid.AutoGenerateColumns=false;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        // Define columns before binding: an unparented grid has no BindingContext
        // yet, so auto-generated columns are not available for immediate styling.
        foreach(var (name,header,weight) in new[] {
            ("Fee",UiText.Fees.FeeType,30f), ("Billing Month","Billing Month",20f),
            ("Amount (Rs.)",UiText.Fees.Amount,20f), ("Reason","Reason",30f) })
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name=name,DataPropertyName=name,HeaderText=header,FillWeight=weight
            });
        grid.Columns["Billing Month"].DefaultCellStyle.Format="MMM-yyyy";
        grid.Columns["Amount (Rs.)"].DefaultCellStyle.Format="#,##0.##";
        grid.Columns["Amount (Rs.)"].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
        grid.Columns["Amount (Rs.)"].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleCenter;
        grid.DataSource=data;
        return grid;
    }
    private static Task<DataTable> ReadPaymentDetailsAsync(int userId,int paymentId,int studentId)
    {
        return Task.Run(()=>DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT F.FeeTypeName [Fee],B.BillingMonth [Billing Month],A.Amount [Amount (Rs.)],
                P.Remarks [Reason]
            FROM dbo.FeePayments P
            JOIN dbo.FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
            JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
            JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
            WHERE P.FeePaymentId=@PaymentId AND P.AdmissionId=@AdmissionId
            ORDER BY A.FeePaymentAllocationId;
            """,DatabaseHelper.Int("@UserId",userId),DatabaseHelper.Int("@PaymentId",paymentId),
            DatabaseHelper.Int("@AdmissionId",studentId)));
    }
    private static int GetInt(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToInt32(row[column])
            : 0;
    private static decimal GetDecimal(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToDecimal(row[column])
            : 0m;
    private static string GetText(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToString(row[column]) ?? string.Empty
            : string.Empty;
    private static DateTime? GetDate(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToDateTime(row[column])
            : null;
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
