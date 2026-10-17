using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class FeeDiscountPeriod
{
    public static DataRow Context(int id) => DatabaseHelper.QuerySql("""
        SELECT S.Frequency, S.EffectiveFrom, S.EffectiveTo, A.EndDate,
            (SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B
             WHERE B.StudentFeeAssignmentId=S.StudentFeeAssignmentId) LastBilledMonth
        FROM dbo.StudentFeeAssignments S
        JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
        JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
        WHERE S.StudentFeeAssignmentId=@Id AND S.CancelledAt IS NULL
        """, DatabaseHelper.Int("@Id", id)).Rows[0];

    public static void Save(int id, decimal discount, DateTime from, DateTime through)
    {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        // Lock the enrollment's schedule until the split and its audit record commit together.
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId, N'Fees.Adjust';
            DECLARE @EnrollmentId int, @FeeTypeId int, @Amount decimal(12,2),
                @Start date, @OriginalEnd date, @YearEnd date, @OldDiscount decimal(12,2), @NewId int;
            SELECT @EnrollmentId=S.EnrollmentId, @FeeTypeId=S.FeeTypeId,
                @Amount=S.Amount, @Start=S.EffectiveFrom, @OriginalEnd=S.EffectiveTo, @OldDiscount=S.DiscountAmount,
                @YearEnd=DATEFROMPARTS(YEAR(A.EndDate),MONTH(A.EndDate),1)
            FROM dbo.StudentFeeAssignments S WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
            JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
            WHERE S.StudentFeeAssignmentId=@Id AND S.CancelledAt IS NULL
                AND S.Frequency=N'Monthly';
            IF @EnrollmentId IS NULL THROW 53102, 'Reload the student fees before saving.', 1;
            IF @Discount<0 OR @Discount>@Amount OR @From>@Through OR @Through>@YearEnd
                OR (@Start IS NOT NULL AND @From<@Start)
                OR (@OriginalEnd IS NOT NULL AND @Through>@OriginalEnd)
                THROW 53103, 'Choose a valid discount and months within this academic year.', 1;
            IF EXISTS (SELECT 1 FROM dbo.BillingCharges WITH (UPDLOCK,HOLDLOCK)
                WHERE StudentFeeAssignmentId=@Id AND BillingMonth>=@From)
                THROW 53104, 'These months have already been billed. Change the bill discount instead.', 1;
            IF EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments WITH (UPDLOCK,HOLDLOCK)
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId
                AND StudentFeeAssignmentId<>@Id AND CancelledAt IS NULL
                AND (EffectiveTo IS NULL OR EffectiveTo>=ISNULL(@Start,'19000101'))
                AND (EffectiveFrom IS NULL OR @OriginalEnd IS NULL OR EffectiveFrom<=@OriginalEnd))
                THROW 53104, 'The fee periods overlap. Review them before saving a discount.', 1;
            DECLARE @Old nvarchar(max)=(SELECT * FROM dbo.StudentFeeAssignments
                WHERE StudentFeeAssignmentId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            IF @Start=@From
            BEGIN
                UPDATE dbo.StudentFeeAssignments SET DiscountAmount=@Discount,
                    EffectiveTo=@Through,UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@Id;
                SET @NewId=@Id;
            END
            ELSE
            BEGIN
                UPDATE dbo.StudentFeeAssignments SET EffectiveTo=DATEADD(MONTH,-1,@From),
                    UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@Id;
                INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,
                    DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo)
                SELECT EnrollmentId,AdmissionId,FeeTypeId,Amount,@Discount,Frequency,
                    Remarks,@From,@Through FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@Id;
                SET @NewId=CONVERT(int,SCOPE_IDENTITY());
            END;
            IF @OriginalEnd IS NULL OR @Through<@OriginalEnd
            INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,
                DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo)
            SELECT EnrollmentId,AdmissionId,FeeTypeId,Amount,
                CASE WHEN @Through=@YearEnd THEN 0 ELSE @OldDiscount END,Frequency,
                Remarks,DATEADD(MONTH,1,@Through),@OriginalEnd
            FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@Id;
            DECLARE @OperationId uniqueidentifier=NEWID(),
                @RecordId nvarchar(200)=CONVERT(nvarchar(200),@Id);
            INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
                VALUES(@OperationId,@UserId,N'FEE_DISCOUNT_CHANGE',@RecordId);
            DECLARE @New nvarchar(max)=(SELECT * FROM dbo.StudentFeeAssignments
                WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId
                FOR JSON PATH);
            EXEC dbo.WriteAuditLog @OperationId,@UserId,N'StudentFeeAssignments',@RecordId,
                N'UPDATE',@Old,@New;
            """, connection, transaction);
        command.Parameters.AddRange(new[] {
            DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@Id", id),
            DatabaseHelper.P("@Discount", discount), DatabaseHelper.P("@From", from.Date),
            DatabaseHelper.P("@Through", through.Date)
        });
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
