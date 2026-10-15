-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF @DiscountAmount IS NULL
        SET @DiscountAmount = 0;
    IF @DiscountAmount < 0
        THROW 53101, 'Discount cannot be negative.', 1;
    DECLARE @Amount AS DECIMAL (12, 2), @EnrollmentId AS INT, @AdmissionId AS INT, @OldDiscountAmount AS DECIMAL (12, 2);
    SELECT @Amount = Amount,
           @EnrollmentId = EnrollmentId,
           @AdmissionId = AdmissionId,
           @OldDiscountAmount = DiscountAmount
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND CancelledAt IS NULL;
    IF @Amount IS NULL
        THROW 53102, 'The selected student fee assignment does not exist.', 1;
    IF @DiscountAmount > @Amount
        THROW 53103, 'Discount cannot be greater than the fee amount.', 1;
    BEGIN TRANSACTION;
    UPDATE dbo.StudentFeeAssignments
    SET    DiscountAmount = @DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND CancelledAt IS NULL;
    IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK;
            THROW 53104, 'The student fee assignment could not be updated.', 1;
        END
    UPDATE BC
    SET    DiscountAmount = CASE WHEN @DiscountAmount > BC.GrossAmount THEN BC.GrossAmount ELSE @DiscountAmount END
    FROM   dbo.BillingCharges AS BC
    WHERE  BC.StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND BC.Status <> N'Cancelled'
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeePaymentAllocations AS FPA
                                  INNER JOIN
                                  dbo.FeePayments AS FP
                                  ON FP.FeePaymentId = FPA.FeePaymentId
                           WHERE  FPA.BillingChargeId = BC.BillingChargeId
                                  AND FP.Status = N'Completed');
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'FEE_DISCOUNT_CHANGE', CONVERT (NVARCHAR (200), @StudentFeeAssignmentId));
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT @StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                   @OldDiscountAmount AS DiscountAmount
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                   @DiscountAmount AS DiscountAmount,
                                                   @EnrollmentId AS EnrollmentId,
                                                   @AdmissionId AS AdmissionId
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @StudentFeeAssignmentId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT StudentFeeAssignmentId,
           EnrollmentId,
           AdmissionId,
           Amount AS StandardAmount,
           DiscountAmount,
           Amount - DiscountAmount AS NetAmount
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
END
