using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class StudentFeeAssignmentAdd
{
    internal static void Save(int enrollmentId,int feeTypeId)
    {
        using var connection=DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction=connection.BeginTransaction(IsolationLevel.Serializable);
        using (var acknowledgement = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.Enrollments E
            JOIN dbo.StudentAdmissionFeeAcknowledgements A WITH(UPDLOCK,HOLDLOCK)
                ON A.AdmissionId=E.AdmissionId
            JOIN dbo.FeeTypes F ON F.FeeTypeId=@FeeTypeId
            WHERE E.EnrollmentId=@EnrollmentId AND F.FeeTypeCode=N'ADMISSION';
            """, connection, transaction))
        {
            acknowledgement.Parameters.AddRange(new[] {
                DatabaseHelper.Int("@EnrollmentId", enrollmentId),
                DatabaseHelper.Int("@FeeTypeId", feeTypeId)
            });
            if (Convert.ToInt32(acknowledgement.ExecuteScalar()) > 0)
                throw new InvalidOperationException("This student's admission fee is recorded as already paid before the system. It cannot be charged again.");
        }
        using var command=new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
            IF EXISTS(SELECT 1 FROM dbo.FeeTypes WHERE FeeTypeId=@FeeTypeId AND IsTransportLinked=1)
                THROW 53113,'Use Start Transport to choose the transport month and review posted fees.',1;
            EXEC dbo.AddStudentFeeAssignment @UserId,@EnrollmentId,@FeeTypeId;
            """,connection,transaction);
        command.Parameters.AddRange(new[]{DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.Int("@EnrollmentId",enrollmentId),DatabaseHelper.Int("@FeeTypeId",feeTypeId)});
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
