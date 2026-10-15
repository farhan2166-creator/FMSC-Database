using System.Text.Json;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=2) {Console.Error.WriteLine("Usage: Level2UiChecks <private test app exe> <evidence directory>");return 2;}
        var report=new Evidence(args[1]);
        using var desktop=new Desktop(args[1]);
        try
        {
            Identity.Check(Path.GetFullPath(args[0])==Path.Combine(Identity.Root,"app","FMSC_SMS.exe"),"Only this run's private test application may launch.");
            using var c=Identity.Open();
            using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(Identity.Root,"credentials.json")));
            string password=fixture.RootElement.GetProperty("password").GetString()!;
            desktop.Launch(args[0]);
            report.Run("L2-SEC-01",desktop,()=>{desktop.Window("Sign In");desktop.Find("Username",ControlType.Edit);desktop.Find("Password",ControlType.Edit);},"Owned fixture verified; no student records.");
            if(!report.Passed("L2-SEC-01")) {report.Block("Visible startup failed; dependent UI scenarios blocked.");return 1;}
            report.Run("L2-SEC-02",desktop,()=>{
                desktop.Click("Sign In",ControlType.Button);
                desktop.Window("Sign In");
                Identity.Check(Identity.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Login validation changed students.");
            },"Zero students after empty login.");
            report.Run("L2-UX-02",desktop,()=>{
                desktop.Find("Username",ControlType.Edit).Focus();
                desktop.Key(VirtualKeyShort.TAB);
                Identity.Check(desktop.Find("Password",ControlType.Edit).Properties.HasKeyboardFocus.Value,"Tab did not focus password.");
            },"Read-only keyboard test.");
            report.Run("L2-SEC-03",desktop,()=>{
                desktop.Fill("Username","level2_admin");desktop.Fill("Password","deliberately-incorrect-fictional-value");
                desktop.Click("Sign In",ControlType.Button);
                var error=desktop.Window("Login");
                desktop.Click("OK",ControlType.Button,error);
                Identity.Check(Identity.Count("SELECT FailedLoginCount FROM dbo.Users WHERE Username=N'level2_admin';")==1,"Failed login counter not persisted.");
            },"Failed-login counter incremented to one.");
            report.Run("L2-SEC-04",desktop,()=>{
                // Clear any remaining refusal dialog before signing in.
                desktop.Fill("Username","level2_admin");desktop.Fill("Password",password);
                desktop.Key(VirtualKeyShort.RETURN);
                desktop.DashboardAfterLogin();
                Identity.Check(Identity.Count("SELECT FailedLoginCount FROM dbo.Users WHERE Username=N'level2_admin';")==0,"Successful login did not reset failure count.");
            },"Administrator authenticated; failure counter reset.");
            if(!report.Passed("L2-SEC-04")) {report.Block("Administrator login failed; dependent workflows blocked.");return 1;}
            Workflows.Run(desktop,report);
            SecurityWorkflows.Run(desktop,report,args[0],password);
            foreach(var row in report.Scenarios.Where(s=>s.Status=="Not Run"))
                row.Actual="Not executed; remaining coverage is listed in the rerun documentation. No success is claimed.";
        }
        catch(Exception ex)
        {
            report.Block("Runner prerequisite failed ("+ex.GetType().Name+"); no UI success claimed.");
            Console.Error.WriteLine("Blocked: "+ex.GetType().Name);
        }
        finally {report.Save();}
        return report.Scenarios.All(s=>s.Status=="Pass")?0:1;
    }
}
