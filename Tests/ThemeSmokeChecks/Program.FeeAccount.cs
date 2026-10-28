using System.Data;
using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Finance.StudentAccounts;

internal static partial class Program
{
    private static void CheckFeeAccountWorkspace()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        var type=typeof(StudentAccountStatementPage);
        CurrentUser.SignIn(920,"Account fixture","Administrator",false,Array.Empty<string>());
        DataTable Statement(int id)
        {
            var table=new DataTable();
            foreach(string name in new[]{"TransactionType","StudentName","FeeTypeName","ReceiptNumber","PaymentMethod","Remarks"})table.Columns.Add(name);
            foreach(string name in new[]{"AdmissionId","TransactionId","FeePaymentId"})table.Columns.Add(name,typeof(int));
            table.Columns.Add("TransactionDate",typeof(DateTime));
            foreach(string name in new[]{"DebitAmount","PaidAmount","Balance"})table.Columns.Add(name,typeof(decimal));
            table.Rows.Add("CHARGE","Student "+id,"Tuition","","","Discount included",id,id,DBNull.Value,new DateTime(2026,10,1),1000m,400m,600m);
            table.Rows.Add("PAYMENT","Student "+id,"Payment","FR-test","Cash","Partial payment",id,id+100,id+100,new DateTime(2026,10,2),0m,400m,0m);
            return table;
        }
        Func<int,DateTime?,DateTime?,Task<(DataTable Transactions,decimal OpeningBalance)>> read=(id,_,_)=>Task.FromResult((Statement(id),0m));
        using var page=new StudentAccountStatementPage(false,read,false,true){Dock=DockStyle.Fill,BindingContext=new BindingContext()};
        T Field<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(page)!;
        object? Invoke(string name,params object[] args)=>type.GetMethod(name,flags)!.Invoke(page,args);
        void Wait(Task task){PumpUntil(()=>task.IsCompleted,"Account asynchronous read finishes");task.GetAwaiter().GetResult();}
        StudentFeeAccountData Snapshot(int id)
        {
            var receipts=new DataTable();receipts.Columns.Add("ReceiptNumber");receipts.Columns.Add("TransactionDate",typeof(DateTime));
            receipts.Columns.Add("TotalSettled",typeof(decimal));receipts.Columns.Add("CashApplied",typeof(decimal));
            receipts.Rows.Add("FR-test",new DateTime(2026,10,2),800m,400m);
            var payments=new DataTable();
            foreach(string column in new[]{"ReceiptNumber","PaymentMethod","Status","StudentName"})payments.Columns.Add(column);
            foreach(string column in new[]{"FeePaymentId","RelatedFeePaymentId"})payments.Columns.Add(column,typeof(int));
            foreach(string column in new[]{"PaidAmount","Refunded","Corrected","ReleasedToCredit"})payments.Columns.Add(column,typeof(decimal));
            payments.Columns.Add("IsCreditEntry",typeof(bool));payments.Columns.Add("TransactionDate",typeof(DateTime));
            payments.Rows.Add("FR-test","Cash","Completed","Student "+id,id+100,DBNull.Value,400m,0m,0m,0m,false,new DateTime(2026,10,2));
            return new(receipts,payments,600m,150m);
        }
        try
        {
            using(var shell=new MainDashboardForm((_,_)=>{}))
            {
                var commands=(List<NavigationCommand>)typeof(MainDashboardForm).GetField("navigationCommands",flags)!.GetValue(shell)!;
                Check(commands.Any(c=>c.Caption==PageTitles.StudentFeeAccount)&&!commands.Any(c=>c.Caption==PageTitles.StudentFeeCorrections),"Full-access navigation consolidates statements and corrections into the account");
                Check(commands.Count(c=>c.Caption=="Fee Collection")==1,"The existing collection destination remains intact");
            }
            page.ReadAccount=id=>Task.FromResult(Snapshot(id));
            using var owner=new Form{ClientSize=new Size(1180,800),ShowInTaskbar=false};owner.Controls.Add(page);owner.Show();Application.DoEvents();
            var picker=Field<StudentPicker>("studentSearch");picker.SetStudent(41,"Student 41");Invoke("SelectSingleStudent");
            Wait((Task)Invoke("LoadStatement")!);
            Check(((DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!).Rows.Count==1,"The established fee-balance statement retains its charge row");
            var receipts=Field<DataGridView>("accountReceipts");var payments=Field<DataGridView>("accountPayments");
            Check(Field<Label>("accountSummary").Text.Contains("Current outstanding: Rs. 600")&&Field<Label>("accountSummary").Text.Contains("Available credit: Rs. 150"),"Student dues and credit are separate, without netting");
            Check(receipts.Rows.Count==1&&payments.Rows.Count==1,"Student receipts and payments share the selected account");
            Check(Field<DataGridView>("accountTransactions").Rows.Count==2,"Chronological statement retains charge and payment rows without changing existing statement calculations");
            Field<TabControl>("accountViews").SelectedIndex=1;
            ((TabControl)Field<TabControl>("accountViews").TabPages[1].Controls[0]).SelectedIndex=1;
            Application.DoEvents();Invoke("UpdateAccountActions");
            Check(Field<ModernButton>("correctPayment").Enabled,"Eligible cash payment exposes the existing correction workflow");
            Field<Dictionary<int,HashSet<int>>>("familySelections")[41]=new(){41,42};Invoke("OnFiltersChanged");
            Check(receipts.DataSource is null&&!Field<ModernButton>("correctPayment").Enabled,"Scope changes immediately clear actionable financial rows");
            Wait((Task)Invoke("LoadStatement")!);
            Check(Field<Label>("accountSummary").Text.Contains("Family account · 2 student(s)")&&Field<Label>("accountSummary").Text.Contains("1,200"),"Family summary sums the same existing student balance readers");
            Check(receipts.Rows.Count==1&&payments.Rows.Count==2,"Family receipt is deduplicated but both student payments remain selectable");
            Check(!receipts.Columns["CashApplied"].Visible,"Deduplicated family list cannot mislabel one child's share as a family total");
            Field<TabControl>("accountViews").SelectedIndex=1;Application.DoEvents();
            Check(picker.SelectedId==41&&Field<Dictionary<int,HashSet<int>>>("familySelections").Count==1,"Switching views preserves student and family scope");
            CurrentUser.SignIn(921,"Read only","Operator",false,new[]{"Reports.View","Fees.View"});
            Invoke("UpdateAccountActions");Check(!Field<ModernButton>("correctPayment").Enabled,"A different signed-in user cannot act on the preceding user's financial rows");
            Wait((Task)Invoke("RefreshAccountAsync")!);Invoke("UpdateAccountActions");
            Check(!Field<ModernButton>("correctPayment").Visible,"Read-only operator is not offered corrections");
            using(var context=(StudentFeeAdjustmentsPage)Activator.CreateInstance(typeof(StudentFeeAdjustmentsPage),flags,null,new object?[]{false,null,null,41,101},null)!)
            {
                var adjustmentType=context.GetType();
                adjustmentType.GetField("admissionId",flags)!.SetValue(context,42);
                adjustmentType.GetField("selectedPaymentId",flags)!.SetValue(context,101);
                Check(!(bool)adjustmentType.GetMethod("ExecuteIsAvailable",flags)!.Invoke(context,null)!,"A contextual correction refuses a different student's payment context");
            }
            CurrentUser.SignIn(920,"Account fixture","Administrator",false,Array.Empty<string>());
            var deferred=new TaskCompletionSource<StudentFeeAccountData>();page.ReadAccount=_=>deferred.Task;
            Task pending=(Task)Invoke("RefreshAccountAsync")!;
            picker.SetStudent(43,"Student 43");Invoke("SelectSingleStudent");deferred.SetResult(Snapshot(41));Wait(pending);
            Check(receipts.DataSource is null&&payments.DataSource is null,"An old asynchronous account response cannot populate a new student");
            page.ReadAccount=id=>Task.FromResult(Snapshot(id));Wait((Task)Invoke("LoadStatement")!);
            var paymentTable=(DataTable)payments.DataSource!;paymentTable.Rows[0]["IsCreditEntry"]=true;Invoke("UpdateAccountActions");
            Check(!Field<ModernButton>("correctPayment").Enabled,"Credit applications never expose cash-payment corrections");
            Directory.CreateDirectory("artifacts/fee-account");
            foreach(var size in new[]{new Size(1180,800),new Size(900,650),new Size(1500,950)})
            {
                owner.ClientSize=size;Field<TabControl>("accountViews").SelectedIndex=0;Application.DoEvents();
                var tabs=Field<TabControl>("accountViews");Check(tabs.Width>500&&tabs.Height>150,"Account views retain working space at "+size);
                var period=Field<Label>("closingBalance");
                Check(period.Height>=period.Font.GetHeight()&&period.Bottom<=period.Parent!.ClientSize.Height,"Period summary amounts remain fully visible at "+size);
                Check(page.SelectNextControl(picker,true,true,true,true),"Account keyboard navigation finds the next focusable control");
                foreach(var viewport in DescendantControls(page).OfType<ScrollableControl>())if(viewport.AutoScroll)viewport.AutoScrollPosition=Point.Empty;
                using var image=new Bitmap(page.Width,page.Height);page.DrawToBitmap(image,page.ClientRectangle);image.Save(Path.GetFullPath($"artifacts/fee-account/account-{size.Width}.png"));
            }
            // The actual snapshot presentation is shared across legacy/current document formats.
            foreach(int version in new[]{1,2,3})
            {
                using var saved=new DataSet();var header=new DataTable();header.Columns.Add("SnapshotVersion",typeof(int));header.Rows.Add(version);saved.Tables.Add(header);
                saved.Tables.Add(new DataTable());saved.Tables.Add(new DataTable());
                using var later=Snapshot(41).Payments;
                using var content=(Control)Invoke("BuildReceiptDetail",saved,"FR-test",later)!;
                using var window=(ApplicationWindowForm)type.GetMethod("CreateRelatedReportDialog",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{"Receipt fixture",content})!;
                window.Show();Application.DoEvents();Check(content.Width>700,"Snapshot version "+version+" opens the existing receipt-detail structure");window.Close();
            }
            owner.Close();
            var scale=(AppScale.Dpi,AppScale.RawFactor);
            try
            {
                foreach(int dpi in new[]{96,120,144,192})
                {
                    var size=new Size(1280*dpi/96,850*dpi/96);AppScale.Recompute(size,dpi);
                    using var scaled=new StudentAccountStatementPage(false,read,false,true){Dock=DockStyle.Fill};
                    using var window=new Form{ClientSize=size,ShowInTaskbar=false};window.Controls.Add(scaled);window.Show();Application.DoEvents();
                    var tabs=(TabControl)type.GetField("accountViews",flags)!.GetValue(scaled)!;
                    Check(tabs.Width>AppScale.Scale(500)&&tabs.Height>AppScale.Scale(130),"Account layout has usable space with synthetic "+dpi+" DPI geometry");window.Close();
                }
            }
            finally{AppScale.Recompute(new Size((int)(1280*scale.RawFactor),(int)(720*scale.RawFactor)),scale.Dpi);}
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
    private static IEnumerable<Control> DescendantControls(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(DescendantControls(c)));
}
