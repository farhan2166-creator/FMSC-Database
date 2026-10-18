using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;

internal static partial class Program
{
    private static void CheckAdvancePosting()
    {
        var preview = new DataTable();
        preview.Columns.Add("EnrollmentId", typeof(int)); preview.Columns.Add("FeeTypeId", typeof(int));
        preview.Columns.Add("BillingMonth", typeof(DateTime));
        var june = new DateTime(2027, 6, 1); var july = june.AddMonths(1); var august = june.AddMonths(2);
        preview.Rows.Add(1, 2, august); preview.Rows.Add(1, 2, june); preview.Rows.Add(1, 2, july); preview.Rows.Add(2, 2, june);
        Check(FeePostingPage.PreviewMonths(preview).SequenceEqual(new[] { june, july, august }), "Vacation months are distinct and posted chronologically");
        foreach (var month in FeePostingPage.PreviewMonths(preview))
        {
            var selection = Tvp.PostingSelectionsFromPreview(preview, row => (DateTime)row["BillingMonth"] == month);
            Check(selection.AsEnumerable().All(row => (DateTime)row["BillingMonth"] == month), "A monthly procedure receives only its own month");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new FeePostingPage(false);
        var through = (LookupBox)typeof(FeePostingPage).GetField("throughMonth", flags)!.GetValue(page)!;
        var picker = (StudentPicker)typeof(FeePostingPage).GetField("student", flags)!.GetValue(page)!;
        var months = new DataTable(); months.Columns.Add("MonthKey", typeof(int)); months.Columns.Add("Display");
        months.Rows.Add(202708, "August 2027"); through.Bind(months, "MonthKey", "Display");
        typeof(FeePostingPage).GetField("eligibleRows", flags)!.SetValue(page, preview);
        through.SelectedValue = 202708;
        Check(typeof(FeePostingPage).GetField("eligibleRows", flags)!.GetValue(page) is null, "Changing the end month invalidates the old preview");
        var signature = typeof(FeePostingPage).GetMethod("FilterSignature", flags)!;
        string before = (string)signature.Invoke(page, null)!;
        picker.SetStudent(12, "Selected student");
        Check((string)signature.Invoke(page, null)! != before, "Student selection participates in stale-preview protection");
        typeof(FeePostingPage).GetField("loadedMonth", flags)!.SetValue(page, june);
        typeof(FeePostingPage).GetField("loadedThrough", flags)!.SetValue(page, august);
        Check((string)typeof(FeePostingPage).GetProperty("PeriodLabel", flags)!.GetValue(page)! == "June 2027 through August 2027", "Confirmation identifies the complete month range");
    }
}
