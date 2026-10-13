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
                ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT FM.* FROM dbo.FamilyMembers FM WITH(UPDLOCK,HOLDLOCK)
                WHERE FM.AdmissionId=@Admission OR FM.FamilyId IN
                    (SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission)
                ORDER BY FamilyMemberId FOR JSON PATH,INCLUDE_NULL_VALUES));
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
            using var family = new SqlCommand("""
                SELECT COALESCE(FM.FamilyMemberId,0) FamilyMemberId,
                    (SELECT COUNT(*) FROM dbo.FamilyMembers WHERE FamilyId=FM.FamilyId) FamilyMemberCount
                FROM dbo.Students S LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId=S.AdmissionId
                WHERE S.AdmissionId=@Admission;
                """,connection,transaction);
            family.Parameters.Add(DatabaseHelper.Int("@Admission",admission));
            using (var reader=family.ExecuteReader())
            {
                data.Columns.Add("FamilyMemberId",typeof(int));
                data.Columns.Add("FamilyMemberCount",typeof(int));
                if(reader.Read()&&data.Rows.Count==1)
                {
                    data.Rows[0]["FamilyMemberId"]=reader.GetInt32(0);
                    data.Rows[0]["FamilyMemberCount"]=reader.GetInt32(1);
                }
            }
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

    internal static Task<DataSet> ChangeFamilyAsync(int admission, string expectedSnapshot, int? sibling)
    {
        int user=CurrentUser.UserId;
        return WriteAsync(sibling.HasValue?"AddStudentSibling":"DeleteStudentSibling",
            sibling.HasValue?new[]{DatabaseHelper.Int("@AdmissionId",admission),
                DatabaseHelper.Int("@SiblingAdmissionId",sibling.Value),DatabaseHelper.P("@RelationshipType","Sibling")}
                :new[]{DatabaseHelper.Int("@FamilyMemberId",0)},user,(connection,transaction)=>{
            using var access=new SqlCommand("EXEC dbo.RequirePermission @UserId,N'Students.Administer';",connection,transaction);
            access.Parameters.Add(DatabaseHelper.Int("@UserId",user));access.ExecuteNonQuery();
            if(string.IsNullOrEmpty(expectedSnapshot)||Snapshot(connection,transaction,admission,user)!=expectedSnapshot)
                throw new InvalidOperationException("This registration or family has changed. Reload Registration before changing its parent link.");
            using var state=new SqlCommand("SELECT Status FROM dbo.Students WHERE AdmissionId=@Admission",connection,transaction);
            state.Parameters.Add(DatabaseHelper.Int("@Admission",admission));
            if(Convert.ToString(state.ExecuteScalar()) is not ("Pending" or "Active"))
                throw new InvalidOperationException("Only pending or active students can change their parent link here.");
        },admission);
    }

    private static Task<DataSet> WriteAsync(string procedure, SqlParameter[] parameters, int user,
        Action<SqlConnection,SqlTransaction> verify, int unlinkAdmission=0) => Task.Run(()=>{
            using var connection = DatabaseHelper.GetConnection();
            DatabaseHelper.OpenConnection(connection);
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            verify(connection,transaction);
            using var command = new SqlCommand(procedure,connection,transaction) { CommandType=CommandType.StoredProcedure,CommandTimeout=120 };
            // The UI's new-admission arguments already contain @UserId. Use the
            // captured signed-in actor exactly once, as DatabaseHelper does.
            command.Parameters.AddRange(parameters.Where(p=>!string.Equals(p.ParameterName,"@UserId",StringComparison.OrdinalIgnoreCase)).ToArray());
            command.Parameters.Add(DatabaseHelper.Int("@UserId",user));
            if(procedure=="DeleteStudentSibling")
            {
                using var member=new SqlCommand("SELECT FamilyMemberId FROM dbo.FamilyMembers WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission",connection,transaction);
                member.Parameters.Add(DatabaseHelper.Int("@Admission",unlinkAdmission));
                object? id=member.ExecuteScalar();
                if(id is not int memberId)throw new InvalidOperationException("This student has no parent link to remove. Reload Registration.");
                command.Parameters["@FamilyMemberId"].Value=memberId;
            }
            var result = new DataSet();
            using (var adapter = new SqlDataAdapter(command)) adapter.Fill(result);
            transaction.Commit();
            return result;
        });
}
