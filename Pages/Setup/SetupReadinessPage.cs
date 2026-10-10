using FMSC_SMS.Core;
using FMSC_SMS.Pages.Administration;
using FMSC_SMS.Pages.Finance.Configuration;
using FMSC_SMS.Pages.Setup.Academics;
using FMSC_SMS.Services;

namespace FMSC_SMS.Pages.Setup;

public sealed class SetupReadinessPage : V2Page
{
    private readonly Action<Func<Control>, string, string> navigate;
    private readonly ModernButton open;
    private bool checking;
    public SetupReadinessPage(Action<Func<Control>, string, string> navigate) : base(
        "Setup Readiness", "Checks configuration for daily admission and billing. These checks do not change school data.")
    {
        this.navigate = navigate;
        AddBusyAction("Check Again", CheckAsync, "Primary");
        open = AddAction("Open Selected Setup", (_, _) => OpenSelected(), "Secondary");
        UseWrappingToolbar();
        open.Enabled = false;
        SetContent(Grid);
        Grid.SelectionChanged += (_, _) => open.Enabled = Grid.CurrentRow?.DataBoundItem is DataRowView;
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        Defer(CheckAsync);
    }
    private async Task CheckAsync()
    {
        if (checking) return;
        checking = true;
        open.Enabled = false;
        try
        {
            var issues = await SetupReadiness.CheckAsync();
            if (IsDisposed) return;
            Bind(issues);
            Grid.Columns["PageKey"].Visible = false;
            Grid.Columns["Area"].FillWeight = 20;
            Grid.Columns["Issue"].FillWeight = 80;
            Grid.Columns["Issue"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            Grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            SetStatus(issues.Rows.Count == 0 ? "The checked setup is ready. Optional fees and transport rates should still be reviewed for your school." :
                $"{issues.Rows.Count} setup item(s) need attention. Select one to open its setup page.", issues.Rows.Count > 0);
        }
        catch (Exception ex) { Grid.DataSource = null; if (!IsDisposed) Fail(ex); }
        finally { checking = false; }
    }
    private void OpenSelected()
    {
        if (Grid.CurrentRow?.DataBoundItem is not DataRowView row) return;
        (Func<Control> Factory, string Module, string Title, string Permission)? route = Convert.ToString(row["PageKey"]) switch {
            "Profile" => (() => new SchoolProfilePage(), "School Setup", "School Profile", "System.Setup"),
            "Campuses" => (() => new CampusesPage(), "School Setup", "Campuses", "System.Setup"),
            "Assignments" => (() => new CampusClassSectionAssignmentsPage(), "School Setup", PageTitles.CampusAssignments, "System.Setup"),
            "Years" => (() => new AcademicYearsPage(), "School Setup", "Academic Years", "Calendar.Manage"),
            "Fees" => (() => FeeConfigurationWorkspace.Create(), "Finance", PageTitles.FeeRateSetup, "Fees.Configuration.Manage"),
            "Backup" => (() => new BackupRestorePage(), "Administration", PageTitles.BackupRestore, "Backup.Create"),
            _ => null
        };
        if (route is { } destination && CurrentUser.HasPermission(destination.Permission))
            navigate(destination.Factory, destination.Module, destination.Title);
    }
}
