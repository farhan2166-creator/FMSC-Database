using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Reports.Certificates;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Reports.Students;

internal static partial class Program
{
    private static void CheckSavedReportViews()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        int actor=CurrentUser.UserId;string name=CurrentUser.Username,role=CurrentUser.RoleName;bool change=CurrentUser.MustChangePassword;
        string[] grants=CurrentUser.GetPermissions().ToArray();
        int fixtureActor=Random.Shared.Next(1800000000,2000000000);
        string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"FMSC_SMS","ReportLayouts",$"user-{fixtureActor}.json");
        if (File.Exists(settings)) throw new Exception("Fixture refuses an existing user settings file.");
        try
        {
            CurrentUser.SignIn(fixtureActor,"Saved-view fixture","Administrator",false,Array.Empty<string>());
            using var scope=new LookupBox { BindingContext=new BindingContext() };
            var places=new DataTable();places.Columns.Add("Id",typeof(int));places.Columns.Add("Name");places.Rows.Add(1,"Boys");places.Rows.Add(2,"Girls");
            scope.Bind(places,"Id","Name","All");scope.SelectedValue=2;
            using var mode=new ModernComboBox();mode.Items.AddRange(new[]{"Receipts","Summary"});mode.SelectedIndex=0;
            using var group=new ModernComboBox();
            void Groups() { group.Items.Clear();group.Items.AddRange(mode.Text=="Receipts" ? new[]{"Whole School","Campus"} : new[]{"Day","Month"});group.SelectedIndex=0; }
            mode.SelectedIndexChanged+=(_,_)=>Groups();Groups();group.SelectedItem="Campus";
            using var from=new ModernDatePicker {ShowCheckBox=true,Value=new DateTime(2026,10,1),Checked=false};
            using var to=new ModernDatePicker {ShowCheckBox=true,Value=new DateTime(2026,10,23),Checked=false};
            using var minimum=new ModernNumericTextBox {Maximum=1000000000m,Value=5000m};
            using var overdue=new CheckBox {Checked=true};
            var fields=new (string Key,Control Control)[]{("View",mode),("Group",group),("Campus",scope),("From",from),("To",to),("Minimum",minimum),("Overdue",overdue)};
            var original=ReportFilterState.Capture(fields);
            mode.SelectedItem="Summary";scope.SelectedValue=1;overdue.Checked=false;minimum.Value=100;
            ReportFilterState.Restore(fields,original);
            Check(mode.Text=="Receipts" && group.Text=="Campus" && scope.SelectedId==2,"Restoration handles dependent grouping choices and exact lookup IDs");
            Check(overdue.Checked && minimum.Value==5000 && !from.Checked && !to.Checked,"Numeric, checkbox and unchecked date filters round-trip");
            bool ready=true;string status="";
            using var bar=new ReportSavedViewsBar("synthetic.views",()=>ReportFilterState.Capture(fields),state=>ReportFilterState.Restore(fields,state),(text,_)=>status=text,()=>ready,from,to);
            var saved=bar.Snapshot();saved["View"]="choice:Summary";saved["Group"]="choice:Month";saved["Campus"]="id:999";
            bool refused=false;try { bar.ApplyState(saved); } catch(ReportViewValidationException) {refused=true;}
            Check(refused && ReportFilterState.Capture(fields).OrderBy(p=>p.Key).SequenceEqual(original.OrderBy(p=>p.Key)),"An unavailable saved campus rolls back earlier view/group changes instead of choosing All");
            var relative=bar.Snapshot();relative["$dates"]="This month";bar.ApplyState(relative);
            DateTime today=DateTime.Today;Check(from.Checked && to.Checked && from.Value.Date==new DateTime(today.Year,today.Month,1)
                && to.Value.Date==new DateTime(today.Year,today.Month,DateTime.DaysInMonth(today.Year,today.Month)),"Relative saved dates resolve when applied");
            Check(bar.Snapshot()["$dates"]=="This month","A relative view remains relative after restoration");
            to.Value=to.Value.AddDays(-1);Check(bar.DatePeriod==ReportDatePeriod.Fixed,"Manual date edits switch to fixed dates");
            ready=false;bar.RefreshAvailability();var before=ReportFilterState.Capture(fields);bar.ApplyState(relative);
            Check(status.Length>0 && ReportFilterState.Capture(fields).SequenceEqual(before),"Busy/unready reports refuse saved-view application without changing filters");
            ready=true;
            using var dialog=new ReportColumnDialog("Synthetic columns",new[]{new ReportColumnDef("Reference","Reference"),new ReportColumnDef("Amount","Amount")},new[]{"Amount"},new[]{"Reference","Amount"},new[]{"Reference"});
            var list=(ListView)typeof(ReportColumnDialog).GetField("list",flags)!.GetValue(dialog)!;
            var reference=list.Items.Cast<ListViewItem>().Single(i=>((ReportColumnDef)i.Tag!).Key=="Reference");
            Check(reference.Checked,"Required receipt/total references are selected even when absent from saved columns");
            _=list.Handle;
            reference.Checked=false;Check(reference.Checked,"Column chooser cannot hide a required group reference");
            using var finance=new CollectionsReceiptsReportPage {BindingContext=new BindingContext()};
            foreach(string field in new[]{"Campus","Year","Class","Section"})
            {
                var lookup=(LookupBox)typeof(CanonicalFinanceReportPage).GetField(field,flags)!.GetValue(finance)!;
                lookup.Bind(places,"Id","Name","All");
            }
            typeof(CanonicalFinanceReportPage).GetField("_lookupsReady",flags)!.SetValue(finance,true);
            var financeBar=ReleaseReportControls(finance).OfType<ReportSavedViewsBar>().Single();financeBar.RefreshAvailability();
            var financeGroup=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("GroupBy",flags)!.GetValue(finance)!;financeGroup.SelectedItem="Campus";
            var data=new DataTable();data.Columns.Add("Receipt / Reference");data.Columns.Add("Campus");data.Columns.Add("Money Received",typeof(decimal));data.Columns.Add("AdmissionId",typeof(int));
            foreach(string amount in new[]{"Refunds","Corrections","Credit Applied","Adjusted Collection"}) data.Columns.Add(amount,typeof(decimal));
            data.Rows.Add("R-FIX-1","Boys",100m,1,0m,0m,0m,100m);CollectionReceiptGrouping.Apply(data,"Campus");
            typeof(V2Page).GetMethod("Bind",flags)!.Invoke(finance,new object[]{data});
            typeof(CanonicalFinanceReportPage).GetMethod("OnReportPageBound",flags)!.Invoke(finance,null);
            typeof(V2Page).GetMethod("SetReportColumns",flags)!.Invoke(finance,new object[]{new[]{"Money Received"}});
            var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(finance)!;
            Check(grid.Columns["Receipt / Reference"].Visible && grid.Columns["Money Received"].Visible && !grid.Columns["AdmissionId"].Visible,
                "Finance column preferences retain group context and cannot reveal raw IDs");
            var preset=financeBar.Snapshot();Check(preset["$columns"].Contains("Receipt / Reference") && !preset["$columns"].Contains("AdmissionId"),"Saved finance view captures only allowed selected columns");
            var projected=ReportExportBuilder.Build(data,grid.Columns.Cast<DataGridViewColumn>(),Array.Empty<string>());
            Check(projected.Columns.Count==2 && CollectionReceiptGrouping.Layout(projected) is not null,"Column projection preserves receipt grouping metadata for print/export");
            financeBar.ApplyState(preset);Check(grid.DataSource is null,"Applying a finance view clears earlier rows and waits for Run Report");
            var rolling=financeBar.Snapshot();rolling["$dates"]="Today";financeBar.ApplyState(rolling);financeBar.ResetDates();
            Check(financeBar.DatePeriod==ReportDatePeriod.Fixed,"Clearing report filters can cancel a rolling date preset");
            using var outstanding=new OutstandingAgingReportPage();using var credit=new CreditBalanceReportPage();
            using var billing=new BillingDiscountsReportPage();using var monthly=new MonthlyFeeReconciliationPage();
            using var certificates=new CertificateIssueRegisterPage();using var sheets=new ClassSheetsPage();using var students=new StudentReportsPage();
            foreach(var page in new Control[]{outstanding,credit,billing,monthly,certificates,sheets})
                Check(ReleaseReportControls(page).OfType<ReportSavedViewsBar>().Count()==1,page.GetType().Name+": saved views use the shared toolbar");
            Check(ReleaseReportControls(students).Any(c=>c.Text=="Rename View"),"Existing Student Reports includes rename without replacing its ready-made views");
            var period=(ModernComboBox)typeof(StudentReportsPage).GetField("datePeriod",flags)!.GetValue(students)!;period.SelectedItem="Today";
            var studentFrom=(ModernDatePicker)typeof(StudentReportsPage).GetField("from",flags)!.GetValue(students)!;
            var studentTo=(ModernDatePicker)typeof(StudentReportsPage).GetField("to",flags)!.GetValue(students)!;
            Check(studentFrom.Value.Date==today && studentTo.Value.Date==today,"Existing Student Reports resolves relative periods");
            Check(new Control[]{finance,outstanding,credit,billing,monthly,certificates,sheets,students}.All(p=>!p.IsHandleCreated),"Inspecting saved-view controls performs no deferred SQL initialization");
            CurrentUser.SignIn(fixtureActor-1,"Different user","Viewer",false,new[]{"Reports.View"});var previous=scope.SelectedId;bar.ApplyState(relative);
            Check(scope.SelectedId==previous,"A toolbar from a previous signed-in user cannot apply their saved view");
        }
        finally
        {
            CurrentUser.SignIn(actor,name,role,change,grants);
            if (File.Exists(settings)) File.Delete(settings);
        }
    }
}
