using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

internal static class LookupDisplayOrder
{
    internal static DataTable ForBinding(DataTable source, string key)
    {
        if (key == "ClassSectionId" && source.Columns.Contains("CampusId") && source.Columns.Contains("ClassId"))
        {
            static int Value(DataRow row,string column) => row.Table.Columns.Contains(column) && !row.IsNull(column)
                ? Convert.ToInt32(row[column]) : int.MaxValue;
            var placements=source.Clone();
            foreach(var row in source.DefaultView.Cast<DataRowView>()
                .OrderBy(r=>Value(r.Row,key)<=0?0:1)
                .ThenBy(r=>Value(r.Row,"CampusDisplayOrder"))
                .ThenBy(r=>Value(r.Row,"CampusId"))
                .ThenBy(r=>Value(r.Row,"__ClassCatalogPosition"))
                .ThenBy(r=>Value(r.Row,"ClassDisplayOrder"))
                .ThenBy(r=>Value(r.Row,"ClassId"))
                .ThenBy(r=>Value(r.Row,"SectionDisplayOrder"))
                .ThenBy(r=>Value(r.Row,"SectionId"))
                .ThenBy(r=>Value(r.Row,key))) placements.ImportRow(row.Row);
            return placements;
        }
        bool classes = source.Columns.Contains("ClassId") && key is "ClassId" or "ClassSectionId";
        string order = key switch {
            "ClassId" or "ClassSectionId" when source.Columns.Contains("ClassDisplayOrder") => "ClassDisplayOrder",
            "SectionId" or "ClassSectionId" when source.Columns.Contains("SectionDisplayOrder") => "SectionDisplayOrder",
            _ => "DisplayOrder"
        };
        if (!source.Columns.Contains(order) && !classes) return source;
        // Preserve the catalog's program order when older result shapes omit names.
        var programs = new Dictionary<int, int>();
        if (classes && source.Columns.Contains("AcademicProgramId"))
            foreach (DataRow row in source.Rows)
                if (!row.IsNull("AcademicProgramId"))
                {
                    int program = Convert.ToInt32(row["AcademicProgramId"]);
                    if (!programs.ContainsKey(program)) programs.Add(program, programs.Count);
                }
        int ProgramPosition(DataRow row) => source.Columns.Contains("__ClassProgramPosition") && !row.IsNull("__ClassProgramPosition")
            ? Convert.ToInt32(row["__ClassProgramPosition"]) :
            source.Columns.Contains("AcademicProgramId") && !row.IsNull("AcademicProgramId")
                ? programs.GetValueOrDefault(Convert.ToInt32(row["AcademicProgramId"]), int.MaxValue) : 0;
        var result = source.Clone();
        foreach (var row in source.DefaultView.Cast<DataRowView>()
            .OrderBy(r => source.Columns.Contains(key) && !r.Row.IsNull(key) &&
                int.TryParse(Convert.ToString(r[key]), out int id) && id <= 0 ? 0 : 1)
            .ThenBy(r => classes ? ProgramGroup(r.Row) : 0)
            .ThenBy(r => classes ? ProgramPosition(r.Row) : 0)
            .ThenBy(r => !source.Columns.Contains(order) || r.Row.IsNull(order) ? int.MaxValue : Convert.ToInt32(r[order]))
            .ThenBy(r => classes && source.Columns.Contains("ClassId") && !r.Row.IsNull("ClassId")
                ? Convert.ToInt32(r["ClassId"]) : 0)
            .ThenBy(r => classes && source.Columns.Contains("SectionDisplayOrder") && !r.Row.IsNull("SectionDisplayOrder")
                ? Convert.ToInt32(r["SectionDisplayOrder"]) : 0))
            result.ImportRow(row.Row);
        return result;
    }

