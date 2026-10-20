using System.Globalization;

namespace FMSC_SMS.Core;

internal static class FeeDocumentData
{
    // Stable XML table name for already saved bill snapshots; RDLC datasets have their own document names.
    internal const string SnapshotTableName="FeeReceiptData";
    internal static string CampusHeading(DataTable source)
    {
        if (!source.Columns.Contains("CampusName")) return "";
        var campuses = source.AsEnumerable().Select(row => Convert.ToString(row["CampusName"])?.Trim() ?? "")
            .Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return campuses.Length switch { 0 => "", 1 => campuses[0], _ => "Multiple campuses" };
    }
    internal static DataTable Create()
    {
        var table = new DataTable(SnapshotTableName);
        table.Columns.Add("ReceiptPageKey", typeof(int));
        table.Columns.Add("InstitutionName", typeof(string));
        table.Columns.Add("DocumentTitle", typeof(string));
        table.Columns.Add("CopyLabel", typeof(string));
        table.Columns.Add("ReceiptNumber", typeof(string));
        table.Columns.Add("StudentName", typeof(string));
        table.Columns.Add("AdmissionNumberText", typeof(string));
        table.Columns.Add("ClassSection", typeof(string));
        table.Columns.Add("PaymentDateText", typeof(string));
        table.Columns.Add("FamilyBatch", typeof(string));
        table.Columns.Add("FeeDescription", typeof(string));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("DiscountApplied", typeof(decimal));
        table.Columns.Add("TotalPaid", typeof(decimal));
        table.Columns.Add("BalanceRemaining", typeof(decimal));
        table.Columns.Add("PaymentMethod", typeof(string));
        table.Columns.Add("ReceiptStatus", typeof(string));
        table.Columns.Add("ShowDiscount", typeof(bool));
        table.Columns.Add("ShowBalance", typeof(bool));
        table.Columns.Add("ContactNumber", typeof(string));
        table.Columns.Add("SchoolAddress", typeof(string));
        table.Columns.Add("CampusName", typeof(string));
        table.Columns.Add("FatherName", typeof(string));
        table.Columns.Add("FeePeriod", typeof(string));
        foreach (string field in new[] { "OriginalPaid", "RefundAmount", "CorrectionAmount", "ReversedAmount", "NetPaid" })
            table.Columns.Add(field, typeof(decimal)).DefaultValue = 0m;
        table.Columns.Add("ShowAdjustments", typeof(bool)).DefaultValue = false;
        table.Columns.Add("PaymentEnrollmentId", typeof(int)).DefaultValue = 0;
        table.Columns.Add("DocumentKind",typeof(string)).DefaultValue="Receipt";
        foreach(string field in new[] {"FeesCovered","CashReceived","CreditApplied","CreditRemaining","CreditRetained"})
            table.Columns.Add(field,typeof(decimal)).DefaultValue=0m;
        table.Columns.Add("ShowSettlement",typeof(bool)).DefaultValue=false;
        table.Columns.Add("ShowCreditRemaining",typeof(bool)).DefaultValue=false;
        table.Columns.Add("BillSourceKey",typeof(string)).DefaultValue="";
        return table;
    }

    internal static void GroupPeriods(DataTable table)
    {
        var ordered = table.AsEnumerable().GroupBy(row => row.Field<int>("ReceiptPageKey"))
            .SelectMany(page => page.GroupBy(row => FormatPeriod(row.Field<string>("FeePeriod") ?? ""))
                .SelectMany(period => period.Select((row,index) =>
                {
                    var values = row.ItemArray;
                    values[table.Columns["FeePeriod"]!.Ordinal] = index == 0 ? period.Key : "";
                    return values;
                }))).ToArray();
        table.Rows.Clear();
        foreach (var values in ordered) table.Rows.Add(values);
    }

    internal static string FormatPeriod(string period) =>
        DateTime.TryParseExact(period.Trim(),
            new[] { "MMMM-yyyy", "MMM-yyyy", "MMMM yyyy", "MMM yyyy", "yyyy-MM" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            ? month.ToString("MMM-yyyy", CultureInfo.InvariantCulture)
            : period;

}
