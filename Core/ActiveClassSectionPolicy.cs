using System.Data;

namespace FMSC_SMS.Core;

/// <summary>
/// Owner decision, 1 Nov 2026: class-section IsActive is a C# choice for
/// NEW placements, not a permanent SQL historical restriction. Do not
/// reject old placements or historical corrections solely because they
/// were later disabled. SQL still owns permissions and financial safety.
/// </summary>
// BD-08: active catalog choices govern new placements; later deactivation must not invalidate recorded history.
internal static class ActiveClassSectionPolicy
{
    private const string ActiveIdsSql =
        "SELECT ClassSectionId FROM dbo.CampusClassSections WHERE IsActive=1;";

    internal static DataTable OnlyActive(DataTable source)
    {
        using var active = DatabaseHelper.QuerySql(ActiveIdsSql);
        return Filter(source, active);
    }

    internal static async Task<DataTable> OnlyActiveAsync(DataTable source)
    {
        using var active = await DatabaseHelper.QuerySqlAsync(ActiveIdsSql);
        return Filter(source, active);
    }

    private static DataTable Filter(DataTable source, DataTable active)
    {
        var ids = active.AsEnumerable().Select(r => Convert.ToInt32(r["ClassSectionId"])).ToHashSet();
        var filtered = source.Clone();
        foreach (DataRow row in source.Rows)
            if (ids.Contains(Convert.ToInt32(row["ClassSectionId"])))
                filtered.ImportRow(row);
        return filtered;
    }

    internal static void EnsureActive(int id)
    {
        if (id <= 0 || DatabaseHelper.QuerySql(
                "SELECT ClassSectionId FROM dbo.CampusClassSections WHERE ClassSectionId=@Id AND IsActive=1;",
                DatabaseHelper.Int("@Id", id)).Rows.Count == 0)
            throw new InvalidOperationException(
                "The class/section is inactive. Choose an active destination and review again.");
    }

    internal static async Task EnsureActiveAsync(int id)
    {
        if (id <= 0 || (await DatabaseHelper.QuerySqlAsync(
                "SELECT ClassSectionId FROM dbo.CampusClassSections WHERE ClassSectionId=@Id AND IsActive=1;",
                DatabaseHelper.Int("@Id", id))).Rows.Count == 0)
            throw new InvalidOperationException(
                "The class/section is inactive. Choose an active destination and review again.");
    }
}
