using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Students;
/* The catalogue of columns, the choices offered for grouping / splitting / sorting, and the arrangement of the loaded rows into the table the grid shows. */
public partial class StudentReportsPage
{
    private const string GroupNone = "none";
    private const string TotalLabel = "Total";
    private const string BalanceKey = "OutstandingBalance";
    private const string MeasureStudents = "Students";
    private const string MeasureChanges = "Changes";
    /// <summary>What the grid shows for one arrangement: which columns exist, and under which saved-layout key.</summary>
    private sealed class ReportShape
    {
        public ReportShape(
            string key,
            IReadOnlyList<ReportColumnDef> catalog,
            IReadOnlyList<string>? defaultKeys,
            bool grouped,
            string firstDimColumn)
        {
            Key = key;
            Catalog = catalog;
            DefaultKeys = defaultKeys;
            Grouped = grouped;
            FirstDimColumn = firstDimColumn;
        }
        public string Key { get; }
        public IReadOnlyList<ReportColumnDef> Catalog { get; }
        public IReadOnlyList<string>? DefaultKeys { get; }
        public bool Grouped { get; }
        public string FirstDimColumn { get; }
    }
    private sealed record GroupDim(string Key, string Text, string[] Columns, string[] Headers);
    private sealed class GroupAcc
    {
        public GroupAcc(string[] values) => Values = values;
        public string[] Values { get; }
        public int Count { get; set; }
        public int Compare { get; set; }
        public decimal Balance { get; set; }
        public int Order { get; set; } = int.MaxValue;
        public Dictionary<string, int> Split { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    // choices
    private static readonly GroupDim[] RegisterDims =
    {
        new("campus", "Campus", new[] { "CampusName" }, new[] { "Campus" }),
        new("class", "Class", new[] { "ClassName" }, new[] { "Class" }),
        new("section", "Section", new[] { "SectionName" }, new[] { "Section" }),
        new("classsection", "Class / Section", new[] { "ClassName", "SectionName" }, new[] { "Class", "Section" }),
        new("campusclass", "Campus / Class", new[] { "CampusName", "ClassName" }, new[] { "Campus", "Class" }),
        new("campusclasssection", "Campus / Class / Section", new[] { "CampusName", "ClassName", "SectionName" }, new[] { "Campus", "Class", "Section" }),
        new("gender", "Gender", new[] { "Gender" }, new[] { "Gender" }),
        new("status", "Student status", new[] { "StudentStatus" }, new[] { "Status" }),
        new("program", "Program", new[] { "ProgramType" }, new[] { "Program" }),
        new("admyear", "Admission year", new[] { "AdmissionYear" }, new[] { "Admission Year" }),
        new("year", "Academic year", new[] { "YearName" }, new[] { "Academic Year" }),
        new("transport", "Transport", new[] { "Transport" }, new[] { "Transport" })
    };
    private static readonly GroupDim[] ChangeDims =
    {
        new("changetype", "Change type", new[] { "ActionType" }, new[] { "Change" }),
        new("campus", "Campus", new[] { "CampusName" }, new[] { "Campus" }),
        new("month", "Month", new[] { "ActionMonth" }, new[] { "Month" })
    };
    private static readonly string[] StrengthGroupKeys =
    {
        "campusclasssection", "classsection", "campusclass", "campus", "class", "section", "gender", "program", "transport"
    };
    private static readonly (string Key, string Text, string Column)[] RegisterSplits =
    {
        ("gender", "Gender", "Gender"),
        ("status", "Student status", "StudentStatus"),
        ("enrol", "Placement status", "EnrollmentStatus"),
        ("transport", "Transport", "Transport"),
        ("program", "Program", "ProgramType")
    };
    private static readonly (string Key, string Text, string Column)[] ChangeSplits =
    {
        ("changetype", "Change type", "ActionType")
    };
    private static GroupDim? FindDim(string mode, string key)
    {
        var dims = mode == ModeChanges ? ChangeDims : RegisterDims;
        return dims.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
    }
    private static IEnumerable<Choice> GroupChoicesFor(string mode)
    {
        if (mode == ModeStrength)
        {
            foreach (string key in StrengthGroupKeys)
                yield return new Choice(key, FindDim(mode, key)!.Text);
            yield break;
        }
        yield return new Choice(
            GroupNone,
            mode == ModeChanges ? "(no grouping - list every change)" : "(no grouping - list every student)");
        var dims = mode == ModeChanges
            ? ChangeDims
            : RegisterDims;
        foreach (var dim in dims)
            yield return new Choice(dim.Key, dim.Text);
    }
    /// <summary>Splits offered for the chosen grouping - a split by the very column already grouped on would be pointless.</summary>
    private static IEnumerable<Choice> SplitChoicesFor(string mode, string groupKey)
    {
        yield return new Choice(GroupNone, "(no split)");
        var splits = mode == ModeChanges ? ChangeSplits : RegisterSplits;
        var used = FindDim(mode, groupKey)?.Columns ?? Array.Empty<string>();
        foreach (var (key, text, column) in splits)
        {
            if (used.Contains(column, StringComparer.OrdinalIgnoreCase))
                continue;
            yield return new Choice(key, text);
        }
    }
    private static IEnumerable<Choice> SortChoicesFor(string mode, bool grouped, bool hasCompare)
    {
        if (grouped)
        {
            yield return new Choice("group", "Group order");
            yield return new Choice("count_desc", mode == ModeChanges ? "Most changes first" : "Most students first");
            yield return new Choice("count_asc", mode == ModeChanges ? "Fewest changes first" : "Fewest students first");
            if (mode == ModeStrength && hasCompare)
            {
                yield return new Choice("change_desc", "Biggest increase first");
                yield return new Choice("change_asc", "Biggest drop first");
            }
            yield break;
        }
        if (mode == ModeChanges)
        {
            yield return new Choice("date_asc", "Date (oldest first)");
            yield return new Choice("date_desc", "Date (newest first)");
            yield return new Choice("name", "Student name (A-Z)");
            yield return new Choice("type", "Change type");
            yield break;
        }
        yield return new Choice("name", "Student name (A-Z)");
        yield return new Choice("admno", "Admission Number (low to high)");
        yield return new Choice("class", "Class, section, then name");
        yield return new Choice("admdate_desc", "Admission date (newest first)");
        yield return new Choice("admdate_asc", "Admission date (oldest first)");
        yield return new Choice("balance_desc", "Outstanding balance (highest first - if shown)");
    }
    /// <summary>The grouping to really use: a key left over from another report falls back rather than breaking.</summary>
    private static string ValidGroupKey(string mode, string key)
    {
        if (GroupChoicesFor(mode).Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
            return key.ToLowerInvariant();
        return mode == ModeStrength
            ? StrengthGroupKeys[0]
            : GroupNone;
    }
    private static string GroupText(string key)
    {
        var dim = RegisterDims.Concat(ChangeDims)
            .FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        return dim?.Text ?? key;
    }
    // column catalogues
    private static readonly ReportColumnDef[] RegisterCatalog =
    {
        new("AdmissionNumber", "Admission Number", "Identity", true),
        new("RegistrationNumber", "Registration No.", "Identity", false),
        new("StudentName", "Student", "Identity", true),
        new("FatherName", "Father", "Identity", true),
        new("Gender", "Gender", "Personal", false),
        new("MissingDetails", "Missing Details", "Personal", false),
        new("DateOfBirth", "Date of Birth", "Personal", true, ReportColumnKind.Date),
        new("Age", "Age", "Personal", false, ReportColumnKind.Number),
        new("BFormNumber", "B-Form No.", "Personal", false),
        new("BloodGroup", "Blood Group", "Personal", false),
        new("Religion", "Religion", "Personal", false),
        new("CasteTribe", "Caste / Tribe", "Personal", false),
        new("Disability", "Disability", "Personal", false),
        new("PlaceOfBirth", "Place of Birth", "Personal", false),
        new("ContactNumber", "Contact", "Contact", false),
        new("AlternateContactNumber", "Alternate Contact", "Contact", false),
        new("Address", "Address", "Contact", false),
        new("GuardianName", "Guardian", "Contact", false),
        new("GuardianRelation", "Guardian Relation", "Contact", false),
        new("FatherOccupation", "Father's Occupation", "Contact", false),
        new("AdmissionDate", "Admission Date", "Admission", true, ReportColumnKind.Date),
        new("AdmissionCampusName", "Admission Campus", "Admission", false),
        new("AdmissionClassSection", "Class Admitted To", "Admission", true),
        new("PreviousSchool", "Previous School", "Admission", false),
        new("BiseEnrolmentNumber", "BISEP Enrolment No. (Record)", "Admission", false),
        new("StudentStatus", "Status", "Status and leaving", true),
        new("WithdrawalDate", "Leaving Date", "Status and leaving", false, ReportColumnKind.Date),
        new("LeavingClassSection", "Class Left", "Status and leaving", true),
        new("YearName", "Academic Year", "Current Class", true),
        new("CampusName", "Campus", "Current Class", true),
        new("ClassSection", "Class / Section", "Current Class", false),
        new("ClassName", "Class (name only)", "Current Class", false),
        new("SectionName", "Section (name only)", "Current Class", false),
        new("ProgramType", "Program", "Current Class", false),
        new("EnrollmentDate", "In This Class Since", "Current Class", false, ReportColumnKind.Date),
        new("EnrollmentStatus", "Class Record Status", "Current Class", false),
        new("Transport", "Transport", "Current Class", false),
        new("BillingStartDate", "Billing Start", "Current Class", false, ReportColumnKind.Date),
        new("FamilyRef", "Family Code", "Family and fees", false),
        new("FamilyMemberCount", "Family Size", "Family and fees", true, ReportColumnKind.Number),
        new(BalanceKey, "Outstanding Balance", "Family and fees", true, ReportColumnKind.Money)
    };
    private static readonly string[] ListDefaults =
    {
        "AdmissionNumber", "StudentName", "FatherName", "DateOfBirth", "AdmissionDate", "AdmissionClassSection",
        "StudentStatus", "LeavingClassSection", "YearName", "CampusName", "ClassSection", "FamilyMemberCount", BalanceKey
    };
    private static readonly string[] AdmissionsDefaults =
    {
        "AdmissionDate", "AdmissionNumber", "RegistrationNumber", "StudentName", "FatherName", "Gender",
        "ContactNumber", "AdmissionClassSection", "CampusName", "ClassSection", "EnrollmentDate", "EnrollmentStatus"
    };
    private static readonly ReportColumnDef[] ChangesCatalog =
    {
        new("ActionDate", "Date", "Change", true, ReportColumnKind.Date),
        new("ActionType", "Change", "Change", true),
        new("AdmissionNumber", "Admission Number", "Student", true),
        new("RegistrationNumber", "Registration No.", "Student", false),
        new("StudentName", "Student", "Student", true),
        new("FatherName", "Father", "Student", false),
        new("CampusName", "Campus", "Student", true),
        new("OldClassSection", "From Class", "Class", true),
        new("NewClassSection", "To Class", "Class", true),
        new("OldClass", "From Class (name only)", "Class", false),
        new("OldSection", "From Section", "Class", false),
        new("NewClass", "To Class (name only)", "Class", false),
        new("NewSection", "To Section", "Class", false),
        new("OldStatus", "Old Status", "Status", true),
        new("NewStatus", "New Status", "Status", true),
        new("Remarks", "Remarks", "Change", true),
        new("ChangedBy", "Changed By", "Change", true),
        new("ActionMonth", "Month", "Change", false)
    };
    private static readonly string[] ChangesDefaults =
    {
        "ActionDate", "ActionType", "AdmissionNumber", "StudentName", "CampusName", "OldClassSection",
        "NewClassSection", "OldStatus", "NewStatus", "Remarks", "ChangedBy"
    };
    /// <summary>The columns, layout key and defaults for an arrangement. <paramref name="splitValues"/> are the breakdown columns known so far.</summary>
    private static ReportShape ComputeShape(ViewArgs v, IReadOnlyList<string> splitValues)
    {
        string groupKey = ValidGroupKey(v.Mode, v.GroupKey);
        bool grouped = groupKey != GroupNone;
        if (!grouped)
        {
            return v.Mode == ModeChanges
                ? new ReportShape($"students.{v.Mode}.list", ChangesCatalog, ChangesDefaults, false, string.Empty)
                : new ReportShape(
                    $"students.{v.Mode}.list",
                    RegisterCatalog,
                    v.Mode == ModeAdmissions ? AdmissionsDefaults : ListDefaults,
                    false,
                    string.Empty);
        }
        GroupDim dim = FindDim(v.Mode, groupKey)!;
        bool changes = v.Mode == ModeChanges;
        bool compare = v.Mode == ModeStrength && v.CompareName.Length > 0;
        var defs = new List<ReportColumnDef>();
        for (int i = 0; i < dim.Columns.Length; i++)
            defs.Add(new ReportColumnDef(dim.Columns[i], dim.Headers[i], "Group", true));
        defs.Add(new ReportColumnDef(
            changes ? MeasureChanges : MeasureStudents,
            changes ? "Changes" : compare ? $"Students ({v.YearName})" : "Students",
            "Counts",
            true,
            ReportColumnKind.Number));
        for (int i = 0; i < splitValues.Count; i++)
            defs.Add(new ReportColumnDef($"Split{i}", splitValues[i], "Breakdown", true, ReportColumnKind.Number));
        if (compare)
        {
            defs.Add(new ReportColumnDef("CompareStudents", $"Students ({v.CompareName})", "Counts", true, ReportColumnKind.Number));
            defs.Add(new ReportColumnDef("Change", "Change", "Counts", true, ReportColumnKind.Number));
        }
        if (IsRegisterMode(v.Mode))
            defs.Add(new ReportColumnDef(BalanceKey, "Outstanding Balance", "Counts", false, ReportColumnKind.Money));
        return new ReportShape($"students.{v.Mode}.by-{groupKey}", defs, null, true, dim.Columns[0]);
    }
    // preparing the loaded rows
    private static string ClassSectionText(string className, string sectionName) =>
        string.IsNullOrWhiteSpace(sectionName)
            ? className
            : string.IsNullOrWhiteSpace(className)
                ? sectionName
                : $"{className} - {sectionName}";
    private static string CellText(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return string.Empty;
        return Convert.ToString(row[column])?.Trim() ?? string.Empty;
    }
    /// <summary>Adds the columns the page computes itself: the admission number under its own name, the current Class / Section as one, and.</summary>
    private static void PrepareRegister(DataTable table)
    {
        var classSection = table.Columns.Add("ClassSection", typeof(string));
        var admittedInto = table.Columns.Add("AdmissionClassSection", typeof(string));
        var leftFrom = table.Columns.Add("LeavingClassSection", typeof(string));
        foreach (DataRow row in table.Rows)
        {
            row[classSection] = ClassSectionText(CellText(row, "ClassName"), CellText(row, "SectionName"));
            row[admittedInto] = ClassSectionText(CellText(row, "AdmissionClassName"), CellText(row, "AdmissionSectionName"));
            row[leftFrom] = ClassSectionText(CellText(row, "LeavingClassName"), CellText(row, "LeavingSectionName"));
        }
    }
    private static void PrepareChanges(DataTable table)
    {
        var oldClass = table.Columns.Add("OldClassSection", typeof(string));
        var newClass = table.Columns.Add("NewClassSection", typeof(string));
        var month = table.Columns.Add("ActionMonth", typeof(string));
        foreach (DataRow row in table.Rows)
        {
            row[oldClass] = ClassSectionText(CellText(row, "OldClass"), CellText(row, "OldSection"));
            row[newClass] = ClassSectionText(CellText(row, "NewClass"), CellText(row, "NewSection"));
            row[month] = table.Columns.Contains("ActionDate") && row["ActionDate"] is DateTime moment
                ? moment.ToString("yyyy-MM")
                : string.Empty;
        }
    }
    // arranging the rows
    /// <summary>Builds the table the grid shows from the loaded rows and the current arrangement, and records the shape (its columns and saved-layout key) that goes with it.</summary>
    private DataTable ShapeTable(ViewArgs v)
    {
        DataTable raw = _raw ?? throw new InvalidOperationException("No report data is loaded.");
        string groupKey = ValidGroupKey(v.Mode, v.GroupKey);
        ViewArgs view = v with { GroupKey = groupKey };
        bool changes = v.Mode == ModeChanges;
        DataTable rows = changes ? FilterChanges(raw, view) : StudentReportQuickViews.FilterTransport(raw, view.Transport);
        if (!changes && view.Mode == ModeList)
            rows = StudentReportQuickViews.FilterCompletion(rows, view.Completion, view.CanViewDetails);
        if (groupKey == GroupNone)
        {
            DataTable list = changes
                ? SortChanges(rows, view.SortKey)
                : SortRegister(rows, view.SortKey);
            _shape = ComputeShape(view, Array.Empty<string>());
            _countText = changes
                ? $"{list.Rows.Count:N0} change(s)"
                : $"{list.Rows.Count:N0} student(s)";
            return list;
        }
        GroupDim dim = FindDim(v.Mode, groupKey)!;
        bool hasBalance = !changes && _rawHasBalance && rows.Columns.Contains(BalanceKey);
        var built = BuildGrouped(rows, changes ? null : _compareRaw, view, dim, hasBalance);
        _shape = ComputeShape(view, built.SplitValues);
        _countText = built.CountText;
        return built.Table;
    }
    private static DataTable FilterChanges(DataTable raw, ViewArgs v)
    {
        if (v.ChangeType.Length == 0 && v.ClientSearch.Length == 0)
            return raw;
        DataTable filtered = raw.Clone();
        AdmissionNumbers.TryNormalize(v.ClientSearch, out string admissionNumber);
        foreach (DataRow row in raw.Rows)
        {
            if (v.ChangeType.Length > 0 &&
                !string.Equals(CellText(row, "ActionType"), v.ChangeType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (v.ClientSearch.Length > 0)
            {
                bool match =
                    CellText(row, "StudentName").Contains(v.ClientSearch, StringComparison.CurrentCultureIgnoreCase) ||
                    CellText(row, "FatherName").Contains(v.ClientSearch, StringComparison.CurrentCultureIgnoreCase) ||
                    CellText(row, "RegistrationNumber").Contains(v.ClientSearch, StringComparison.CurrentCultureIgnoreCase) ||
                    CellText(row, "AdmissionNumber").Contains(v.ClientSearch, StringComparison.OrdinalIgnoreCase);
                if (!match)
                    continue;
            }
            filtered.ImportRow(row);
        }
        return filtered;
    }
    private static DataTable Sorted(DataTable table, string expression)
    {
        var view = new DataView(table) { Sort = expression };
        return view.ToTable();
    }
    private static DataTable SortChanges(DataTable rows, string key) => Sorted(rows, key switch {
        "date_desc" => "ActionDate DESC, StudentName ASC",
        "name" => "StudentName ASC, ActionDate ASC",
        "type" => "ActionType ASC, ActionDate ASC",
        _ => "ActionDate ASC, StudentName ASC"
    });
    private static DataTable SortRegister(DataTable rows, string key) => key == "admno" ? AdmissionNumbers.Sort(rows) : Sorted(rows, key switch {
        "admno" => "AdmissionNumber ASC",
        "class" => "ClassOrder ASC, SectionName ASC, StudentName ASC",
        "admdate_desc" => "AdmissionDate DESC, StudentName ASC",
        "admdate_asc" => "AdmissionDate ASC, StudentName ASC",
        "balance_desc" when rows.Columns.Contains(BalanceKey) => $"{BalanceKey} DESC, StudentName ASC",
        _ => "StudentName ASC, AdmissionId ASC"
    });
    private sealed record GroupedResult(DataTable Table, List<string> SplitValues, string CountText);
    private static GroupedResult BuildGrouped(
        DataTable rows,
        DataTable? compareRows,
        ViewArgs v,
        GroupDim dim,
        bool hasBalance)
    {
        bool changes = v.Mode == ModeChanges;
        bool compare = compareRows is not null;
        string? splitColumn = ResolveSplitColumn(v, dim);
        bool useOrder = dim.Columns.Contains("ClassName");
        var groups = new Dictionary<string, GroupAcc>(StringComparer.OrdinalIgnoreCase);
        var splitSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        GroupAcc Acc(DataRow row)
        {
            string[] values = dim.Columns
                .Select(c => {
                    string text = CellText(row, c);
                    return text.Length == 0 ? NotSet : text;
                })
                .ToArray();
            string key = string.Join("\u001f", values);
            if (!groups.TryGetValue(key, out var acc))
                groups[key] = acc = new GroupAcc(values);
            if (useOrder && row.Table.Columns.Contains("ClassOrder") && row["ClassOrder"] != DBNull.Value)
                acc.Order = Math.Min(acc.Order, Convert.ToInt32(row["ClassOrder"]));
            return acc;
        }
        foreach (DataRow row in rows.Rows)
        {
            GroupAcc acc = Acc(row);
            acc.Count++;
            if (splitColumn is not null)
            {
                string value = CellText(row, splitColumn);
                if (value.Length == 0)
                    value = NotSet;
                if (!splitSeen.ContainsKey(value))
                    splitSeen[value] = value;
                acc.Split.TryGetValue(value, out int already);
                acc.Split[value] = already + 1;
            }
            if (hasBalance && row[BalanceKey] != DBNull.Value)
                acc.Balance += Convert.ToDecimal(row[BalanceKey]);
        }
        if (compareRows is not null)
        {
            foreach (DataRow row in compareRows.Rows)
                Acc(row).Compare++;
        }
        List<string> splitValues = splitSeen.Values
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .Take(12)
            .ToList();
        int CompareDefault(GroupAcc a, GroupAcc b)
        {
            for (int i = 0; i < dim.Columns.Length; i++)
            {
                if (useOrder && dim.Columns[i] == "ClassName")
                {
                    int byOrder = a.Order.CompareTo(b.Order);
                    if (byOrder != 0)
                        return byOrder;
                }
                bool aMissing = a.Values[i] == NotSet;
                bool bMissing = b.Values[i] == NotSet;
                if (aMissing != bMissing)
                    return aMissing ? 1 : -1;
                int byText = string.Compare(a.Values[i], b.Values[i], StringComparison.CurrentCultureIgnoreCase);
                if (byText != 0)
                    return byText;
            }
            return 0;
        }
        var ordered = groups.Values.ToList();
        switch (v.SortKey)
        {
            case "count_desc":
                ordered.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : CompareDefault(a, b));
                break;
            case "count_asc":
                ordered.Sort((a, b) => a.Count != b.Count ? a.Count.CompareTo(b.Count) : CompareDefault(a, b));
                break;
            case "change_desc" when compare:
                ordered.Sort((a, b) => {
                    int byChange = (b.Count - b.Compare).CompareTo(a.Count - a.Compare);
                    return byChange != 0 ? byChange : CompareDefault(a, b);
                });
                break;
            case "change_asc" when compare:
                ordered.Sort((a, b) => {
                    int byChange = (a.Count - a.Compare).CompareTo(b.Count - b.Compare);
                    return byChange != 0 ? byChange : CompareDefault(a, b);
                });
                break;
            default:
                ordered.Sort(CompareDefault);
                break;
        }
        string measure = changes ? MeasureChanges : MeasureStudents;
        var table = new DataTable();
        foreach (string column in dim.Columns)
            table.Columns.Add(column, typeof(string));
        table.Columns.Add(measure, typeof(int));
        for (int i = 0; i < splitValues.Count; i++)
            table.Columns.Add($"Split{i}", typeof(int));
        if (compare)
        {
            table.Columns.Add("CompareStudents", typeof(int));
            table.Columns.Add("Change", typeof(int));
        }
        if (hasBalance)
            table.Columns.Add(BalanceKey, typeof(decimal));
        int total = 0;
        int compareTotal = 0;
        decimal balanceTotal = 0m;
        var splitTotals = new int[splitValues.Count];
        foreach (GroupAcc acc in ordered)
        {
            DataRow row = table.NewRow();
            for (int i = 0; i < dim.Columns.Length; i++)
                row[dim.Columns[i]] = acc.Values[i];
            row[measure] = acc.Count;
            total += acc.Count;
            for (int i = 0; i < splitValues.Count; i++)
            {
                acc.Split.TryGetValue(splitValues[i], out int n);
                row[$"Split{i}"] = n;
                splitTotals[i] += n;
            }
            if (compare)
            {
                row["CompareStudents"] = acc.Compare;
                row["Change"] = acc.Count - acc.Compare;
                compareTotal += acc.Compare;
            }
            if (hasBalance)
            {
                row[BalanceKey] = acc.Balance;
                balanceTotal += acc.Balance;
            }
            table.Rows.Add(row);
        }
        if (ordered.Count > 0)
        {
            DataRow totalRow = table.NewRow();
            totalRow[dim.Columns[0]] = TotalLabel;
            for (int i = 1; i < dim.Columns.Length; i++)
                totalRow[dim.Columns[i]] = string.Empty;
            totalRow[measure] = total;
            for (int i = 0; i < splitValues.Count; i++)
                totalRow[$"Split{i}"] = splitTotals[i];
            if (compare)
            {
                totalRow["CompareStudents"] = compareTotal;
                totalRow["Change"] = total - compareTotal;
            }
            if (hasBalance)
                totalRow[BalanceKey] = balanceTotal;
            table.Rows.Add(totalRow);
        }
        string unit = changes ? "change(s)" : "student(s)";
        string countText = compare
            ? $"{ordered.Count:N0} group(s) - {total:N0} {unit} against {compareTotal:N0}"
            : $"{ordered.Count:N0} group(s) - {total:N0} {unit}";
        return new GroupedResult(table, splitValues, countText);
    }
    /// <summary>The column the counts are broken down by, or null when there is no split (or it would repeat the grouping).</summary>
    private static string? ResolveSplitColumn(ViewArgs v, GroupDim dim)
    {
        if (v.SplitKey.Length == 0 || v.SplitKey == GroupNone)
            return null;
        var splits = v.Mode == ModeChanges ? ChangeSplits : RegisterSplits;
        var match = splits.FirstOrDefault(s => string.Equals(s.Key, v.SplitKey, StringComparison.OrdinalIgnoreCase));
        if (match.Column is null || dim.Columns.Contains(match.Column))
            return null;
        return match.Column;
    }
}
