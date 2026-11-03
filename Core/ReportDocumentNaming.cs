using System.Globalization;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core;

/// <summary>One document identity for preview captions, print jobs and default export names.</summary>
internal static class ReportDocumentNaming
{
    internal static string PreviewTitle(string requestedTitle, string reportPath, DataTable data)
    {
        string file = Path.GetFileName(reportPath);
        var names = file switch {
            "StudentBill.rdlc" => ("Student Fee Bill", "Student Fee Bills", "bills"),
            "FamilyBill.rdlc" => ("Family Fee Bill", "Family Fee Bills", "bills"),
            "StudentReceipt.rdlc" when requestedTitle.Contains("Annual", StringComparison.OrdinalIgnoreCase)
                => ("Annual Fee Receipt", "Annual Fee Receipts", "receipts"),
            "StudentReceipt.rdlc" when Unique(data,"DocumentTitle")=="STUDENT COPY OF FAMILY RECEIPT"
                => ("Student Copy of Family Receipt", "Student Copies of Family Receipts", "copies"),
            "StudentReceipt.rdlc" => ("Student Payment Receipt", "Student Payment Receipts", "receipts"),
            "FamilyReceipt.rdlc" => ("Family Payment Receipt", "Family Payment Receipts", "receipts"),
            "FeeChanges.rdlc" => ("Fee Change Note", "Fee Change Notes", "notes"),
            _ => (requestedTitle, requestedTitle, "documents")
        };
        bool feeDocument = file is "StudentBill.rdlc" or "FamilyBill.rdlc" or "StudentReceipt.rdlc" or "FamilyReceipt.rdlc" or "FeeChanges.rdlc";
        if (!feeDocument)
        {
            string certificate = Unique(data,"CertificateNumber");
            return certificate.Length == 0 ? requestedTitle : Join(requestedTitle,certificate,Name(data,"StudentName"),Name(data,"StaffName"));
        }
        string[] numbers = Values(data,"ReceiptNumber");
        int count = numbers.Length;
        if (count == 0) count = Math.Max(1,Values(data,"ReceiptPageKey").Length);
        string name = count > 1 || requestedTitle.Contains("Annual",StringComparison.OrdinalIgnoreCase) ? names.Item2 : names.Item1;
        string admission = Unique(data,"AdmissionNumberText");
        string student = Name(data,"StudentName");
        // Family references can contain several children. Keep the caption readable.
        if (admission.Length > 30) admission = "";
        return Join(name, count > 1 ? $"{count} {names.Item3}" : numbers.FirstOrDefault() ?? "",
            admission.Length > 0 ? "Adm. " + admission : "", student);
    }

    internal static string ExportFileName(string title, DateTime? at = null)
    {
        // Use Windows filename rules on every platform so portable tests agree.
        string stem = Regex.Replace(title, "[<>:\"/\\\\|?*\\x00-\\x1F]", " - ");
        stem = Regex.Replace(stem,@"\s+"," ").Trim(' ','.','-');
        if (stem.Length == 0) stem = "Report";
        if (stem.Length > 160) stem = stem[..160].TrimEnd(' ','.','-');
        return stem + "_" + (at ?? DateTime.Now).ToString("yyyy-MM-dd_HH-mm-ss",CultureInfo.InvariantCulture);
    }

    private static string[] Values(DataTable data,string column) => !data.Columns.Contains(column) ? Array.Empty<string>() :
        data.AsEnumerable().Select(row=>Convert.ToString(row[column])?.Trim()??"").Where(value=>value.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string Unique(DataTable data,string column)
    {
        var values=Values(data,column);return values.Length==1?values[0]:"";
    }
    private static string Name(DataTable data,string column) => RecordTextPolicy.DisplayText(column,Unique(data,column));
    private static string Join(params string[] parts) => string.Join(" — ",parts.Where(value=>!string.IsNullOrWhiteSpace(value)));
}
