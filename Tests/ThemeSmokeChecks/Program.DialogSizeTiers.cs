using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Administration;
using FMSC_SMS.Pages.Finance.Configuration;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckDialogSizeTiers()
    {
        // Pure layout inspection: no live SQL, installer, or update access.
        var years = new DataTable();
        years.Columns.Add("AcademicYearId", typeof(int));
        years.Columns.Add("YearName", typeof(string));
        years.Columns.Add("StartDate", typeof(DateTime));
        years.Columns.Add("EndDate", typeof(DateTime));
        years.Columns.Add("IsCurrent", typeof(bool));
        years.Rows.Add(1, "2026-27", new DateTime(2026, 7, 1), new DateTime(2027, 6, 30), true);

        using var period = new ReportAcademicPeriodDialog(years, 12, 1, null);
        using var annual = new AnnualReceiptYearDialog("Example student", years);
        Check(period.ClientSize == AppMetrics.CompactChoiceDialogClientSize &&
            annual.ClientSize == AppMetrics.CompactChoiceDialogClientSize,
            "Related year and period selection dialogs share a compact choice size");

        using var fee = new FeeRateDetailsDialog("Class 1", "Tuition", 500, "Monthly", "");
        Check(fee.ClientSize.Width == AppMetrics.CompactEditorDialogClientSize.Width &&
            fee.ClientSize.Height >= AppMetrics.CompactEditorDialogClientSize.Height,
            "Fee editor remains at least the compact tier and preserves action-row room");

        using var reset = new PasswordResetDialog("test account",
            (_, _) => Task.FromException<AccessSaveResult>(new InvalidOperationException("UI-only fixture")));
        using var change = new ChangePasswordForm();
        Check(reset.ClientSize == AppMetrics.CredentialDialogClientSize,
            "Account password reset follows the credential editor standard");
        Check(change.ClientSize.Width == AppMetrics.CredentialDialogClientSize.Width &&
            change.ClientSize.Height >= AppMetrics.CredentialDialogClientSize.Height,
            "Own password change preserves any content-driven extra height");

        using var about = new AboutForm();
        using var navigation = new CommandPaletteDialog(Array.Empty<NavigationCommand>());
        Check(about.ClientSize == AppMetrics.MediumDialogClientSize &&
            navigation.ClientSize == AppMetrics.MediumDialogClientSize,
            "Related medium utility dialogs have the same client dimensions");

        // These constructors accept a no-database initialization mode.
        using var copy = new CopyFeeRatesForm(0, 0, false);
        using var bulk = new BulkFeeRateChangesForm(0, 0, false);
        Check(copy.ClientSize == AppMetrics.RateWorkspaceDialogClientSize &&
            bulk.ClientSize == AppMetrics.RateWorkspaceDialogClientSize,
            "Related fee-rate workspaces share their client dimensions");

        Check(AppMetrics.CompactChoiceDialogClientSize.Height >= AppScale.S(230) &&
            AppMetrics.CompactEditorDialogClientSize.Width >= AppScale.S(510) &&
            AppMetrics.CompactEditorDialogClientSize.Height >= AppScale.S(285),
            "Compact tiers never shrink the original known content areas");

        // The program manager and add-fee dialog load live catalogs in their
        // constructors; their geometry follows the tiers but isn't opened here.
    }
}
