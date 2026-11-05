using System.Text.Json;
using FMSC_SMS.Core;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static void CheckCollectionReceiptPresentation()
    {
        using var rows=new DataTable();
        foreach(string field in new[]{"Receipt / Reference","Scope","Student / Family","Campus","Class / Section","Payment Method","Received By","__ReceiptStudents","__ReceiptSnapshot"})rows.Columns.Add(field);
        rows.Columns.Add("Date",typeof(DateTime));
        foreach(string field in new[]{"Money Received","Refunds","Corrections","Credit Applied","Adjusted Collection"})rows.Columns.Add(field,typeof(decimal));
        string roster=JsonSerializer.Serialize(new[]{
            new{AdmissionId=1,StudentName="Hamid",FatherName="Changed Parent",Gender="M",Placements=new[]{new{ClassName="New Class",SectionName="X",CampusName="Campus Boys"}}},
            new{AdmissionId=2,StudentName="Kausar",FatherName="Changed Parent",Gender="F",Placements=new[]{new{ClassName="New Class",SectionName="X",CampusName="Campus Girls"}}},
            new{AdmissionId=3,StudentName="Hadia",FatherName="Changed Parent",Gender="F",Placements=new[]{new{ClassName="New Class",SectionName="X",CampusName="Campus Girls"}}}
        });
        string snapshot=JsonSerializer.Serialize(new{
            Students=new[]{new{AdmissionId=3,StudentName="Hadia",FatherName="Ali"},new{AdmissionId=1,StudentName="Hamid",FatherName="Ali"},new{AdmissionId=2,StudentName="Kausar",FatherName="Ali"}},
            Lines=new[]{new{AdmissionId=3,ClassName="Nine",SectionName="F",CampusName="Campus Girls"},new{AdmissionId=3,ClassName="Nine",SectionName="F",CampusName="Campus Girls"},
                new{AdmissionId=1,ClassName="Four",SectionName="M",CampusName="Campus Boys"},new{AdmissionId=2,ClassName="Four",SectionName="M",CampusName="Campus Girls"}}
        });
        var row=rows.NewRow();
        row["Receipt / Reference"]="FR-2026-00001";row["Date"]=new DateTime(2026,11,3);row["Scope"]="Family";
        row["Student / Family"]="Hadia, Hamid, Kausar";row["Campus"]="Multiple";row["Class / Section"]="Multiple";
        row["Payment Method"]="Cash + Account Credit";row["Received By"]="Example Operator";row["__ReceiptStudents"]=roster;row["__ReceiptSnapshot"]=snapshot;
        row["Money Received"]=8000m;row["Refunds"]=0m;row["Corrections"]=0m;row["Credit Applied"]=1700m;row["Adjusted Collection"]=8000m;rows.Rows.Add(row);
        CollectionReceiptPresentation.Apply(rows);
        Check((string)row["Student / Family"]=="Hadia, Hamid, Kausar s/d/o Ali","Mixed siblings keep their names together and append their shared saved father once");
        Check((string)row["Class / Section"]=="Nine – F\nFour – M\nFour – M","Saved class/section follows student order, including repeated classes");
        Check((string)row["__ReceiptSnapshot"]==snapshot && (string)row["__ReceiptStudents"]==roster,"Display enrichment never changes snapshot source JSON");
        Check((decimal)row["Money Received"]==8000m && (decimal)row["Credit Applied"]==1700m && rows.Rows.Count==1,"Enrichment preserves each receipt and its signed financial measures");
        Check((string)row["Payment Method"]=="Cash + Account Credit","Actual combined methods remain visible");
        foreach(string grouping in new[]{"Whole School","Campus","Class","Section"})
        {
            using var grouped=rows.Copy();CollectionReceiptGrouping.Apply(grouped,grouping);
            using var details=CollectionReceiptGrouping.DetailsOnly(grouped);
            Check(details.Rows.Count==1 && details.Rows[0].Field<decimal>("Money Received")==8000m && grouped.Rows[^1].Field<decimal>("Credit Applied")==1700m,grouping+" groups the receipt once without multiplying financial totals");
        }
        using(var individual=rows.Copy())
        {
            individual.Rows[0]["__ReceiptSnapshot"]=DBNull.Value;
            individual.Rows[0]["__ReceiptStudents"]=JsonSerializer.Serialize(new[]{new{AdmissionId=1,StudentName="Hamid",FatherName="Ali",Gender="Male",Placements=Array.Empty<object>()}});
            CollectionReceiptPresentation.Apply(individual);
            Check(individual.Rows[0].Field<string>("Student / Family")=="Hamid s/o Ali" && individual.Rows[0].Field<string>("Class / Section")=="Not recorded","Individual legacy entry uses its recorded parent and does not invent a missing historical class");
        }
        using(var sisters=rows.Copy())
        {
            sisters.Rows[0]["__ReceiptSnapshot"]=JsonSerializer.Serialize(new{Students=new[]{new{AdmissionId=3,StudentName="Hadia",FatherName="Ali"},new{AdmissionId=2,StudentName="Kausar",FatherName="Ali"}}});
            sisters.Rows[0]["__ReceiptStudents"]=JsonSerializer.Serialize(new[]{new{AdmissionId=3,StudentName="Hadia",FatherName="Ali",Gender="F"},new{AdmissionId=2,StudentName="Kausar",FatherName="Ali",Gender="F"}});
            CollectionReceiptPresentation.Apply(sisters);
            Check(sisters.Rows[0].Field<string>("Student / Family")=="Hadia, Kausar d/o Ali","Sisters append their shared father once with d/o");
        }
        using(var brothers=rows.Copy())
        {
            brothers.Rows[0]["__ReceiptSnapshot"]=DBNull.Value;
            brothers.Rows[0]["__ReceiptStudents"]=JsonSerializer.Serialize(new[]{new{AdmissionId=1,StudentName="Hamid",FatherName="Ali",Gender="M"},new{AdmissionId=4,StudentName="Ahmad",FatherName="Ali",Gender="M"}});
            CollectionReceiptPresentation.Apply(brothers);
            Check(brothers.Rows[0].Field<string>("Student / Family")=="Hamid, Ahmad s/o Ali","Brothers append their shared father once with s/o");
            brothers.Rows[0]["__ReceiptStudents"]=JsonSerializer.Serialize(new[]{new{AdmissionId=1,StudentName="Hamid",FatherName="Ali",Gender="M"},new{AdmissionId=4,StudentName="Ahmad",FatherName="Khan",Gender="M"}});
            CollectionReceiptPresentation.Apply(brothers);
            Check(brothers.Rows[0].Field<string>("Student / Family")=="Hamid s/o Ali, Ahmad s/o Khan","Different recorded fathers are not incorrectly presented as a shared parent");
        }
        var review=ReceiptExportReview(rows,"Whole School");using var export=review.Rows;
        Check(!export.Columns.Contains("Campus") && export.Columns.Contains("Payment Received") && !export.Columns.Cast<DataColumn>().Any(c=>c.ColumnName.StartsWith("__")),"CSV/Excel/PDF projection contains meaningful visible fields without private metadata");
        var totals=new DataTable();totals.Columns.Add("Money Received",typeof(decimal));totals.Rows.Add(8000m);
        var metrics=ReportPrintSummary.FromTotals(totals);
        Check(metrics.Single().Label=="Payment Received" && ReportPrintSummary.Split("Money Received: Rs. 8,000").Metrics.Single().Label=="Payment Received","Structured and legacy print totals both use Payment Received");
        string folder=Path.GetFullPath("artifacts/collection-receipt-presentation");Directory.CreateDirectory(folder);
        var built=GridReportPreview.Build("Fictional Meadow School & College",review.Title,review.Scope,export,"Example Operator",summaryMetrics:metrics);
        using(built.Data)
        {
            string path=Path.Combine(folder,"family-receipts.pdf");GridReportPreview.ExportPdf(path,built);
            var search=ReportPageSearchIndex.FromPdf(File.ReadAllBytes(path));
            foreach(string text in new[]{"Family: Ali","Hadia (Nine – F), Hamid, Kausar (Four – M)","Cash + Account Credit","Payment","Grand Total (1 entry)"})Check(search.FindPages(text).SequenceEqual(new[]{1}),"Receipt PDF contains "+text);
            Check(search.FindPages("Mixed").Length==0 && search.FindPages("Multiple").Length==0 && search.FindPages("Money").Length==0,"Printed receipt uses actual placements and methods and Payment terminology");
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(built.Definition));report.DataSources.Add(new ReportDataSource("ReportData",built.Data));
            File.WriteAllBytes(Path.Combine(folder,"family-receipts.png"),report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>130</DpiX><DpiY>130</DpiY></DeviceInfo>"));
        }
        var campusReview=ReceiptExportReview(rows,"Campus");using var campusExport=campusReview.Rows;
        var campusBuilt=GridReportPreview.Build("Fictional Meadow School & College",campusReview.Title,campusReview.Scope,campusExport,"Example Operator");
        using(campusBuilt.Data)
        {
            string path=Path.Combine(folder,"campus-receipts.pdf");GridReportPreview.ExportPdf(path,campusBuilt);
            var search=ReportPageSearchIndex.FromPdf(File.ReadAllBytes(path));
            Check(search.FindPages("(1 entry)").Length==1 && search.FindPages("Subtotal").Length==1,"Campus PDF renders entry counts and named subtotals");
            Check(campusBuilt.Data.Rows[0].Field<string>("C0")=="1" && campusBuilt.Data.Rows[0].Field<string>("C2")=="Family: Ali\nHadia (Nine – F), Hamid, Kausar (Four – M)","Printed receipt pairs each child with their class and shares repeated class labels");
            Check(rows.Rows[0].Field<string>("Student / Family")=="Hadia, Hamid, Kausar s/d/o Ali" && !rows.Columns.Contains("#"),"Compact print identities and numbering do not rewrite source or raw export columns");
        }
    }
}
