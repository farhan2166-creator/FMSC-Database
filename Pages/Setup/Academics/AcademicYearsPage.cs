using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
// BD-23: any enrollment or promotion usage locks year dates; Name/Remarks/Current remain editable.
// The transactional SQL guard must also reject stale-form date changes.
public sealed class AcademicYearsPage : MasterPage
{
    private FormValidator? _yearValidator;
    protected override bool ReserveEditorValidationSpace => true;
    protected override int EditorValidationLines => 2;
    protected override bool EditorValidationSpansCaption => true;
    private FormValidator YearValidator
    {
        get
        {
            if (_yearValidator is null)
            {
                _yearValidator = CreateValidator();
                BindEditorValidationMessages(_yearValidator);
            }
            return _yearValidator;
        }
    }
    private void ValidateDates()
    {
        if (TryGetEditorControl("StartDate", out var start) && start is ModernDatePicker first &&
            TryGetEditorControl("EndDate", out var end) && end is ModernDatePicker last)
            YearValidator.Rule(last, () => last.Value.Date >= first.Value.Date, "End Date must be on or after Start Date.");
    }
    private bool datesLocked;
    private DateTime loadedStartDate, loadedEndDate;
    private const string LockedDatesMessage = "Start and End dates are locked because this year has student or promotion history. Name, Remarks and Current remain editable.";
    protected override void LayoutRecordColumns()
    {
        if (Grid.Columns.Contains("DatesLocked")) Grid.Columns["DatesLocked"].Visible = false;
        if (Grid.Columns.Contains("Remarks")) Grid.Columns["Remarks"].Visible = false;
        base.LayoutRecordColumns();
    }
    protected override bool CanSaveRecord()
    {
        var validator = YearValidator;
        validator.Begin();
        validator.RequiredFields(this);
        ValidateDates();
        if (TryGetEditorControl("YearName", out var name) && name is not null)
            validator.Rule(name, () => AcademicYearName.TryParse(name.Text, out _), AcademicYearName.ValidationMessage);
        if (datesLocked && TryGetEditorControl("StartDate", out var start) && start is ModernDatePicker startDate &&
            TryGetEditorControl("EndDate", out var end) && end is ModernDatePicker endDate &&
            (startDate.Value.Date != loadedStartDate || endDate.Value.Date != loadedEndDate))
        {
            Warn(LockedDatesMessage);
            return false;
        }
        return validator.Finish();
    }
    protected override void EditorCleared(bool afterSave)
    {
        base.EditorCleared(afterSave);
        _yearValidator?.ClearAll();
        startDateChosen = false;
        endDateChosen = false;
        SetDateLock(false);
        // MasterPage restores each control's pre-save enabled state in finally.
        // Reapply the new-record state after that restoration has completed.
        if (afterSave) Defer(() => SetDateLock(datesLocked));
    }
    protected override void RecordLoaded(DataGridViewRow row)
    {
        base.RecordLoaded(row);
        _yearValidator?.ClearAll();
        startDateChosen = endDateChosen = true;
        // Fail closed if an unexpected reader omits the lock flag.
        SetDateLock(row.DataGridView is not { } owner || !owner.Columns.Contains("DatesLocked") ||
            row.Cells["DatesLocked"].Value is not bool locked || locked);
        if (TryGetEditorControl("StartDate", out var start) && start is ModernDatePicker startDate)
            loadedStartDate = startDate.Value.Date;
        if (TryGetEditorControl("EndDate", out var end) && end is ModernDatePicker endDate)
            loadedEndDate = endDate.Value.Date;
    }
    private void SetDateLock(bool locked)
    {
        datesLocked = locked;
        foreach (string key in new[] { "StartDate", "EndDate" })
            if (TryGetEditorControl(key, out var date) && date is not null)
            {
                date.Enabled = !locked;
                SetToolTip(date, locked ? LockedDatesMessage : "Choose the academic year's date.");
            }
        if (locked) SetStatus(LockedDatesMessage);
    }
    public AcademicYearsPage() : this(true) { }
    internal AcademicYearsPage(bool initialize) : base(
        "Academic Years",
        "Create academic years, set their dates and mark the current year.",
        "GetAcademicYears",
        "SaveAcademicYear",
        new[]
        {
            new FieldSpec(
                "YearName",
                "Year Name",
                Required: true),
            new FieldSpec(
                "StartDate",
                "Start Date",
                FieldKind.Date,
                Required: true),
            new FieldSpec(
                "EndDate",
                "End Date",
                FieldKind.Date,
                Required: true),
            new FieldSpec(
                "IsCurrent",
                "Current",
                FieldKind.Boolean),
            new FieldSpec( "Remarks", "Remarks")
        },
        v => new[]
        {
            DatabaseHelper.P(
                "@UserId",
                CurrentUser.UserId),
            DatabaseHelper.Int(
                "@AcademicYearId",
                Convert.ToInt32(v["Id"] ?? 0)),
            DatabaseHelper.P(
                "@YearName",
                v["YearName"]),
            DatabaseHelper.Date(
                "@StartDate",
                (DateTime)v["StartDate"]!),
            DatabaseHelper.Date(
                "@EndDate",
                (DateTime)v["EndDate"]!),
            DatabaseHelper.Bit(
                "@IsCurrent",
                Convert.ToBoolean(v["IsCurrent"])),
            DatabaseHelper.P( "@Remarks", v["Remarks"])
        },
        splitEditorAndRecords: true,
        getParams: () => new[]
        {
            DatabaseHelper.Int(
                "@UserId",
                CurrentUser.UserId)
        },
        deleteProcedure: "DeleteAcademicYear",
        deleteIdParameter: "@AcademicYearId",
        deleteDisplayField: "YearName",
        usageCountProcedure: "GetAcademicYearUsageCount",
        usageCountIdParameter: "@AcademicYearId",
        usageCountLabel: "placement record(s)",
        deleteBlockedProcedure: "GetAcademicYearDeleteBlockedIds",
        initialize: initialize)
    {
        WireYearDateDefaults();
        // MasterPage performs its own deferred initialization. Apply the page-specific date defaults after that initialization.
        if (initialize) Defer(ApplyInitialDateDefaults);
        foreach (string key in new[] { "StartDate", "EndDate" })
            if (TryGetEditorControl(key, out var date) && date is ModernDatePicker picker)
            {
                picker.ValueChanged += (_, _) => { if (!applyingDateDefaults && picker.Focused) ValidateDates(); };
                picker.Leave += (_, _) => ValidateDates();
            }
    }
    /* These flags record that a date field already carries a value that is meant to be kept: either the operator picked it or a saved record put it there. */
    private bool startDateChosen;
    private bool endDateChosen;
    /* Set while this page writes the dates itself, so its own auto-fill does not count as "someone chose these" and block the next auto-fill. */
    private bool applyingDateDefaults;
    private void WireYearDateDefaults()
    {
        if (TryGetEditorControl(
                "YearName",
                out var control) &&
            control is ModernTextBox yearName)
        {
            yearName.PlaceholderText = "2025-2026";
            yearName.KeyPress += (_, e) => {
                if (!char.IsControl(e.KeyChar) &&
                    !(e.KeyChar >= '0' && e.KeyChar <= '9') && e.KeyChar != '-')
                    e.Handled = true;
            };
            yearName.TextChanged +=
                (_, _) => {
                    /* The flags were set by any date change and never reset, so once a year had been loaded or a date picked, the auto-fill was off for the rest of… */
                    if (string.IsNullOrWhiteSpace(yearName.Text))
                    {
                        startDateChosen = false;
                        endDateChosen = false;
                        return;
                    }
                    ApplyYearDateDefaults(yearName);
                    if (yearName.Focused)
                        YearValidator.Rule(yearName, () => AcademicYearName.TryParse(yearName.Text, out _), AcademicYearName.ValidationMessage);
                };
            yearName.Leave += (_, _) => YearValidator.Rule(yearName,
                () => AcademicYearName.TryParse(yearName.Text, out _), AcademicYearName.ValidationMessage);
        }
        /* A date the operator (or a loaded record) has already set is a date the year name must not overwrite. */
        if (TryGetEditorControl(
                "StartDate",
                out var startControl) &&
            startControl is ModernDatePicker startDate)
        {
            startDate.ValueChanged +=
                (_, _) => {
                    if (!applyingDateDefaults)
                        startDateChosen = true;
                };
        }
        if (TryGetEditorControl(
                "EndDate",
                out var endControl) &&
            endControl is ModernDatePicker endDate)
        {
            endDate.ValueChanged +=
                (_, _) => {
                    if (!applyingDateDefaults)
                        endDateChosen = true;
                };
        }
    }
    private void ApplyInitialDateDefaults()
    {
        if (IsDisposed ||
            Disposing)
        {
            return;
        }
        if (!TryGetEditorControl(
                "YearName",
                out var control) ||
            control is not ModernTextBox yearName)
        {
            return;
        }
        ApplyYearDateDefaults(yearName);
    }
    private void ApplyYearDateDefaults(ModernTextBox yearName)
    {
        if (!TryParseAcademicYear(yearName.Text, out int startYear))
        {
            return;
        }
        if (!TryGetEditorControl(
                "StartDate",
                out var startControl) ||
            startControl is not ModernDatePicker startDate)
        {
            return;
        }
        if (!TryGetEditorControl(
                "EndDate",
                out var endControl) ||
            endControl is not ModernDatePicker endDate)
        {
            return;
        }
        /* fill only what is still open. */
        var proposedStart = new DateTime(startYear, 4, 1);
        var proposedEnd = new DateTime(startYear + 1, 3, 31);
        if (proposedStart < startDate.MinDate || proposedStart > startDate.MaxDate ||
            proposedEnd < endDate.MinDate || proposedEnd > endDate.MaxDate) return;
        applyingDateDefaults = true;
        try
        {
            if (!startDateChosen)
            {
                startDate.Value = proposedStart;
            }
            if (!endDateChosen)
            {
                endDate.Value = proposedEnd;
            }
        }
        finally
        {
            applyingDateDefaults = false;
        }
    }
    private static bool TryParseAcademicYear(string? value, out int startYear)
    {
        return AcademicYearName.TryParse(value, out startYear);
    }
}
