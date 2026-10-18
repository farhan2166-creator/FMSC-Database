using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Billing;
/// <summary>Fee Posting workspace - manual posting and its reversal, side by side.</summary>
public static class FeePostingWorkspace
{
    public static Control Create(Action<Func<Control>, string, string>? navigate = null) =>
        new WorkspacePage(
            PageTitles.FeePosting,
            "Review automatic billing, post fees or reverse a monthly posting. Receive payments in Fee Collection.",
            ("Billing Status", () => new BillingStatusPage(navigate)),
            ("Manual / Advance Posting", () => new FeePostingPage()),
            ("Post Additional Fee", () => new AdditionalFeePostingPage()),
            ("Reverse a Posting", () => new FeePostingReversalPage()));
}
