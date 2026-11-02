using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Core;

// Search opens an existing document. Never call IssueFeeBill or a payment writer on this path.
internal sealed class SavedFinancialDocumentPage : V2Page,IBusyPage
{
    private readonly GlobalSearchRecord target;
    private readonly Action<Func<Control>,string,string> navigate;
    private readonly ModernButton open,account;
    private readonly CancellationTokenSource lifetime=new();
    private readonly int actor=CurrentUser.UserId;
    private readonly string endpoint=DatabaseHelper.DatabaseEndpoint;
    private DataTable? savedBill;
    private bool busy,hasDocument;
    public bool IsBusy=>busy;
    public string BusyMessage=>"The saved financial record is being opened. Wait for it to finish.";
    internal SavedFinancialDocumentPage(GlobalSearchRecord target,Action<Func<Control>,string,string> navigate)
        :base(target.Kind=="Bill"?"Saved Bill":"Saved Receipt",target.Reference+" · "+target.Name)
    {
        this.target=target;this.navigate=navigate;
        open=AddBusyAction("Open Saved Document",()=>Guard(OpenAsync),"Primary");open.Enabled=false;
        account=AddAction("Open Student Account",(_,_)=>{
            if(Current()&&!busy&&target.AdmissionId>0&&StudentAccountStatementPage.CanOpenAccount)
                navigate(()=>StudentAccountStatementPage.CreateAccount(target.AdmissionId),"Daily Work","Student Fee Account");
        });account.Enabled=false;
        Defer(()=>Guard(LoadAsync));
        Disposed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();savedBill?.Dispose();};
    }
    private bool Current()=>!IsDisposed&&actor==CurrentUser.UserId&&endpoint==DatabaseHelper.DatabaseEndpoint&&CurrentUser.HasPermission("Fees.View");
    private async Task Guard(Func<Task> work)
    {
        if(busy||!Current())return;
        busy=true;open.Enabled=account.Enabled=false;
        try {await work();}
        catch(OperationCanceledException) when(!Current()||lifetime.IsCancellationRequested) { }
        catch(Exception ex) {if(Current())Fail(ex);}
        finally
        {
            busy=false;
            if(!IsDisposed){open.Enabled=Current()&&hasDocument;account.Enabled=Current()&&target.AdmissionId>0&&StudentAccountStatementPage.CanOpenAccount;}
        }
    }
    private async Task LoadAsync()
    {
        SetStatus("Loading saved record…");
        using var details=new DataTable();details.Columns.Add("Field");details.Columns.Add("Value");
        void Add(DataRow row,string field,string label)
        {
            if(!row.Table.Columns.Contains(field)||row.IsNull(field))return;
            string value=row[field] is DateTime date?date.ToString("dd-MMM-yyyy hh:mm tt"):
                row[field] is decimal money?money.ToString(money==decimal.Truncate(money)?"N0":"N2"):Convert.ToString(row[field])??"";
            details.Rows.Add(label,value);
        }
        if(target.Kind=="Bill")
        {
            using var bill=await DatabaseHelper.QuerySqlAsync(GlobalRecordSearch.ReadBill,lifetime.Token,
                DatabaseHelper.Int("@UserId",actor),DatabaseHelper.P("@Id",Guid.Parse(target.Key)),DatabaseHelper.P("@Reference",target.Reference));
            if(!Current())return;
            if(bill.Rows.Count!=1){Warn("This saved bill is no longer available.");return;}
            savedBill=bill.Copy();var row=bill.Rows[0];
            Add(row,"BillNumber","Bill No.");Add(row,"IssuedAt","Date");
            details.Rows.Add("Type",Convert.ToString(row["DocumentType"])=="FB"?"Family Bill":"Student Bill");
            hasDocument=true;
        }
        else
        {
            // Payment references can resolve to their original collection receipt.
            // An older standalone payment remains a labelled entry, not a reconstructed receipt.
            if(target.Kind=="PaymentReference")
            {
                using var payment=await DatabaseHelper.QuerySqlAsync(GlobalRecordSearch.ReadPayment,lifetime.Token,
                    DatabaseHelper.Int("@UserId",actor),DatabaseHelper.Int("@Id",int.Parse(target.Key)),DatabaseHelper.P("@Reference",target.Reference));
                if(!Current())return;
                if(payment.Rows.Count!=1){Warn("This payment reference is no longer available.");return;}
                var row=payment.Rows[0];Add(row,"ReceiptNumber","Payment Reference");Add(row,"PaymentDate","Payment Date");
                Add(row,"TotalAmount","Recorded Amount (Rs.)");Add(row,"Status","Payment Status");
            }
            if(target.Kind=="Receipt")
            {
                using var found=await DatabaseHelper.QuerySqlAsync(GlobalRecordSearch.ReadReceiptIdentity,lifetime.Token,
                    DatabaseHelper.Int("@UserId",actor),DatabaseHelper.P("@Id",Guid.Parse(target.Key)),DatabaseHelper.P("@Reference",target.Reference));
                if(!Current())return;
                if(found.Rows.Count!=1){Warn("This saved receipt is no longer available.");return;}
            }
            using var receipt=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",lifetime.Token,DatabaseHelper.P("@ReceiptNumber",target.Reference));
            if(!Current())return;
            if(receipt.Tables.Count>=3&&receipt.Tables[0].Rows.Count==1)
            {
                var row=receipt.Tables[0].Rows[0];
                foreach(var (field,label) in new[]{("ReceiptNumber","Receipt No."),("TransactionDate","Date"),("PaymentMethod","Method"),
                    ("CashReceived","Received (Rs.)"),("CreditApplied","Credit Applied (Rs.)"),("DiscountApplied","Discount (Rs.)"),
                    ("BalanceRemaining","Saved Balance (Rs.)"),("ReceiptStatus","Status")})Add(row,field,label);
                hasDocument=true;
            }
            else if(target.Kind=="Receipt"){Warn("This saved receipt is no longer available.");return;}
        }
        if(!Current())return;
        Grid.DataSource=details.Copy();
        Grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        SetStatus(hasDocument?"Saved document loaded. Opening it does not issue a new number or record a payment.":
            "Legacy payment entry: no saved collection document is available. Open the student account for allocations and history.");
    }
    private async Task OpenAsync()
    {
        if(!hasDocument)return;
        var institution=await InstitutionProfile.GetPrintIdentityAsync();
        if(!Current())return;
        if(target.Kind=="Bill"&&savedBill is not null)await FeeBillDocuments.ShowSavedAsync(FindForm(),savedBill.Rows[0]);
        else if(!await FeeReceiptDocuments.TryShowAsync(FindForm(),institution,target.Reference,selectedChild:0))Warn("The saved receipt could not be opened.");
    }

}
