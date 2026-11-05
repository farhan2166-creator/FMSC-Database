using System.Data;
using System.Text.Json;
using FMSC_SMS.Core;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    // Dashboard -> Reports only. These fixtures never query SQL, post receipts,
    // or touch the finalized bill, payment-receipt, and certificate templates.
    // They exercise the same shared PDF export renderer as the Reports screens.
    private static void CheckDashboardReportScenarios()
    {
        const string folder = "artifacts/dashboard-report-scenarios";
        Directory.CreateDirectory(folder);

        void Render(string file, string title, string scope, DataTable rows, int minimumPages = 1)
        {
            var report = GridReportPreview.Build("Fictional Meadow School & College",
                title, scope + " | Fictional layout stress test", rows, "Example Operator");
            using (report.Data)
            {
                string path = Path.Combine(folder, file + ".pdf");
                GridReportPreview.ExportPdf(path, report);
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Check(pdf.PageCount >= minimumPages, file + " renders the expected page count");
            Check(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(page =>
                    page.Width.Point > 0 && page.Height.Point > 0), file + " has printable pages");
            }
        }

        DataTable ReceiptSource()
        {
            var rows = new DataTable();
            foreach (string field in new[] { "Receipt / Reference", "Scope", "Student / Family", "Campus",
                "Class / Section", "Payment Method", "Received By", "__ReceiptStudents", "__ReceiptSnapshot" })
                rows.Columns.Add(field);
            rows.Columns.Add("Date", typeof(DateTime));
            foreach (string field in new[] { "Money Received", "Refunds", "Corrections",
                "Credit Applied", "Adjusted Collection" })
                rows.Columns.Add(field, typeof(decimal));
            return rows;
        }
        void AddReceipt(DataTable rows, int sequence, int childrenCount)
        {
            string parent = "Parent " + sequence.ToString("D3");
            var students = Enumerable.Range(1, childrenCount).Select(index => new
            {
                AdmissionId = sequence * 100 + index,
                StudentName = index % 3 == 0 ? "Muhammad Student With Extended Name " + index : "Student " + index,
                FatherName = parent,
                Gender = index % 2 == 0 ? "Female" : "Male",
                Placements = new[] { new {
                    CampusName = index % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    ClassName = "Class " + ((index % 10) + 1),
                    SectionName = index % 2 == 0 ? "F" : "M"
                } }
            }).ToArray();
            var row = rows.NewRow();
            row["Receipt / Reference"] = (childrenCount > 1 ? "FR" : "R") + "-2026-" + sequence.ToString("D5");
            row["Scope"] = childrenCount > 1 ? "Family" : "Individual";
            row["Student / Family"] = "Fictional";
            row["Campus"] = childrenCount > 1 ? "Multiple" : "Campus Boys";
            row["Class / Section"] = "Not projected";
            row["Payment Method"] = sequence % 7 == 0 ? "Cash + Account Credit" : "Cash";
            row["Received By"] = "Example Operator";
            row["Date"] = new DateTime(2026, 11, 1).AddDays(sequence % 5);
            row["Money Received"] = 500m + sequence * 25m;
            row["Refunds"] = 0m;
            row["Corrections"] = 0m;
            row["Credit Applied"] = sequence % 7 == 0 ? 250m : 0m;
            row["Adjusted Collection"] = 500m + sequence * 25m;
            row["__ReceiptStudents"] = JsonSerializer.Serialize(students);
            row["__ReceiptSnapshot"] = DBNull.Value;
            rows.Rows.Add(row);
        }
        void Receipts(string filename, int[] familySizes, int minimumPages)
        {
            using var source = ReceiptSource();
            for (int i = 0; i < familySizes.Length; i++) AddReceipt(source, i + 1, familySizes[i]);
            var projected = ReceiptExportReview(source, "Whole School");
            using var exported = projected.Rows;
            Check(CollectionReceiptGrouping.Layout(exported)?.Count(r => r.Kind == "Detail") == familySizes.Length,
                filename + " retains exactly one transaction per receipt");
            Render(filename, projected.Title, projected.Scope, exported, minimumPages);
        }

        // One family with 3 children, one with 10, and 60 mixed receipts across pages.
        Receipts("receipts-family-three", new[] { 3 }, 1);
        Receipts("receipts-family-ten", new[] { 10 }, 1);
        Receipts("receipts-mixed-multipage",
            Enumerable.Range(0, 60).Select(i => i % 11 == 0 ? 10 : i % 4 == 0 ? 3 : 1).ToArray(), 2);

        // Real family-advance projection rather than a separately drawn table.
        using (var students = new DataTable())
        {
            foreach (string field in new[] { "Adm. / QR No.", "Student", "Father Name",
                "__StudentGender", "Family", "Campus", "Class / Section", "Student Status" })
                students.Columns.Add(field);
            students.Columns.Add("Available Credit", typeof(decimal));
            students.Columns.Add("Last Activity", typeof(DateTime));
            students.Columns.Add("AdmissionId", typeof(int));
            students.Columns.Add("FamilyId", typeof(int));
            for (int family = 1; family <= 22; family++)
                for (int child = 1; child <= (family % 7 == 0 ? 10 : family % 3 + 1); child++)
                    students.Rows.Add((family * 100 + child).ToString(), "Student " + child,
                        "Parent " + family, child % 2 == 0 ? "Female" : "Male",
                        "FAM-" + family, child % 2 == 0 ? "Campus Girls" : "Campus Boys",
                        "Class " + child + " - " + (child % 2 == 0 ? "F" : "M"),
                        "Active", 100m * child, new DateTime(2026, 11, 4),
                        family * 100 + child, family);
            var projected = CreditBalanceReportData.Create(students, true, 0m);
            using (projected.Rows)
            using (projected.Totals)
            using (var export = projected.Rows.DefaultView.ToTable(false,
                "Family / Student", "Members", "Student Advances", "Available Credit"))
            {
                export.Columns["Family / Student"]!.ColumnName = "Father / Parent";
                export.Columns["Members"]!.ColumnName = "Children / Class";
                export.Columns["Student Advances"]!.ColumnName = "Advance";
                export.Columns["Available Credit"]!.ColumnName = "Family Total";
                Check(export.Rows.Count == 22, "Family advance fixture retains 22 distinct families");
                Render("advance-families-multipage", "Student Advance Balance Register — Families",
                    "View: Families | Campus: All", export, 2);
            }
        }

        // Stress-test the shared report PDF layout with plausible screen report columns.
        // These three are layout fixtures, not claims about stored procedure output.
        using (var outstanding = new DataTable())
        {
            outstanding.Columns.Add("Student / Admission");
            outstanding.Columns.Add("Campus / Class");
            outstanding.Columns.Add("Oldest Due / Aging");
            outstanding.Columns.Add("Total Fees", typeof(decimal));
            outstanding.Columns.Add("Paid Towards Fees", typeof(decimal));
            outstanding.Columns.Add("Outstanding", typeof(decimal));
            for (int i = 1; i <= 75; i++)
                outstanding.Rows.Add(i + " - Example Student With Long Family Name " + i,
                    "Campus Boys\nClass " + (i % 10 + 1) + " - M",
                    "01-09-2026\nOverdue", 7500m + i, 2000m, 5500m + i);
            Render("outstanding-students-multipage", "Outstanding & Aging — Students",
                "Campus: All | View: Students", outstanding, 2);
        }
        using (var billing = new DataTable())
        {
            billing.Columns.Add("Charge Date", typeof(DateTime));
            billing.Columns.Add("Student / Admission");
            billing.Columns.Add("Fee Type");
            billing.Columns.Add("Gross Billed", typeof(decimal));
            billing.Columns.Add("Discounts", typeof(decimal));
            billing.Columns.Add("Net Billed", typeof(decimal));
            for (int i = 1; i <= 75; i++)
                billing.Rows.Add(new DateTime(2026, 10, 1).AddDays(i % 28),
                    i + " - Fictional Student " + i, i % 2 == 0 ? "Monthly Tuition" : "Transport",
                    5000m, i % 5 == 0 ? 500m : 0m, i % 5 == 0 ? 4500m : 5000m);
            Render("billing-detail-multipage", "Billing & Discounts — Billing Detail",
                "Campus: All | View: Billing Detail", billing, 2);
        }
        using (var monthly = new DataTable())
        {
            monthly.Columns.Add("Billing Month");
            monthly.Columns.Add("Campus");
            foreach (string field in new[] { "Net Fees", "Money Allocated", "Credit Applied", "Outstanding" })
                monthly.Columns.Add(field, typeof(decimal));
            for (int i = 0; i < 48; i++)
                monthly.Rows.Add(new DateTime(2023 + i / 12, i % 12 + 1, 1).ToString("MMM yyyy"),
                    i % 2 == 0 ? "Campus Boys" : "Campus Girls",
                    100000m + i * 1000, 60000m + i * 500, 10000m, 30000m + i * 500);
            Render("monthly-reconciliation-multipage", "Monthly Fee Reconciliation — Monthly Summary",
                "Academic Year: All | Grouped by: Month", monthly, 2);
        }
    }
}