-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.StudentFeeAssignments
                   WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
                          AND CancelledAt IS NULL)
        THROW 53120, 'The selected student fee assignment does not exist.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.BillingCharges AS BC
               WHERE  BC.StudentFeeAssignmentId = @StudentFeeAssignmentId
                      AND (SELECT ISNULL(SUM(FA.Amount), 0)
                           FROM   dbo.FeePaymentAllocations AS FA
                                  INNER JOIN
                                  dbo.FeePayments AS FP
                                  ON FP.FeePaymentId = FA.FeePaymentId
                                     AND FP.Status = N'Completed'
                           WHERE  FA.BillingChargeId = BC.BillingChargeId) > 0)
        THROW 53121, 'This fee has already been paid, so it cannot be removed. Reverse the payment on its charge in Fee Collection first.', 1;
    BEGIN TRANSACTION;
    UPDATE dbo.BillingCharges
    SET    Status  = N'Cancelled',
           Remarks = ISNULL(NULLIF (Remarks, N''), N'') + N' Cancelled with its fee assignment.'
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND Status <> N'Cancelled';
    UPDATE dbo.StudentFeeAssignments
    SET    CancelledAt = SYSDATETIME(),
           UpdatedAt   = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK;
            THROW 53122, 'The student fee assignment could not be removed.', 1;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'FEE_ASSIGNMENT_REMOVE', CONVERT (NVARCHAR (200), @StudentFeeAssignmentId));
    DECLARE @CancellationValues AS NVARCHAR (MAX) = (SELECT CancelledAt
                                                     FROM   dbo.StudentFeeAssignments
                                                     WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
                                                     FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @StudentFeeAssignmentId, @Action = N'UPDATE', @OldValues = N'{"CancelledAt":null}', @NewValues = @CancellationValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
END
