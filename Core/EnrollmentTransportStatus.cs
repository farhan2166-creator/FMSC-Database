namespace FMSC_SMS.Core;

internal static class EnrollmentTransportStatus
{
    internal static string Read(int enrollmentId)
    {
        if(enrollmentId<=0) return "No placement";
        var rows=DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId,N'Students.View';
            SELECT S.EffectiveFrom,S.EffectiveTo FROM dbo.StudentFeeAssignments S
            JOIN dbo.FeeTypes F ON F.FeeTypeId=S.FeeTypeId AND F.IsTransportLinked=1
            WHERE S.EnrollmentId=@EnrollmentId AND S.CancelledAt IS NULL
            ORDER BY CASE WHEN S.EffectiveTo IS NULL THEN 0 ELSE 1 END,
                S.EffectiveFrom DESC,S.StudentFeeAssignmentId DESC;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",enrollmentId));
        return Describe(rows,DateTime.Today);
    }
    internal static string Describe(DataTable rows,DateTime today)
    {
        if(rows.Rows.Count==0) return "No";
        DateTime month=new(today.Year,today.Month,1);
        DataRow row=rows.Rows[0];
        if(row["EffectiveTo"]!=DBNull.Value)
        {
            DateTime stop=Convert.ToDateTime(row["EffectiveTo"]).AddMonths(1);
            return $"{(stop>month ? "Stops" : "Stopped")} from {stop:MMMM yyyy}";
        }
        if(row["EffectiveFrom"]!=DBNull.Value && Convert.ToDateTime(row["EffectiveFrom"])>month)
            return $"Starts from {Convert.ToDateTime(row["EffectiveFrom"]):MMMM yyyy}";
        return "Yes";
    }
}
