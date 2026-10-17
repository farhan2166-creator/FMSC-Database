using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Staff;
public sealed class CurrentEmploymentPanel : HumanCard, IUnsavedChanges, IBusyPage
{
    private bool _saving;
    private int _pendingLoads;
    private Dictionary<Control, bool>? _loadEnabled;
    private void BeginEmploymentLoad()
    {
        if (_pendingLoads++ != 0) return;
        _loadEnabled = new Control[] { designation, department, employmentType, hireDate, leavingDate, remarks, saveButton, clearButton }
            .Cast<Control>().ToDictionary(control => control, control => control.Enabled);
        foreach (Control control in _loadEnabled.Keys) control.Enabled = false;
    }
    private void EndEmploymentLoad()
    {
        if (--_pendingLoads != 0 || _loadEnabled is null) return;
        foreach (var entry in _loadEnabled) if (!entry.Key.IsDisposed) entry.Key.Enabled = entry.Value;
        _loadEnabled = null;
    }
    public bool IsBusy => _saving;
    public string BusyMessage => "Please wait for employment to finish saving.";
    public event Action? EmploymentChanged;
    public Func<bool>? SaveGuard { get; set; }
    public Task RefreshEmploymentAsync() => LoadCurrentEmploymentAsync();
    /* D-H7 - the caps match the COLUMNS, because a longer value is truncated silently rather than refused. */
    private readonly ModernTextBox designation = HumanUi.TextBox(false, 100);
    private readonly ModernTextBox department = HumanUi.TextBox(false, 100);
    private readonly ModernComboBox employmentType = HumanUi.Combo();
    private readonly ModernDatePicker hireDate = HumanUi.Date();
    private readonly ModernDatePicker leavingDate = HumanUi.Date();
    private readonly ModernTextBox remarks = HumanUi.TextBox(true, 500);
    private readonly ModernButton saveButton = new() { Text = UiText.Actions.Save, ThemeRole = "Success" };
    /* UX - a missing designation is marked on the field; the summary goes to the panel's status line. */
    private FormValidator? validator;
    private readonly ModernButton clearButton = new() { Text = UiText.Actions.Clear, ThemeRole = "Secondary" };
    /// <summary>this panel had no way to report anything. The panel now carries its own one-line status instead of reaching for a host method that was never written.</summary>
    private readonly Label statusLabel = new() {
        Dock = DockStyle.Bottom,
        Height = Math.Max(20, AppMetrics.TextHeight(AppTheme.NormalFont)),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        ForeColor = AppTheme.MutedText,
        BackColor = Color.Transparent
    };
    private int _staffId;
    internal DataTable? LoadedEmploymentRecords { get; private set; }
    private int _staffEmploymentId;
    private int _editVersion;
    private int _loadVersion;
    /* this panel had no dirty mechanism at all, which the register tracked as the remaining limit of. */
    private string? _appliedSignature;
    /// <summary>every value this panel edits, in one comparable string. Trimmed, because the save path trims, and dates compared by day, because the pickers carry a day.</summary>
    private string SnapshotSignature() =>
        string.Join(
            "\u001f",
            designation.Text.Trim(),
            department.Text.Trim(),
            employmentType.Text.Trim(),
            hireDate.Value.Date.ToString("yyyy-MM-dd"),
            leavingDate.Checked
                ? leavingDate.Value.Date.ToString("yyyy-MM-dd")
                : string.Empty,
            remarks.Text.Trim());
    /// <summary>(the remaining limit of ) - true when the fields no longer match what was loaded or cleared.</summary>
    public bool HasUnsavedChanges =>
        _appliedSignature is not null &&
        !string.Equals(_appliedSignature, SnapshotSignature(), StringComparison.Ordinal);
    public string UnsavedChangesMessage =>
        "Current Employment has edits that have not been saved.";
    public CurrentEmploymentPanel()
        : base("Current Employment", "View or update the selected staff member’s current job.")
    {
        leavingDate.ShowCheckBox = true;
        UiAccessibility.Describe(leavingDate, "Leaving date",
            "Leave unchecked for active employment. Check and select a date when the staff member leaves.");
        employmentType.Items.AddRange(new object[] { "Permanent", "Contract", "Visiting" });
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(0, 4, 0, 0) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeightFor(78)));
        editor.Controls.Add(HumanUi.Field("Designation", designation, true), 0, 0);
        editor.Controls.Add(HumanUi.Field("Department", department), 1, 0);
        editor.Controls.Add(HumanUi.Field("Employment Type", employmentType), 0, 1);
        editor.Controls.Add(HumanUi.Field("Hire Date", hireDate, true), 1, 1);
        editor.Controls.Add(HumanUi.Field("Leaving Date", leavingDate), 0, 2);
        var remarksField = HumanUi.Field("Remarks", remarks, false, fillHeight: false);
        editor.Controls.Add(remarksField, 0, 3);
        editor.SetColumnSpan(remarksField, 2);
        var buttonRow = HumanUi.ButtonRow(saveButton, clearButton);
        buttonRow.Dock = DockStyle.Top;
        Content.Controls.Add(editor);
        Content.Controls.Add(buttonRow);
        /* Added last so docking gives it the bottom edge: the button row takes the top, the editor fills what is left. */
        Content.Controls.Add(statusLabel);
        /* Save runs dbo.SaveStaffEmployment, which also synchronizes the staff member's lifecycle status in the same transaction, so a second click during the first write could save. */
        saveButton.Click += async (_, _) => {
            bool restoreEnabled = saveButton.Enabled;
            if (!restoreEnabled)
                return;
            saveButton.Enabled = false;
            _saving = true;
            try
            {
                await SaveAsync();
            }
            finally
            {
                _saving = false;
                if (!IsDisposed) saveButton.Enabled = restoreEnabled;
            }
        };
        clearButton.Click += (_, _) => {
            if (_saving) return;
            ClearForm();
            /* and it re-armed an INSERT in silence: with _staffEmploymentId back at 0 the next Save adds a second employment record for the same staff member instead of updating the loaded… */
            if (_staffId > 0)
            {
                SetStatus("Form cleared. Save will add a NEW employment record for this staff member.");
            }
        };
        /* this panel lives inside Staff Management, whose navigation requires Staff.View, but Save writes through dbo.SaveStaffEmployment, which requires Staff.Manage. */
        if (!CurrentUser.HasPermission("Staff.Manage"))
        {
            saveButton.Visible = false;
            saveButton.Enabled = false;
        }
        ClearForm();
    }
    public async Task SetStaffIdAsync(int staffId)
    {
        _staffId = staffId;
        LoadedEmploymentRecords = null;
        /* The form still held the PREVIOUS staff member's employment id and fields while the new one loaded (and stayed that way if the load failed). */
        ClearForm();
        await LoadCurrentEmploymentAsync();
    }
    private void ClearForm()
    {
        _editVersion++;
        _loadVersion++;
        _staffEmploymentId = 0;
        designation.Clear();
        department.Clear();
        employmentType.SelectedIndex = -1;
        hireDate.Value = DateTime.Today;
        leavingDate.Value = DateTime.Today;
        leavingDate.Checked = false;
        remarks.Clear();
        /* The cleared form is a baseline like any other: what is on screen after Clear is not "unsaved changes" until the operator types into it. */
        _appliedSignature = SnapshotSignature();
    }
    private async Task LoadCurrentEmploymentAsync()
    {
        BeginEmploymentLoad();
        /* The id this load is for, captured before the await. */
        int requestedStaffId = _staffId;
        int version = ++_loadVersion;
        if (requestedStaffId <= 0)
        {
            ClearForm();
            EndEmploymentLoad();
            return;
        }
        try
        {
            LoadedEmploymentRecords = null;
            var dt = await DatabaseHelper.QueryUserAsync("GetStaffEmployment", DatabaseHelper.Int("@StaffId", requestedStaffId));
            /* The captured id must still be the selected one, or this result belongs to a staff member the operator has already moved off. */
            if (IsDisposed || version != _loadVersion || _staffId != requestedStaffId)
                return;
            LoadedEmploymentRecords = dt.Copy();
            if (dt is null || dt.Rows.Count == 0)
            {
                ClearForm();
                SetStatus("No active employment record.");
                return;
            }
            DataRow? active = null;
            // Employment availability follows its lifecycle status.
            if (active is null && dt.Columns.Contains("Status"))
            {
                active = dt.AsEnumerable().FirstOrDefault(r => string.Equals(Convert.ToString(r["Status"]), "Active", StringComparison.OrdinalIgnoreCase));
            }
            // Final fallback: LeavingDate is null
            if (active is null && dt.Columns.Contains("LeavingDate"))
            {
                active = dt.AsEnumerable().FirstOrDefault(r => r["LeavingDate"] == DBNull.Value);
            }
            if (active is null)
            {
                ClearForm();
                SetStatus("No active employment record.");
                return;
            }
            _staffEmploymentId = Convert.ToInt32(active["StaffEmploymentId"]);
            _editVersion++;
            designation.Text = Convert.ToString(active["Designation"]) ?? string.Empty;
            department.Text = Convert.ToString(active["Department"]) ?? string.Empty;
            employmentType.Text = Convert.ToString(active["EmploymentType"]) ?? string.Empty;
            if (dt.Columns.Contains("HireDate") && active["HireDate"] != DBNull.Value)
                hireDate.Value = Convert.ToDateTime(active["HireDate"]);
            if (dt.Columns.Contains("LeavingDate") && active["LeavingDate"] != DBNull.Value)
            {
                leavingDate.Value = Convert.ToDateTime(active["LeavingDate"]);
                leavingDate.Checked = true;
            }
            else
            {
                leavingDate.Checked = false;
            }
            remarks.Text = Convert.ToString(active["Remarks"]) ?? string.Empty;
            /* The record just loaded is the baseline "unsaved" is measured against. */
            _appliedSignature = SnapshotSignature();
            SetStatus("Active employment loaded.");
        }
        catch (Exception ex)
        {
            /* A failed load must not leave a record belonging to someone else in the editor. */
            if (!IsDisposed && version == _loadVersion && _staffId == requestedStaffId)
            {
                ClearForm();
                Fail(ex);
            }
        }
        finally { EndEmploymentLoad(); }
    }
    private void SetStatus(string text, bool warning = false)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor =
            warning ? AppTheme.Warning : AppTheme.MutedText;
    }
    private async Task SaveAsync()
    {
        if (_pendingLoads > 0 || SaveGuard?.Invoke() == false) return;
        if (_staffId <= 0)
        {
            SetStatus("Select a staff member first.", true);
            return;
        }
        if (validator is null)
        {
            validator = new FormValidator {
                SummaryChanged = summary => {
                    if (!statusLabel.IsDisposed && summary is not null)
                        SetStatus(summary, true);
                }
            };
            Disposed += (_, _) => validator.Dispose();
        }
        validator.Begin();
        validator.Required(designation, "Required");
        validator.Check(leavingDate, !leavingDate.Checked || leavingDate.Value.Date >= hireDate.Value.Date,
            "Leaving date cannot be earlier than hire date.");
        if (!validator.Finish())
            return;
        int staffId = _staffId;
        int employmentId = _staffEmploymentId;
        int editVersion = _editVersion;
        using var hold = new BusyScope(string.Empty,
            new Control[] { designation, department, employmentType, hireDate, leavingDate, remarks, clearButton },
            () => statusLabel.Text, () => PageStatusKind.Info,
            (message, _) => SetStatus(message));
        try
        {
            // Freeze both identity and entered values before the asynchronous pre-check.
            var parameters = new[] {
                DatabaseHelper.Int("@StaffEmploymentId", employmentId),
                DatabaseHelper.Int("@StaffId", staffId),
                DatabaseHelper.P("@Designation", designation.Text.Trim()),
                DatabaseHelper.P("@Department", DatabaseHelper.OptionalText(department.Text)),
                DatabaseHelper.P("@EmploymentType", string.IsNullOrWhiteSpace(employmentType.Text) ? null : employmentType.Text),
                DatabaseHelper.Date("@HireDate", hireDate.Value.Date),
                DatabaseHelper.P("@LeavingDate", leavingDate.Checked ? leavingDate.Value.Date : null),
                DatabaseHelper.P("@Remarks", DatabaseHelper.OptionalText(remarks.Text))
            };
            if (employmentId == 0 && !leavingDate.Checked)
            {
                bool hasActive = await HasActiveEmploymentAsync(staffId);
                if (IsDisposed || _editVersion != editVersion || _staffId != staffId)
                    return;
                if (hasActive)
                {
                    const string message =
                        "This staff member already has an active employment record.\r\n\r\n" +
                        "Set a leaving date on that record first - or select it and use Update - and " +
                        "then add the new one. Only one employment record can be active at a time.";
                    SetStatus(message.Split('\r')[0], true);
                    ModernMessageDialog.ShowWarning(FindForm(), message, "Current Employment");
                    return;
                }
            }
            await DatabaseHelper.ExecuteUserAsync(
                "SaveStaffEmployment",
                parameters);
            if (IsDisposed || _editVersion != editVersion || _staffId != staffId)
                return;
            /* The lifecycle step is NOT the UI's to make. dbo.SaveStaffEmployment already synchronizes Staff.Status inside its own transaction. */
            // Refresh to pick up the assigned id and the status the save set.
            await LoadCurrentEmploymentAsync();
            EmploymentChanged?.Invoke();
            if (!IsDisposed && _staffId == staffId) SetStatus("Employment saved.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed && _staffId == staffId) Fail(ex);
        }
    }
    /// <summary>D-H6 - true when the database already holds an active employment record for the selected staff member: the state the server's filtered unique index refuses with a raw 2601.</summary>
    private async Task<bool> HasActiveEmploymentAsync(int staffId)
    {
        try
        {
            var existing = await Task.Run(() =>
                DatabaseHelper.QueryUser(
                    "GetStaffEmployment",
                    DatabaseHelper.Int("@UserId", CurrentUser.UserId),
                    DatabaseHelper.Int("@StaffId", staffId)));
            if (existing is null ||
                existing.Rows.Count == 0 ||
                !existing.Columns.Contains("Status"))
            {
                return false;
            }
            foreach (DataRow row in existing.Rows)
            {
                if (row["Status"] == DBNull.Value)
                    continue;
                if (string.Equals(Convert.ToString(row["Status"]), "Active", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            try
            {
                AppExceptionHandler.LogNonFatal(ex, "CurrentEmploymentPanel: active-employment pre-check");
            }
            catch (Exception nonFatalException38)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException38, "CurrentEmploymentPanel: HasActiveEmploymentAsync fallback");
                // A pre-check must never be the reason a save fails.
            }
            return false;
        }
    }
    private void Fail(Exception ex)
    {
        try
        {
            AppExceptionHandler.LogNonFatal(ex, "CurrentEmploymentPanel");
        }
        catch (Exception nonFatalException39)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException39, "CurrentEmploymentPanel: Fail fallback");
        }
        /* The failure must also be visible where the panel reports, not only in a dialog the operator may have dismissed. */
        string message = DbMessage.UserMessage(ex);
        SetStatus(message, true);
        try
        {
            ModernMessageDialog.Show(FindForm(), message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception nonFatalException40)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException40, "CurrentEmploymentPanel: Fail fallback");
        }
    }
}
