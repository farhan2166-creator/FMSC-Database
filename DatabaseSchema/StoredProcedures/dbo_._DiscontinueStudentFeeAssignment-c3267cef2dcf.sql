-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 53130, 'A reason is required to discontinue a fee assignment.', 1;
    IF @EffectiveTo IS NULL
        THROW 53136, 'An effective-to date is required.', 1;
    DECLARE @EnrollmentId AS INT, @AdmissionId AS INT, @FeeTypeId AS INT, @Amount AS DECIMAL (12, 2), @DiscountAmount AS DECIMAL (12, 2), @Frequency AS NVARCHAR (20), @ExistingEffectiveFrom AS DATE, @ExistingEffectiveTo AS DATE, @CancelledAt AS DATETIME2;
    SELECT @EnrollmentId = EnrollmentId,
           @AdmissionId = AdmissionId,
           @FeeTypeId = FeeTypeId,
           @Amount = Amount,
           @DiscountAmount = DiscountAmount,
           @Frequency = Frequency,
           @ExistingEffectiveFrom = EffectiveFrom,
           @ExistingEffectiveTo = EffectiveTo,
           @CancelledAt = CancelledAt
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    IF @EnrollmentId IS NULL
        THROW 53131, 'The selected student fee assignment does not exist.', 1;
    IF @CancelledAt IS NOT NULL
        THROW 53132, 'This fee assignment has been cancelled.', 1;
    IF @ExistingEffectiveTo IS NOT NULL
        THROW 53133, 'This fee assignment has already been discontinued.', 1;
    DECLARE @NormalizedEffectiveTo AS DATE = DATEFROMPARTS(YEAR(@EffectiveTo), MONTH(@EffectiveTo), 1);
    IF @ExistingEffectiveFrom IS NOT NULL
       AND @NormalizedEffectiveTo < @ExistingEffectiveFrom
        THROW 53137, 'Effective-to month cannot be earlier than this version''s own starting month.', 1;
    DECLARE @LastBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                        FROM   dbo.BillingCharges
                                        WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId);
    IF @LastBilledMonth IS NOT NULL
       AND @NormalizedEffectiveTo < @LastBilledMonth
        THROW 53134, 'Effective-to month cannot be earlier than the last month already billed for this fee.', 1;
    BEGIN TRANSACTION;
    UPDATE dbo.StudentFeeAssignments
    SET    EffectiveTo = @NormalizedEffectiveTo,
           UpdatedAt   = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND EffectiveTo IS NULL;
    IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK;
            THROW 53135, 'The fee assignment could not be discontinued (it may have just been changed by another operation).', 1;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'FEE_ASSIGNMENT_DISCONTINUE', CONVERT (NVARCHAR (200), @StudentFeeAssignmentId));
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT @StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                   @ExistingEffectiveFrom AS EffectiveFrom,
                                                   CAST (NULL AS DATE) AS EffectiveTo
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                   @ExistingEffectiveFrom AS EffectiveFrom,
                                                   @NormalizedEffectiveTo AS EffectiveTo,
                                                   @Reason AS Reason,
                                                   @EnrollmentId AS EnrollmentId,
                                                   @AdmissionId AS AdmissionId,
                                                   @FeeTypeId AS FeeTypeId
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @StudentFeeAssignmentId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT StudentFeeAssignmentId,
           EnrollmentId,
           AdmissionId,
           FeeTypeId,
           Amount,
           DiscountAmount,
           Frequency,
           EffectiveFrom,
           EffectiveTo
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
END
