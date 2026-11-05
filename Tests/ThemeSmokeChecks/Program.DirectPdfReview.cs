using System.Security.Cryptography;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static void CheckDirectPdfReview()
    {
        string folder=Path.GetFullPath("artifacts/direct-pdf-review");Directory.CreateDirectory(folder);
        string finalPath=Path.Combine(folder,"REVIEW-ALL.pdf"),pendingPath=Path.Combine(folder,"REVIEW-ALL.pending.pdf");
        // A failed rerun must not advertise the previous successful review file.
        File.Delete(finalPath);
        CheckActualPdfExportContext();
        CheckGridPdfReports();
        CheckReportStudentIdentity();
        CheckCollectionReceiptPresentation();
        CheckDashboardReportScenarios();
        CheckReportCaptions(new[]{"ClassSectionSummary","ClassOutstandingCharges","ClassMarksSheet","ClassAttendanceSheet","ClassMonthlyAttendanceSheet"});
        var samples=new (string Title,string Path)[] {
            ("Collections & Receipts — Whole School","grid-pdf/collections-receipts-export.pdf"),
            ("Collections & Receipts — By Campus","grid-pdf/collections-by-campus-export.pdf"),
            ("Family Receipts — Names, Classes and Payment Methods","collection-receipt-presentation/family-receipts.pdf"),
            ("Receipts — Family with 3 Children","dashboard-report-scenarios/receipts-family-three.pdf"),
            ("Receipts — Family with 10 Children","dashboard-report-scenarios/receipts-family-ten.pdf"),
            ("Receipts — Mixed 60-Entry Multipage","dashboard-report-scenarios/receipts-mixed-multipage.pdf"),
            ("Advance Balance — 22 Families Multipage","dashboard-report-scenarios/advance-families-multipage.pdf"),
            ("Outstanding & Aging — 75 Students Multipage","dashboard-report-scenarios/outstanding-students-multipage.pdf"),
            ("Billing & Discounts — 75 Rows Multipage","dashboard-report-scenarios/billing-detail-multipage.pdf"),
            ("Monthly Reconciliation — 48 Months Multipage","dashboard-report-scenarios/monthly-reconciliation-multipage.pdf"),
            ("Outstanding & Aging — Compact","grid-pdf/outstanding-aging-export.pdf"),
            ("Student List","report-student-identity/student-list.pdf"),
            ("Outstanding & Aging — Student Identity","report-student-identity/outstanding-aging.pdf"),
            ("Student Advance Balance Register — Families","report-student-identity/advance-balance-families.pdf"),
            ("Class & Section Summary","report-captions/ClassSectionSummary.pdf"),
            ("Class Outstanding Charges","report-captions/ClassOutstandingCharges.pdf"),
            ("Class Marks Sheet","report-captions/ClassMarksSheet.pdf"),
            ("Class Exam Attendance Sheet","report-captions/ClassAttendanceSheet.pdf"),
            ("Class Monthly Attendance Register","report-captions/ClassMonthlyAttendanceSheet.pdf"),
            ("Student Changes","report-student-identity/student-changes.pdf"),
            ("Certificate Issue Register","report-student-identity/certificate-register.pdf"),
            ("Wide Layout Check — Multipage","grid-pdf/wide-multipage-export.pdf"),
            ("Empty Result Layout","grid-pdf/empty-report-export.pdf")
        };
        string commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"Local build";
        var expected=new List<(double Width,double Height,string Text)>();
        var provenance=new List<object>();
        try
        {
            using(var combined=new PdfDocument())
            {
                combined.Info.Title="Management Report PDF Export Review";
                combined.Info.Subject=$"Fictional fixtures; production Windows PDF exports. Source commit: {commit}";
                foreach(var sample in samples)
                {
                    string path=Path.GetFullPath(Path.Combine("artifacts",sample.Path));
                    using var input=PdfReader.Open(path,PdfDocumentOpenMode.Import);
                    using var text=UglyToad.PdfPig.PdfDocument.Open(path);
                    int first=combined.PageCount;
                    for(int page=0;page<input.PageCount;page++)
                    {
                        combined.AddPage(input.Pages[page]);
                        expected.Add((input.Pages[page].Width.Point,input.Pages[page].Height.Point,text.GetPage(page+1).Text));
                    }
                    combined.Outlines.Add(sample.Title,combined.Pages[first],false);
                    provenance.Add(new {sample.Title,File=sample.Path,Pages=input.PageCount,CombinedFirstPage=first+1,CombinedLastPage=combined.PageCount,
                        Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))});
                }
                Check(combined.PageCount==expected.Count,"Combined review retains every source PDF page");
                combined.Save(pendingPath);
            }
            using(var verify=PdfReader.Open(pendingPath,PdfDocumentOpenMode.Modify))
            using(var text=UglyToad.PdfPig.PdfDocument.Open(pendingPath))
            {
                Check(verify.PageCount==expected.Count&&text.NumberOfPages==expected.Count,"The saved combined review contains every scenario page");
                Check(verify.Outlines.Count==samples.Length,"The saved review has a bookmark for every report scenario");
                for(int page=0;page<expected.Count;page++)
                {
                    Check(Math.Abs(verify.Pages[page].Width.Point-expected[page].Width)<.01&&Math.Abs(verify.Pages[page].Height.Point-expected[page].Height)<.01,
                        "Combining retains the original paper size and orientation");
                    Check(text.GetPage(page+1).Text==expected[page].Text,"Combined PDF retains all source page text in the original order");
                }
            }
            File.Move(pendingPath,finalPath,overwrite:true);
            File.WriteAllText(Path.Combine(folder,"provenance.json"),JsonSerializer.Serialize(new {
                SourceCommit=commit,Checks="PASSED",ScenarioCount=samples.Length,PageCount=expected.Count,
                WorkflowRun=Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
                Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(finalPath))),Samples=provenance
            },new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"::notice title=Direct PDF export review::{samples.Length} scenarios, {expected.Count} pages in REVIEW-ALL.pdf.");
        }
        finally { File.Delete(pendingPath); }
    }
}
