using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

internal static class PromotionWorkflows
{
    internal static void Run(Desktop ui,Evidence report)
    {
        report.Run("L2-PRO-01",ui,()=>{
            ui.Click("Student Management",ControlType.Button);ui.Click("Student Promotion",ControlType.Button);
            ui.Select("Source Campus","Fictional Main Campus");
            ui.Select("Source Academic Year","Fictional "+DateTime.UtcNow.Year);
            ui.Select("Source Class","Class 6 (Fictional Middle School)");
            ui.Click("Load Candidates",ControlType.Button);
            ui.Find("Select All",ControlType.Button);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Enrollments;")==1,"Candidate load changed enrollments.");
        },"Candidate screen uses the UI-created student's source enrollment; count unchanged.");
        if(!report.Passed("L2-PRO-01"))return;
        report.Run("L2-PRO-02",ui,()=>{
            ui.Select("Destination Academic Year","Fictional "+(DateTime.UtcNow.Year+1));
            ui.Select("Destination Class","Class 7 (Fictional Middle School)");
            ui.Select("Destination Section","Class 7 — Section A");
            ui.Click("Select All",ControlType.Button);ui.Click("Ticked: Promote",ControlType.Button);
            ui.Click("Process Ticked",ControlType.Button);
            var dialog=ui.Window("Confirm Student Promotion");ui.Screenshot("L2-PRO-02-confirmation");
            ui.Click("No",ControlType.Button,dialog);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Enrollments;")==1,"Cancelled promotion wrote enrollment.");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.PromotionBatches;")==0,"Cancelled promotion wrote batch.");
        },"Cancellation leaves one enrollment and zero promotion batches.");
        if(!report.Passed("L2-PRO-02"))return;
        report.Run("L2-PRO-03",ui,()=>{
            ui.Click("Process Ticked",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Confirm Student Promotion"));
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.PromotionBatchStudents;")==1?new object():null,"promotion history commit");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Enrollments;")==2,"Expected source and next-year enrollments.");
            Identity.Check(Identity.Count("""
                SELECT COUNT(*) FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
                JOIN dbo.CampusClassSections S ON S.ClassSectionId=E.ClassSectionId JOIN dbo.Classes C ON C.ClassId=S.ClassId
                WHERE Y.YearName=@Year AND C.ClassName=N'Class 7' AND E.BillingReady=0;
                """,new Microsoft.Data.SqlClient.SqlParameter("@Year","Fictional "+(DateTime.UtcNow.Year+1)))==1,"Next-year placement or billing gate differs.");
        },"Two enrollments; one promotion-history row; next-year Class 7 awaits confirmed return with billing disabled.");
        report.Run("L2-PRO-04",ui,()=>{
            // A completed-processing informational dialog may still be visible.
            var ok=ui.TryFind("OK",ControlType.Button);if(ok!=null)ui.Click("OK",ControlType.Button);
            ui.Click("View Promotion History",ControlType.Button);ui.Window("Promotion History");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.PromotionBatchStudents;")==1,"History count changed.");
            ui.Click("Close application",ControlType.Button,ui.Window("Promotion History"));
            ui.Find("Load Candidates",ControlType.Button);
        },"History opens for the committed promotion and closes back to promotion details.");
    }
}
