-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    SET @TransactionType = LTRIM(RTRIM(@TransactionType));
    IF @TransactionType NOT IN (N'Collection', N'Correction', N'Refund', N'Reversal')
        THROW 54001, 'Invalid fee transaction type.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 54002, 'The selected student was not found.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId
                          AND AdmissionId = @AdmissionId)
        THROW 54003, 'The selected enrollment does not belong to this student.', 1;
    IF @TransactionDate IS NULL
        THROW 54005, 'The transaction date is required.', 1;
    IF @TransactionDate > CAST (GETDATE() AS DATE)
        THROW 54004, 'The transaction date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @ReceiptNumber AS NVARCHAR (40) = NULL, @ResultPaymentId AS INT = NULL, @NewBalance AS DECIMAL (12, 2) = NULL, @PaymentAmount AS DECIMAL (12, 2) = 0, @DiscountTotal AS DECIMAL (12, 2) = 0, @EnrollmentDate AS DATE = NULL;
    SELECT @EnrollmentDate = EnrollmentDate
    FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
    WHERE  EnrollmentId = @EnrollmentId
           AND AdmissionId = @AdmissionId;
    IF @EnrollmentDate IS NULL
        THROW 54006, 'The selected enrollment has no valid enrollment date.', 1;
    IF @TransactionType = N'Collection'
        BEGIN
            IF @TransactionDate < @EnrollmentDate
                THROW 54007, 'The transaction date cannot be before the enrollment date.', 1;
            SELECT @PaymentAmount = ISNULL(SUM(CASE WHEN Amount > 0 THEN Amount ELSE 0 END), 0),
                   @DiscountTotal = ISNULL(-SUM(CASE WHEN Amount < 0 THEN Amount ELSE 0 END), 0)
            FROM   @Allocations;
            IF @PaymentAmount <= 0
               AND @DiscountTotal <= 0
                THROW 54010, 'Enter a payment or a discount.', 1;
            IF ABS(@Amount - @PaymentAmount) > 0.005
                THROW 54011, 'The payment total does not match the Pay Now allocations.', 1;
            IF EXISTS (SELECT 1
                       FROM   @Allocations AS A
                              LEFT OUTER JOIN
                              dbo.BillingCharges AS BC WITH (UPDLOCK, HOLDLOCK)
                              ON BC.BillingChargeId = A.BillingChargeId CROSS APPLY (SELECT ISNULL(SUM(CASE WHEN X.Amount > 0 THEN X.Amount ELSE 0 END), 0) AS PaymentPart,
                                                                                            ISNULL(-SUM(CASE WHEN X.Amount < 0 THEN X.Amount ELSE 0 END), 0) AS DiscountPart
                                                                                     FROM   @Allocations AS X
                                                                                     WHERE  X.BillingChargeId = A.BillingChargeId) AS V
                       WHERE  BC.BillingChargeId IS NULL
                              OR BC.AdmissionId <> @AdmissionId
                              OR BC.EnrollmentId <> @EnrollmentId
                              OR BC.Status = N'Cancelled'
                              OR BC.ChargeDate IS NULL
                              OR V.DiscountPart > dbo.GetFeeChargeBalance(A.BillingChargeId)
                              OR V.PaymentPart + V.DiscountPart > dbo.GetFeeChargeBalance(A.BillingChargeId))
                THROW 54013, 'One or more selected charges is invalid: it has been cancelled, belongs to another student or enrollment, has no charge date, or its remaining balance no longer covers this allocation. Refresh and try again.', 1;
            IF @PaymentAmount > 0
               AND NULLIF (LTRIM(RTRIM(@PaymentMethod)), N'') IS NULL
                THROW 54014, 'Select a payment method.', 1;
            UPDATE BC
            SET    DiscountAmount = BC.DiscountAmount + D.DiscountAmount,
                   Remarks        = CASE WHEN NULLIF (LTRIM(RTRIM(@Remarks)), N'') IS NULL THEN BC.Remarks WHEN NULLIF (BC.Remarks, N'') IS NULL THEN @Remarks ELSE BC.Remarks + N' | ' + @Remarks END
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT   BillingChargeId,
                             -SUM(CASE WHEN Amount < 0 THEN Amount ELSE 0 END) AS DiscountAmount
                    FROM     @Allocations
                    GROUP BY BillingChargeId
                    HAVING   SUM(CASE WHEN Amount < 0 THEN Amount ELSE 0 END) < 0) AS D
                   ON D.BillingChargeId = BC.BillingChargeId;
            IF @PaymentAmount > 0
                BEGIN
                    IF @GroupReceiptNumber IS NULL
                        BEGIN
                            EXECUTE dbo.GetNextFeeReceiptNumber @ReceiptDate = @TransactionDate, @ReceiptNumber = @ReceiptNumber OUTPUT;
                        END
                    ELSE
                        BEGIN
                            SET @ReceiptNumber = @GroupReceiptNumber;
                        END
                    INSERT  dbo.FeePayments (AdmissionId, EnrollmentId, PaymentDate, ReceiptNumber, TotalAmount, PaymentMethod, ReferenceNumber, Remarks, Status, IsGroupPayment, CreatedByUserId)
                    VALUES                 (@AdmissionId, @EnrollmentId, @TransactionDate, @ReceiptNumber, @PaymentAmount, @PaymentMethod, NULLIF (LTRIM(RTRIM(@ReferenceNumber)), N''), NULLIF (LTRIM(RTRIM(@Remarks)), N''), N'Completed', CASE WHEN @GroupReceiptNumber IS NULL THEN 0 ELSE 1 END, @UserId);
                    SET @ResultPaymentId = CONVERT (INT, SCOPE_IDENTITY());
                    INSERT dbo.FeePaymentAllocations (FeePaymentId, AdmissionId, BillingChargeId, Amount, Remarks)
                    SELECT @ResultPaymentId,
                           @AdmissionId,
                           BillingChargeId,
                           Amount,
                           NULL
                    FROM   @Allocations
                    WHERE  Amount > 0;
                END
            UPDATE BC
            SET    Status = CASE WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) <= 0 THEN N'Paid' WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) < BC.NetAmount THEN N'PartiallyPaid' ELSE N'Open' END
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT DISTINCT BillingChargeId
                    FROM   @Allocations) AS A
                   ON A.BillingChargeId = BC.BillingChargeId;
            SELECT TOP (1) @NewBalance = dbo.GetFeeChargeBalance(BC.BillingChargeId)
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT   TOP (1) BillingChargeId
                    FROM     @Allocations
                    ORDER BY BillingChargeId) AS A
                   ON A.BillingChargeId = BC.BillingChargeId;
        END
    IF @TransactionType IN (N'Correction', N'Refund')
        BEGIN
            IF @FeePaymentId IS NULL
                THROW 54020, 'Select a completed payment.', 1;
            DECLARE @OriginalTotal AS DECIMAL (12, 2), @OriginalPaymentDate AS DATE, @AlreadyReduced AS DECIMAL (12, 2), @RemainingReducible AS DECIMAL (12, 2), @ReductionAmount AS DECIMAL (12, 2);
            SELECT @OriginalTotal = TotalAmount,
                   @OriginalPaymentDate = PaymentDate
            FROM   dbo.FeePayments WITH (UPDLOCK, HOLDLOCK)
            WHERE  FeePaymentId = @FeePaymentId
                   AND AdmissionId = @AdmissionId
                   AND EnrollmentId = @EnrollmentId
                   AND Status = N'Completed'
                   AND TotalAmount > 0;
            IF @OriginalTotal IS NULL
                THROW 54022, 'The selected payment is not an active completed payment.', 1;
            IF @OriginalPaymentDate IS NULL
                THROW 54026, 'The selected payment has no valid payment date.', 1;
            IF @TransactionDate < @OriginalPaymentDate
                THROW 54027, 'The transaction date cannot be before the original payment date.', 1;
            SELECT @AlreadyReduced = ISNULL(-SUM(FPA.Amount), 0)
            FROM   dbo.FeePayments AS P
                   INNER JOIN
                   dbo.FeePaymentAllocations AS FPA
                   ON FPA.FeePaymentId = P.FeePaymentId
            WHERE  P.RelatedFeePaymentId = @FeePaymentId
                   AND P.Status = N'Completed'
                   AND P.TotalAmount < 0
                   AND FPA.Amount < 0;
            SET @AlreadyReduced = ISNULL(@AlreadyReduced, 0);
            SET @RemainingReducible = @OriginalTotal - @AlreadyReduced;
            IF @TransactionType = N'Correction'
                BEGIN
                    IF @Amount < 0
                        THROW 54021, 'The corrected amount cannot be negative. To remove the entire payment, correct it to 0 or use Reversal.', 1;
                    IF @Amount >= @RemainingReducible
                        THROW 54023, 'The corrected amount must be less than the currently collected amount.', 1;
                    SET @ReductionAmount = @RemainingReducible - @Amount;
                END
            ELSE
                BEGIN
                    IF @Amount <= 0
                        THROW 54036, 'The refund amount must be greater than zero.', 1;
                    SET @ReductionAmount = @Amount;
                END
            IF @ReductionAmount <= 0
                THROW 54024, 'No reduction is required for the selected amount.', 1;
            EXECUTE dbo.GetNextFeeReceiptNumber @ReceiptDate = @TransactionDate, @ReceiptNumber = @ReceiptNumber OUTPUT;
            INSERT  dbo.FeePayments (AdmissionId, EnrollmentId, PaymentDate, ReceiptNumber, TotalAmount, PaymentMethod, ReferenceNumber, Remarks, Status, CreatedByUserId, RelatedFeePaymentId)
            VALUES                 (@AdmissionId, @EnrollmentId, @TransactionDate, @ReceiptNumber, -@ReductionAmount, CASE WHEN @TransactionType = N'Correction' THEN N'Correction' ELSE N'Refund' END, COALESCE (NULLIF (LTRIM(RTRIM(@ReferenceNumber)), N''), (SELECT ReceiptNumber
                                                                                                                                                                                                                                                                 FROM   dbo.FeePayments
                                                                                                                                                                                                                                                                 WHERE  FeePaymentId = @FeePaymentId)), NULLIF (LTRIM(RTRIM(@Remarks)), N''), N'Completed', @UserId, @FeePaymentId);
            SET @ResultPaymentId = CONVERT (INT, SCOPE_IDENTITY());
            DECLARE @RemainingReduction AS DECIMAL (12, 2) = @ReductionAmount, @ChargeId AS BIGINT, @OriginalAllocation AS DECIMAL (12, 2), @AlreadyOnCharge AS DECIMAL (12, 2), @Available AS DECIMAL (12, 2), @Take AS DECIMAL (12, 2);
            DECLARE ReductionCursor CURSOR LOCAL FAST_FORWARD
                FOR SELECT   FPA.BillingChargeId,
                             FPA.Amount,
                             ISNULL((SELECT -SUM(RA.Amount)
                                     FROM   dbo.FeePayments AS RP
                                            INNER JOIN
                                            dbo.FeePaymentAllocations AS RA
                                            ON RA.FeePaymentId = RP.FeePaymentId
                                     WHERE  RP.RelatedFeePaymentId = @FeePaymentId
                                            AND RP.Status = N'Completed'
                                            AND RP.TotalAmount < 0
                                            AND RA.BillingChargeId = FPA.BillingChargeId
                                            AND RA.Amount < 0), 0) AS AlreadyOnCharge
                    FROM     dbo.FeePaymentAllocations AS FPA
                    WHERE    FPA.FeePaymentId = @FeePaymentId
                             AND FPA.Amount > 0
                    ORDER BY FPA.FeePaymentAllocationId;
            OPEN ReductionCursor;
            FETCH NEXT FROM ReductionCursor INTO @ChargeId, @OriginalAllocation, @AlreadyOnCharge;
            WHILE @@FETCH_STATUS = 0
                  AND @RemainingReduction > 0
                BEGIN
                    SET @Available = @OriginalAllocation - ISNULL(@AlreadyOnCharge, 0);
                    IF @Available > 0
                        BEGIN
                            SET @Take = CASE WHEN @Available < @RemainingReduction THEN @Available ELSE @RemainingReduction END;
                            INSERT  dbo.FeePaymentAllocations (FeePaymentId, AdmissionId, BillingChargeId, Amount, Remarks)
                            VALUES                           (@ResultPaymentId, @AdmissionId, @ChargeId, -@Take, CASE WHEN @TransactionType = N'Correction' THEN N'Correction' ELSE N'Refund' END);
                            SET @RemainingReduction -= @Take;
                        END
                    FETCH NEXT FROM ReductionCursor INTO @ChargeId, @OriginalAllocation, @AlreadyOnCharge;
                END
            CLOSE ReductionCursor;
            DEALLOCATE ReductionCursor;
            IF @RemainingReduction > 0
                THROW 54025, 'The amount could not be fully allocated against the selected payment.', 1;
            UPDATE BC
            SET    Status  = CASE WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) <= 0 THEN N'Paid' WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) < BC.NetAmount THEN N'PartiallyPaid' ELSE N'Open' END,
                   Remarks = CASE WHEN NULLIF (LTRIM(RTRIM(@Remarks)), N'') IS NULL THEN BC.Remarks WHEN NULLIF (BC.Remarks, N'') IS NULL THEN @Remarks ELSE BC.Remarks + N' | ' + @Remarks END
            FROM   dbo.BillingCharges AS BC
            WHERE  BC.BillingChargeId IN (SELECT DISTINCT BillingChargeId
                                          FROM   dbo.FeePaymentAllocations
                                          WHERE  FeePaymentId = @ResultPaymentId);
            SELECT TOP (1) @NewBalance = dbo.GetFeeChargeBalance(BC.BillingChargeId)
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT   TOP (1) BillingChargeId
                    FROM     dbo.FeePaymentAllocations
                    WHERE    FeePaymentId = @ResultPaymentId
                    ORDER BY BillingChargeId) AS A
                   ON A.BillingChargeId = BC.BillingChargeId;
        END
    IF @TransactionType = N'Reversal'
        BEGIN
            IF @FeePaymentId IS NULL
                THROW 54030, 'Select a completed payment to reverse.', 1;
            IF EXISTS (SELECT 1
                       FROM   dbo.FeePayments
                       WHERE  RelatedFeePaymentId = @FeePaymentId
                              AND Status = N'Completed'
                              AND TotalAmount < 0)
                THROW 54031, 'This payment already has a refund or correction. Reverse it through the appropriate transaction instead.', 1;
            DECLARE @AffectedCharges TABLE (
                BillingChargeId BIGINT PRIMARY KEY);
            INSERT @AffectedCharges (BillingChargeId)
            SELECT DISTINCT BillingChargeId
            FROM   dbo.FeePaymentAllocations
            WHERE  FeePaymentId = @FeePaymentId;
            DECLARE @OriginalReversalPaymentDate AS DATE = NULL;
            SELECT @Amount = TotalAmount,
                   @OriginalReversalPaymentDate = PaymentDate
            FROM   dbo.FeePayments WITH (UPDLOCK, HOLDLOCK)
            WHERE  FeePaymentId = @FeePaymentId
                   AND AdmissionId = @AdmissionId
                   AND EnrollmentId = @EnrollmentId
                   AND Status = N'Completed'
                   AND TotalAmount > 0;
            IF @Amount IS NULL
               OR @Amount <= 0
                THROW 54032, 'The selected payment is already reversed or is not a valid completed payment.', 1;
            IF @OriginalReversalPaymentDate IS NULL
                THROW 54034, 'The selected payment has no valid payment date.', 1;
            IF @TransactionDate < @OriginalReversalPaymentDate
                THROW 54035, 'The reversal date cannot be before the original payment date.', 1;
            UPDATE dbo.FeePayments
            SET    Status = N'Reversed'
            WHERE  FeePaymentId = @FeePaymentId
                   AND AdmissionId = @AdmissionId
                   AND EnrollmentId = @EnrollmentId
                   AND Status = N'Completed';
            IF @@ROWCOUNT = 0
                THROW 54033, 'The selected payment could not be reversed.', 1;
            UPDATE BC
            SET    Status = CASE WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) <= 0 THEN N'Paid' WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) < BC.NetAmount THEN N'PartiallyPaid' ELSE N'Open' END
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   @AffectedCharges AS AC
                   ON AC.BillingChargeId = BC.BillingChargeId;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType)
    VALUES                     (@OperationId, @UserId, CASE WHEN @TransactionType = N'Collection' THEN N'PAYMENT' WHEN @TransactionType = N'Correction' THEN N'FEE_CORRECTION' WHEN @TransactionType = N'Refund' THEN N'FEE_REFUND' ELSE N'FEE_REVERSAL' END);
    DECLARE @AuditTableName AS NVARCHAR (128), @AuditRecordId AS NVARCHAR (200), @AuditAction AS NVARCHAR (100), @AuditNewValues AS NVARCHAR (MAX);
    SET @AuditTableName = N'FeePayments';
    SET @AuditRecordId = CASE WHEN @TransactionType IN (N'Collection', N'Correction', N'Refund') THEN CONVERT (NVARCHAR (200), @ResultPaymentId) ELSE CONVERT (NVARCHAR (200), @FeePaymentId) END;
    SET @AuditAction = CASE WHEN @TransactionType = N'Collection' THEN N'PAYMENT' WHEN @TransactionType = N'Correction' THEN N'UPDATE' WHEN @TransactionType = N'Refund' THEN N'REFUND' WHEN @TransactionType = N'Reversal' THEN N'REVERSAL' ELSE N'OTHER' END;
    SET @AuditNewValues = (SELECT @AdmissionId AS AdmissionId,
                                  @EnrollmentId AS EnrollmentId,
                                  @TransactionType AS TransactionType,
                                  @Amount AS Amount,
                                  @PaymentAmount AS PaymentAmount,
                                  @DiscountTotal AS DiscountAmount,
                                  @FeePaymentId AS FeePaymentId,
                                  @ResultPaymentId AS ResultPaymentId,
                                  @ReceiptNumber AS ReceiptNumber
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = @AuditTableName, @RecordId = @AuditRecordId, @Action = @AuditAction, @OldValues = NULL, @NewValues = @AuditNewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @ResultPaymentId AS FeePaymentId,
           @ReceiptNumber AS ReceiptNumber,
           @TransactionType AS TransactionType,
           @Amount AS Amount,
           @NewBalance AS Balance;
END
