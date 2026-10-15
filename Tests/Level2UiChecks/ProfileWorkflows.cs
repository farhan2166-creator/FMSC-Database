using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

internal static class ProfileWorkflows
{
    internal static void Run(Desktop ui,Evidence report)
    {
        report.Run("L2-PRF-01",ui,()=>{
            var history=ui.TryWindow("Promotion History");
            if(history!=null)ui.Click("Close application",ControlType.Button,history);
            ui.Click("Student Profile",ControlType.Button);ui.PickStudent("Student");
            Desktop.Wait(()=>ui.Window().FindFirstDescendant(cf=>cf.ByName("Open Family / Siblings")) is {IsEnabled:true} b?b:null,"loaded student profile");
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students WHERE StudentName=N'Fictional Example Student';")==1,"Profile student identity differs.");
        },"Profile loaded for the one UI-created student; identity verified in the owned database.");
        if(!report.Passed("L2-PRF-01"))return;
        report.Run("L2-PRF-04",ui,()=>{
            ui.Click("Open Admissions",ControlType.Button);
            Identity.Check(ui.Find("Student Name",ControlType.Edit).AsTextBox().Text=="Fictional Example Student","Profile shortcut lost student identity.");
            ui.Click("Student Profile",ControlType.Button);
            ui.PickStudent("Student");
            ui.Find("Open Family / Siblings",ControlType.Button);
        },"Profile/admissions shortcut carries the same committed student; profile can be reopened.");
    }
}
