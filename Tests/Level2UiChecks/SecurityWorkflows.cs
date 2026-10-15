using FlaUI.Core.Definitions;

internal static class SecurityWorkflows
{
    internal static void Run(Desktop ui,Evidence report,string exe,string password)
    {
        report.Run("L2-SEC-06",ui,()=>{
            ui.Key(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            ui.Click("⚙  Account",ControlType.Button);ui.Click("Sign Out",ControlType.MenuItem);
            Desktop.Wait(()=>{
                if(ui.Process!.HasExited)return new object();
                var active=ui.TryWindow("Unsaved Changes");
                if(active!=null)ui.Click("Yes",ControlType.Button,active);
                return null;
            },"signed-out application exit");
            ui.Launch(exe);ui.Window("Sign In");ui.Find("Username",ControlType.Edit);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Users;")==2,"Logout changed user records.");
        },"Sign-out closes the session; fresh launch requires authentication; two fixture accounts unchanged.");
        if(!report.Passed("L2-SEC-06"))return;
        report.Run("L2-SEC-05",ui,()=>{
            ui.Fill("Username","level2_restricted");ui.Fill("Password",password);ui.Click("Sign In",ControlType.Button);
            ui.DashboardAfterLogin();
            foreach(string name in new[]{"Student Admissions","Student Promotion","School Information","Class Fee Rates"})
            {
                var action=ui.TryFind(name,ControlType.Button);
                Identity.Check(action==null||!action.IsEnabled,"Restricted action is enabled: "+name);
            }
            ui.Click("Fee Collection",ControlType.Button);ui.Find("Amount Received (Rs.)",ControlType.Edit);
            Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Users WHERE Username=N'level2_restricted' AND FailedLoginCount=0;")==1,"Restricted login did not persist clean authentication state.");
        },"Restricted login succeeds; forbidden navigation absent/disabled; permitted collection opens; account permissions unchanged.");
    }
}
