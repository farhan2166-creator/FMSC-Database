using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

internal static class FinanceWorkflows
{
    internal static decimal Charges()=>Identity.Money("SELECT COALESCE(SUM(NetAmount),0) FROM dbo.BillingCharges WHERE Status<>N'Cancelled';");
    internal static decimal Paid()=>Identity.Money("SELECT COALESCE(SUM(TotalAmount),0) FROM dbo.FeePayments;");
    internal static decimal Allocated()=>Identity.Money("SELECT COALESCE(SUM(Amount),0) FROM dbo.FeePaymentAllocations;");
    internal static decimal Displayed(Desktop ui,string caption)
    {
        var label=ui.Find(caption,ControlType.Text);
        var values=label.Parent.FindAllChildren(cf=>cf.ByControlType(ControlType.Text))
            .Where(e=>e.Name!=caption).Select(e=>e.Name).ToArray();
        Identity.Check(values.Length==1,"Ambiguous financial summary: "+caption);
        return decimal.Parse(values[0],System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.CurrentCulture);
    }
    internal static void Run(Desktop ui,Evidence report)
    {
        report.Run("L2-FIN-01",ui,()=>{
            ui.Navigate("Fee Collection");ui.PickStudent();
            Desktop.Wait(()=>ui.Find("Collect Payment",ControlType.Button).IsEnabled?new object():null,"loaded outstanding charges");
            Identity.Check(Charges()==7000&&Paid()==0,"Unexpected prepayment totals.");
            Identity.Check(Displayed(ui,"Outstanding (Rs.)")==7000,"Displayed outstanding amount differs from SQL.");
        },"UI-created student has exactly 7000 outstanding; zero fixture payments.");
        if(!report.Passed("L2-FIN-01"))return;
        report.Run("L2-FIN-10",ui,()=>{
            ui.Fill("Amount Received (Rs.)","2500");ui.Click("Collect Payment",ControlType.Button);
            var dialog=ui.Window("Confirm Fee Transaction");
            ui.Screenshot("L2-FIN-10-confirmation");ui.Click("No",ControlType.Button,dialog);
            Identity.Check(Paid()==0&&Allocated()==0,"Cancelled payment persisted.");
        },"Zero payments and allocations after cancelling confirmation.");
        report.Run("L2-FIN-04",ui,()=>{
            ui.Fill("Amount Received (Rs.)","2500");ui.Click("Collect Payment",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Confirm Fee Transaction"));
            Desktop.Wait(()=>Paid()==2500?new object():null,"partial payment commit");
            Identity.Check(Allocated()==2500&&Charges()-Allocated()==4500,"Partial allocation or balance differs.");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.FeePayments;")==1,"Partial payment duplicated.");
        },"Payment 2500; allocations 2500; independent remaining balance 4500.");
        if(!report.Passed("L2-FIN-04"))return;
        report.Run("L2-FIN-05",ui,()=>{
            ui.PickStudent();
            Identity.Check(Displayed(ui,"Outstanding (Rs.)")==4500,"Reopened partial balance differs from SQL.");
            ui.Fill("Amount Received (Rs.)","4500");ui.Click("Collect Payment",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Confirm Fee Transaction"));
            Desktop.Wait(()=>Paid()==7000?new object():null,"full settlement commit");
            Identity.Check(Allocated()==7000&&Charges()-Allocated()==0,"Settlement allocations or balance differ.");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.FeePayments;")==2,"Settlement payment count differs.");
        },"Two committed payments total 7000; allocations 7000; remaining balance zero.");
        if(!report.Passed("L2-FIN-05"))return;
        report.Run("L2-FIN-08",ui,()=>{
            ui.Click("Print Receipt",ControlType.Button);
            var receipt=ui.Window("Fee Receipt");
            Desktop.Wait(()=>ui.Find("Export PDF",ControlType.Button,receipt) is {IsEnabled:true} button?button:null,"rendered receipt",60);
            ui.Screenshot("L2-FIN-08-receipt");
            Identity.Check(Paid()==7000&&Identity.Count("SELECT COUNT(*) FROM dbo.FeePayments;")==2,"Receipt preview changed payments.");
            ui.Click("Close",ControlType.Button,receipt);
        },"Real receipt preview rendered; no physical printing; two committed payments still total 7000.");
        report.Run("L2-FIN-09",ui,()=>{
            ui.PickStudent();
            Desktop.Wait(()=>!ui.Find("Collect Payment",ControlType.Button).IsEnabled?new object():null,"settled submission guard");
            Identity.Check(Paid()==7000&&Identity.Count("SELECT COUNT(*) FROM dbo.FeePayments;")==2,"Settled replay changed payments.");
        },"Settled collection disabled; payment count and total unchanged.");
        report.Run("L2-FIN-07",ui,()=>{
            ui.Click("Student Fees ▾",ControlType.Button);
            ui.Click("Account Statement",ControlType.MenuItem);
            ui.Window("Fee Statement History");
            Identity.Check(Charges()==Allocated()&&Paid()==Allocated(),"Statement source totals differ.");
        },"Committed charges, payments and allocations reconcile to a zero balance; inspect statement screenshot.");
        // Close only the known statement dialog, then return to collection.
        try {ui.Click("Close",ControlType.Button,ui.Window("Fee Statement History"));} catch {return;}
        report.Run("L2-FIN-11",ui,()=>{
            Identity.Check(Paid()==Allocated(),"Payments do not reconcile to allocations.");
            Identity.Check(Identity.Count("""
                SELECT COUNT(*) FROM dbo.FeePaymentAllocations A JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
                JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
                WHERE A.AdmissionId<>B.AdmissionId OR A.AdmissionId<>P.AdmissionId OR B.EnrollmentId<>P.EnrollmentId;
                """)==0,"Allocation ownership mismatch.");
            Identity.Check(Identity.Count("""
                SELECT COUNT(*) FROM dbo.BillingCharges B OUTER APPLY(SELECT SUM(Amount) Paid FROM dbo.FeePaymentAllocations A WHERE A.BillingChargeId=B.BillingChargeId) X
                WHERE COALESCE(X.Paid,0)>B.NetAmount OR B.NetAmount<0;
                """)==0,"Overallocated or negative charge.");
        },"Independent SQL: payment/allocation equality, ownership, enrollment links, no over-allocation or negative charges.");
        FeeSetupWorkflows.Run(ui,report);
    }
}
