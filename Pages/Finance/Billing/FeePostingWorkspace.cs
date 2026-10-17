using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>Fee Posting workspace - manual posting and its reversal, side by side.</summary>
public static class FeePostingWorkspace
{
    public static Control Create() =>
        new WorkspacePage(
            PageTitles.FeePosting,
            "Review and post monthly or additional fees, or reverse uncollected charges.",
            ("Post Monthly Fees", () => new FeePostingPage()),
            ("Post Additional Fee", () => new AdditionalFeePostingPage()),
            ("Reverse a Posting", () => new FeePostingReversalPage()));
}
