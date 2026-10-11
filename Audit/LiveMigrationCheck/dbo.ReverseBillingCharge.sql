
-- ReverseBillingCharge
CREATE   PROCEDURE dbo.ReverseBillingCharge
@UserId INT, @BillingChargeId BIGINT, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 53201, 'A reason is required to reverse a charge.', 1;
    DECLARE @Status AS NVARCHAR (40);
    DECLARE @FeeTypeName AS NVARCHAR (200);
    DECLARE @NetAmount AS DECIMAL (18, 2);
    DECLARE @BillingMonth AS DATE;
    DECLARE @AdmissionId AS INT;
    SELECT @Status = BC.Status,
           @FeeTypeName = FT.FeeTypeName,
           @NetAmount = BC.NetAmount,
           @BillingMonth = BC.BillingMonth,
           @AdmissionId = BC.AdmissionId
    FROM   dbo.BillingCharges AS BC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = BC.FeeTypeId
    WHERE  BC.BillingChargeId = @BillingChargeId;
    IF @Status IS NULL
        THROW 53200, 'The selected charge does not exist. Reload the list and try again.', 1;
    IF @Status = N'Cancelled'
        THROW 53202, 'This charge has already been reversed.', 1;
    IF @Status <> N'Open'
       OR (SELECT ISNULL(SUM(A.Amount), 0)
           FROM   dbo.FeePaymentAllocations AS A
                  INNER JOIN
                  dbo.FeePayments AS FP
                  ON FP.FeePaymentId = A.FeePaymentId
                     AND FP.Status = N'Completed'
           WHERE  A.BillingChargeId = @BillingChargeId) > 0
        THROW 53203, 'This fee has already been collected. It cannot be reversed as an uncollected charge - use Correction, Refund or Reversal in Student Fees.', 1;
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT BC.BillingChargeId AS BillingChargeId,
                                                   BC.StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                   BC.EnrollmentId AS EnrollmentId,
                                                   BC.AdmissionId AS AdmissionId,
                                                   BC.FeeTypeId AS FeeTypeId,
                                                   BC.BillingMonth AS BillingMonth,
                                                   BC.ChargeDate AS ChargeDate,
                                                   BC.DueDate AS DueDate,
                                                   BC.GrossAmount AS GrossAmount,
                                                   BC.DiscountAmount AS DiscountAmount,
                                                   BC.NetAmount AS NetAmount,
                                                   BC.Status AS Status,
                                                   BC.Remarks AS Remarks
                                            FROM   dbo.BillingCharges AS BC
                                            WHERE  BC.BillingChargeId = @BillingChargeId
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @CleanedReason AS NVARCHAR (500) = LTRIM(RTRIM(@Reason));
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.BillingCharges
        SET    Status  = N'Cancelled',
               Remarks = LEFT(ISNULL(BC.Remarks + N' | ', N'') + N'Charge reversed: ' + @CleanedReason, 1000)
        FROM   dbo.BillingCharges AS BC
        WHERE  BC.BillingChargeId = @BillingChargeId
               AND BC.Status = N'Open';
        IF @@ROWCOUNT = 0
            THROW 53204, 'The charge could not be reversed because it changed while this screen was open. Reload the list and try again.', 1;
        DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
        VALUES                     (@OperationId, @UserId, N'FEE_CHARGE_REVERSAL', CONVERT (NVARCHAR (50), @BillingChargeId));
        DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT N'Cancelled' AS Status,
                                                       @CleanedReason AS Reason,
                                                       N'Single uncollected charge reversed; no payment had been allocated to it.' AS Note
                                                FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
        EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BillingCharges', @RecordId = @BillingChargeId, @Action = N'REVERSAL', @OldValues = @OldValues, @NewValues = @NewValues;
        DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;
        THROW;
    END CATCH
    SELECT @BillingChargeId AS BillingChargeId,
           @AdmissionId AS AdmissionId,
           @FeeTypeName AS FeeTypeName,
           @BillingMonth AS BillingMonth,
           @NetAmount AS AmountReversed,
           @CleanedReason AS Reason,
           N'Cancelled' AS Status;
END



