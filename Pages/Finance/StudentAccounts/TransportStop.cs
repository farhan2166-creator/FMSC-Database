using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class TransportStop
{
    internal static DataTable Context(int id) => DatabaseHelper.QuerySql("""
        SELECT E.BillingStartDate, E.EnrollmentDate, Y.EndDate,
            (SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B
             WHERE B.EnrollmentId=S.EnrollmentId AND B.FeeTypeId=S.FeeTypeId
             AND (B.Status IN (N'Paid',N'PartiallyPaid') OR EXISTS
                 (SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P
                  ON P.FeePaymentId=A.FeePaymentId AND P.Status=N'Completed'
                  WHERE A.BillingChargeId=B.BillingChargeId GROUP BY A.BillingChargeId
                  HAVING SUM(A.Amount)>0))) LastProtectedMonth,
            B.BillingMonth, B.NetAmount, B.Status
        FROM dbo.StudentFeeAssignments S
        JOIN dbo.FeeTypes F ON F.FeeTypeId=S.FeeTypeId AND F.IsTransportLinked=1
        JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
        JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
        LEFT JOIN dbo.BillingCharges B ON B.EnrollmentId=S.EnrollmentId AND B.FeeTypeId=S.FeeTypeId
        WHERE S.StudentFeeAssignmentId=@Id AND S.CancelledAt IS NULL AND S.EffectiveTo IS NULL
        """, DatabaseHelper.Int("@Id", id));

    internal static void Save(int id, DateTime from, string reason)
    {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
            IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 53130,'A reason is required.',1;
            DECLARE @EnrollmentId int,@FeeTypeId int,@Start date,@End date;
            SELECT @EnrollmentId=S.EnrollmentId,@FeeTypeId=S.FeeTypeId,
                @Start=COALESCE(E.BillingStartDate,E.EnrollmentDate),@End=Y.EndDate
            FROM dbo.StudentFeeAssignments S WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.FeeTypes F ON F.FeeTypeId=S.FeeTypeId AND F.IsTransportLinked=1
            JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
            JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE S.StudentFeeAssignmentId=@Id AND S.CancelledAt IS NULL AND S.EffectiveTo IS NULL;
            IF @EnrollmentId IS NULL THROW 53131,'Transport has changed. Reload the student fees.',1;
            IF @From<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
                OR @From>DATEADD(MONTH,1,DATEFROMPARTS(YEAR(@End),MONTH(@End),1))
                THROW 53137,'Choose a stopping month within this enrollment.',1;
            -- Lock all transport versions and charges, including future versions, until commit.
            DECLARE @Locked int;
            SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId;
            SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId;
            IF EXISTS(SELECT 1 FROM dbo.BillingCharges B
                WHERE B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=@FeeTypeId AND B.BillingMonth>=@From
                AND (B.Status IN(N'Paid',N'PartiallyPaid') OR EXISTS
                    (SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
                     JOIN dbo.FeePayments P WITH(UPDLOCK,HOLDLOCK)
                     ON P.FeePaymentId=A.FeePaymentId AND P.Status=N'Completed'
                     WHERE A.BillingChargeId=B.BillingChargeId GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)))
                THROW 53134,'A transport payment was recorded for this or a later month. Reload and choose a later stopping month.',1;
            DECLARE @Old nvarchar(max)=(SELECT * FROM dbo.StudentFeeAssignments
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId FOR JSON PATH);
            DECLARE @OldCharges nvarchar(max)=(SELECT * FROM dbo.BillingCharges
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND BillingMonth>=@From
                AND Status<>N'Cancelled' FOR JSON PATH);
            UPDATE dbo.BillingCharges SET Status=N'Cancelled',
                Remarks=LEFT(CONCAT(Remarks,N' | Transport stopped: ',@Reason),1000)
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND BillingMonth>=@From
                AND Status<>N'Cancelled';
            UPDATE dbo.StudentFeeAssignments SET
                CancelledAt=CASE WHEN EffectiveFrom>=@From THEN SYSDATETIME() ELSE CancelledAt END,
                EffectiveTo=CASE WHEN EffectiveFrom>=@From THEN EffectiveTo ELSE DATEADD(MONTH,-1,@From) END,
                UpdatedAt=SYSDATETIME()
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND CancelledAt IS NULL
                AND (EffectiveTo IS NULL OR EffectiveTo>=@From);
            DECLARE @Operation uniqueidentifier=NEWID(),@Record nvarchar(200)=CONVERT(nvarchar(200),@Id);
            INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
                VALUES(@Operation,@UserId,N'TRANSPORT_STOP',LEFT(CONCAT(@Record,N'; StopFrom=',CONVERT(nvarchar(10),@From,120),N'; ',@Reason),200));
            DECLARE @New nvarchar(max)=(SELECT * FROM dbo.StudentFeeAssignments
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId FOR JSON PATH);
            DECLARE @NewCharges nvarchar(max)=(SELECT * FROM dbo.BillingCharges
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND BillingMonth>=@From
                AND BillingChargeId IN(SELECT BillingChargeId FROM OPENJSON(@OldCharges)
                    WITH(BillingChargeId bigint '$.BillingChargeId')) FOR JSON PATH);
            EXEC dbo.WriteAuditLog @Operation,@UserId,N'StudentFeeAssignments',@Record,N'UPDATE',@Old,@New;
            EXEC dbo.WriteAuditLog @Operation,@UserId,N'BillingCharges',NULL,N'REVERSAL',@OldCharges,@NewCharges;
            """, connection, transaction);
        command.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@Id", id), DatabaseHelper.Date("@From", from), DatabaseHelper.P("@Reason", reason) });
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
