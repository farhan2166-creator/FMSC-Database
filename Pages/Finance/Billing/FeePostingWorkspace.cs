using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>Fee Posting workspace - manual posting and its reversal, side by side.</summary>
public static class FeePostingWorkspace
{
    public static Control Create() =>
        new WorkspacePage(
            PageTitles.FeePosting,
            "Monthly fees are automatic. Review billing exceptions or use administrative recovery when needed.",
            ("Billing Status", () => new BillingStatusPage()),
            ("Manual Recovery", () => new FeePostingPage()),
            ("Post Additional Fee", () => new AdditionalFeePostingPage()),
            ("Reverse a Posting", () => new FeePostingReversalPage()));
}
