using System.Data;
using System.Reflection;
using System.Xml.Linq;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Security;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static IEnumerable<Control> ReleaseReportControls(Control parent)
    {
        foreach(Control child in parent.Controls) { yield return child; foreach(var nested in ReleaseReportControls(child))yield return nested; }
    }
    private static void CheckReleaseReports()
    {
        int user=CurrentUser.UserId;string name=CurrentUser.Username,role=CurrentUser.RoleName;bool mustChange=CurrentUser.MustChangePassword;
        var permissions=CurrentUser.GetPermissions().ToArray();
        try
        {
            CurrentUser.SignIn(1,"Report fixture","Administrator",false,Array.Empty<string>());
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            using var collections=new CollectionsReceiptsReportPage();
            var method=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("PaymentMethod",flags)!.GetValue(collections)!;
            Check(method.Items.Cast<string>().Contains("Account Credit"),"Collections has an explicit Account Credit filter");
            var view=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("View",flags)!.GetValue(collections)!;
            var group=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("GroupBy",flags)!.GetValue(collections)!;
            Check(group.Enabled&&group.Items.Cast<string>().SequenceEqual(new[]{"Whole School","Campus","Class","Section"}),"Receipt grouping choices remain available");
            view.SelectedItem="Summary";Check(group.Enabled&&group.Items.Contains("Month"),"Collection summary keeps its original grouping choices");
            view.SelectedItem="Detailed Transactions";Check(!group.Enabled,"Transaction details do not advertise receipt grouping");
            using var outstanding=new OutstandingAgingReportPage();
            var outView=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("View",flags)!.GetValue(outstanding)!;
            var days=(ModernTextBox)typeof(OutstandingAgingReportPage).GetField("maximumDays",flags)!.GetValue(outstanding)!;
            outView.SelectedItem="Aging";Check(!days.Enabled,"Exact-day filters are disabled for aggregate aging buckets");
            outView.SelectedItem="Students";Check(days.Enabled,"Exact-day filters return for student rows");
            var statement=(ModernButton)typeof(OutstandingAgingReportPage).GetField("openStatement",flags)!.GetValue(outstanding)!;
            Check(!statement.Enabled&&statement.Parent is not null,"Open Statement starts disabled and is in the filter actions");
            var a4=(ModernButton)typeof(CanonicalFinanceReportPage).GetField("_classOutstandingButton",flags)!.GetValue(outstanding)!;
            days.Text="30";Check(!a4.Enabled,"Full-location A4 is disabled for exact-day filters");
            days.Text="";Check(a4.Enabled,"Full-location A4 returns when extra filters clear");
            Check(!collections.IsHandleCreated&&!outstanding.IsHandleCreated,"Form inspection executes no SQL initialization");
            CurrentUser.SignIn(2,"Reports only","Viewer",false,new[]{"Reports.View"});
            using var reportsOnly=new ReportsHomePage((Action<Func<Control>,string,string>)((_,_,_)=>{}));
            Check(!ReleaseReportControls(reportsOnly).Any(c=>c.Text=="Student Credit Balance Register"),"Credit tile is absent without fee-view permission");
            using var restricted=new OutstandingAgingReportPage();
            var denied=(ModernButton)typeof(OutstandingAgingReportPage).GetField("openStatement",flags)!.GetValue(restricted)!;
            Check(!denied.Visible&&!denied.Enabled,"Open Statement is hidden without statement permission");
            CurrentUser.SignIn(1,"Report fixture","Administrator",false,Array.Empty<string>());
            var source=new DataTable();
            foreach(string field in new[]{"Receipt / Reference","Student / Family","Campus","Class / Section"})source.Columns.Add(field);
            foreach(string field in new[]{"Money Received","Refunds","Corrections","Credit Applied","Adjusted Collection"})source.Columns.Add(field,typeof(decimal));
            for(int i=0;i<1200;i++)source.Rows.Add("R-FIX-"+i,"Synthetic Student "+i,i<700?"Boys":"Girls","Class 1 – A",100m,0m,0m,0m,100m);
            CollectionReceiptGrouping.Apply(source,"Campus");
            using var grid=new DataGridView {AutoGenerateColumns=false};
            foreach(DataColumn field in source.Columns)grid.Columns.Add(new DataGridViewTextBoxColumn {Name=field.ColumnName,DataPropertyName=field.ColumnName,HeaderText=field.ColumnName});
            var exported=ReportExportBuilder.Build(source,grid.Columns.Cast<DataGridViewColumn>(),Array.Empty<string>());
            Check(CollectionReceiptGrouping.Layout(exported) is not null,"Print/export projection preserves receipt group metadata");
            var details=CollectionReceiptGrouping.DetailsOnly(exported);
            Check(details.Rows.Count==1200&&details.AsEnumerable().Sum(r=>r.Field<decimal>("Money Received"))==120000m,"CSV details cannot double-count subtotals");
            var built=GridReportPreview.Build("Synthetic School","Collections & Receipts","Report generated: 01-Oct-2026 09:00",exported,"Fixture");
            Check(built.Data.Rows.Count==1200,"Printed receipt dataset excludes synthetic headings and totals");
            var xml=XDocument.Parse(built.Definition);XNamespace ns=xml.Root!.Name.Namespace;
            Check(xml.Descendants(ns+"GroupExpression").Any(e=>e.Value=="=Fields!GroupKey.Value"),"Print creates actual RDLC receipt groups");
            Check(xml.Descendants(ns+"Value").Any(e=>e.Value.Contains("01-Oct-2026 09:00")),"Captured report time stays separate from today's print time");
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(built.Definition));report.DataSources.Add(new ReportDataSource("ReportData",built.Data));
            byte[] pdf=report.Render("PDF");
            using var parsed=PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Check(parsed.PageCount>1,"Grouped receipt PDF renders across multiple pages");
            string folder=Path.GetFullPath("artifacts/release-reports");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"grouped-collections.pdf"),pdf);
        }
        finally { CurrentUser.SignIn(user,name,role,mustChange,permissions); }
    }
}
