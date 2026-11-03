using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckFinanceReportAsync()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var actor=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Grants:CurrentUser.GetPermissions().ToArray());
        int fixtureActor=Random.Shared.Next(1800000000,2000000000);
        string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"FMSC_SMS","ReportLayouts",$"user-{fixtureActor}.json");
        if(File.Exists(settings))throw new Exception("Fixture refuses an existing settings file.");
        try
        {
            CurrentUser.SignIn(fixtureActor,"Async report fixture","Accountant",false,new[]{"Reports.View"});
            int uiThread=Environment.CurrentManagedThreadId,readThread=0,completionThread=0;
            var response=new TaskCompletionSource<DataSet>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var page=new BillingDiscountsReportPage((parameters,token)=>{
                readThread=Environment.CurrentManagedThreadId;
                Check(parameters.Single(p=>p.ParameterName=="@View").Value.Equals("Billing Summary"),"Billing Summary parameters are captured before the read");
                return response.Task;
            }){BindingContext=new BindingContext()};
            var view=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("View",flags)!.GetValue(page)!;
            _=view.Handle; // Enforce real WinForms cross-thread checks on the view.
            bool failureDialog=false;
            using var modalGuard=new System.Windows.Forms.Timer{Interval=30};
            modalGuard.Tick+=(_,_)=>{
                foreach(var form in Application.OpenForms.Cast<Form>().Where(form=>form.Modal).ToArray())
                {failureDialog=true;form.DialogResult=DialogResult.Cancel;form.Close();}
            };
            modalGuard.Start();
            var run=(Task)typeof(CanonicalFinanceReportPage).GetMethod("RunAsync",flags)!.Invoke(page,null)!;
            Check(readThread==uiThread && !run.IsCompleted,"The real report waits asynchronously after capturing its UI settings");
            using var data=new DataSet();var rows=new DataTable();rows.Columns.Add("Group");rows.Columns.Add("Campus");rows.Columns.Add("Charges",typeof(int));
            foreach(string field in new[]{"Gross Charges","Discounts Applied","Net Charges"})rows.Columns.Add(field,typeof(decimal));
            rows.Rows.Add("03-11-2026","Fictional Campus",1,1000m,100m,900m);data.Tables.Add(rows);
            var totals=new DataTable();foreach(string field in new[]{"Gross Billed","Discounts","Net Billed"})totals.Columns.Add(field,typeof(decimal));
            totals.Rows.Add(1000m,100m,900m);data.Tables.Add(totals);
            var complete=Task.Run(()=>{completionThread=Environment.CurrentManagedThreadId;response.SetResult(data);});
            PumpUntil(()=>run.IsCompleted,"Delayed Billing Summary completes",TimeSpan.FromSeconds(15));run.GetAwaiter().GetResult();complete.GetAwaiter().GetResult();
            Check(completionThread!=uiThread && !failureDialog,"A provider response on another thread generates no cross-thread failure dialog");
            var displayed=(DataTable?)typeof(CanonicalFinanceReportPage).GetField("_displayed",flags)!.GetValue(page);
            Check(displayed is not null && displayed.Rows.Count==2,"Billing Summary lands its data and one authoritative total row");
            Check(displayed!.Columns.Contains("Charge Date") && displayed.Columns.Contains("Gross Billed") && displayed.Columns.Contains("Discounts") && displayed.Columns.Contains("Net Billed"),"Actual Billing Summary transformations still supply meaningful headings");
            Check(displayed.Rows[0].Field<decimal>("Net Billed")==900m && displayed.Rows[1].Field<decimal>("Net Billed")==900m && displayed.Rows[1].Field<string>("Charge Date")=="TOTAL","Delayed results retain billed values and their authoritative total");
            var print=(ModernButton)typeof(CanonicalFinanceReportPage).GetField("_print",flags)!.GetValue(page)!;
            var export=(ModernButton)typeof(CanonicalFinanceReportPage).GetField("_export",flags)!.GetValue(page)!;
            Check(print.Enabled && export.Enabled,"Successful delayed Billing Summary enables print and export");
        }
        finally
        {
            CurrentUser.SignIn(actor.UserId,actor.Username,actor.RoleName,actor.MustChangePassword,actor.Grants);
            if(File.Exists(settings))File.Delete(settings);
        }
    }
}
