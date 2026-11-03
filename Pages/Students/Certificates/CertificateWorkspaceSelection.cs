using System.Data;

namespace FMSC_SMS.Pages.Students.Certificates;

/// <summary>
/// The workspace always rebinds after a saved change. Preserve the exact
/// selected issue when still available; otherwise highlight the most recent
/// issue of the operated-on type. Never match by displayed certificate number.
/// </summary>
internal static class CertificateWorkspaceSelection
{
    internal readonly record struct Key(string Kind, int RecordId, int IssueId, bool PreferNewest = false);

    internal static Key? FromRow(DataRowView? row)
    {
        if (row is null) return null;
        string kind = Convert.ToString(row["Kind"]) ?? "";
        if (string.IsNullOrEmpty(kind)) return null;
        return new Key(kind, Convert.ToInt32(row["RecordId"]), Convert.ToInt32(row["IssueId"]));
    }

    internal static DataRowView? Resolve(DataView view, Key? selection)
    {
        if (view.Count == 0) return null;
        if (selection is null) return view[0]; // Select latest for a newly chosen student.

        var key = selection.Value;
        DataRowView? latestType = null;
        foreach (DataRowView row in view)
        {
            if (!string.Equals(Convert.ToString(row["Kind"]), key.Kind, StringComparison.Ordinal))
                continue;
            latestType ??= row; // View is already sorted by descending recorded date.
            if (!key.PreferNewest && Convert.ToInt32(row["RecordId"]) == key.RecordId &&
                Convert.ToInt32(row["IssueId"]) == key.IssueId)
                return row;
        }
        return latestType; // A freshly created/reissued copy, or an older deleted row.
    }
}
