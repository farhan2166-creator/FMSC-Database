using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    // Exercise the page's actual captions, column selection and export projection.
    private static (DataTable Rows,string Title,string Scope) ReceiptExportReview(DataTable source,string grouping,Action<V2Page,DataTable>? inspectExport=null)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var prepared=source.Copy();
        CollectionReceiptPresentation.Apply(prepared);
        var names=prepared.AsEnumerable().SelectMany(row=>(Convert.ToString(row["Campus"])??"")
            .Split(", ",StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            .Where(name=>name is not ("Multiple" or "Unassigned")&&name.Length>0).Distinct().ToArray();
        CollectionReceiptGrouping.Apply(prepared,grouping);
        using var page=new CollectionsReceiptsReportPage {BindingContext=new BindingContext()};
        var campus=(LookupBox)typeof(CanonicalFinanceReportPage).GetField("Campus",flags)!.GetValue(page)!;
        using var campuses=new DataTable();campuses.Columns.Add("CampusId",typeof(int));campuses.Columns.Add("CampusName");
        for(int index=0;index<names.Length;index++)campuses.Rows.Add(index+1,names[index]);
        campus.Bind(campuses,"CampusId","CampusName","All");
        if(names.Length==1)campus.SelectedValue=1;
        var group=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("GroupBy",flags)!.GetValue(page)!;
        group.SelectedItem=grouping;
        var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        foreach(DataColumn field in prepared.Columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name=field.ColumnName,DataPropertyName=field.ColumnName,HeaderText=field.ColumnName,ValueType=field.DataType});
        typeof(CollectionsReceiptsReportPage).GetMethod("OnReportPageBound",flags)!.Invoke(page,null);
        Check(!grid.Columns["Campus"].Visible&&grid.Columns["Class / Section"].Visible,
            "Receipt review follows the actual page: campus stays in scope/group headings and class/section stays in detail rows");
        Check(grid.Columns["Money Received"].HeaderText=="Payment Received","Receipt review uses the production payment caption");
        var rows=ReportExportBuilder.Build(prepared,grid.Columns.Cast<DataGridViewColumn>(),new[]{"Date"});
        string title=(string)typeof(CanonicalFinanceReportPage).GetMethod("ReportExportTitle",flags)!.Invoke(page,null)!;
        string scope=(string)typeof(CanonicalFinanceReportPage).GetMethod("FiltersText",flags)!.Invoke(page,null)!;
        typeof(CanonicalFinanceReportPage).GetField("_filterSummary",flags)!.SetValue(page,scope);
        inspectExport?.Invoke(page,rows);
        return(rows,title,scope);
    }
}
