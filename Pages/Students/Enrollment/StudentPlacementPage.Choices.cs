using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

public sealed partial class StudentPlacementPage
{
    private DataTable campusCatalog = new(), yearCatalog = new(), sectionCatalog = new(), sourcePlacements = new();
    private bool choicesReady;
    internal Func<int, Task<DataTable>> ReadSourceChoices = PlacementPlan.ReadSourcesAsync;
    internal Func<int,int,int,int,Task<DataTable>> ReadStudents = PlacementPlan.ReadAsync;

    private static DataTable Matching(DataTable table, Func<DataRow, bool> predicate)
    {
        var result = table.Clone();
        foreach (var row in table.AsEnumerable().Where(predicate)) result.ImportRow(row);
        return result;
    }
    private static int Id(DataRow row, string column) => Convert.ToInt32(row[column]);

    private void WirePlacementChoices()
    {
        sourceYear.SelectedIndexChanged += async (_, _) => {
            if (!choicesReady || binding || pending is not null) return;
            await Guard(async () => {
                InvalidatePlan();
                WithChoiceBinding(() => {
                    sourceCampus.SelectedIndex = 0;
                    sourceClass.SelectedIndex = 0;
                    sourceSection.SelectedIndex = 0;
                    if (targetCampus.SelectedId == 0) targetClass.SelectedIndex = 0;
                });
                await RefreshSourceContext(true);
            }, sourceYear);
        };
        sourceCampus.SelectedIndexChanged += (_, _) => {
            if (!choicesReady || binding || pending is not null) return;
            InvalidatePlan();
            WithChoiceBinding(() => {
                sourceClass.SelectedIndex = 0;
                sourceSection.SelectedIndex = 0;
                if (targetCampus.SelectedId == 0) targetClass.SelectedIndex = 0;
                RefreshSourceClasses();
                RefreshTargetClasses();
            });
        };
        sourceClass.SelectedIndexChanged += (_, _) => {
            if (!choicesReady || binding || pending is not null) return;
            InvalidatePlan();
            WithChoiceBinding(() => {
                sourceSection.SelectedIndex = 0;
                RefreshSourceSections();
            });
        };
        sourceSection.SelectedIndexChanged += (_, _) => { if (choicesReady && !binding) InvalidatePlan(); };
        targetCampus.SelectedIndexChanged += (_, _) => {
            if (!choicesReady || binding || pending is not null) return;
            WithChoiceBinding(() => {
                targetClass.SelectedIndex = 0;
                RefreshTargetClasses();
            });
            Suggest();
        };
        targetClass.SelectedIndexChanged += (_, _) => { if (choicesReady && !binding && pending is null) Suggest(); };
        targetYear.SelectedIndexChanged += (_, _) => {
            if (!choicesReady || binding || pending is not null) return;
            WithChoiceBinding(() => { if (!Annual) targetYear.SelectedValue = sourceYear.SelectedId; FitPromotionDate(); });
        };
    }
    private void WithChoiceBinding(Action action)
    {
        bool previous = binding; binding = true;
        try { action(); } finally { binding = previous; RefreshActionAvailability(); }
    }
    private void BindPlacementChoices()
    {
        WithChoiceBinding(() => {
            Bind(sourceYear, yearCatalog, "AcademicYearId", "YearName", false);
            RefreshSourceFilters(); RefreshTargetYears(true);
            using var targets = Matching(campusCatalog, row => placements.AsEnumerable().Any(p => Id(p,"CampusId") == Id(row,"CampusId") && (operation == 2 || Convert.ToBoolean(p["IsActive"]))));
            Bind(targetCampus, targets, "CampusId", "CampusName", true, "Keep current campus");
            RefreshTargetClasses();
            ((DataGridViewComboBoxColumn)students.Columns["Destination"]).DataSource = WithEmptyDestination(placements.Clone());
        });
        choicesReady = true;
        RefreshActionAvailability();
    }
    private async Task RefreshSourceContext(bool chooseNextYear)
    {
        var selected = (Campus: sourceCampus.SelectedId, Class: sourceClass.SelectedId, Section: sourceSection.SelectedId, TargetClass: targetClass.SelectedId);
        // Clear stale choices before the read. A failed refresh cannot leave the
        // old campus/classes selectable under a newly chosen year.
        sourcePlacements = sourcePlacements.Clone();
        WithChoiceBinding(() => { RefreshSourceFilters(); RefreshTargetYears(chooseNextYear); RefreshTargetClasses(); });
        int year = sourceYear.SelectedId;
        if (year <= 0) return;
        var rows = await ReadSourceChoices(year);
        if (IsDisposed || sourceYear.SelectedId != year) { rows.Dispose(); return; }
        sourcePlacements.Dispose(); sourcePlacements = rows;
        WithChoiceBinding(() => {
            RefreshSourceFilters(); RestoreLookup(sourceCampus, selected.Campus);
            RefreshSourceFilters(); RestoreLookup(sourceClass, selected.Class);
            RefreshSourceSections(); RestoreLookup(sourceSection, selected.Section);
            RefreshTargetClasses(); RestoreLookup(targetClass, selected.TargetClass);
        });
    }
    private static void RestoreLookup(LookupBox lookup, int value)
    {
        if (lookup.DataSource is DataTable rows && rows.AsEnumerable().Any(row => Id(row,lookup.ValueMember) == value))
            lookup.SelectedValue = value;
    }
    private static void BindRetaining(LookupBox lookup, DataTable table, string key, string text, string blank = "All")
    {
        int selected = lookup.SelectedId;
        Bind(lookup, table, key, text, true, blank);
        if (table.AsEnumerable().Any(row => Id(row,key) == selected)) lookup.SelectedValue = selected;
    }
    private void RefreshSourceFilters()
    {
        using var campuses = Matching(campusCatalog, row => sourcePlacements.AsEnumerable().Any(p => Id(p,"CampusId") == Id(row,"CampusId")));
        BindRetaining(sourceCampus, campuses, "CampusId", "CampusName");
        RefreshSourceClasses();
    }
    private void RefreshSourceClasses()
    {
        using var classes = Matching(catalog, row => sourcePlacements.AsEnumerable().Any(p => Id(p,"ClassId") == Id(row,"ClassId") && (sourceCampus.SelectedId == 0 || Id(p,"CampusId") == sourceCampus.SelectedId)));
        BindRetaining(sourceClass, classes, "ClassId", "ClassName");
        RefreshSourceSections();
    }
    private void RefreshSourceSections()
    {
        using var sections = Matching(sectionCatalog, row => sourcePlacements.AsEnumerable().Any(p => Id(p,"SectionId") == Id(row,"SectionId")
            && (sourceCampus.SelectedId == 0 || Id(p,"CampusId") == sourceCampus.SelectedId)
            && (sourceClass.SelectedId == 0 || Id(p,"ClassId") == sourceClass.SelectedId)));
        BindRetaining(sourceSection, sections, "SectionId", "SectionName");
    }
    private void RefreshTargetClasses()
    {
        var campuses = targetCampus.SelectedId > 0 ? new HashSet<int> { targetCampus.SelectedId }
            : sourceCampus.SelectedId > 0 ? new HashSet<int> { sourceCampus.SelectedId }
            : sourcePlacements.AsEnumerable().Select(row => Id(row,"CampusId")).ToHashSet();
        using var assignments = Matching(placements, p => campuses.Contains(Id(p,"CampusId")) && (operation == 2 || Convert.ToBoolean(p["IsActive"])));
        using var classes = ActiveClassSectionPolicy.ClassesWithAssignments(catalog, assignments);
        BindRetaining(targetClass, classes, "ClassId", "ClassName", "Keep current class");
    }
    private void RefreshTargetYears(bool chooseNext)
    {
        int selected = targetYear.SelectedId;
        var source = yearCatalog.AsEnumerable().SingleOrDefault(row => Id(row,"AcademicYearId") == sourceYear.SelectedId);
        var years = yearCatalog.Clone();
        if (source is not null)
        {
            var allowed = Annual
                ? yearCatalog.AsEnumerable().Where(row => !row.IsNull("StartDate") && Convert.ToDateTime(row["StartDate"]) > Convert.ToDateTime(source["StartDate"]))
                    .OrderBy(row => Convert.ToDateTime(row["StartDate"])).ThenBy(row => Id(row,"AcademicYearId"))
                : new[] { source }.AsEnumerable();
            foreach (var row in allowed) years.ImportRow(row);
        }
        Bind(targetYear, years, "AcademicYearId", "YearName", false);
        if (!chooseNext && years.AsEnumerable().Any(row => Id(row,"AcademicYearId") == selected)) targetYear.SelectedValue = selected;
        else if (years.Rows.Count > 0) targetYear.SelectedValue = years.Rows[0]["AcademicYearId"];
        FitPromotionDate();
        if (Annual && source is not null && years.Rows.Count == 0)
            SetStatus("No later academic year is configured. Add the destination year in Academic Years before promotion.", true);
    }
    private void FitPromotionDate()
    {
        if (!Annual) return;
        var year = yearCatalog.AsEnumerable().SingleOrDefault(row => Id(row,"AcademicYearId") == targetYear.SelectedId);
        if (year is null || year.IsNull("StartDate") || year.IsNull("EndDate")) return;
        DateTime start = Convert.ToDateTime(year["StartDate"]).Date, end = Convert.ToDateTime(year["EndDate"]).Date;
        if (effective.Value.Date < start || effective.Value.Date > end) effective.Value = start;
    }
    private DataTable DestinationChoices(DataRow student, string decision)
    {
        if (decision == "HOLD") return Matching(placements, p => Id(p,"ClassSectionId") == Id(student,"SourcePlacementId"));
        if (decision == "SKIP") return placements.Clone();
        int campus = targetCampus.SelectedId > 0 ? targetCampus.SelectedId : Id(student,"CampusId");
        int cls = Annual ? PromotionChoices.NextClass(catalog, Id(student,"ClassId")) is { } next ? Id(next,"ClassId") : 0
            : targetClass.SelectedId > 0 ? targetClass.SelectedId : Id(student,"ClassId");
        return Matching(placements, p => Id(p,"CampusId") == campus && Id(p,"ClassId") == cls && (operation == 2 || Convert.ToBoolean(p["IsActive"])));
    }
    private static DataTable WithEmptyDestination(DataTable rows)
    {
        var empty = rows.NewRow(); empty["ClassSectionId"] = 0; empty["Display"] = "Choose a destination"; rows.Rows.InsertAt(empty, 0); return rows;
    }
    private void RefreshDestinationCell(DataGridViewRow gridRow)
    {
        if (gridRow.DataBoundItem is not DataRowView view) return;
        bool previous = binding; binding = true;
        try {
            string decision = Convert.ToString(view["Decision"])!;
            var allowed = DestinationChoices(view.Row, decision);
            int selected = Id(view.Row,"Destination");
            if (!allowed.AsEnumerable().Any(row => Id(row,"ClassSectionId") == selected)) view["Destination"] = 0;
            var cell = (DataGridViewComboBoxCell)gridRow.Cells["Destination"];
            cell.ReadOnly = decision is "HOLD" or "SKIP" || allowed.Rows.Count == 0;
            cell.DataSource = WithEmptyDestination(allowed);
        } finally { binding = previous; }
    }
    private void RefreshDestinationCells()
    {
        foreach (DataGridViewRow row in students.Rows) RefreshDestinationCell(row);
    }
    private DataTable CommonDestinations(DataRow[] rows, string decision)
    {
        HashSet<int>? ids = null;
        foreach (var row in rows)
        {
            using var choices = DestinationChoices(row, decision);
            var allowed = choices.AsEnumerable().Select(p => Id(p,"ClassSectionId")).ToHashSet();
            if (ids is null) ids = allowed; else ids.IntersectWith(allowed);
        }
        return Matching(placements, p => ids?.Contains(Id(p,"ClassSectionId")) == true);
    }
    private void ValidatePlacementChoices(DataRow[] selected)
    {
        var source = yearCatalog.AsEnumerable().SingleOrDefault(row => Id(row,"AcademicYearId") == sourceYear.SelectedId);
        var destination = yearCatalog.AsEnumerable().SingleOrDefault(row => Id(row,"AcademicYearId") == targetYear.SelectedId);
        if (source is null || destination is null || (Annual
            ? Convert.ToDateTime(destination["StartDate"]) <= Convert.ToDateTime(source["StartDate"])
            : targetYear.SelectedId != sourceYear.SelectedId))
            throw new InvalidOperationException(Annual ? "Choose a destination academic year later than the source year." : "Transfers and corrections must remain in the source academic year.");
        if (Annual && (effective.Value.Date < Convert.ToDateTime(destination["StartDate"]).Date || effective.Value.Date > Convert.ToDateTime(destination["EndDate"]).Date))
            throw new InvalidOperationException("The promotion date must fall within the destination academic year.");
        foreach (var row in selected)
        {
            using var choices = DestinationChoices(row, Convert.ToString(row["Decision"])!);
            if (!choices.AsEnumerable().Any(p => Id(p,"ClassSectionId") == Id(row,"Destination")))
                throw new InvalidOperationException("A destination no longer matches its campus/class assignment or promotion order. Refresh and review the plan again.");
        }
    }
}
