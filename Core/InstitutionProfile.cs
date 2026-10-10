namespace FMSC_SMS.Core;
/// <summary>The institution identity that official documents must show.: the admission bill, the fee receipt and the fee statement printed the literal "Fahim Model School &amp; College".</summary>
internal static class InstitutionProfile
{
    internal sealed record PrintIdentity(string Name, string ContactNumber, string Address);
    public static async Task<string> GetNameAsync() => (await GetPrintIdentityAsync()).Name;
    internal static async Task<PrintIdentity> GetPrintIdentityAsync()
    {
        try
        {
            DataTable profile =
                await DatabaseHelper.QueryUserAsync("GetSchoolProfile");
            if (profile.Rows.Count == 0)
                return new(string.Empty, string.Empty, string.Empty);
            DataRow row =
                profile.Rows[0];
            string Text(string column) => row.Table.Columns.Contains(column)
                ? (Convert.ToString(row[column]) ?? string.Empty).Trim() : string.Empty;
            return new(Text("SchoolName"), Text("ContactNumber"), Text("AddressLine"));
        }
        catch (Exception ex)
        {
            /* Logged, not swallowed: the caller turns the empty result into a refusal to print, and the log is what explains why. */
            AppExceptionHandler.LogNonFatal(ex, "Reading the school profile for an official printout");
            return new(string.Empty, string.Empty, string.Empty);
        }
    }
}
