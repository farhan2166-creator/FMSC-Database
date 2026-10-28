using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;

namespace FMSC_SMS.Pages.Reports.Finance;

public sealed partial class StudentAccountStatementPage
{
    private readonly bool accountMode;
    private readonly LatestReadScope accountReads=new();
    internal Func<int,Task<StudentFeeAccountData>> ReadAccount=StudentFeeAccountData.ReadAsync;
    private readonly DataGridView accountReceipts=HumanUi.Grid(false),accountPayments=HumanUi.Grid(false);
    private readonly DataGridView accountTransactions=HumanUi.Grid(false);
    private readonly DataGridView accountCredits=HumanUi.Grid(false);
    private TabControl? statementViews;
    private bool accountActionBusy;
    private sealed class ReceiptDetailsHost : TableLayoutPanel,IBusyPage
    {
        internal bool Printing;
        public bool IsBusy=>Printing;
        public string BusyMessage=>"The original receipt preview is being prepared. Wait for it before closing.";
    }
    private ModernButton? feeChanges,relatedPayment,recoverPayment;
    private readonly Label accountSummary=new(){AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6),Font=AppTheme.ButtonFont};
    private TabControl? accountViews;
    private ModernButton? accountFamily,receiptDetails,paymentDetails,correctPayment,entryDetails;
    private string accountSignature="";
    private Dictionary<int,string> accountStudentNames=new();
    private int accountUser;

    internal static bool CanOpenAccount=>CurrentUser.HasPermission("Reports.View")||CurrentUser.HasPermission("Fees.Collect");
    public static StudentAccountStatementPage CreateAccount(int admissionId=0)
    {
        var page=new StudentAccountStatementPage(true,null,false,true);
        if(admissionId>0)page.Defer(()=>page.PreloadAdmissionIdAsync(admissionId));
        return page;
    }

    private void InitializeAccount()
    {
        accountFamily=CreateFilterButton("Include Siblings",AppTheme.MutedText);
        accountFamily.Click+=async(_,_)=>{
            if(IsBusy||studentSearch.SelectedId<=0)return;
            if(familySelections.Count>0){SelectSingleStudent();accountFamily.Text="Include Siblings";}
            else{SelectFamilyForStudent(studentSearch.SelectedId);accountFamily.Text=familySelections.Count>0?"Student Only":"Include Siblings";OnFiltersChanged();}
            await LoadStatement();
        };
        // Reuse the related-report strip; its existing wrapping/DPI arrangement is retained.
        if(familyGroupButton?.Parent is FlowLayoutPanel reports)
        {
            var menu=new ContextMenuStrip();
            var groups=menu.Items.Add("Family / Group Statements…",null,(_,_)=>OpenFamilyGroupStatements());
            var due=menu.Items.Add("Outstanding Bills…",null,async(_,_)=>await PrintDueReport());
            var annual=menu.Items.Add("Annual Receipts…",null,async(_,_)=>await OpenAnnualReceipts());
            menu.Opening+=(_,_)=>{groups.Enabled=familyGroupButton.Enabled;due.Enabled=printAmountsDueButton?.Enabled==true;annual.Enabled=annualReceiptsButton?.Enabled==true;};
            foreach(var button in new[]{familyGroupButton,printAmountsDueButton,annualReceiptsButton})if(button is not null)reports.Controls.Remove(button);
            var related=CreateFilterButton("Reports…",AppTheme.MutedText);related.Click+=(_,_)=>menu.Show(related,new Point(0,related.Height));
            reports.Controls.Add(accountFamily);reports.Controls.Add(related);
            Disposed+=(_,_)=>{menu.Dispose();familyGroupButton.Dispose();printAmountsDueButton?.Dispose();annualReceiptsButton?.Dispose();};
            arrangeStatement?.Invoke();
        }
        Grid.SelectionChanged+=(_,_)=>UpdateAccountActions();
        if(!CurrentUser.HasPermission("Reports.View"))
        {
            accountViews!.SelectedIndex=1;
            from.Enabled=to.Enabled=false;exportButton.Visible=false;
            if(generateStatementButton is not null)generateStatementButton.Text="View Account";
        }
        InvalidateAccount();
    }

    private Control BuildAccountViews()
    {
        accountViews=new TabControl {Dock=DockStyle.Fill,Name="accountViews",AccessibleName="Account views"};
        accountReceipts.Name="accountReceipts";
        var statement=new TabPage("Statement");
        entryDetails=CreateFilterButton("Entry Details",AppTheme.MutedText);
        entryDetails.Click+=async(_,_)=>await AccountActionAsync(ShowStatementEntryAsync);
        feeChanges=CreateFilterButton("Fee Change History…",AppTheme.MutedText);
        feeChanges.Click+=async(_,_)=>await AccountActionAsync(async()=>{
            int admission=SelectedStatementAdmission();if(admission<=0)return;
            await StudentFeeChangeHistory.ShowAsync(this,()=>SelectedStatementAdmission());
        });
        relatedPayment=CreateFilterButton("Related Payment…",AppTheme.MutedText);
        relatedPayment.Click+=(_,_)=>{
            if(!AccountCurrent)return;
            var entry=SelectedAccountRow(statementViews?.SelectedIndex==1?accountTransactions:Grid);
            if(entry is null)return;
            accountViews!.SelectedIndex=1;
            ((TabControl)accountViews.TabPages[1].Controls[0]).SelectedIndex=1;
            SelectAccountPayment(StudentFeeAccountData.Number(entry,"FeePaymentId"));
            accountPayments.Focus();UpdateAccountActions();
        };
        statementViews=new TabControl {Dock=DockStyle.Fill,AccessibleName="Statement presentation"};
        var balances=new TabPage("Fee Balances");balances.Controls.Add(Grid);
        var chronological=new TabPage("Chronological Entries");chronological.Controls.Add(accountTransactions);
        accountTransactions.Dock=DockStyle.Fill;accountTransactions.DataBindingComplete+=(_,_)=>StyleAccountGrid(accountTransactions);
        accountTransactions.SelectionChanged+=(_,_)=>UpdateAccountActions();
        accountTransactions.CellFormatting+=(_,e)=>{
            if(e.RowIndex>=0&&e.ColumnIndex>=0&&accountTransactions.Columns[e.ColumnIndex].Name=="Balance"
                &&accountTransactions.Rows[e.RowIndex].DataBoundItem is DataRowView row&&StudentFeeAccountData.Text(row.Row,"TransactionType")=="PAYMENT")
            {e.Value="";e.FormattingApplied=true;}
        };
        statementViews.TabPages.AddRange(new[]{balances,chronological});
        statementViews.SelectedIndexChanged+=(_,_)=>{exportButton.Visible=statementViews.SelectedIndex==0;UpdateAccountActions();};
        var statementBody=AccountLayout(statementViews,HumanUi.ButtonRow(entryDetails,relatedPayment,feeChanges));
        statement.Controls.Add(statementBody);
        var receipts=new TabPage("Receipts");
        var receiptViews=new TabControl {Dock=DockStyle.Fill,AccessibleName="Receipt and payment views"};
        receiptDetails=CreateFilterButton("Open Receipt",AppTheme.MutedText);
        receiptDetails.Click+=async(_,_)=>await AccountActionAsync(OpenAccountReceiptAsync);
        var documents=new TabPage("Issued Documents");
        documents.Controls.Add(AccountLayout(accountReceipts,HumanUi.ButtonRow(receiptDetails)));
        paymentDetails=CreateFilterButton("Fee Breakdown",AppTheme.MutedText);
        paymentDetails.Click+=async(_,_)=>await AccountActionAsync(ShowAccountPaymentDetailsAsync);
        correctPayment=CreateFilterButton("Review Correction…",AppTheme.MutedText);
        correctPayment.Click+=async(_,_)=>await AccountActionAsync(()=>ReviewAccountPaymentAsync(SelectedAccountRow(accountPayments)));
        recoverPayment=CreateFilterButton("Check Save Outcome…",AppTheme.MutedText);
        recoverPayment.Click+=async(_,_)=>await AccountActionAsync(async()=>{
            if(!CanOpenAccount||!CurrentUser.HasPermission("Fees.Adjust"))return;
            using(var dialog=CreateRelatedReportDialog("Payment Save Recovery",PaymentCorrectionReview.CreateRecoveryReview()))dialog.ShowDialog(FindForm());
            if(!IsDisposed)await LoadStatement();
        });
        var payments=new TabPage("Payments & Adjustments");
        payments.Controls.Add(AccountLayout(accountPayments,HumanUi.ButtonRow(paymentDetails,correctPayment,recoverPayment)));
        var credit=new TabPage("Account Credit");accountCredits.Dock=DockStyle.Fill;
        accountCredits.DataBindingComplete+=(_,_)=>StyleAccountGrid(accountCredits);credit.Controls.Add(accountCredits);
        receiptViews.TabPages.AddRange(new[]{documents,payments,credit});receipts.Controls.Add(receiptViews);
        accountViews.TabPages.AddRange(new[]{statement,receipts});
        accountViews.Selecting+=(_,e)=>{if(e.TabPageIndex==0&&!CurrentUser.HasPermission("Reports.View"))e.Cancel=true;};
        accountViews.SelectedIndexChanged+=(_,_)=>{exportButton.Visible=CurrentUser.HasPermission("Reports.View")&&accountViews.SelectedIndex==0&&statementViews.SelectedIndex==0;UpdateAccountActions();};
        foreach(var grid in new[]{accountReceipts,accountPayments})
        {
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.MultiSelect=false;grid.AutoGenerateColumns=true;
            grid.SelectionChanged+=(_,_)=>UpdateAccountActions();
            grid.DataBindingComplete+=(_,_)=>StyleAccountGrid(grid);
        }
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(accountSummary,0,0);root.Controls.Add(accountViews,0,1);
        return root;
    }

    private static Control AccountLayout(Control content,Control actions)
    {
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(4)};
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content,0,0);actions.Dock=DockStyle.Fill;root.Controls.Add(actions,0,1);return root;
    }

    private bool AccountCurrent=>!IsDisposed&&accountSignature.Length>0&&accountSignature==FilterSignature()
        &&accountUser==CurrentUser.UserId&&CanOpenAccount&&CurrentUser.HasPermission("Fees.View");
    private static DataRow? SelectedAccountRow(DataGridView grid)=>(grid.CurrentRow?.DataBoundItem as DataRowView)?.Row;
    private async Task AccountActionAsync(Func<Task> action)
    {
        if(IsBusy)return;accountActionBusy=true;
        try{await action();}catch(Exception ex){if(!IsDisposed)Fail(ex);}finally{accountActionBusy=false;if(!IsDisposed){UpdateSelectionActions();UpdateAccountActions();}}
    }

    private void InvalidateAccount(bool clearTransactions=true)
    {
        accountReads.Cancel();accountSignature="";
        accountReceipts.DataSource=null;accountPayments.DataSource=null;
        accountCredits.DataSource=null;
        if(clearTransactions){accountTransactions.DataSource=null;accountStudentNames.Clear();}
        accountSummary.Text="Select a student and view their account. Current dues and available credit are shown separately.";
        if(accountFamily is not null){accountFamily.Enabled=studentSearch.SelectedId>0;accountFamily.Text=familySelections.Count>0?"Student Only":"Include Siblings";}
        UpdateAccountActions();
    }

    private async Task RefreshAccountAsync()
    {
        InvalidateAccount(false);
        if(!CanOpenAccount||!CurrentUser.HasPermission("Fees.View"))
        {accountSummary.Text="Receipt, payment and current credit details require Fees.View permission. Statement access is unchanged.";return;}
        var request=accountReads.Begin();string signature=FilterSignature();int user=CurrentUser.UserId;
        var ids=GetSelectedAdmissionIds();var receipts=new DataTable();var payments=new DataTable();var credits=new DataTable();
        decimal outstanding=0,credit=0;
        try
        {
            foreach(int id in ids)
            {
                var data=await ReadAccount(id);
                using(data.Receipts)using(data.Payments)
                using(data.CreditEntries)
                {
                    if(IsDisposed||!accountReads.IsCurrent(request)||signature!=FilterSignature()||user!=CurrentUser.UserId)return;
                    if(!accountStudentNames.ContainsKey(id)&&data.Payments.Rows.Count>0)
                        accountStudentNames[id]=StudentFeeAccountData.Text(data.Payments.Rows[0],"StudentName");
                    MergeAccountRows(receipts,data.Receipts,id);MergeAccountRows(payments,data.Payments,id);
                    if(data.CreditEntries is not null)MergeAccountRows(credits,data.CreditEntries,id);
                    outstanding+=data.Outstanding;credit+=data.Credit;
                }
            }
            // A family receipt appears once. Its saved participant breakdown remains intact on opening it.
            var unique=receipts.Clone();
            foreach(var group in receipts.AsEnumerable().GroupBy(row=>StudentFeeAccountData.Text(row,"ReceiptNumber")))unique.ImportRow(group.First());
            if(unique.Columns.Contains("TransactionDate"))unique.DefaultView.Sort="TransactionDate DESC";
            if(payments.Columns.Contains("TransactionDate"))payments.DefaultView.Sort="TransactionDate DESC, FeePaymentId DESC";
            accountSignature=signature;accountUser=user;
            accountReceipts.DataSource=unique;accountPayments.DataSource=payments;accountCredits.DataSource=credits;
            UiHelper.SetGridEmptyMessage(accountReceipts,"No saved receipts for this account. Older standalone payments remain under Payments & Adjustments.");
            UiHelper.SetGridEmptyMessage(accountPayments,"No payments or adjustments for this account.");
            UiHelper.SetGridEmptyMessage(accountCredits,"No account credit movements for this account.");
            foreach(string column in new[]{"CashApplied","CreditApplied","DiscountApplied","Adjusted","NetSettled"})
                if(accountReceipts.Columns.Contains(column))accountReceipts.Columns[column].Visible=false;
            accountSummary.Text=$"{(ids.Count==1?"Student":"Family")} account · {ids.Count} student(s) · Current outstanding: Rs. {outstanding:N0} · Available credit: Rs. {credit:N0}\r\n"+
                (CurrentUser.HasPermission("Reports.View")?"Current totals and receipt history cover all dates; the statement totals above use the selected dates.":"Current totals and receipt history cover all dates. Statement access requires report permission.");
            statementContext.Text=$"{studentSearch.Text} · {(ids.Count==1?"Student account":$"Family account · {ids.Count} students")}";
            UpdateAccountActions();
        }
        catch(Exception ex)
        {
            if(!IsDisposed&&accountReads.IsCurrent(request))
            {InvalidateAccount();accountSummary.Text="Account details could not be refreshed. Use View Statement to retry. "+DbMessage.UserMessage(ex);}
        }
    }

    private void MergeAccountRows(DataTable target,DataTable source,int admission)
    {
        var copy=source.Copy();
        if(!copy.Columns.Contains("AccountAdmissionId"))copy.Columns.Add("AccountAdmissionId",typeof(int));
        if(!copy.Columns.Contains("Student"))copy.Columns.Add("Student");
        foreach(DataRow row in copy.Rows){row["AccountAdmissionId"]=admission;row["Student"]=accountStudentNames.GetValueOrDefault(admission,studentSearch.Text);}
        target.Merge(copy,true,MissingSchemaAction.Add);copy.Dispose();
    }

    private static void StyleAccountGrid(DataGridView grid)
    {
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        foreach(DataGridViewColumn column in grid.Columns)
        {
            column.Visible=!new[]{"CollectionReceiptId","FeePaymentId","EnrollmentId","RelatedFeePaymentId","IsCreditEntry","AccountAdmissionId"}.Contains(column.Name);
            if(grid.Name=="accountReceipts"&&new[]{"CashApplied","CreditApplied","DiscountApplied","Adjusted","NetSettled"}.Contains(column.Name))column.Visible=false;
            if(column.Name=="Student"&&(grid.Name=="accountReceipts"||grid.Columns.Contains("StudentName")))column.Visible=false;
            column.MinimumWidth=AppScale.Scale(90);column.Width=AppScale.Scale(125);
            if(column.ValueType==typeof(decimal)){column.DefaultCellStyle.Format="N0";column.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;}
            if(column.ValueType==typeof(DateTime))column.DefaultCellStyle.Format="dd MMM yyyy";
            column.HeaderText=column.Name switch{
                "AccountAdmissionId"=>"Student ID","PaidAmount"=>"Original amount","CashApplied"=>"Student cash share",
                "Balance"=>"Fee due",
                "CreditApplied"=>"Student credit share","Adjusted"=>"Student adjustments","NetSettled"=>"Student still recorded",
                "TotalSettled"=>"Whole receipt total",_=>System.Text.RegularExpressions.Regex.Replace(column.Name,"([a-z])([A-Z])","$1 $2")};
        }
        HumanUi.ApplyGridStyle(grid,AppTheme.Primary);
        int position=0;
        foreach(string name in new[]{"StudentName","ReceiptNumber","TransactionDate","PaymentMethod","PaidAmount","RemainingRecorded","AccountState"})
            if(grid.Columns.Contains(name))grid.Columns[name].DisplayIndex=position++;
    }

    private void UpdateAccountActions()
    {
        if(!accountMode)return;
        var payment=SelectedAccountRow(accountPayments);
        if(receiptDetails is not null)receiptDetails.Enabled=AccountCurrent&&SelectedAccountRow(accountReceipts) is not null;
        if(paymentDetails is not null)paymentDetails.Enabled=AccountCurrent&&payment is not null;
        if(correctPayment is not null){correctPayment.Visible=CurrentUser.HasPermission("Fees.Adjust");correctPayment.Enabled=AccountCurrent&&payment is not null&&StudentFeeAccountData.Eligibility(payment).Actions.Length>0;}
        if(recoverPayment is not null){recoverPayment.Visible=CurrentUser.HasPermission("Fees.Adjust");recoverPayment.Enabled=CanOpenAccount&&CurrentUser.HasPermission("Fees.Adjust");}
        if(entryDetails is not null)entryDetails.Enabled=(statementViews?.SelectedIndex==1?accountTransactions:Grid).CurrentRow?.DataBoundItem is DataRowView&&_appliedFilterSignature==FilterSignature();
        if(feeChanges is not null){feeChanges.Visible=CurrentUser.HasPermission("Fees.View")&&CurrentUser.HasPermission("Fees.Statement.View");feeChanges.Enabled=feeChanges.Visible&&SelectedStatementAdmission()>0&&_appliedFilterSignature==FilterSignature();}
        if(relatedPayment is not null){var entry=SelectedAccountRow(statementViews?.SelectedIndex==1?accountTransactions:Grid);relatedPayment.Enabled=AccountCurrent&&entry is not null&&StudentFeeAccountData.Number(entry,"FeePaymentId")>0;}
    }

    private async Task ReviewAccountPaymentAsync(DataRow? payment)
    {
        if(!AccountCurrent||!CurrentUser.HasPermission("Fees.Adjust")||payment is null||StudentFeeAccountData.Eligibility(payment).Actions.Length==0)return;
        int admission=StudentFeeAccountData.Number(payment,"AccountAdmissionId"),id=StudentFeeAccountData.Number(payment,"FeePaymentId");
        string receipt=SelectedAccountRow(accountReceipts) is DataRow document?StudentFeeAccountData.Text(document,"ReceiptNumber"):"";
        using(var dialog=CreateRelatedReportDialog("Review Payment · "+StudentFeeAccountData.Text(payment,"ReceiptNumber"),new PaymentCorrectionReview(admission,id)))dialog.ShowDialog(FindForm());
        // Includes recovery resolution and a cancelled review. Never replay the financial operation.
        if(!IsDisposed)await LoadStatement();
        SelectAccountPayment(id);
        foreach(DataGridViewRow row in accountReceipts.Rows)
            if(row.DataBoundItem is DataRowView documentView&&StudentFeeAccountData.Text(documentView.Row,"ReceiptNumber")==receipt)
            {accountReceipts.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(cell=>cell.Visible);break;}
    }

    private void SelectAccountPayment(int paymentId)
    {
        foreach(DataGridViewRow row in accountPayments.Rows)
            if(row.DataBoundItem is DataRowView view&&StudentFeeAccountData.Number(view.Row,"FeePaymentId")==paymentId)
            {accountPayments.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(cell=>cell.Visible);break;}
    }

    private async Task ShowAccountPaymentDetailsAsync()
    {
        var row=SelectedAccountRow(accountPayments);if(!AccountCurrent||row is null)return;
        int user=CurrentUser.UserId;string signature=accountSignature;
        using var data=await PaymentCorrectionReview.ReadPaymentDetailsAsync(user,StudentFeeAccountData.Number(row,"FeePaymentId"),StudentFeeAccountData.Number(row,"AccountAdmissionId"));
        if(!AccountCurrent||user!=CurrentUser.UserId||signature!=accountSignature||!ReferenceEquals(row,SelectedAccountRow(accountPayments)))return;
        ShowAccountTable("Payment fee allocations",data);
    }

    private void ShowAccountTable(string title,DataTable data)
    {
        var grid=HumanUi.Grid(false);grid.Dock=DockStyle.Fill;grid.DataSource=data;StyleAccountGrid(grid);
        using var dialog=CreateRelatedReportDialog(title,grid);dialog.ShowDialog(FindForm());
    }

    private Task ShowStatementEntryAsync()
    {
        if((statementViews?.SelectedIndex==1?accountTransactions:Grid).CurrentRow?.DataBoundItem is not DataRowView view||_appliedFilterSignature!=FilterSignature())return Task.CompletedTask;
        using var details=new DataTable();details.Columns.Add("Field");details.Columns.Add("Value");
        foreach(DataColumn column in view.Row.Table.Columns)details.Rows.Add(column.ColumnName,Convert.ToString(view.Row[column]));
        ShowAccountTable("Statement entry · related payment references",details);return Task.CompletedTask;
    }

    private int SelectedStatementAdmission()
    {
        var row=SelectedAccountRow(statementViews?.SelectedIndex==1?accountTransactions:Grid);
        if(row is not null)return StudentFeeAccountData.Number(row,row.Table.Columns.Contains("AccountAdmissionId")?"AccountAdmissionId":"AdmissionId");
        var ids=GetSelectedAdmissionIds();return ids.Count==1?ids[0]:0;
    }

    private async Task OpenAccountReceiptAsync()
    {
        var row=SelectedAccountRow(accountReceipts);if(!AccountCurrent||row is null)return;
        string receipt=StudentFeeAccountData.Text(row,"ReceiptNumber"),signature=accountSignature;int user=CurrentUser.UserId;
        try
        {
            using var saved=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receipt));
            using var later=await StudentFeeAccountData.ReadReceiptPaymentsAsync(receipt);
            if(!AccountCurrent||signature!=accountSignature||user!=CurrentUser.UserId||!ReferenceEquals(row,SelectedAccountRow(accountReceipts)))return;
            if(saved.Tables.Count<3||saved.Tables[0].Rows.Count==0){Warn("This entry has no saved collection document. Review its payment allocations instead.");return;}
            using var dialog=CreateRelatedReportDialog("Original receipt · "+receipt,BuildReceiptDetail(saved,receipt,later));
            dialog.ShowDialog(FindForm());
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
    }

    private Control BuildReceiptDetail(DataSet saved,string receipt,DataTable later)
    {
        var header=saved.Tables[0].Rows[0];
        var title=new Label {AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(8),Text=
            $"{receipt} · {StudentFeeAccountData.Text(header,"TransactionDate")}\r\nOriginal cash received: Rs. {StudentFeeAccountData.Money(header,"CashReceived"):N0} · Credit applied: Rs. {StudentFeeAccountData.Money(header,"CreditApplied"):N0} · Recorded outstanding: Rs. {StudentFeeAccountData.Money(header,"BalanceRemaining"):N0}\r\n"+
            (FeeReceiptDocuments.IsCompleteSnapshot(header)?"Saved issue-time document. Subsequent changes are shown separately.":"Legacy snapshot: some original fee details or concessions were not saved. Today's values have not been substituted.")};
        var tabs=new TabControl {Dock=DockStyle.Fill};
        for(int i=1;i<saved.Tables.Count;i++)
        {
            var tab=new TabPage(i==1?"Original student shares":i==2?"Original fee details":"Original fee plans");
            var grid=HumanUi.Grid(false);grid.Dock=DockStyle.Fill;grid.DataSource=saved.Tables[i];StyleAccountGrid(grid);tab.Controls.Add(grid);tabs.TabPages.Add(tab);
        }
        var laterTab=new TabPage("Payments & Later Changes");
        var laterGrid=HumanUi.Grid(false);laterGrid.Dock=DockStyle.Fill;laterGrid.DataSource=later;StyleAccountGrid(laterGrid);
        var review=CreateFilterButton("Review Selected Payment…",AppTheme.MutedText);
        review.Visible=CurrentUser.HasPermission("Fees.Adjust");
        void Availability()=>review.Enabled=AccountCurrent&&SelectedAccountRow(laterGrid) is DataRow selected&&StudentFeeAccountData.Eligibility(selected).Actions.Length>0;
        laterGrid.SelectionChanged+=(_,_)=>Availability();
        review.Click+=async(_,_)=>{
            review.Enabled=false;
            try{await ReviewAccountPaymentAsync(SelectedAccountRow(laterGrid));review.FindForm()?.Close();}
            catch(Exception ex){if(!IsDisposed)Fail(ex);}finally{if(!review.IsDisposed)Availability();}
        };
        laterTab.Controls.Add(AccountLayout(laterGrid,HumanUi.ButtonRow(review)));tabs.TabPages.Add(laterTab);Availability();
        var print=CreateFilterButton("Reprint Original",AppTheme.MutedText);
        var root=new ReceiptDetailsHost {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};
        print.Click+=async(_,_)=>{
            if(!AccountCurrent||!CurrentUser.HasPermission("Fees.View"))return;
            var owner=print.FindForm();print.Enabled=false;root.Printing=true;
            try{var institution=await InstitutionProfile.GetPrintIdentityAsync();if(!AccountCurrent)return;
                if(owner?.IsDisposed==true)return;
                if(!await FeeReceiptDocuments.TryShowAsync(owner,institution,receipt))Warn("The original saved receipt could not be found. Nothing was recreated.");}
            catch(Exception ex){if(!IsDisposed)Fail(ex);}finally{root.Printing=false;if(!print.IsDisposed)print.Enabled=true;}
        };
        var related=CreateFilterButton("Payments / Later Changes",AppTheme.MutedText);
        related.Click+=(_,_)=>{
            // Return to the same account; current payment history is deliberately separate from saved document tables.
            if(accountViews?.TabPages[1].Controls[0] is TabControl receiptViews)receiptViews.SelectedIndex=1;
            print.FindForm()?.Close();
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(title,0,0);root.Controls.Add(tabs,0,1);root.Controls.Add(HumanUi.ButtonRow(print,related),0,2);return root;
    }

    private static void AppendAccountTransactions(DataTable target,DataTable source,int admission,string name)
    {
        using var copy=source.Copy();
        if(!copy.Columns.Contains("StatementStudent"))copy.Columns.Add("StatementStudent");
        if(!copy.Columns.Contains("AccountAdmissionId"))copy.Columns.Add("AccountAdmissionId",typeof(int));
        foreach(DataRow row in copy.Rows){row["StatementStudent"]=name;row["AccountAdmissionId"]=admission;}
        target.Merge(copy,true,MissingSchemaAction.Add);
    }
    private void BindAccountTransactions(DataTable transactions)
    {
        if(transactions.Columns.Contains("TransactionDate"))transactions.DefaultView.Sort="TransactionDate ASC, StatementStudent ASC";
        accountTransactions.DataSource=transactions;
    }
}
