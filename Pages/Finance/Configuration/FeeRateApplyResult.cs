namespace FMSC_SMS.Pages.Finance.Configuration;

internal sealed record FeeRateApplyResult(int Total, int Succeeded, int Failed, DataTable Failures)
{
    internal static FeeRateApplyResult Read(DataSet result)
    {
        // The internal amount-change procedure emits a row for each new version
        // before the bulk procedure emits its summary and failures.
        var summary = result.Tables.Cast<DataTable>().SingleOrDefault(t =>
            t.Columns.Contains("TotalAssignments") && t.Columns.Contains("Succeeded") && t.Columns.Contains("Failed"));
        if (summary is null || summary.Rows.Count != 1)
            throw new InvalidOperationException("The update did not return a valid summary. Refresh the preview before trying again.");
        var failures = result.Tables.Cast<DataTable>().FirstOrDefault(t =>
            t.Columns.Contains("StudentFeeAssignmentId") && t.Columns.Contains("ErrorMessage")) ?? new DataTable();
        var row = summary.Rows[0];
        int total = Convert.ToInt32(row["TotalAssignments"]), succeeded = Convert.ToInt32(row["Succeeded"]), failed = Convert.ToInt32(row["Failed"]);
        if (total < 0 || succeeded < 0 || failed < 0 || succeeded + failed != total)
            throw new InvalidOperationException("The update returned inconsistent counts. Review the fees again before retrying.");
        return new(total, succeeded, failed, failures);
    }
}

/// <summary>Records only server-confirmed outcomes; an interrupted call remains unknown.</summary>
internal sealed class FeeRateApplyProgress(int expectedGroups)
{
    private readonly List<string> completed = new();
    internal string? PendingGroup { get; private set; }
    internal int Total { get; private set; }
    internal int Succeeded { get; private set; }
    internal int Failed { get; private set; }
    internal void Begin(string group) => PendingGroup = group;
    internal void Confirm(FeeRateApplyResult result)
    {
        if (PendingGroup is null) throw new InvalidOperationException("No fee group is awaiting confirmation.");
        completed.Add(PendingGroup); Total += result.Total; Succeeded += result.Succeeded; Failed += result.Failed;
        PendingGroup = null;
    }
    internal string Summary() => $"Confirmed: {completed.Count:N0} of {expectedGroups:N0} groups completed; {Succeeded:N0} of {Total:N0} student fees updated; {Failed:N0} reported unsuccessful.";
    internal string Interrupted()
    {
        string groups = completed.Count == 0 ? "" : " Completed groups: " + string.Join("; ", completed.Take(4)) + (completed.Count > 4 ? "; and more." : ".");
        string pending = PendingGroup is null
            ? "The results above were confirmed, but the refresh or result display did not finish."
            : $"The outcome for {PendingGroup} is unknown because its confirmation was not received. {expectedGroups - completed.Count - 1:N0} later group(s) were not started.";
        return Summary() + groups + " " + pending + " Earlier confirmed updates were not undone. Choose Review Fee Changes before retrying.";
    }
}
