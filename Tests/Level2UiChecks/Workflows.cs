using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Microsoft.Data.SqlClient;

internal static class Workflows
{
    internal static void Run(Desktop ui,Evidence report)
    {
        UsabilityWorkflows.Initial(ui,report);
        UsabilityWorkflows.CaptureViewports(ui,report);
        report.Run("L2-ADM-01",ui,()=>{
            ui.Click("Student Admissions",ControlType.Button);
            ui.Find("Student Name",ControlType.Edit);
        },"No students seeded; admission opened against the owned catalog.");
        if(!report.Passed("L2-ADM-01")) return;
        report.Run("L2-ADM-02",ui,()=>{
            ui.Click("Save",ControlType.Button);
            ui.Find("Student Name",ControlType.Edit);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Invalid admission created a student.");
        },"Students remains empty after invalid Save.");
        report.Run("L2-ADM-09",ui,()=>{
            ui.Click("Find Student",ControlType.Button);
            var dialog=ui.Window("Find Student");
            ui.Fill("Search","Fictional no-such-student",dialog);ui.Click("Search",ControlType.Button,dialog);
            ui.Find("No students match the selected criteria.",ControlType.Text,dialog);
            Identity.Check(!ui.Find("Open Student",ControlType.Button,dialog).IsEnabled,"Empty search allowed a student open.");
            ui.Screenshot("L2-ADM-09-empty-search");ui.Click("Close",ControlType.Button,dialog);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Search created a student.");
        },"No-match feedback and disabled Open Student; no student records created.");
        report.Run("L2-ADM-03",ui,()=>{
            ui.Fill("Student Name","Fictional Example Student");
            ui.Fill("Father Name","Fictional Example Parent");
            ui.Fill("Place of Birth","Example City");
            ui.Fill("Address","Fictional Level Two Lane");
            ui.Select("Gender","Male");
            ui.Date("First Admission",new DateTime(DateTime.UtcNow.Year-1,4,1));
            ui.Select("Campus","Fictional Main Campus");
            ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year-1));
            ui.Select("Class","Class 6 (Fictional Middle School)");
            ui.Select("Section","Section A");
            ui.Fill("Admission Number","9001");
        },"Selected fixture class/section belongs to the displayed campus.");
        // The following scenarios are deliberately gated by actual UI admission.
        // No fixture writer creates the record used by these tests.
        if(report.Passed("L2-ADM-03"))
        {
            report.Run("L2-ADM-13",ui,()=>{
                ui.Select("Campus","Fictional Empty Campus");ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year-1));
                ui.Fill("Admission Number","9001");
                Identity.Check(ui.Find("Section",ControlType.ComboBox).AsComboBox().Value!="Section A","Section from the previous campus leaked into the empty campus.");
                ui.Click("Save",ControlType.Button);
                Desktop.Wait(()=>ui.TryFind("Select a class")??ui.TryFind("Select a section"),"missing valid placement feedback");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Unconfigured campus placement persisted.");
                ui.Screenshot("L2-ADM-13-empty-campus");
                ui.Select("Campus","Fictional Main Campus");ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year-1));
                ui.Select("Class","Class 6 (Fictional Middle School)");ui.Select("Section","Section A");ui.Fill("Admission Number","9001");
            },"Empty campus clears the prior section and cannot save a student; valid campus/class/section restored.");
            report.Run("L2-ADM-12",ui,()=>{
                ui.Fill("Primary Phone (optional)","123");ui.Click("Save",ControlType.Button);
                ui.Find("Enter a full Pakistan phone number");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Malformed phone admission persisted.");
                ui.Screenshot("L2-ADM-12-phone-refusal");
                ui.Fill("Primary Phone (optional)","");
                Identity.Check(ui.Find("Primary Phone (optional)",ControlType.Edit).AsTextBox().Text=="","Clearing the phone input failed.");
                ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year+1));
                Desktop.Wait(()=>ui.Window().FindAllDescendants(cf=>cf.ByControlType(ControlType.Text))
                    .FirstOrDefault(e=>(e.Properties.Name.ValueOrDefault??"").StartsWith("Admission date must be within",StringComparison.Ordinal)),"date/year validation feedback");
                ui.Click("Save",ControlType.Button);
                Desktop.Wait(()=>ui.Find("Save",ControlType.Button).IsEnabled?new object():null,"date validation completed");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Mismatched admission date/year persisted.");
                ui.Screenshot("L2-ADM-12-date-refusal");
                ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year-1));
                ui.Select("Class","Class 6 (Fictional Middle School)");ui.Select("Section","Section A");
                ui.Fill("Admission Number","9001");
            },"Malformed phone and date/year mismatch both leave Students empty; valid context restored.");
            report.Run("L2-ADM-04",ui,()=>{
                ui.Fill("Student Name","Fictional Example Student");
                ui.Fill("Father Name","Fictional Example Parent");
                ui.Fill("Place of Birth","Example City");
                ui.Fill("Address","Fictional Level Two Lane");
                ui.Select("Gender","Male");
                ui.Click("Save",ControlType.Button);
                var optionalDate=ui.Window("Date of Birth Not Entered");
                ui.Screenshot("L2-ADM-04-optional-date-confirmation");
                ui.Click("Yes",ControlType.Button,optionalDate);
                Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.Students WHERE StudentName=N'Fictional Example Student';")==1?new object():null,"persisted UI admission");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==1,"Unexpected extra students.");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Enrollments;")==1,"Expected one enrollment.");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students WHERE DateOfBirth IS NULL AND ContactNumber IS NULL;")==1,"Omitted optional fields did not persist as NULL.");
                Identity.Check(Identity.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges;")==7000,"Opening fees differ from 5000 tuition plus 2000 admission.");
            },"One UI-created student/enrollment; opening charges exactly 7000.");
        }
        if(report.Passed("L2-ADM-04"))
        {
            report.Run("L2-ADM-05",ui,()=>{
                ui.Click("Save",ControlType.Button,twice:true);
                Desktop.Wait(()=>ui.Find("Save",ControlType.Button).IsEnabled?new object():null,"save completed");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==1,"Repeated Save duplicated student.");
                Identity.Check(Identity.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges;")==7000,"Repeated Save duplicated charges.");
            },"Student count and 7000 opening total unchanged.");
            report.Run("L2-ADM-06",ui,()=>{
                ui.OpenStudent();
                Identity.Check(ui.Find("Student Name",ControlType.Edit).AsTextBox().Text=="Fictional Example Student","Reopened student name differs.");
                Identity.Check(ui.Find("Father Name",ControlType.Edit).AsTextBox().Text=="Fictional Example Parent","Reopened parent differs.");
                Identity.Check(ui.Find("Address",ControlType.Edit).AsTextBox().Text=="Fictional Level Two Lane","Reopened address differs.");
            },"Reopened student/parent/address match UI-submitted and committed values.");
            report.Run("L2-ADM-07",ui,()=>{
                ui.Fill("Father Occupation","Fictional Example Operator");ui.Click("Save",ControlType.Button);
                Desktop.Wait(()=>Convert.ToString(Identity.Scalar("SELECT FatherOccupation FROM dbo.Students WHERE StudentName=N'Fictional Example Student';"))=="Fictional Example Operator"?new object():null,"persisted edit");
                ui.OpenStudent();
                Identity.Check(ui.Find("Father Occupation",ControlType.Edit).AsTextBox().Text=="Fictional Example Operator","Reopened occupation differs.");
            },"Allowed field edit committed and survives reopening.");
            report.Run("L2-ADM-11",ui,()=>{
                ui.Fill("Father Occupation","Fictional Cancelled Draft");
                ui.Click("Student Profile",ControlType.Button);
                var dialog=ui.Window("Unsaved Changes");
                ui.Screenshot("L2-ADM-11-warning");
                ui.Click("No",ControlType.Button,dialog);
                Identity.Check(ui.Find("Father Occupation",ControlType.Edit).AsTextBox().Text=="Fictional Cancelled Draft","Draft lost on cancellation.");
                Identity.Check(Convert.ToString(Identity.Scalar("SELECT FatherOccupation FROM dbo.Students WHERE StudentName=N'Fictional Example Student';"))=="Fictional Example Operator","Unsubmitted edit persisted.");
            },"Cancelled navigation retains draft and leaves stored occupation unchanged.");
            report.Run("L2-ADM-08",ui,()=>{
                ui.Click("Student Profile",ControlType.Button);
                ui.Click("Yes",ControlType.Button,ui.Window("Unsaved Changes"));
                ui.Click("Student Admissions",ControlType.Button);ui.OpenStudent();
                Identity.Check(ui.Find("Father Occupation",ControlType.Edit).AsTextBox().Text=="Fictional Example Operator","Discarded edit returned.");
                Identity.Check(Convert.ToString(Identity.Scalar("SELECT FatherOccupation FROM dbo.Students WHERE StudentName=N'Fictional Example Student';"))=="Fictional Example Operator","Discard changed stored occupation.");
            },"Discarded occupation absent from database and reopened form.");
            if(Environment.GetEnvironmentVariable("FMSC_LEVEL2_ADMISSIONS_ONLY")=="1")return;
            FinanceWorkflows.Run(ui,report);
            PromotionWorkflows.Run(ui,report);
            MonthlyWorkflows.Run(ui,report);
            ProfileWorkflows.Run(ui,report);
            FamilyWorkflows.Run(ui,report);
            RecoveryWorkflows.Run(ui,report);
        }
        else report.BlockIds("The UI-created admission prerequisite did not complete.",
            "L2-ADM-05","L2-ADM-06","L2-ADM-07","L2-ADM-08","L2-ADM-11",
            "L2-FIN-01","L2-FIN-04","L2-FIN-05","L2-FIN-07","L2-FIN-09","L2-FIN-10","L2-FIN-11",
            "L2-PRO-01","L2-PRO-02","L2-PRO-03","L2-PRO-04","L2-PRF-01","L2-PRF-04");
    }
}
