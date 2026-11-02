using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    private static void CheckReportPurposeControls()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new BillingDiscountsReportPage();
        var view=(ModernComboBox)typeof(CanonicalFinanceReportPage).GetField("View",flags)!.GetValue(page)!;
        var from=(ModernDatePicker)typeof(CanonicalFinanceReportPage).GetField("From",flags)!.GetValue(page)!;
        var to=(ModernDatePicker)typeof(CanonicalFinanceReportPage).GetField("To",flags)!.GetValue(page)!;
        var asOf=(ModernDatePicker)typeof(BillingDiscountsReportPage).GetField("asOf",flags)!.GetValue(page)!;
        Check(!asOf.Enabled && from.Enabled && to.Enabled,"Ordinary billing views retain optional date range");
        view.SelectedItem="Concession Summary";
        Check(asOf.Enabled && !from.Enabled && !to.Enabled,"Concession Summary uses as-of date instead of period range");
        var signature=typeof(CanonicalFinanceReportPage).GetMethod("ReportFilterSignature",flags)!;
        var before=(string)signature.Invoke(page,null)!;
        asOf.Value=asOf.Value.AddDays(-1);
        Check(before!=(string)signature.Invoke(page,null)!,"Changing concession as-of invalidates prior report signature");
        view.SelectedItem="Concession Assignments";
        Check(!asOf.Enabled && from.Enabled && to.Enabled,"Agreement history restores date range controls");
        Check(!page.IsHandleCreated,"Report-purpose controls initialize without database access");
    }
}
