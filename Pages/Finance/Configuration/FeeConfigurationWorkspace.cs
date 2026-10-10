using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Configuration;
/// <summary>Fee-rate setup, its bulk tools and applying revised rates to enrolled students.</summary>
public static class FeeConfigurationWorkspace
{
    /// <summary>Keep rate configuration separate from student payment corrections.</summary>
    public static Control Create() =>
        new WorkspacePage(
            PageTitles.FeeRateSetup,
            UiText.Fees.SetupSteps,
            (PageTitles.FeeRateMatrix, () => FeeRateMatrixForm.CreateEmbedded()),
            ("Fee Types", () => new FeeTypesPage()),
            (PageTitles.CopyFeeRates, () => CopyFeeRatesForm.CreateEmbedded()),
            (PageTitles.BulkFeeRateChanges, () => BulkFeeRateChangesForm.CreateEmbedded()));
}
