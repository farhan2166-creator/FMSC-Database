using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class AcademicYearsPage : MasterPage
{
    public AcademicYearsPage() : base(
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
        usageCountLabel: "enrollment record(s)",
        deleteBlockedProcedure: "GetAcademicYearDeleteBlockedIds")
    {
        WireYearDateDefaults();
        // MasterPage performs its own deferred initialization. Apply the page-specific date defaults after that initialization.
        Defer(ApplyInitialDateDefaults);
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
                };
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
        applyingDateDefaults = true;
        try
        {
            if (!startDateChosen)
            {
                startDate.Value = new DateTime(startYear, 4, 1);
            }
            if (!endDateChosen)
            {
                endDate.Value = new DateTime(startYear + 1, 3, 31);
            }
        }
        finally
        {
            applyingDateDefaults = false;
        }
    }
    private static bool TryParseAcademicYear(string? value, out int startYear)
    {
        startYear = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var match =
            System.Text.RegularExpressions.Regex.Match(value.Trim(), @"\b(20\d{2})\b");
        return match.Success &&
               int.TryParse(match.Groups[1].Value, out startYear);
    }
}
