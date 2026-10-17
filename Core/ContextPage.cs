namespace FMSC_SMS.Core;
public abstract class ContextPage : V2Page
{
    protected LookupBox Campus = new();
    protected LookupBox Year = new();
    protected LookupBox Class = new();
    protected LookupBox Section = new();
    protected readonly ModernDatePicker DateBox =
        new ModernDatePicker {
            Format =
                DateTimePickerFormat.Custom,
            CustomFormat =
                "dd-MMM-yyyy",
            Dock =
                DockStyle.Fill
        };
    protected ContextPage(string title, string context)
        : base(title, context)
    {
        /* Two overlapping loads (the campus-change path runs the year and class loads together) then attached the same handler twice. */
        Campus.SelectedIndexChanged += CampusChanged;
        Year.SelectedIndexChanged += YearChanged;
        Class.SelectedIndexChanged += ClassChanged;
    }
    /* greater than zero while this class is filling a picker itself. */
    private int _contextPickerFills;
    private void ContextPickerFill(Action fill)
    {
        _contextPickerFills++;
        try
        {
            fill();
        }
        finally
        {
            _contextPickerFills--;
        }
    }
    protected TableLayoutPanel ContextLayout(bool includeSection = true)
    {
        WorkspaceState.Remember(Campus, "Context:Campus");
        WorkspaceState.Remember(Year, "Context:Year");
        WorkspaceState.Remember(Class, "Context:Class");
        if (includeSection)
            WorkspaceState.Remember(Section, "Context:Section");
        var table =
            new TableLayoutPanel {
                Dock =
                    DockStyle.Top,
                ColumnCount =
                    2,
                AutoSize =
                    true,
                Padding =
                    new Padding(0)
            };
        table.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        var fields =
            new List<(string Label, Control Control)>
            {
                ("Campus", Campus),
                ("Academic Year", Year)
            };
        fields.Add(("Class", Class));
        if (includeSection)
        {
            fields.Add(("Section", Section));
        }
        int fieldRowHeight =
            ContextFieldRowHeight();
        for (int i = 0;
             i < fields.Count;
             i += 2)
        {
            int row =
                table.RowCount++;
            table.RowStyles.Add(
                new RowStyle(SizeType.Absolute, fieldRowHeight));
            table.Controls.Add(
                CreateContextField(fields[i].Label, fields[i].Control), 0, row);
            if (i + 1 < fields.Count)
            {
                table.Controls.Add(
                    CreateContextField(fields[i + 1].Label, fields[i + 1].Control), 1, row);
            }
        }
        return table;
    }
    private static int ContextLabelRowHeight() =>
        Math.Max(
            16,
            AppMetrics.TextHeight(AppTheme.NormalFont));
    protected static int ContextFieldRowHeight() =>
        ContextLabelRowHeight() +
        AppMetrics.InputControlHeight +
        12;
    private static Control CreateContextField(string label, Control control)
    {
        return HumanUi.Field(label, control);
    }
    private CancellationTokenSource? _contextLoadCts;
    private int _contextLoadVersion;
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _contextLoadVersion++;
            _contextLoadCts?.Cancel();
            _contextLoadCts?.Dispose();
            _contextLoadCts = null;
        }
        base.Dispose(disposing);
    }
    /* The four context pickers are filled from the database asynchronously, so until that data arrives an empty or stale picker must not be usable. */
    private int _contextLoadsInFlight;
    private readonly HashSet<Control> _contextBlockedControls = new();
    private readonly HashSet<Control> _contextControlsChangedWhileLoading = new();
    private readonly List<(Control Control, bool Enabled)> _contextRestoreState = new();
    /* UX fix - picking the next item in a picker with the Down arrow fires its SelectedIndexChanged. */
    private Control? _contextFocusToRestore;
    private LookupBox? _pendingTabSource;
    private LookupBox? _pendingTabTarget;
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_contextLoadsInFlight > 0 && (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab)))
        {
            var pickers = ContextPickers();
            int index = Array.FindIndex(pickers, picker => picker.ContainsFocus);
            int next = index + (keyData == Keys.Tab ? 1 : -1);
            if (index >= 0 && next >= 0 && next < pickers.Length && Contains(pickers[next]) &&
                pickers[next].Visible && !pickers[next].Enabled &&
                _contextRestoreState.Any(entry => entry.Control == pickers[next] && entry.Enabled))
            {
                _pendingTabSource = pickers[index];
                _pendingTabTarget = pickers[next];
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private LookupBox[] ContextPickers() =>
        new[] { Campus, Year, Class, Section };
    private void ContextControlChangedWhileLoading(object? sender, EventArgs e)
    {
        if (sender is Control control)
            _contextControlsChangedWhileLoading.Add(control);
    }
    private void EnterContextLoad()
    {
        if (_contextLoadsInFlight > 0)
        {
            _contextLoadsInFlight++;
            return;
        }
        _contextRestoreState.Clear();
        _contextControlsChangedWhileLoading.Clear();
        foreach (LookupBox picker in ContextPickers())
            _contextRestoreState.Add((picker, picker.Enabled));
        foreach (Control action in Actions.Controls)
            _contextRestoreState.Add((action, action.Enabled));
        _contextFocusToRestore = _contextRestoreState
            .Select(entry => entry.Control)
            .FirstOrDefault(control => control.ContainsFocus);
        foreach (var (control, _) in _contextRestoreState)
        {
            /* Keep the picker the operator is actively navigating enabled. Disabling it on every
               Up/Down selection moved keyboard focus elsewhere while its dependent lists loaded. */
            if (ReferenceEquals(control, _contextFocusToRestore))
                continue;
            control.Enabled = false;
            control.EnabledChanged += ContextControlChangedWhileLoading;
        }
        _contextLoadsInFlight = 1;
    }
    private void ExitContextLoad(LookupBox owner, bool? loaded)
    {
        if (loaded is true)
            _contextBlockedControls.Remove(owner);
        else if (loaded is false)
            BlockContextControl(owner);
        if (_contextLoadsInFlight > 0)
            _contextLoadsInFlight--;
        if (_contextLoadsInFlight > 0)
            return;
        RestoreContextState();
    }
    /* A failed load makes its own picker unusable, so the picker is held down and cleared together with the pickers that depend on it. */
    private void BlockContextControl(LookupBox owner)
    {
        _contextBlockedControls.Add(owner);
        ContextPickerFill(() => {
            owner.ClearLookup();
            if (owner == Campus || owner == Year)
            {
                _contextBlockedControls.Add(Class);
                Class.ClearLookup();
            }
            if (owner == Campus || owner == Year || owner == Class)
            {
                _contextBlockedControls.Add(Section);
                Section.ClearLookup();
            }
        });
    }
    /* The mirror of BlockContextControl: the dependents of a picker the operator has just changed have been cleared and are about to be rebuilt from the new parent. */
    private void UnblockContextDependents(LookupBox owner)
    {
        if (owner == Campus || owner == Year)
            _contextBlockedControls.Remove(Class);
        if (owner == Campus || owner == Year || owner == Class)
            _contextBlockedControls.Remove(Section);
    }
    private void RestoreContextState()
    {
        foreach (var (control, _) in _contextRestoreState)
            control.EnabledChanged -= ContextControlChangedWhileLoading;
        if (IsDisposed)
            return;
        bool incomplete = _contextBlockedControls.Count > 0;
        foreach (var (control, wasEnabled) in _contextRestoreState)
        {
            if (control.IsDisposed ||
                _contextControlsChangedWhileLoading.Contains(control))
                continue;
            bool heldDown = control is LookupBox picker
                ? _contextBlockedControls.Contains(picker)
                : incomplete;
            control.Enabled = wasEnabled && !heldDown;
        }
        _contextFocusToRestore = null;
        // The source picker stayed enabled. Keep its native focus as-is;
        // restoring focus here steals a later Tab or mouse selection.
        var tabSource = _pendingTabSource;
        var tabTarget = _pendingTabTarget;
        _pendingTabSource = _pendingTabTarget = null;
        if (tabSource is { ContainsFocus: true } && tabTarget is { Enabled: true, Visible: true })
            tabTarget.Focus();
    }
    // Clear inherited placement before verifying a student who has never been enrolled.
    protected async Task PrepareFirstPlacementAsync(int admissionCampusId)
    {
        int version = ++_contextLoadVersion;
        _contextLoadCts?.Cancel();
        var campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
        var yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
        await Task.WhenAll(campusesTask, yearsTask);
        if (IsDisposed || version != _contextLoadVersion) return;
        ContextPickerFill(() => {
            Campus.Bind(campusesTask.Result, "CampusId", "CampusName");
            Campus.SelectedValue = admissionCampusId;
            Year.Bind(yearsTask.Result, "AcademicYearId", "YearName");
            Class.ClearLookup();
            Section.ClearLookup();
        });
    }
    protected void LoadCampuses() => _ = LoadCampusesAsync();
    private async Task LoadCampusesAsync()
    {
        if (!IsHandleCreated)
        {
            Defer(LoadCampuses);
            return;
        }
        int version = ++_contextLoadVersion;
        bool? loaded = false;
        EnterContextLoad();
        try
        {
            _contextLoadCts?.Cancel();
            _contextLoadCts?.Dispose();
            _contextLoadCts = new CancellationTokenSource();
            DataTable campuses = await DatabaseHelper.QueryUserAsync("GetCampuses");
            if (IsDisposed || version != _contextLoadVersion)
            {
                /* Superseded by a newer campus load - that one owns the conclusion, so this one neither blocks nor restores. */
                loaded = null;
                return;
            }
            ContextPickerFill(() => Campus.Bind(campuses, "CampusId", "CampusName"));
            if (Campus.Items.Count <= 1)
                SetStatus("No campuses are configured. Configure a campus before continuing.", true);
            loaded = true;
        }
        catch (Exception ex)
        {
            if (IsDisposed || version != _contextLoadVersion)
            {
                loaded = null;
            }
            else
            {
                Fail(ex);
            }
        }
        finally
        {
            ExitContextLoad(Campus, loaded);
        }
    }
    private void CampusChanged(object? sender, EventArgs e)
    {
        /* A list this class is filling itself is not the operator changing the selection. */
        if (_contextPickerFills > 0)
            return;
        /* The class and section lists on screen belong to the campus that has just been replaced. */
        ContextPickerFill(() => {
            Class.ClearLookup();
            Section.ClearLookup();
        });
        UnblockContextDependents(Campus);
        LoadYears();
        LoadAllClasses();
    }
    protected void LoadYears() => _ = LoadYearsAsync();
    private async Task LoadYearsAsync()
    {
        int version = _contextLoadVersion;
        int campusId = Campus.SelectedId;
        bool? loaded = false;
        EnterContextLoad();
        try
        {
            if (Campus.SelectedId <= 0)
            {
                ContextPickerFill(() => {
                    Year.ClearLookup();
                    Class.ClearLookup();
                    Section.ClearLookup();
                });
                loaded = true;
                return;
            }
            DataTable years = await DatabaseHelper.QueryUserAsync("GetAcademicYears");
            if (IsDisposed || version != _contextLoadVersion || Campus.SelectedId <= 0)
            {
                loaded = null;
                return;
            }
            ContextPickerFill(() => Year.Bind(years, "AcademicYearId", "YearName"));
            loaded = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == _contextLoadVersion)
                Fail(ex);
            /* A request the operator has already superseded must not hold down or clear the list a newer load is producing. */
            if (IsDisposed || version != _contextLoadVersion || Campus.SelectedId != campusId)
                loaded = null;
        }
        finally
        {
            ExitContextLoad(Year, loaded);
        }
    }
    private void YearChanged(object? sender, EventArgs e)
    {
        /* A list this class is filling itself is not the operator changing the selection. */
        if (_contextPickerFills > 0)
            return;
        /* The class list is built for a campus AND a year (LoadAllClassesAsync refuses to run without either) and the section list is built for the class. */
        ContextPickerFill(() => {
            Class.ClearLookup();
            Section.ClearLookup();
        });
        UnblockContextDependents(Year);
        LoadAllClasses();
    }
    protected void LoadAllClasses() => _ = LoadAllClassesAsync();
    private async Task LoadAllClassesAsync()
    {
        if (!IsHandleCreated)
        {
            Defer(LoadAllClasses);
            return;
        }
        int version = _contextLoadVersion;
        int campusId = Campus.SelectedId;
        int yearId = Year.SelectedId;
        bool? loaded = false;
        EnterContextLoad();
        try
        {
            if (campusId <= 0 || yearId <= 0)
            {
                ContextPickerFill(() => {
                    Class.ClearLookup();
                    Section.ClearLookup();
                });
                loaded = true;
                return;
            }
            DataTable classes = await DatabaseHelper.QueryUserAsync("GetClasses");
            if (IsDisposed || version != _contextLoadVersion ||
                Campus.SelectedId != campusId || Year.SelectedId != yearId)
            {
                loaded = null;
                return;
            }
            ContextPickerFill(() => {
                Class.Bind(classes, "ClassId", "ClassName");
                Section.ClearLookup();
            });
            loaded = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == _contextLoadVersion)
                Fail(ex);
            /* A request the operator has already superseded must not hold down or clear the list a newer load is producing. */
            if (IsDisposed || version != _contextLoadVersion ||
                Campus.SelectedId != campusId || Year.SelectedId != yearId)
                loaded = null;
        }
        finally
        {
            ExitContextLoad(Class, loaded);
        }
    }
    private void ClassChanged(object? sender, EventArgs e)
    {
        /* A list this class is filling itself is not the operator changing the selection. */
        if (_contextPickerFills > 0)
            return;
        /* The section list is built for the class that has just been replaced, so it goes at the moment the parent changes. */
        ContextPickerFill(() => Section.ClearLookup());
        UnblockContextDependents(Class);
        LoadSections();
    }
    protected void LoadSections() => _ = LoadSectionsAsync();
    private async Task LoadSectionsAsync()
    {
        int version = _contextLoadVersion;
        int campusId = Campus.SelectedId;
        int classId = Class.SelectedId;
        EnterContextLoad();
        bool? loaded = false;
        try
        {
            if (campusId <= 0 || classId <= 0)
            {
                ContextPickerFill(() => Section.ClearLookup());
                loaded = true;
                return;
            }
            DataTable sections = await DatabaseHelper.QueryUserAsync(
                "GetClassSections",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Int("@ClassId", classId),
                DatabaseHelper.Int("@SectionId", 0));
            if (IsDisposed || version != _contextLoadVersion ||
                Campus.SelectedId != campusId || Class.SelectedId != classId)
            {
                loaded = null;
                return;
            }
            ContextPickerFill(() => Section.Bind(sections, "ClassSectionId", "SectionName"));
            loaded = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == _contextLoadVersion)
                Fail(ex);
            /* A request the operator has already superseded must not hold down or clear the list a newer load is producing. */
            if (IsDisposed || version != _contextLoadVersion ||
                Campus.SelectedId != campusId || Class.SelectedId != classId)
                loaded = null;
        }
        finally
        {
            ExitContextLoad(Section, loaded);
        }
    }
}
