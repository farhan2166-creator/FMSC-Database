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
    private readonly Func<Task<DataTable>> checkSetup;
    private string? checkSummary;
    private bool hasIssues;
    public SetupReadinessPage(Action<Func<Control>, string, string> navigate)
        : this(navigate, () => SetupReadiness.CheckAsync(), true) { }
    internal SetupReadinessPage(Action<Func<Control>, string, string> navigate, Func<Task<DataTable>> checkSetup, bool initialize) : base(
        PageTitles.SetupReadiness, "Check what needs to be set up before admitting students or billing fees.")
    {
        this.navigate = navigate;
        this.checkSetup = checkSetup;
        AddBusyAction("Check Again", CheckAsync, "Primary");
        open = AddAction("Open Selected Setup", (_, _) => OpenSelected(), "Secondary");
        UseWrappingToolbar();
        open.Enabled = false;
        SetContent(Grid);
        Grid.SelectionChanged += (_, _) => UpdateOpenAction();
        // SelectionChanged can precede CurrentRow moving to the new destination.
        Grid.CurrentCellChanged += (_, _) => UpdateOpenAction();
        open.EnabledChanged += (_, _) => { if (open.Enabled && !CanOpenSelected()) UpdateOpenAction(); };
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        if (initialize) Defer(CheckAsync);
    }
    private async Task CheckAsync()
    {
        if (checking) return;
        checking = true;
        checkSummary = null;
        UpdateOpenAction();
        SetStatus("Checking setup…");
        try
        {
            var issues = await checkSetup();
            if (IsDisposed) return;
            Bind(issues);
            Grid.Columns["PageKey"].Visible = false;
            Grid.Columns["Area"].FillWeight = 20;
            Grid.Columns["Issue"].FillWeight = 80;
            Grid.Columns["Issue"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            Grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            hasIssues = issues.Rows.Count > 0;
            checkSummary = hasIssues
                ? $"{issues.Rows.Count} setup item(s) need attention. Select one to open its setup page."
                : "The checked setup is ready. Optional fees and transport rates should still be reviewed for your school.";
        }
        catch (Exception ex) { Grid.DataSource = null; if (!IsDisposed) Fail(ex); }
        finally { checking = false; if (!IsDisposed) UpdateOpenAction(); }
    }
    private (Func<Control> Factory, string Module, string Title, string Permission)? SelectedDestination()
    {
        if (Grid.CurrentRow?.DataBoundItem is not DataRowView row) return null;
        return Convert.ToString(row["PageKey"]) switch {
            "Profile" => (() => new SchoolProfilePage(), "School Setup", "School Profile", "System.Setup"),
            "Campuses" => (() => new CampusesPage(), "School Setup", "Campuses", "System.Setup"),
            "Assignments" => (() => new CampusClassSectionAssignmentsPage(), "School Setup", PageTitles.CampusAssignments, "System.Setup"),
            "Years" => (() => new AcademicYearsPage(), "School Setup", "Academic Years", "Calendar.Manage"),
            "Fees" => (() => FeeConfigurationWorkspace.Create(), "School Setup", PageTitles.FeeRateSetup, "Fees.Configuration.Manage"),
            "Backup" => (() => new BackupRestorePage(), "Administration", PageTitles.BackupRestore, "Backup.Create"),
            _ => null
        };
    }
    private bool CanOpenSelected() => !checking && SelectedDestination() is { } destination && CurrentUser.HasPermission(destination.Permission);
    private void UpdateOpenAction()
    {
        var destination = SelectedDestination();
        bool permitted = destination is { } route && CurrentUser.HasPermission(route.Permission);
        open.Enabled = !checking && permitted;
        string guidance = checking ? "Wait for the setup check to finish."
            : destination is null ? "Select a setup item with an available page."
            : !permitted ? $"Ask an administrator to open {destination.Value.Title}. Your account cannot open this page."
            : $"Open {destination.Value.Title}.";
        open.AccessibleDescription = guidance;
        SetToolTip(open, guidance);
        if (!checking && checkSummary is not null)
            SetStatus(destination is not null && !permitted ? guidance : checkSummary,
                destination is not null && !permitted || hasIssues);
    }
    private void OpenSelected()
    {
        UpdateOpenAction();
        if (!CanOpenSelected()) return;
        if (SelectedDestination() is { } destination)
            navigate(destination.Factory, destination.Module, destination.Title);
    }
}
