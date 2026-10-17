namespace FMSC_SMS.Core;

internal static class FamilyBillingData
{
    private static string Text(DataRow row, string column) => row.Table.Columns.Contains(column)
        && !row.IsNull(column) ? Convert.ToString(row[column]) ?? "" : "";
    private static bool Flag(DataRow row, string column) => row.Table.Columns.Contains(column)
        && !row.IsNull(column) && Convert.ToBoolean(row[column]);

    internal static bool AwaitsReturn(DataRow row) => Text(row, "Status") == "Active"
        && Text(row, "EnrollmentStatus") == "Active" && !Flag(row, "BillingReady")
        && row.Table.Columns.Contains("PlacementConfirmedAt") && !row.IsNull("PlacementConfirmedAt");

    internal static string Readiness(DataRow row)
    {
        if (Text(row, "Status") == "Pending") return "Pending Registration";
        if (AwaitsReturn(row)) return "Awaiting Return";
        if (Text(row, "Status") == "Active" && Text(row, "EnrollmentStatus") == "Active"
            && Flag(row, "BillingReady")) return "Ready for Billing";
        return Text(row, "Status") == "Active" ? "Review Placement" : Text(row, "Status");
    }

    internal static int[] SelectMembers(DataTable members, IEnumerable<int>? selected)
    {
        int[] family = members.AsEnumerable().Select(row => Convert.ToInt32(row["AdmissionId"])).Distinct().ToArray();
        int[] ids = selected?.Distinct().ToArray() ?? family;
        if (ids.Length == 0 || ids.Any(id => id <= 0 || !family.Contains(id)))
            throw new InvalidOperationException("The selected children no longer belong to this family. Refresh and select them again.");
        return ids;
    }

    internal static string IncompleteNotice(DataTable members, IEnumerable<int> selected)
    {
        var ids = selected.ToHashSet();
        string[] names = members.AsEnumerable().Where(row => ids.Contains(Convert.ToInt32(row["AdmissionId"]))
                && (AwaitsReturn(row) || Readiness(row) == "Review Placement"))
            .Select(row => Text(row, "StudentName") + " (" + Readiness(row) + ")").ToArray();
        return names.Length == 0 ? "" : string.Join(", ", names) +
            ". Their new-year opening charges may not exist yet. This bill contains only posted outstanding charges, including older dues. " +
            "Printing does not confirm a return or create charges.";
    }
}
