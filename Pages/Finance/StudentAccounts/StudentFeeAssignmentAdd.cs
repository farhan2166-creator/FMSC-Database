using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class StudentFeeAssignmentAdd
{
    internal static void Save(int enrollmentId,int feeTypeId)
    {
        using var connection=DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction=connection.BeginTransaction(IsolationLevel.Serializable);
        using var command=new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
            IF NOT EXISTS(SELECT 1 FROM dbo.FeeTypes WHERE FeeTypeId=@FeeTypeId AND IsTransportLinked=1)
            BEGIN
                EXEC dbo.AddStudentFeeAssignment @UserId,@EnrollmentId,@FeeTypeId;
                RETURN;
            END;
            DECLARE @AdmissionId int,@CampusId int,@ClassId int,@YearEnd date,@EnrollmentStart date;
            SELECT @AdmissionId=E.AdmissionId,@CampusId=C.CampusId,@ClassId=C.ClassId,
                @YearEnd=Y.EndDate,@EnrollmentStart=COALESCE(E.BillingStartDate,E.EnrollmentDate)
            FROM dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.CampusClassSections C ON C.ClassSectionId=E.ClassSectionId
            JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE E.EnrollmentId=@EnrollmentId AND E.BillingReady=1;
            IF @AdmissionId IS NULL THROW 53114,'Confirm an active billing enrollment before starting transport.',1;
            DECLARE @Locked int;
            SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId;
            SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId;
            IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId
                AND FeeTypeId=@FeeTypeId AND CancelledAt IS NULL AND EffectiveTo IS NULL)
                THROW 53113,'Transport is already active or scheduled. Reload the student fees.',1;
            DECLARE @Cutoff int=(SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile),
                @From date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),@Limit date;
            IF @Cutoff BETWEEN 1 AND 28 AND DAY(GETDATE())>=@Cutoff SET @From=DATEADD(MONTH,1,@From);
            SET @EnrollmentStart=DATEFROMPARTS(YEAR(@EnrollmentStart),MONTH(@EnrollmentStart),1);
            IF @EnrollmentStart>@From SET @From=@EnrollmentStart;
            SELECT @Limit=DATEADD(MONTH,1,MAX(B.BillingMonth)) FROM dbo.BillingCharges B
            WHERE B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=@FeeTypeId
                AND (B.Status IN(N'Paid',N'PartiallyPaid') OR EXISTS(
                    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
                    JOIN dbo.FeePayments P WITH(UPDLOCK,HOLDLOCK)
                    ON P.FeePaymentId=A.FeePaymentId AND P.Status=N'Completed'
                    WHERE A.BillingChargeId=B.BillingChargeId GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0));
            IF @Limit>@From SET @From=@Limit;
            SELECT @Limit=DATEADD(MONTH,1,MAX(EffectiveTo)) FROM dbo.StudentFeeAssignments
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND CancelledAt IS NULL;
            IF @Limit>@From SET @From=@Limit;
            IF @From>DATEFROMPARTS(YEAR(@YearEnd),MONTH(@YearEnd),1)
                THROW 53113,'No transport restart month remains in this academic year.',1;
            DECLARE @ClosedId int,@ClosedOld nvarchar(max),@ClosedNew nvarchar(max);
            SELECT @ClosedId=StudentFeeAssignmentId FROM dbo.StudentFeeAssignments
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND CancelledAt IS NOT NULL AND EffectiveTo IS NULL;
            IF @ClosedId IS NOT NULL
            BEGIN
                SET @ClosedOld=(SELECT * FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@ClosedId
                    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
                -- The unique open-version index also includes cancelled rows.
                UPDATE dbo.StudentFeeAssignments SET EffectiveTo=COALESCE(EffectiveFrom,DATEADD(MONTH,-1,@From)),
                    UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@ClosedId;
                SET @ClosedNew=(SELECT * FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@ClosedId
                    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
            END;
            INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,
                Frequency,Remarks,EffectiveFrom,EffectiveTo)
            SELECT @EnrollmentId,@AdmissionId,F.FeeTypeId,F.Amount,0,F.Frequency,
                N'Transport started after protected months and previous fee periods.',@From,NULL
            FROM dbo.FeeConfigurations F WHERE F.CampusId=@CampusId AND F.ClassId=@ClassId
                AND F.FeeTypeId=@FeeTypeId AND F.Frequency=N'Monthly';
            IF @@ROWCOUNT<>1 THROW 53111,'A single monthly transport rate must be configured for this campus/class.',1;
            DECLARE @Id int=CONVERT(int,SCOPE_IDENTITY()),@Operation uniqueidentifier=NEWID(),
                @Record nvarchar(200),@New nvarchar(max);
            SET @Record=CONVERT(nvarchar(200),@Id);
            INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
                VALUES(@Operation,@UserId,N'TRANSPORT_RESTART',CONCAT(@Record,N'; From=',CONVERT(nvarchar(10),@From,120)));
            IF @ClosedId IS NOT NULL
            BEGIN
                DECLARE @ClosedRecord nvarchar(200)=CONVERT(nvarchar(200),@ClosedId);
                EXEC dbo.WriteAuditLog @Operation,@UserId,N'StudentFeeAssignments',@ClosedRecord,N'UPDATE',@ClosedOld,@ClosedNew;
            END;
            SET @New=(SELECT *,N'Transport restarted; paid months and prior fee periods preserved.' Reason
                FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@Id
                FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
            EXEC dbo.WriteAuditLog @Operation,@UserId,N'StudentFeeAssignments',@Record,N'INSERT',NULL,@New;
            -- Existing cancelled bills stay cancelled. Normal posting creates charges for the new rate.
            """,connection,transaction);
        command.Parameters.AddRange(new[]{DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.Int("@EnrollmentId",enrollmentId),DatabaseHelper.Int("@FeeTypeId",feeTypeId)});
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
