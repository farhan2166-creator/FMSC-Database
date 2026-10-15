using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

internal static class FeeSetupWorkflows
{
    private static decimal TuitionRate()=>Identity.Money("""
        SELECT F.Amount FROM dbo.FeeConfigurations F JOIN dbo.Classes C ON C.ClassId=F.ClassId
        JOIN dbo.FeeTypes T ON T.FeeTypeId=F.FeeTypeId WHERE C.ClassName=N'Class 6' AND T.FeeTypeCode=N'TUITION';
        """);
    internal static void Run(Desktop ui,Evidence report)
    {
        report.Run("L2-FIN-02",ui,()=>{
            ui.Navigate("Fee Rate Setup","Fees & Billing");ui.Select("Campus","Fictional Main Campus");
            void SetRate(int amount)
            {
                var grid=ui.Find("Class fee rate matrix");
                grid.Focus();ui.Key(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,FlaUI.Core.WindowsAPI.VirtualKeyShort.HOME);
                ui.Key(FlaUI.Core.WindowsAPI.VirtualKeyShort.RIGHT);ui.Key(FlaUI.Core.WindowsAPI.VirtualKeyShort.RIGHT);
                ui.Click("Edit Selected Fee",ControlType.Button);
                var details=ui.Window("Class 6 — Tuition fee");
                ui.Fill("Amount",amount.ToString(),details);ui.Click("Use in Grid",ControlType.Button,details);
                ui.Click("Save Grid",ControlType.Button);
                Desktop.Wait(()=>TuitionRate()==amount?new object():null,"fee configuration commit");
            }
            SetRate(5100);ui.Screenshot("L2-FIN-02-updated-rate");SetRate(5000);
            Identity.Check(Identity.Money("SELECT TOP(1) Amount FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes T ON T.FeeTypeId=A.FeeTypeId WHERE T.FeeTypeCode=N'TUITION';")==5000,"Configuration editing unexpectedly changed the student's assignment.");
        },"UI changed Class 6 tuition to 5100 and restored 5000; existing student assignment stayed 5000.");
        report.Run("L2-FIN-03",ui,()=>{
            ui.Navigate("Fee Posting","Fees & Billing");
            ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year-1));
            ui.Select("Campus","Fictional Main Campus");ui.Select("Class","Class 6 (Fictional Middle School)");
            ui.Select("Fee Type","Tuition fee");
            ui.Select("Billing Month",$"May {DateTime.UtcNow.Year-1}  -  1 to post");
            ui.Click("Load Eligible Fees",ControlType.Button);
            Desktop.Wait(()=>ui.Find("Post Selected Fees",ControlType.Button) is {IsEnabled:true} b?b:null,"eligible monthly preview");
            ui.Click("Post Selected Fees",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Post Monthly Fees"));
            var month=new Microsoft.Data.SqlClient.SqlParameter("@Month",new DateTime(DateTime.UtcNow.Year-1,5,1));
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE BillingMonth=@Month;",new Microsoft.Data.SqlClient.SqlParameter("@Month",month.Value))==1?new object():null,"monthly charge commit");
            Identity.Check(Identity.Money("SELECT NetAmount FROM dbo.BillingCharges WHERE BillingMonth=@Month;",month)==5000,"Monthly charge differs from the configured 5000 rate.");
            ui.AssertMissingChoice("Billing Month",$"May {DateTime.UtcNow.Year-1}");
            ui.Navigate("Fee Collection");ui.PickStudent();ui.Fill("Amount Received (Rs.)","5000");
            ui.Click("Collect Payment",ControlType.Button);ui.Click("Yes",ControlType.Button,ui.Window("Confirm Fee Transaction"));
            Desktop.Wait(()=>FinanceWorkflows.Paid()==12000?new object():null,"monthly-charge collection");
            Identity.Check(FinanceWorkflows.Allocated()==12000&&FinanceWorkflows.Charges()==12000,"Monthly-charge settlement does not reconcile.");
        },"One May charge at 5000; posted month absent from eligible choices; collection brings charges/payments/allocations to 12000.");
        report.Run("L2-FIN-06",ui,()=>{
            decimal originalCharges=FinanceWorkflows.Charges(),originalPaid=FinanceWorkflows.Paid();
            ui.Navigate("Student Purchases");ui.PickStudent("Student / Admission Number");
            ui.Select("Fee Type","Fictional workbook purchase");
            ui.Date("Date",new DateTime(DateTime.UtcNow.Year-1,6,1));
            ui.Fill("Amount (Rs.)","430");ui.Fill("Discount (Rs.)","30");
            ui.Fill("What is this charge for?","Fictional Example Workbook");ui.Click("Add Charge",ControlType.Button);
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE ChargeKind=N'Manual';")==1?new object():null,"one-off charge commit");
            Identity.Check(Identity.Money("SELECT GrossAmount FROM dbo.BillingCharges WHERE ChargeKind=N'Manual';")==430&&
                Identity.Money("SELECT DiscountAmount FROM dbo.BillingCharges WHERE ChargeKind=N'Manual';")==30&&
                Identity.Money("SELECT NetAmount FROM dbo.BillingCharges WHERE ChargeKind=N'Manual';")==400,"One-off charge arithmetic differs.");
            ui.Navigate("Fee Collection");ui.PickStudent();
            ui.Fill("Amount Received (Rs.)","400");ui.Click("Collect Payment",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Confirm Fee Transaction"));
            Desktop.Wait(()=>FinanceWorkflows.Paid()==originalPaid+400?new object():null,"additional-charge collection");
            Identity.Check(FinanceWorkflows.Charges()==originalCharges+400&&FinanceWorkflows.Allocated()==FinanceWorkflows.Charges(),"Additional-charge settlement does not reconcile.");
        },"UI charge gross 430 minus discount 30 equals 400; collection adds exactly 400 and total charges/payments/allocations reconcile.");
    }
}
