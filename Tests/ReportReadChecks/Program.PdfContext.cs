using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckPdfContext()
    {
        using var source=ReceiptRows().Clone();
        source.Rows.Add("R-2026-00001","Boys","One – A",100m,0m,0m,0m,100m);
        source.Rows.Add("FR-2026-00002","Boys","Two – B",200m,0m,0m,20m,200m);
        using(var pdf=ReportContextProjection.ForPdf(source,"Boys"))
        {
            Check(!pdf.Columns.Contains("Campus")&&pdf.Columns.Contains("Class / Section"),"A selected campus is context once, while class/section stays in the actual PDF projection");
            Check(pdf.Rows[0].Field<string>(0)=="R-2026-00001"&&pdf.Rows[1].Field<string>(0)=="FR-2026-00002",
                "PDF context never changes stored receipt references");
            Check(pdf.Rows[1].Field<decimal>("Credit Applied")==20m&&pdf.Columns["Money Received"]!.DataType==typeof(decimal),
                "PDF context preserves financial values and numeric types");
        }
        Check(source.Columns.Contains("Campus")&&source.Rows[0].Field<string>("Campus")=="Boys","CSV/Excel source rows retain campus data after PDF projection");
        using(var all=ReportContextProjection.ForPdf(source))Check(all.Columns.Contains("Campus"),"A report with no campus context keeps its location column");
        source.Rows[1]["Campus"]="Girls";
        using(var mixed=ReportContextProjection.ForPdf(source,"Boys"))Check(mixed.Columns.Contains("Campus"),"A broader heading cannot hide a different campus in the data");
        foreach(string grouping in new[]{"Campus","Class","Section"})
        {
            using var grouped=source.Copy();CollectionReceiptGrouping.Apply(grouped,grouping);
            using var pdf=ReportContextProjection.ForPdf(grouped);
            Check(!pdf.Columns.Contains("Campus")&&CollectionReceiptGrouping.Layout(pdf)!.Any(row=>row.Kind=="Heading"&&row.Label.Contains("Girls")),
                grouping+": actual group headings carry the omitted campus through the PDF projection");
            using var details=CollectionReceiptGrouping.DetailsOnly(pdf);
            Check(details.Rows.Count==2&&details.AsEnumerable().Sum(row=>row.Field<decimal>("Adjusted Collection"))==300m,
                grouping+": the PDF context does not remove records or multiply totals");
            var last=CollectionReceiptGrouping.Page(grouped,1,1);
            using var page=ReportContextProjection.ForPdf(last.Rows);
            Check(!page.Columns.Contains("Campus"),grouping+": repeated page headings retain their campus context");
        }
        using var renamed=source.Clone();renamed.Columns["Campus"]!.ColumnName="Location";
        renamed.Columns["Location"]!.ExtendedProperties[ReportContextProjection.SourceColumnKey]="CampusName";
        renamed.Rows.Add("R-2026-00003","Boys","One – A",100m,0m,0m,0m,100m);
        using(var pdf=ReportContextProjection.ForPdf(renamed,"Boys"))Check(!pdf.Columns.Contains("Location"),"A friendly header retains its underlying campus identity");
        using var campusOnly=new DataTable();campusOnly.Columns.Add("Campus");campusOnly.Rows.Add("Boys");
        using(var pdf=ReportContextProjection.ForPdf(campusOnly,"Boys"))Check(pdf.Columns.Count==1,"A PDF cannot lose its only selected data column");
        using var empty=source.Clone();
        using(var pdf=ReportContextProjection.ForPdf(empty,"Boys"))Check(pdf.Columns.Contains("Campus"),"Empty reports retain their selected field headings");
    }
}
