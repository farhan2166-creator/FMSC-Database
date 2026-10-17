using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>Fee Posting workspace - manual posting and its reversal, side by side.</summary>
public static class FeePostingWorkspace
{
    public static Control Create() =>
        new WorkspacePage(
            PageTitles.FeePosting,
            "Create monthly bills after setting student fees. Check billing or cancel uncollected charges.",
            ("Post Monthly Fees", () => new FeePostingPage()),
            ("Post Additional Fee", () => new AdditionalFeePostingPage()),
            ("Billing Status", () => new BillingStatusReportPage()),
            (PageTitles.UnpostedFees, () => new UnpostedFeesReportPage()),
            ("Reverse a Posting", () => new FeePostingReversalPage()));
}
