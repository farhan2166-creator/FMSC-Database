using System.Diagnostics;
using Microsoft.Data.SqlClient;
using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;

internal sealed class OwnedConnectionInterruption : IDisposable
{
    private bool stopped,removed;
    internal OwnedConnectionInterruption()
    {
        using var proof=Identity.Open(); // exact owner/catalog/private path before fault
        using(var exclusive=new SqlCommand("""
            IF (SELECT COUNT(*) FROM sys.databases WHERE database_id>4)<>1
                THROW 51000,'Unexpected other catalog; fault refused',1;
            IF EXISTS(SELECT 1 FROM sys.database_files WHERE LEFT(physical_name,LEN(@Prefix))<>@Prefix)
                THROW 51000,'Unexpected database file; fault refused',1;
            """,proof))
        {
            exclusive.Parameters.AddWithValue("@Prefix",Path.Combine(Identity.Root,"database")+Path.DirectorySeparatorChar);
            exclusive.ExecuteNonQuery();
        }
        Run("SqlLocalDB","stop",Identity.Catalog,"-k");stopped=true;
        try {Run("SqlLocalDB","delete",Identity.Catalog);removed=true;}
        catch {Restore();throw;}
    }
    private static void Run(string tool,params string[] args)
    {
        var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in args)start.ArgumentList.Add(arg);
        using var p=Process.Start(start)??throw new InvalidOperationException("Private fault helper did not start.");
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        if(!p.WaitForExit(60000)){p.Kill();throw new TimeoutException("Private fault helper exceeded its time limit.");}
        Task.WhenAll(output,error).GetAwaiter().GetResult();
        if(p.ExitCode!=0)throw new InvalidOperationException("Owned LocalDB fault/recovery command failed; private state retained.");
    }
    internal void Restore()
    {
        if(!stopped)return;
        string root=Identity.Root;
        if(removed)
        {
            string data=Path.Combine(root,"database","fixture.mdf"),log=Path.Combine(root,"database","fixture_log.ldf");
            Identity.Check(File.Exists(data)&&File.Exists(log),"Original owned fixture files are missing.");
            Run("SqlLocalDB","create",Identity.Catalog,"-s");
            string Escape(string value)=>value.Replace("'","''");
            string query=$"""
                IF EXISTS(SELECT 1 FROM sys.databases WHERE database_id>4) THROW 51000,'Unexpected catalog in recreated private instance',1;
                CREATE DATABASE [{Identity.Catalog}] ON (FILENAME=N'{Escape(data)}'),(FILENAME=N'{Escape(log)}') FOR ATTACH;
                """;
            Run("sqlcmd","-S",Identity.Endpoint,"-E","-d","master","-b","-l","30","-t","90","-Q",query);
        }
        else Run("SqlLocalDB","start",Identity.Catalog);
        SqlConnection.ClearAllPools();using var proof=Identity.Open();
        stopped=false;removed=false;
    }
    public void Dispose()=>Restore();
}

internal static class RecoveryWorkflows
{
    internal static void Run(Desktop ui,Evidence report)
    {
        if(!report.Passed("L2-FIN-03")) {report.BlockIds("Current-year posting/return prerequisites did not complete.","L2-UX-07");return;}
        if(DateTime.Today.Month<3) {report.BlockIds("The March read-only preview used for fault/retry is not eligible yet on the current calendar date.","L2-UX-07");return;}
        report.Run("L2-UX-07",ui,()=>{
            ui.Navigate("Fee Posting","Daily Work");
            Identity.Check(ui.Find("Academic Year",ControlType.ComboBox).AsComboBox().Value=="Fictional "+DateTime.UtcNow.Year,"Wrong current posting year.");
            ui.Select("Campus","Fictional Main Campus");ui.Select("Class","Class 7 (Fictional Middle School)");
            ui.Select("Fee Type","Tuition fee");
            ui.Select("Billing Month",$"March {DateTime.UtcNow.Year}  -  1 to post");
            int payments=Identity.Count("SELECT COUNT(*) FROM dbo.FeePayments;"),charges=Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges;");
            decimal paid=FinanceWorkflows.Paid(),allocated=FinanceWorkflows.Allocated(),charged=FinanceWorkflows.Charges();
            using(var interruption=new OwnedConnectionInterruption())
            {
                ui.ReadOnlyFaultRefresh();
                var recovery=ui.Window("School Records Connection");
                Desktop.Wait(()=>ui.Find("Reconnect",ControlType.Button,recovery) is {IsEnabled:true} button?button:null,"connection failure feedback",45);
                ui.Screenshot("L2-UX-07-interrupted-connection");
                interruption.Restore();
                ui.Click("Reconnect",ControlType.Button,recovery);
                Desktop.Wait(()=>ui.TryFind("Connection successful",ControlType.Text,recovery),"successful connection feedback",45);
                ui.Click("Close",ControlType.Button,recovery);
                Desktop.Wait(()=>ui.TryWindow("School Records Connection")==null?new object():null,"successful reconnect",45);
            }
            ui.Click("Load Eligible Fees",ControlType.Button);
            Desktop.Wait(()=>ui.Find("Post Eligible Fees",ControlType.Button) is {IsEnabled:true} button?button:null,"eligible read preview after reconnection");
            Identity.Check(!ui.Process!.HasExited&&Identity.Count("SELECT COUNT(*) FROM dbo.FeePayments;")==payments&&
                Identity.Count("SELECT COUNT(*) FROM dbo.BillingCharges;")==charges,"Read retry crashed or changed financial records.");
            Identity.Check(FinanceWorkflows.Paid()==paid&&FinanceWorkflows.Allocated()==allocated&&FinanceWorkflows.Charges()==charged,"Connection interruption changed financial amounts.");
        },"Owned LocalDB endpoint interrupted and restored from its original private files; UI reconnect/read retry succeeds without changing charge/payment counts.");
    }
}
