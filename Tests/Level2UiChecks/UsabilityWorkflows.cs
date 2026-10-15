using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

internal static class UsabilityWorkflows
{
    internal static void Initial(Desktop ui,Evidence report)
    {
        report.Run("L2-UX-01",ui,()=>{
            ui.Navigate("Student Profile");ui.Find("Student",ControlType.Edit);
            ui.Navigate("Fee Collection");ui.Find("Amount Received (Rs.)",ControlType.Edit);
            ui.Navigate("Student Admissions");ui.Find("Student Name",ControlType.Edit);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Initial navigation unexpectedly created records.");
        },"Mouse navigation opened profile, collection and admission controls; zero students remained.");
        report.Run("L2-UX-03",ui,()=>{
            string date=ui.Find("First Admission",ControlType.ComboBox).AsComboBox().Value;
            ui.Find("First Admission",ControlType.ComboBox).Focus();ui.Key(VirtualKeyShort.F4);
            ui.Fill("Year","2001");ui.Key(VirtualKeyShort.ESCAPE);
            Identity.Check(ui.Find("First Admission",ControlType.ComboBox).AsComboBox().Value==date,"Cancelling calendar navigation changed the admission date.");
            ui.Click("Find Student",ControlType.Button);ui.Window("Find Student");ui.Key(VirtualKeyShort.ESCAPE);
            ui.Find("Student Name",ControlType.Edit);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Dialog cancellation created records.");
        },"Esc cancelled calendar year navigation and closed student search; admission date and stored records unchanged.");
        report.Run("L2-UX-04",ui,()=>{
            ui.Click("Find Student",ControlType.Button);var dialog=ui.Window("Find Student");
            ui.Fill("Search","Fictional No Matching Record",dialog);ui.Click("Search",ControlType.Button,dialog);
            ui.Find("No students match the selected criteria.",ControlType.Text,dialog);
            Identity.Check(!ui.Find("Open Student",ControlType.Button,dialog).IsEnabled,"Empty results offered an open action.");
            ui.Screenshot("L2-UX-04-empty-results");ui.Click("Reset",ControlType.Button,dialog);
            Identity.Check(ui.Find("Search",ControlType.Edit,dialog).AsTextBox().Text=="","Reset did not clear search criteria.");
            ui.Click("Close",ControlType.Button,dialog);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Empty-grid search or reset changed records.");
        },"No-match empty grid, disabled open action, reset and dialog close all worked without writes.");
    }
    internal static void CaptureViewports(Desktop ui,Evidence report)
    {
        var row=report.Scenarios.Single(s=>s.Id=="L2-UX-05");
        try
        {
            ui.Navigate("Student Admissions");ui.OpenStudent();
            var main=ui.Window("School & College Management System");
            using var proof=Identity.Open();
            main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            main.Patterns.Transform.Pattern.Resize(1280,720);
            Desktop.Wait(()=>Math.Abs(main.BoundingRectangle.Width-1280)<2&&Math.Abs(main.BoundingRectangle.Height-720)<2?main:null,"actual 1280x720 window bounds");
            ui.Find("Student Name",ControlType.Edit);ui.Screenshot("L2-UX-05-1280x720");
            main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            Desktop.Wait(()=>main.Patterns.Window.Pattern.WindowVisualState.Value==WindowVisualState.Maximized?main:null,"maximized window state");
            ui.Find("Student Name",ControlType.Edit);ui.Screenshot("L2-UX-05-maximized");
            row.Actual="Captured admission at 1280x720 and maximized on the current display. Manual visual review of these and workflow screenshots is pending.";
            row.Evidence="L2-UX-05-1280x720.png, L2-UX-05-maximized.png";
            row.DatabaseVerification="Owned catalog identity checked; viewport changes perform no database writes.";
        }
        catch(Exception ex)
        {
            row.Status="Blocked";row.Actual="Viewport capture could not complete ("+ex.GetType().Name+").";
        }
        report.BlockIds("Only the current monitor/DPI was available for this run. Other actual desktop resolutions and 125/150 percent scaling were not exercised; app-window resizing is reported separately.","L2-UX-06");
        report.Save();
    }
}
