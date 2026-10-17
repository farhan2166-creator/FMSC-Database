namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>The Billing Month lists on the posting and reversal pages are LookupBoxes.</summary>
internal static class BillingMonthKey
{
    public static int From(DateTime month) => month.Year * 100 + month.Month;
    /// <summary>The first day of the month a key stands for; null for "nothing chosen".</summary>
    public static DateTime? ToMonth(int key)
    {
        int year = key / 100;
        int month = key % 100;
        return year >= 1900 && month >= 1 && month <= 12
            ? new DateTime(year, month, 1)
            : null;
    }
    public static bool IsListed(DataTable months, int key) =>
        key > 0 &&
        months.AsEnumerable().Any(row => Convert.ToInt32(row["MonthKey"]) == key);
}
