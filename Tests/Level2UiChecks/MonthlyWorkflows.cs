using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Data.SqlClient;

internal static class MonthlyWorkflows
{
    internal static void Run(Desktop ui,Evidence report)
    {
        if(!report.Passed("L2-PRO-06")) {report.BlockIds("Current-year return confirmation is required before this monthly posting.","L2-FIN-03");return;}
        if(DateTime.Today.Month<2) {report.BlockIds("January's opening month is already billed; there is no later current-year month eligible for this posting scenario yet.","L2-FIN-03");return;}
        report.Run("L2-FIN-03",ui,()=>{
            ui.Navigate("Fee Posting","Fees & Billing");
            var year=ui.Find("Academic Year",ControlType.ComboBox);
            Identity.Check(!year.IsEnabled&&year.AsComboBox().Value=="Fictional "+DateTime.UtcNow.Year,"Posting must remain locked to the actual current academic year.");
            ui.Select("Campus","Fictional Main Campus");ui.Select("Class","Class 7 (Fictional Middle School)");ui.Select("Fee Type","Tuition fee");
            ui.Select("Billing Month",$"February {DateTime.UtcNow.Year}  -  1 to post");ui.Click("Load Eligible Fees",ControlType.Button);
            Desktop.Wait(()=>ui.Find("Post Eligible Fees",ControlType.Button) is {IsEnabled:true} b?b:null,"eligible monthly preview");
            ui.Click("Post Eligible Fees",ControlType.Button);ui.Click("Yes",ControlType.Button,ui.Window("Post Monthly Fees"));
            DateTime month=new(DateTime.UtcNow.Year,2,1);
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE BillingMonth=@Month;",new SqlParameter("@Month",month))==1?new object():null,"monthly charge commit");
            Identity.Check(Identity.Money("SELECT NetAmount FROM dbo.BillingCharges WHERE BillingMonth=@Month;",new SqlParameter("@Month",month))==6000,"Current Class 7 month differs from its configured 6000 rate.");
            ui.AssertMissingChoice("Billing Month",$"February {DateTime.UtcNow.Year}");
            decimal previouslyPaid=FinanceWorkflows.Paid();
            ui.Navigate("Fee Collection");ui.PickStudent();
            Identity.Check(FinanceWorkflows.Displayed(ui,"Outstanding (Rs.)")==14000,"Current-year dues differ from 8000 opening plus 6000 monthly.");
            ui.Fill("Amount Received (Rs.)","14000");ui.Click("Collect Payment",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Confirm Fee Transaction"));
            Desktop.Wait(()=>FinanceWorkflows.Paid()==previouslyPaid+14000?new object():null,"current-year settlement");
            Identity.Check(FinanceWorkflows.Allocated()==FinanceWorkflows.Charges(),"Current-year collection does not reconcile.");
        },"Current-year-only UI posts February once at 6000, removes it from eligible choices, and settles exact current-year dues of 14000.");
    }
}
