-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54720, 'A billing month is required. The month is a business decision and is never derived from today''s date.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   @Selection)
        THROW 54721, 'No charges were selected to post.', 1;
    IF @AcademicYearId IS NULL OR @AcademicYearId<=0 OR NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
        THROW 54743, 'Select a valid academic year before reviewing or posting fees.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @Remarks AS NVARCHAR (500) = LEFT(ISNULL(NULLIF (LTRIM(RTRIM(@Reason)), N''), N'Manual fee posting (selected charges).'), 500);
    DECLARE @RowsRequested AS INT = (SELECT COUNT(*)
                                     FROM   @Selection);
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM @Selection S JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=S.EnrollmentId WHERE E.AcademicYearId<>@AcademicYearId)
    BEGIN
        ROLLBACK;
        THROW 54744, 'Some selected enrollments belong to another academic year. Refresh the review; nothing was posted.', 1;
    END;
    DECLARE @CreatedCharges TABLE (
        BillingChargeId BIGINT PRIMARY KEY);
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, Remarks, ChargeKind)
    OUTPUT inserted.BillingChargeId INTO @CreatedCharges (BillingChargeId)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @Month,
           @Month,
           @Month,
           SFA.Amount,
           SFA.DiscountAmount,
           @Remarks,
           N'Monthly'
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           dbo.Enrollments AS E
           ON E.EnrollmentId = SFA.EnrollmentId
    WHERE  E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
           AND E.BillingReady = 1
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly'
           AND @Month >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
           AND (SFA.EffectiveFrom IS NULL
                OR @Month >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @Month <= SFA.EffectiveTo)
           AND (E.LeavingDate IS NULL
                OR DATEDIFF(MONTH, E.LeavingDate, @Month) <= 0)
           AND NOT (E.Status = N'Promoted'
                    AND EXISTS (SELECT 1
                                FROM   dbo.Enrollments AS NX
                                WHERE  NX.AdmissionId = E.AdmissionId
                                       AND NX.EnrollmentId > E.EnrollmentId
                                       AND NX.BillingReady = 1
                                       AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                       AND @Month >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1)))
           AND EXISTS (SELECT 1
                       FROM   @Selection AS S
                       WHERE  S.EnrollmentId = SFA.EnrollmentId
                              AND S.FeeTypeId = SFA.FeeTypeId
                              AND DATEFROMPARTS(YEAR(S.BillingMonth), MONTH(S.BillingMonth), 1) = @Month)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = SFA.EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @Month
                                  AND BC.ChargeKind = N'Monthly'
                                  AND BC.Status <> N'Cancelled');
    DECLARE @Created AS INT = @@ROWCOUNT;
    DECLARE @RowsNotPosted AS INT = @RowsRequested - @Created;
    IF @Created > 0
        BEGIN
            DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
            INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
            VALUES                     (@OperationId, @UserId, N'FEE_POSTING', N'BillingMonth=' + CONVERT (NVARCHAR (10), @Month, 120) + N'; Requested=' + CONVERT (NVARCHAR (10), @RowsRequested) + N'; Created=' + CONVERT (NVARCHAR (10), @Created));
            DECLARE @SummaryJson AS NVARCHAR (MAX) = (SELECT @Month AS BillingMonth,
                                                             @RowsRequested AS RowsRequested,
                                                             @Created AS ChargesCreated,
                                                             @RowsNotPosted AS RowsNotPosted,
                                                             @Reason AS Reason,
                                                             N'Manual monthly fee posting of the selected charges. Each charge was created Open with no payment allocated.' AS Note
                                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BillingCharges', @RecordId = NULL, @Action = N'INSERT', @OldValues = NULL, @NewValues = @SummaryJson;
            DECLARE @ChargesJson AS NVARCHAR (MAX) = (SELECT BC.BillingChargeId AS BillingChargeId,
                                                             BC.StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                             BC.EnrollmentId AS EnrollmentId,
                                                             BC.AdmissionId AS AdmissionId,
                                                             BC.FeeTypeId AS FeeTypeId,
                                                             BC.BillingMonth AS BillingMonth,
                                                             BC.GrossAmount AS GrossAmount,
                                                             BC.DiscountAmount AS DiscountAmount,
                                                             BC.NetAmount AS NetAmount,
                                                             BC.Status AS Status
                                                      FROM   dbo.BillingCharges AS BC
                                                             INNER JOIN
                                                             @CreatedCharges AS C
                                                             ON C.BillingChargeId = BC.BillingChargeId
                                                      FOR    JSON PATH);
            IF @ChargesJson IS NOT NULL
                EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BillingCharges', @RecordId = NULL, @Action = N'INSERT', @OldValues = NULL, @NewValues = @ChargesJson;
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @Month AS BillingMonth,
           @RowsRequested AS RowsRequested,
           @Created AS ChargesCreated,
           @RowsNotPosted AS RowsNotPosted;
END
