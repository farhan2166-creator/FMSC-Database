using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class FeeAssignmentPosting
{
    internal static Task<int> PostReviewedAsync(DataTable selected) => Task.Run(() => PostReviewed(selected));
    private static int PostReviewed(DataTable selected)
    {
        if (selected.Rows.Count == 0) throw new InvalidOperationException("Select at least one fee.");
        var months = selected.AsEnumerable().Select(row => Convert.ToDateTime(row["BillingMonth"])).Distinct().ToArray();
        if (months.Length != 1) throw new InvalidOperationException("Review and post one month at a time.");
        int enrollmentId = Convert.ToInt32(selected.Rows[0]["EnrollmentId"]);
        if (selected.AsEnumerable().Any(row => Convert.ToInt32(row["EnrollmentId"]) != enrollmentId))
            throw new InvalidOperationException("Review one student at a time.");
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var validate = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
            SELECT StudentFeeAssignmentId, FeeTypeId, Amount, DiscountAmount, EffectiveFrom, EffectiveTo, CancelledAt
            FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
            SELECT FeeTypeId FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
            WHERE EnrollmentId=@EnrollmentId AND BillingMonth=@Month AND ChargeKind=N'Monthly' AND Status<>N'Cancelled';
            SELECT DISTINCT FeeTypeId FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
            WHERE EnrollmentId=@EnrollmentId AND BillingMonth=@Month AND ChargeKind=N'Monthly' AND Status=N'Cancelled';
            """, connection, transaction);
        validate.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@EnrollmentId", enrollmentId), DatabaseHelper.Date("@Month", months[0]) });
        var current = new DataSet();
        using (var adapter = new SqlDataAdapter(validate)) adapter.Fill(current);
        foreach (DataRow expected in selected.Rows) {
            bool reversed = current.Tables[2].AsEnumerable().Any(row => Convert.ToInt32(row["FeeTypeId"]) == Convert.ToInt32(expected["FeeTypeId"]));
            bool reviewedReversal = selected.Columns.Contains("ReviewNote") && Convert.ToString(expected["ReviewNote"])!.StartsWith("Previously reversed");
            var actual = current.Tables[0].AsEnumerable().SingleOrDefault(row =>
                Convert.ToInt32(row["StudentFeeAssignmentId"]) == Convert.ToInt32(expected["StudentFeeAssignmentId"]));
            if (reversed != reviewedReversal || actual == null || actual["CancelledAt"] != DBNull.Value ||
                Convert.ToDecimal(actual["Amount"]) != Convert.ToDecimal(expected["GrossAmount"]) ||
                Convert.ToDecimal(actual["DiscountAmount"]) != Convert.ToDecimal(expected["DiscountAmount"]) ||
                (actual["EffectiveFrom"] != DBNull.Value && Convert.ToDateTime(actual["EffectiveFrom"]) > months[0]) ||
                (actual["EffectiveTo"] != DBNull.Value && Convert.ToDateTime(actual["EffectiveTo"]) < months[0]) ||
                current.Tables[1].AsEnumerable().Any(row => Convert.ToInt32(row["FeeTypeId"]) == Convert.ToInt32(expected["FeeTypeId"])))
                throw new InvalidOperationException("Fees changed since this review. Refresh and review the amounts again; nothing was posted.");
        }
        using var post = new SqlCommand("PostSelectedMonthlyFees", connection, transaction) { CommandType = CommandType.StoredProcedure };
        post.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Date("@BillingMonth", months[0]),
            new SqlParameter("@Selection", SqlDbType.Structured) { TypeName="dbo.PostingSelectionType", Value=Tvp.PostingSelectionsFromPreview(selected,_=>true) },
            DatabaseHelper.P("@Reason", "Explicitly reviewed monthly fee posting from student fee changes.") });
        int created;
        using (var reader = post.ExecuteReader()) { reader.Read(); created = Convert.ToInt32(reader["ChargesCreated"]); }
        if (created != selected.Rows.Count) throw new InvalidOperationException("Eligibility changed. Nothing was posted; refresh the review.");
        transaction.Commit();
        return created;
    }
    internal static DataTable PendingThrough(int enrollmentId, DateTime throughMonth)
    {
        var enrollment = DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId, N'Fees.Collect';
            SELECT AcademicYearId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
            """, DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@EnrollmentId", enrollmentId));
        if (enrollment.Rows.Count == 0) throw new InvalidOperationException("The enrollment no longer exists.");
        var months = DatabaseHelper.QueryUser("GetPostableMonths",
            DatabaseHelper.Int("@AcademicYearId", Convert.ToInt32(enrollment.Rows[0]["AcademicYearId"])),
            DatabaseHelper.Int("@CampusId", 0), DatabaseHelper.Int("@ClassId", 0), DatabaseHelper.Int("@SectionId", 0),
            new SqlParameter("@FeeTypeIds", SqlDbType.Structured) { TypeName = "dbo.FeeTypeIdList", Value = Tvp.FeeTypeIds(Array.Empty<int>()) });
        DataTable pending = null!;
        foreach (DataRow row in months.Rows) {
            DateTime month = Convert.ToDateTime(row["BillingMonth"]);
            if (month > throughMonth) continue;
            var preview = Preview(enrollmentId, month);
            pending ??= preview.Clone();
            foreach (DataRow charge in preview.Rows) pending.ImportRow(charge);
        }
        return pending ?? Preview(enrollmentId, throughMonth);
    }
    internal static Task<DataTable> PendingThroughAsync(int enrollmentId, DateTime throughMonth) =>
        Task.Run(() => PendingThrough(enrollmentId, throughMonth));
    private static SqlParameter[] Parameters(DateTime month) => new[] {
        DatabaseHelper.Date("@BillingMonth", month),
        DatabaseHelper.Int("@CampusId", 0), DatabaseHelper.Int("@ClassId", 0), DatabaseHelper.Int("@SectionId", 0),
        new SqlParameter("@FeeTypeIds", SqlDbType.Structured) { TypeName = "dbo.FeeTypeIdList", Value = Tvp.FeeTypeIds(Array.Empty<int>()) },
        DatabaseHelper.Bit("@ExcludeAlreadyPosted", true)
    };
    internal static DataTable Preview(int enrollmentId, DateTime month) => Select(
        DatabaseHelper.QueryUserSet("PreviewMonthlyFees", Parameters(month)), enrollmentId);
    internal static async Task<DataTable> PreviewAsync(int enrollmentId, DateTime month) => Select(
        await DatabaseHelper.QueryUserSetAsync("PreviewMonthlyFees", Parameters(month)), enrollmentId);
    private static DataTable Select(DataSet result, int enrollmentId)
    {
        var rows = result.Tables[0];
        var selected = rows.Clone();
        foreach (DataRow row in rows.Rows)
            if (Convert.ToInt32(row["EnrollmentId"]) == enrollmentId) selected.ImportRow(row);
        return selected;
    }
}
