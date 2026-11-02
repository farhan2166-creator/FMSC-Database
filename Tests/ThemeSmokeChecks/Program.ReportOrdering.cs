using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Certificates;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckReportOrderingControls()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Grants:CurrentUser.GetPermissions().ToArray());
        int actor=Random.Shared.Next(1800000000,2000000000);
        string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"FMSC_SMS","ReportLayouts",$"user-{actor}.json");
        if(File.Exists(settings))throw new Exception("Fixture refuses an existing user settings file.");
        try
        {
            CurrentUser.SignIn(actor,"Order fixture","Accountant",false,new[]{"Reports.View","Fees.View","Certificates.Create","Students.Leave"});
            object Field(CanonicalFinanceReportPage page,string name)=>typeof(CanonicalFinanceReportPage).GetField(name,flags)!.GetValue(page)!;
            using var page=new OutstandingAgingReportPage {BindingContext=new BindingContext()};
            using var locations=new DataTable();locations.Columns.Add("Id",typeof(int));locations.Columns.Add("Name");locations.Rows.Add(1,"Synthetic location");
            foreach(string field in new[]{"Campus","Year","Class","Section"})
                ((LookupBox)Field(page,field)).Bind(locations,"Id","Name","All");
            typeof(CanonicalFinanceReportPage).GetField("_lookupsReady",flags)!.SetValue(page,true);
            var order=(ModernComboBox)Field(page,"_orderBy");
            void Select(ModernComboBox combo,string text)=>combo.SelectedItem=combo.Items.Cast<object>().Single(item=>item.ToString()==text);
            var bar=ReleaseReportControls(page).OfType<ReportSavedViewsBar>().Single();bar.RefreshAvailability();
            Select(order,"Outstanding: highest first");
            var saved=bar.Snapshot();
            Check(saved["Order by"]=="choice:Outstanding: highest first","Finance saved views capture their selected report order");
            var view=(ModernComboBox)Field(page,"View");view.SelectedItem="Aging";
            Check(!order.Items.Cast<object>().Any(item=>item.ToString()!.StartsWith("Days overdue")),"Aging buckets do not offer unavailable individual day ordering");
            bar.ApplyState(saved);
            Check(view.Text=="Students" && order.Text=="Outstanding: highest first","Applying a saved view restores the view before its sort choices");
            saved.Remove("Order by");bar.ApplyState(saved);
            Check(order.Text=="Default","Earlier saved views retain the report's default order");
            typeof(CanonicalFinanceReportPage).GetField("_displayed",flags)!.SetValue(page,new DataTable());
            string signature=(string)typeof(CanonicalFinanceReportPage).GetMethod("Signature",flags)!.Invoke(page,null)!;
            typeof(CanonicalFinanceReportPage).GetField("_appliedSignature",flags)!.SetValue(page,signature);
            var print=(ModernButton)Field(page,"_print");print.Enabled=true;
            Select(order,"Outstanding: highest first");
            Check(!print.Enabled && signature!=(string)typeof(CanonicalFinanceReportPage).GetMethod("Signature",flags)!.Invoke(page,null)!,
                "Changing order marks existing results stale and refuses printing until Run Report");
            using var billing=new BillingDiscountsReportPage();
            var billingView=(ModernComboBox)Field(billing,"View");billingView.SelectedItem="Billing Summary";
            var grouping=(ModernComboBox)Field(billing,"GroupBy");grouping.SelectedItem="Month";
            var billingOrder=(ModernComboBox)Field(billing,"_orderBy");
            Check(billingOrder.Items.Cast<ReportRowOrder.Choice>().Any(c=>c.Column=="Charge Month"),"Billing group ordering follows the current summary's renamed column");
            billingView.SelectedItem="Concession Summary";
            Check(!billingOrder.Items.Cast<ReportRowOrder.Choice>().Any(c=>c.Column=="Net Billed"),"Concession summaries offer their own applicable ordering");
            using var certificates=new CertificateIssueRegisterPage();
            var certificateOrder=(ModernComboBox)typeof(CertificateIssueRegisterPage).GetField("orderBy",flags)!.GetValue(certificates)!;
            Select(certificateOrder,"Recorded on: oldest first");
            var certBar=ReleaseReportControls(certificates).OfType<ReportSavedViewsBar>().Single();
            var certView=certBar.Snapshot();certificateOrder.SelectedIndex=0;certBar.ApplyState(certView);
            Check(certificateOrder.Text=="Recorded on: oldest first","Certificate register saved views restore their sort order");
            Check(new Control[]{page,billing,certificates}.All(p=>!p.IsHandleCreated),"Report ordering controls initialize without deferred database reads");
        }
        finally
        {
            CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Grants);
            if(File.Exists(settings))File.Delete(settings);
        }
    }
}
