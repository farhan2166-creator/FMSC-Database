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
        return new(Convert.ToInt32(row["TotalAssignments"]), Convert.ToInt32(row["Succeeded"]),
            Convert.ToInt32(row["Failed"]), failures);
    }
}