    private static int ProgramGroup(DataRow row)
    {
        if (row.Table.Columns.Contains("__ClassProgramGroup") && !row.IsNull("__ClassProgramGroup"))
            return Convert.ToInt32(row["__ClassProgramGroup"]);
        foreach (string column in new[] { "ProgramType", "ProgramCode", "ProgramName" })
        {
            if (!row.Table.Columns.Contains(column) || row.IsNull(column)) continue;
            string value = Convert.ToString(row[column])?.Trim() ?? string.Empty;
            if (value.Equals("School", StringComparison.OrdinalIgnoreCase)) return 0;
            if (value.Equals("College", StringComparison.OrdinalIgnoreCase)) return 1;
        }
        return 2;
    }

    internal static async Task<DataTable> EnrichAsync(string procedure, SqlParameter[] parameters, DataTable table)
    {
        if (procedure.Equals("GetClasses", StringComparison.OrdinalIgnoreCase)) return ForBinding(table, "ClassId");
        if (!procedure.Equals("GetClassSections", StringComparison.OrdinalIgnoreCase) &&
            !procedure.Equals("ReportClassSectionLookup", StringComparison.OrdinalIgnoreCase)) return table;
        int userId = Convert.ToInt32(parameters.First(p => p.ParameterName.Equals("@UserId", StringComparison.OrdinalIgnoreCase)).Value);
        var classes = Task.Run(() => DatabaseHelper.Query("GetClasses", DatabaseHelper.Int("@UserId", userId)));
        var sections = Task.Run(() => DatabaseHelper.Query("GetSections", DatabaseHelper.Int("@UserId", userId)));
        var campuses = Task.Run(() => DatabaseHelper.Query("GetCampuses", DatabaseHelper.Int("@UserId", userId)));
        await Task.WhenAll(classes, sections, campuses).ConfigureAwait(false);
        AddOrder(table, campuses.Result, "CampusId", "CampusDisplayOrder");
        AddOrder(table, classes.Result, "ClassId", "ClassDisplayOrder");
        AddOrder(table, sections.Result, "SectionId", "SectionDisplayOrder");
        if (table.Columns.Contains("ClassId"))
        {
            var orderedCatalog = ForBinding(classes.Result, "ClassId");
            var catalog = orderedCatalog.AsEnumerable().ToDictionary(row => Convert.ToInt32(row["ClassId"]));
            var classPositions = orderedCatalog.AsEnumerable().Select((row,position)=>(id:Convert.ToInt32(row["ClassId"]),position))
                .ToDictionary(item=>item.id,item=>item.position);
            var positions = orderedCatalog.AsEnumerable().Select(row => Convert.ToInt32(row["AcademicProgramId"]))
                .Distinct().Select((id, position) => (id, position)).ToDictionary(item => item.id, item => item.position);
            foreach (string column in new[] { "__ClassProgramGroup", "__ClassProgramPosition", "__ClassCatalogPosition" })
                if (!table.Columns.Contains(column)) table.Columns.Add(column, typeof(int));
            foreach (DataRow row in table.Rows)
                if (!row.IsNull("ClassId") && catalog.TryGetValue(Convert.ToInt32(row["ClassId"]), out var match))
                {
                    row["__ClassProgramGroup"] = ProgramGroup(match);
                    row["__ClassProgramPosition"] = positions[Convert.ToInt32(match["AcademicProgramId"])];
                    row["__ClassCatalogPosition"] = classPositions[Convert.ToInt32(match["ClassId"])];
                }
        }
        return ForBinding(table, "ClassSectionId");
    }

    private static void AddOrder(DataTable table, DataTable catalog, string key, string column)
    {
        if (!catalog.Columns.Contains("DisplayOrder") || !table.Columns.Contains(key)) return;
        var positions = catalog.AsEnumerable().ToDictionary(r => Convert.ToInt32(r[key]),
            r => r.IsNull("DisplayOrder") ? int.MaxValue : Convert.ToInt32(r["DisplayOrder"]));
        if (!table.Columns.Contains(column)) table.Columns.Add(column, typeof(int));
        foreach (DataRow row in table.Rows)
            row[column] = positions.GetValueOrDefault(Convert.ToInt32(row[key]), int.MaxValue);
    }
}
