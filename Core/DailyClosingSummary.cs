using System.Data;

namespace FMSC_SMS.Core;

internal static class DailyClosingSummary
{
    internal static bool IsAdjustment(DataRow row) => Convert.ToString(row["Payment Method"]) is "Refund" or "Correction";

    internal static DataTable CreatePrintable(DataTable amounts)
    {
        var printable = amounts.Copy();
        var total = printable.NewRow();
        total["Payment Method"] = "TOTAL NET";
        decimal recorded = amounts.AsEnumerable().Sum(row => Convert.ToDecimal(row["Recorded Amount"]));
        total["Recorded Amount"] = recorded;
        if (amounts.AsEnumerable().Where(row => !IsAdjustment(row)).All(row => !row.IsNull("Actual Amount")))
        {
            // Refund/correction actuals deliberately stay blank. Do not convert
            // DBNull or invent counted adjustment amounts to complete the print.
            decimal actual = amounts.AsEnumerable().Where(row => !row.IsNull("Actual Amount"))
                .Sum(row => Convert.ToDecimal(row["Actual Amount"]));
            total["Actual Amount"] = actual;
            total["Difference"] = actual - recorded;
        }
        printable.Rows.Add(total);
        return printable;
    }
}
