using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Setup.Academics;
using FMSC_SMS.Pages.Finance.Configuration;

internal static partial class Program
{
    private static void CheckSelectiveWorkspaceGuards()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var saving = typeof(MasterPage).GetField("_saving", flags)!;
        foreach (SetupCatalog catalog in Enum.GetValues<SetupCatalog>())
        {
            using var setup = new ClassSectionCatalogDialog(catalog);
            var child = setup.Editor;
            Check((catalog, child) switch {
                (SetupCatalog.Campuses, FMSC_SMS.Pages.Setup.CampusesPage) => true,
                (SetupCatalog.Classes, ClassesPage) => true,
                (SetupCatalog.Sections, SectionsPage) => true,
                _ => false
            }, "Catalog dialog retains the complete existing editor: " + catalog);
            saving.SetValue(child, true);
            Check(setup.IsBusy && setup.BusyMessage == child.BusyMessage, "Catalog dialog forwards submitted saves");
            saving.SetValue(child, false);
            Check(!setup.IsBusy, "Completed saves release catalog close guard");
            var deleting = typeof(MasterPage).GetField("_deleting", flags)!;
            deleting.SetValue(child, true);
            Check(setup.IsBusy, "Catalog delete in flight also blocks dialog close");
            deleting.SetValue(child, false);
        }
        using var bulk = new BulkFeeRateChangesForm(0, 0, false);
        bulk.TopLevel = false; // Match CreateEmbedded: a form must be made a child before hosting.
        using var workspace = new WorkspacePage("Fees", "", ("Adjust rates", () => bulk));
        typeof(WorkspacePage).GetMethod("EnsureSelectedTab", flags)!.Invoke(workspace, null);
        Check(!workspace.HasUnsavedChanges, "Loaded bulk fee-rate defaults do not prompt");
        var amount = (ModernNumericTextBox)typeof(BulkFeeRateChangesForm).GetField("adjustmentValue", flags)!.GetValue(bulk)!;
        amount.Value = 10;
        Check(bulk.HasUnsavedChanges && workspace.HasUnsavedChanges, "Meaningful bulk fee-rate changes reach the shell dirty contract");
        amount.Value = 0;
        Check(!workspace.HasUnsavedChanges, "Reverting a bulk adjustment restores the clean shell contract");
        amount.Value = 10;
        typeof(BulkFeeRateChangesForm).GetMethod("CaptureAppliedBaseline", flags)!.Invoke(bulk, null);
        Check(!workspace.HasUnsavedChanges, "A committed embedded bulk adjustment does not leave a stale discard warning");
        amount.Value = 20;
        Check(workspace.HasUnsavedChanges, "Further bulk adjustment edits are protected after a previous successful apply");
    }
}
