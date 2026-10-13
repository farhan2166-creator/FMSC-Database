using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Students.Admissions;

// Keep preview validation and the existing stored procedure inside one transaction.
// Locks protect the comparison through the procedure's nested transaction/commit.
internal static class QuickRegistrationWriter
{
    internal const string FeeQuery = """
        SELECT F.FeeTypeId,F.FeeTypeName,F.IsMandatory,F.IsTransportLinked,C.Amount,C.Frequency,
            CONVERT(bit,F.IsMandatory) [Include],CONVERT(decimal(12,2),0) Discount
        FROM dbo.CampusClassSections P CROSS JOIN dbo.FeeTypes F
        LEFT JOIN dbo.FeeConfigurations C ON C.CampusId=P.CampusId AND C.ClassId=P.ClassId AND C.FeeTypeId=F.FeeTypeId
        WHERE P.ClassSectionId=@Placement AND (F.IsMandatory=1 OR C.FeeTypeId IS NOT NULL)
        ORDER BY F.DisplayOrder,F.FeeTypeName;
        """;

    private const string SnapshotSql = """
        EXEC dbo.RequirePermission @UserId,N'Students.View';
        SELECT CONCAT(
            (SELECT S.* FROM dbo.Students S WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission
                FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT E.* FROM dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission
                ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT A.* FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission
                ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES));
        """;

    private const string StoredFeesSql = """
        SELECT A.StudentFeeAssignmentId,F.FeeTypeId,F.FeeTypeName,F.IsMandatory,F.IsTransportLinked,
            A.Amount,A.Frequency,CONVERT(bit,1) [Include],A.DiscountAmount Discount,
            CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BillingCharges B JOIN dbo.FeePaymentAllocations PA ON PA.BillingChargeId=B.BillingChargeId
                JOIN dbo.FeePayments FP ON FP.FeePaymentId=PA.FeePaymentId WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId AND FP.Status=N'Completed') THEN 1 ELSE 0 END) HasPayments
        FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
        JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
        WHERE E.AdmissionId=@Admission AND E.Status IN(N'Pending',N'Active') AND A.CancelledAt IS NULL
        ORDER BY F.DisplayOrder,F.FeeTypeName;
        """;

    internal sealed record Registration(DataTable Details, DataTable Fees, string Snapshot);

    internal static Task<Registration> LoadAsync(int admission)
    {
        int user = CurrentUser.UserId;
        return Task.Run(() => {
            using var connection = DatabaseHelper.GetConnection();
            DatabaseHelper.OpenConnection(connection);
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            // Read the token first to lock the same rows that supply the displayed values.
            string snapshot = Snapshot(connection, transaction, admission, user);
            using var details = new SqlCommand("GetStudentDetails", connection, transaction) { CommandType=CommandType.StoredProcedure };
            details.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId",user), DatabaseHelper.Int("@AdmissionId",admission) });
            var data = new DataTable();
            using (var adapter = new SqlDataAdapter(details)) adapter.Fill(data);
            using var fees = new SqlCommand(StoredFeesSql, connection, transaction);
            fees.Parameters.Add(DatabaseHelper.Int("@Admission",admission));
            var rates = new DataTable();
            using (var adapter = new SqlDataAdapter(fees)) adapter.Fill(rates);
            transaction.Commit();
            return new Registration(data,rates,snapshot);
        });
    }

    private static string Snapshot(SqlConnection connection, SqlTransaction transaction, int admission, int user)
    {
        using var command = new SqlCommand(SnapshotSql,connection,transaction);
        command.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId",user),DatabaseHelper.Int("@Admission",admission) });
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    internal static string Quote(DataTable rates) => QuickRegistrationQuote.Create(rates);

    internal static Task<DataSet> SaveNewAsync(int placement, string expectedQuote, params SqlParameter[] parameters)
    {
        int user = CurrentUser.UserId;
        return WriteAsync("AdmitStudent",parameters,user,(connection,transaction)=>{
            using var check = new SqlCommand("EXEC dbo.RequirePermission @UserId,N'Students.Admit';\n"+FeeQuery,connection,transaction);
            check.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId",user),DatabaseHelper.Int("@Placement",placement) });
            var current = new DataTable();
            using (var adapter = new SqlDataAdapter(check)) adapter.Fill(current);
            if (Quote(current)!=expectedQuote)
                throw new InvalidOperationException("The class fees have changed. Choose the placement again to review the current fees before saving.");
        });
    }

    internal static Task<DataSet> SavePendingAsync(int admission, string expectedSnapshot, params SqlParameter[] parameters)
    {
        int user = CurrentUser.UserId;
        return WriteAsync("SaveStudentDetailsWithFirstEnrollment",parameters,user,(connection,transaction)=>{
            if (string.IsNullOrEmpty(expectedSnapshot) || Snapshot(connection,transaction,admission,user)!=expectedSnapshot)
                throw new InvalidOperationException("This registration has changed since it was opened. Your edits have been kept. Reopen it and review the current details before saving.");
        });
    }

    private static Task<DataSet> WriteAsync(string procedure, SqlParameter[] parameters, int user,
        Action<SqlConnection,SqlTransaction> verify) => Task.Run(()=>{
            using var connection = DatabaseHelper.GetConnection();
            DatabaseHelper.OpenConnection(connection);
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            verify(connection,transaction);
            using var command = new SqlCommand(procedure,connection,transaction) { CommandType=CommandType.StoredProcedure,CommandTimeout=120 };
            command.Parameters.AddRange(parameters);
            command.Parameters.Add(DatabaseHelper.Int("@UserId",user));
            var result = new DataSet();
            using (var adapter = new SqlDataAdapter(command)) adapter.Fill(result);
            transaction.Commit();
            return result;
        });
}
