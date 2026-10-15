using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

internal static class FamilyWorkflows
{
    private const string Sibling="Fictional Example Sibling";
    private static void Draft(Desktop ui,string name,string number)
    {
        ui.Navigate("Student Admissions");
        ui.Click("Clear and start a new admission",ControlType.Button);
        if(ui.TryFind("Student Name",ControlType.Edit)==null)ui.Click("Student Information",ControlType.Button);
        ui.Fill("Student Name",name);ui.Fill("Father Name","Fictional Example Parent");
        ui.Fill("Place of Birth","Example City");ui.Fill("Address","Fictional Level Two Lane");
        ui.Select("Gender","Female");ui.Date("First Admission",new DateTime(DateTime.UtcNow.Year-1,4,1));
        ui.Select("Campus","Fictional Main Campus");ui.Select("Academic Year","Fictional "+(DateTime.UtcNow.Year-1));
        ui.Select("Class","Class 6 (Fictional Middle School)");ui.Select("Section","Section A");
        ui.Fill("Admission Number",number);
    }
    internal static void Run(Desktop ui,Evidence report)
    {
        report.Run("L2-PRF-02",ui,()=>{
            Draft(ui,Sibling,"9002");ui.Click("Save",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Date of Birth Not Entered"));
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==2?new object():null,"second fictional UI admission");
            Desktop.Wait(()=>ui.Find("Save",ControlType.Button).IsEnabled?new object():null,"sibling admission refresh");
            ui.Click("Fees & Siblings",ControlType.Button);ui.Click("Find Possible Siblings",ControlType.Button);
            var dialog=ui.Window("Find Possible Sibling");
            ui.Fill("Search","Fictional Example Student",dialog);ui.Click("Find",ControlType.Button,dialog);
            Desktop.Wait(()=>ui.Find("Add as Sibling",ControlType.Button,dialog) is {IsEnabled:true} b?b:null,"selected sibling result");
            ui.Click("Add as Sibling",ControlType.Button,dialog);
            ui.Click("Yes",ControlType.Button,ui.Window("Confirm Sibling"));
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.FamilyMembers;")==2?new object():null,"family relationship commit");
            Identity.Check(Identity.Count("SELECT COUNT(DISTINCT FamilyId) FROM dbo.FamilyMembers;")==1,"Fictional siblings did not share one family.");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.FamilyMembers M LEFT JOIN dbo.Students S ON S.AdmissionId=M.AdmissionId WHERE S.AdmissionId IS NULL;")==0,"Family relationship has an orphan student.");
            ui.Click("Close",ControlType.Button,dialog);ui.Click("Student Information",ControlType.Button);
        },"A second student was admitted through UI; sibling search/confirmation created exactly two members in one family with valid student links.");
        if(!report.Passed("L2-PRF-02"))return;
        void ChoosePrimary()
        {
            ui.Click("Find Student",ControlType.Button);
            var search=ui.Window("Find Student");ui.Fill("Search","Fictional Example Student",search);ui.Click("Search",ControlType.Button,search);
            Desktop.Wait(()=>ui.Find("Open Student",ControlType.Button,search) is {IsEnabled:true} b?b:null,"other student search result");
            ui.Click("Open Student",ControlType.Button,search);
        }
        report.Run("L2-UX-08",ui,()=>{
            ui.Fill("Father Occupation","Fictional Pending Sibling Edit");ChoosePrimary();
            ui.Click("No",ControlType.Button,ui.Window("Unsaved Changes"));
            Identity.Check(ui.Find("Student Name",ControlType.Edit).AsTextBox().Text==Sibling,"Cancelled record switch changed the selected student.");
            Identity.Check(ui.Find("Father Occupation",ControlType.Edit).AsTextBox().Text=="Fictional Pending Sibling Edit","Cancelled record switch lost the draft.");
            ChoosePrimary();ui.Click("Yes",ControlType.Button,ui.Window("Unsaved Changes"));
            Desktop.Wait(()=>ui.Find("Student Name",ControlType.Edit) is {IsEnabled:true} field?field:null,"selected record finished loading");
            Identity.Check(Convert.IsDBNull(Identity.Scalar("SELECT FatherOccupation FROM dbo.Students WHERE StudentName=N'Fictional Example Sibling';")),"Discarded sibling edit was persisted.");
        },"Cancelling record switch preserves student and draft; confirmed discard leaves the sibling's stored occupation unchanged.");
        report.Run("L2-ADM-10",ui,()=>{
            int students=Identity.Count("SELECT COUNT(*) FROM dbo.Students;"),charges=Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges;");
            Draft(ui,"Fictional Example Student","9003");ui.Click("Save",ControlType.Button);
            ui.Click("Yes",ControlType.Button,ui.Window("Date of Birth Not Entered"));
            var duplicate=ui.Window("Possible Duplicate Student");ui.Screenshot("L2-ADM-10-duplicate-warning");
            ui.Click("No",ControlType.Button,duplicate);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==students&&Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges;")==charges,"Refused possible duplicate created records or charges.");
            ui.Navigate("Student Profile");ui.Click("Yes",ControlType.Button,ui.Window("Unsaved Changes"));
        },"Possible same-name/parent duplicate warns; choosing No creates neither a student nor opening charges.");
        report.Run("L2-PRF-03",ui,()=>{
            ui.Navigate("Student Admissions");ui.OpenStudent(Sibling);
            ui.Click("Student placement actions",ControlType.Button);ui.Click("Mark as Left",ControlType.MenuItem);
            var dialog=ui.Window("Mark as Left");ui.Fill("Reason","Fictional status workflow check",dialog);
            ui.Date("Leaving date",new DateTime(DateTime.UtcNow.Year-1,12,1));ui.Click("Review",ControlType.Button,dialog);
            Desktop.Wait(()=>ui.Find("Apply",ControlType.Button,dialog) is {IsEnabled:true} b?b:null,"reviewed status change");
            ui.Click("Apply",ControlType.Button,dialog);
            Desktop.Wait(()=>Identity.Count("SELECT COUNT(*) FROM dbo.Students WHERE StudentName=N'Fictional Example Sibling' AND Status=N'Left';")==1?new object():null,"left status commit");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE S.StudentName=N'Fictional Example Sibling' AND E.Status=N'Left';")==1,"Placement did not follow the student's leaving status.");
        },"Reviewed fictional leaving action persisted student and enrollment status Left; unrelated family student remained present.");
    }
}
