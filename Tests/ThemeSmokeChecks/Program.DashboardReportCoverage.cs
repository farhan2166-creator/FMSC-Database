using System.Data;
using FMSC_SMS.Core;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    // Names are natural-looking, deliberately fictional examples. Never load real student records.
    private static readonly string[] DashboardStudentNames = {
        "Ayesha Khan", "Hamza Ali", "Fatima Tariq", "Muhammad Haris",
        "Zainab Ahmad", "Bilal Shah", "Maryam Iqbal", "Usman Farooq",
        "Sana Raza", "Abdullah Siddiqui", "Hira Nadeem", "Hassan Mahmood",
        "Noor Fatima", "Ibrahim Yousaf", "Areeba Khalid", "Saad Ahmed",
        "Maham Hassan", "Ali Haider", "Amina Aslam", "Faisal Rehman",
        "Saba Iqbal", "Huzaifa Anwar", "Rimsha Yousaf", "Taha Nadeem"
    };
    private static readonly string[] DashboardParentNames = {
        "Imran Khan", "Rashid Ali", "Tariq Mahmood", "Naveed Ahmad",
        "Akram Shah", "Sajid Iqbal", "Farooq Ahmed", "Nadeem Raza",
        "Aslam Siddiqui", "Yousaf Khan", "Khalid Mehmood", "Javed Anwar"
    };
    private static string SampleStudentName(int index) =>
        DashboardStudentNames[Math.Abs(index % DashboardStudentNames.Length)];
    private static string SampleParentName(int index) =>
        DashboardParentNames[Math.Abs(index % DashboardParentNames.Length)];

    // Additional layout scenarios for views reachable from Dashboard -> Reports.
    // These are plausible screen-shaped rows, not claimed reproductions of SQL procedure output.
    // Bills, payment-receipt documents, and certificate templates are deliberately out of scope.
    private static void CheckAdditionalDashboardReportScenarios()
    {
        const string folder = "artifacts/dashboard-report-coverage";
        Directory.CreateDirectory(folder);

        DataTable Rows(string[] text, string[]? money = null, string[]? dates = null)
        {
            var table = new DataTable();
            foreach (string name in text) table.Columns.Add(name);
            foreach (string name in money ?? Array.Empty<string>()) table.Columns.Add(name, typeof(decimal));
            foreach (string name in dates ?? Array.Empty<string>()) table.Columns.Add(name, typeof(DateTime));
            return table;
        }

        void Render(string file, string title, string scope, DataTable data, int minPages = 1)
        {
            int originalRows = data.Rows.Count;
            var built = GridReportPreview.Build("Fictional Meadow School & College", title,
                scope + " | Fictional, realistic layout sample", data, "Amina Javed");
            using (built.Data)
            {
                string path = Path.Combine(folder, file + ".pdf");
                GridReportPreview.ExportPdf(path, built);
                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                Check(pdf.PageCount >= minPages, file + " preserves all expected PDF pages");
                Check(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(page =>
                    page.Width.Point > 0 && page.Height.Point > 0), file + " preserves printable paper geometry");
                Check(data.Rows.Count == originalRows, file + " export never modifies its input data");
            }
        }

        using (var admissions = Rows(
            new[] { "Admission Number", "Student", "Father Name", "Campus", "Class / Section", "Status" },
            dates: new[] { "Admission Date" }))
        {
            for (int i = 0; i < 64; i++)
                admissions.Rows.Add((4201 + i).ToString(), SampleStudentName(i), SampleParentName(i),
                    i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    "Class " + (i % 10 + 1) + " – " + (i % 2 == 0 ? "F" : "M"),
                    i % 17 == 0 ? "Former" : "Active", new DateTime(2026, 3, 1).AddDays(i * 3));
            Render("student-admissions-multipage", "Student Reports — Admissions",
                "Academic Year: 2026–2027 | Campus: All", admissions, 2);
        }

        using (var strength = Rows(new[] { "Campus", "Class", "Section" },
            new[] { "Boys", "Girls", "Total Students" }))
        {
            for (int i = 0; i < 24; i++)
            {
                decimal boys = i % 2 == 0 ? 0 : 19 + (i * 7) % 18;
                decimal girls = i % 2 == 0 ? 18 + (i * 5) % 19 : 0;
                strength.Rows.Add(i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    "Class " + (i / 2 + 1), i % 3 == 0 ? "A" : "B", boys, girls, boys + girls);
            }
            Render("student-strength-by-class", "Student Reports — Strength by Class",
                "Academic Year: 2026–2027 | Campus: All", strength);
        }

        using (var changes = Rows(new[] { "Admission Number", "Student", "Previous Class",
            "New Class", "Change Type", "Remarks" }, dates: new[] { "Effective Date" }))
        {
            for (int i = 0; i < 58; i++)
                changes.Rows.Add((3201 + i).ToString(), SampleStudentName(i), "Class " + (i % 9 + 1),
                    "Class " + (i % 9 + 2), i % 6 == 0 ? "Transfer" : "Promotion",
                    i % 6 == 0 ? "Campus transfer recorded" : "Promoted after annual results",
                    new DateTime(2026, 4, 2).AddDays(i % 22));
            Render("student-changes-multipage", "Student Reports — Changes",
                "Change history | Academic Year: 2026–2027", changes, 2);
        }

        using (var transactions = Rows(
            new[] { "Receipt / Reference", "Student / Family", "Payment Method", "Campus" },
            new[] { "Payment Received", "Refunds", "Corrections", "Credit Applied", "Net Collection" },
            new[] { "Date" }))
        {
            for (int i = 0; i < 70; i++)
            {
                decimal paid = 1450m + (i % 8) * 375m + (i % 3) * 125m;
                decimal refund = i % 19 == 0 ? 350m : 0m;
                decimal correction = i % 23 == 0 ? 175m : 0m;
                decimal credit = i % 7 == 0 ? 600m : 0m;
                string method = credit > 0 ? "Cash + Account Credit" : i % 3 == 0 ? "Bank" : "Cash";
                transactions.Rows.Add($"R-2026-{i + 801:D5}", SampleStudentName(i),
                    method, i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    paid, refund, correction, credit, paid - refund - correction,
                    new DateTime(2026, 10, 1).AddDays(i % 31));
            }
            Render("collections-detailed-transactions", "Collections & Receipts — Detailed Transactions",
                "October 2026 | All campuses | All payment methods", transactions, 2);
        }

        using (var collectionSummary = Rows(new[] { "Payment Method" },
            new[] { "Receipts", "Payment Received", "Refunds", "Corrections", "Net Collection" }))
        {
            foreach (var (method, count, collected, refund, correction) in new[] {
                ("Cash", 134m, 395850m, 2150m, 900m),
                ("Bank", 42m, 189750m, 0m, 375m),
                ("Card", 18m, 58200m, 0m, 0m),
                ("Online", 27m, 112675m, 1200m, 0m),
                ("Cash + Account Credit", 13m, 31900m, 0m, 0m),
                ("Account Credit", 9m, 0m, 0m, 0m)
            })
                collectionSummary.Rows.Add(method, count, collected, refund, correction,
                    collected - refund - correction);
            Render("collections-payment-method-summary", "Collections & Receipts — Summary by Payment Method",
                "October 2026 | Campus: All", collectionSummary);
        }

        using (var charges = Rows(new[] { "Admission Number", "Student", "Campus / Class",
            "Fee Type", "Due Status" }, new[] { "Net Charges", "Already Settled", "Outstanding" },
            new[] { "Due Date" }))
        {
            for (int i = 0; i < 76; i++)
            {
                decimal charge = 1850m + (i % 8) * 420m;
                decimal settled = i % 5 == 0 ? charge : i % 4 == 0 ? charge / 2m : 0m;
                charges.Rows.Add((5501 + i).ToString(), SampleStudentName(i),
                    (i % 2 == 0 ? "Campus Girls" : "Campus Boys") + " / Class " + (i % 10 + 1),
                    i % 4 == 0 ? "Transport Fee" : "Tuition Fee",
                    i % 3 == 0 ? "Overdue" : i % 3 == 1 ? "Due Today" : "Not Yet Due",
                    charge, settled, charge - settled, new DateTime(2026, 10, 16).AddDays(i % 45));
            }
            Render("outstanding-charges-multipage", "Outstanding & Aging — Charges",
                "Campus: All | Due status: All", charges, 2);
        }

        using (var aging = Rows(new[] { "Aging Band", "Due Status" },
            new[] { "Students", "Outstanding", "Overdue Amount" }))
        {
            aging.Rows.Add("Not yet due", "Upcoming", 43m, 91570m, 0m);
            aging.Rows.Add("Due today", "Due today", 16m, 32850m, 0m);
            aging.Rows.Add("1–30 days", "Overdue", 28m, 67240m, 67240m);
            aging.Rows.Add("31–60 days", "Overdue", 17m, 41950m, 41950m);
            aging.Rows.Add("61–90 days", "Overdue", 9m, 22680m, 22680m);
            aging.Rows.Add("90+ days", "Overdue", 5m, 17450m, 17450m);
            Render("outstanding-aging-bands", "Outstanding & Aging — Aging Buckets",
                "As of: 05 November 2026 | Campus: All", aging);
        }

        using (var families = Rows(new[] { "Father / Parent", "Children / Class", "Campus" },
            new[] { "Open Charges", "Payments Applied", "Outstanding" }))
        {
            for (int i = 0; i < 44; i++)
            {
                decimal gross = 5300m + (i % 6) * 1360m;
                decimal applied = i % 4 == 0 ? gross / 2m : i % 9 == 0 ? gross : 0m;
                families.Rows.Add(SampleParentName(i), SampleStudentName(i) + " / Class " + (i % 10 + 1)
                    + "\n" + SampleStudentName(i + 1) + " / Class " + ((i + 2) % 10 + 1),
                    i % 3 == 0 ? "Multiple campuses" : "Campus Boys",
                    gross, applied, gross - applied);
            }
            Render("outstanding-families-multipage", "Outstanding & Aging — Families",
                "Campus: All | Only families with open charges", families, 2);
        }

        using (var summary = Rows(new[] { "Charge Month", "Campus", "Fee Type" },
            new[] { "Charges", "Gross Billed", "Discounts", "Net Billed" }))
        {
            for (int i = 0; i < 26; i++)
            {
                decimal gross = 87000m + 3250m * (i % 10);
                decimal discount = (i % 4 + 1) * 1750m;
                summary.Rows.Add(new DateTime(2025 + i / 12, i % 12 + 1, 1).ToString("MMM yyyy"),
                    i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    i % 3 == 0 ? "Transport Fee" : "Tuition Fee",
                    21m + (i % 11), gross, discount, gross - discount);
            }
            Render("billing-summary-multipage", "Billing & Discounts — Billing Summary",
                "Group: Month | Campus: All", summary, 2);
        }

        using (var discountDetail = Rows(new[] { "Admission Number", "Student", "Fee Type",
            "Discount Reason", "Campus" }, new[] { "Original Charge", "Discount", "Net Billed" }))
        {
            for (int i = 0; i < 68; i++)
            {
                decimal original = 2150m + (i % 7) * 475m;
                decimal discount = i % 13 == 0 ? original : i % 3 == 0 ? 500m : 250m;
                discountDetail.Rows.Add((6201 + i).ToString(), SampleStudentName(i),
                    i % 4 == 0 ? "Transport Fee" : "Tuition Fee",
                    discount == original ? "Full fee waiver" : i % 3 == 0 ? "Scholarship" : "Sibling concession",
                    i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    original, discount, original - discount);
            }
            Render("billing-discount-detail", "Billing & Discounts — Discount Detail",
                "October–November 2026 | All campuses", discountDetail, 2);
        }

        using (var assignments = Rows(new[] { "Student", "Class / Section", "Fee Type",
            "Agreement", "Campus" }, new[] { "Regular Fee", "Concession", "Agreed Fee" },
            new[] { "Effective From" }))
        {
            for (int i = 0; i < 64; i++)
            {
                decimal fee = 1850m + (i % 8) * 420m;
                decimal waiver = i % 11 == 0 ? fee : i % 4 == 0 ? 600m : 250m;
                assignments.Rows.Add(SampleStudentName(i), "Class " + (i % 10 + 1),
                    i % 5 == 0 ? "Transport Fee" : "Monthly Tuition",
                    waiver == fee ? "Full waiver" : "Fixed concession",
                    i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    fee, waiver, fee - waiver, new DateTime(2026, 4, 1).AddDays(i % 14));
            }
            Render("billing-concession-assignments", "Billing & Discounts — Concession Assignments",
                "Academic Year: 2026–2027 | All campuses", assignments, 2);
        }

        using (var concessions = Rows(new[] { "Campus", "Class", "Fee Type", "Term" },
            new[] { "Students", "Regular Fees", "Current Concessions", "Agreed Fees" }))
        {
            for (int i = 0; i < 20; i++)
            {
                decimal regular = 62500m + (i % 7) * 3550m;
                decimal rebate = 4500m + (i % 5) * 825m;
                concessions.Rows.Add(i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    "Class " + (i % 10 + 1), i % 3 == 0 ? "Transport Fee" : "Monthly Tuition",
                    i % 3 == 0 ? "Yearly" : "Monthly",
                    18m + i % 13, regular, rebate, regular - rebate);
            }
            Render("billing-concession-summary", "Billing & Discounts — Concession Summary",
                "Effective agreements as of 05 November 2026", concessions);
        }

        using (var advance = Rows(new[] { "Admission Number", "Student", "Father Name",
            "Campus / Class" }, new[] { "Available Advance" }, new[] { "Last Activity" }))
        {
            for (int i = 0; i < 75; i++)
                advance.Rows.Add((7401 + i).ToString(), SampleStudentName(i), SampleParentName(i),
                    (i % 2 == 0 ? "Campus Girls" : "Campus Boys") + " / Class " + (i % 10 + 1),
                    125m + (i % 11) * 175m + (i % 4) * 50m,
                    new DateTime(2026, 10, 1).AddDays(i % 31));
            Render("advance-students-multipage", "Student Advance Balance Register — Students",
                "Campus: All | Available credit greater than zero", advance, 2);
        }

        using (var reconciliation = Rows(new[] { "Billing Month", "Campus", "Fee Type" },
            new[] { "Net Fees", "Money Allocated", "Credit Applied", "Outstanding" }))
        {
            for (int i = 0; i < 36; i++)
            {
                decimal net = 59000m + i * 1150m;
                decimal money = 35000m + i * 620m;
                decimal credit = i % 4 == 0 ? 1750m : 750m;
                reconciliation.Rows.Add(new DateTime(2024 + i / 12, i % 12 + 1, 1).ToString("MMM yyyy"),
                    i % 2 == 0 ? "Campus Girls" : "Campus Boys",
                    i % 3 == 0 ? "Transport Fee" : "Monthly Tuition",
                    net, money, credit, net - money - credit);
            }
            Render("monthly-reconciliation-by-campus", "Monthly Fee Reconciliation — By Campus and Fee Type",
                "Billing month | All academic years", reconciliation, 2);
        }
    }
}
