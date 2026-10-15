-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54740, 'A billing month is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 54741, 'A reason is required to reverse a fee posting.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    BEGIN TRANSACTION;
    DECLARE @Reversed TABLE (
        BillingChargeId BIGINT PRIMARY KEY,
        AdmissionId     INT   );
    UPDATE BC
    SET    Status  = N'Cancelled',
           Remarks = LEFT(ISNULL(BC.Remarks + N' | ', N'') + N'Posting reversed: ' + @Reason, 500)
    OUTPUT inserted.BillingChargeId, inserted.AdmissionId INTO @Reversed (BillingChargeId, AdmissionId)
    FROM   dbo.BillingCharges AS BC
           INNER JOIN
           dbo.Enrollments AS E
           ON E.EnrollmentId = BC.EnrollmentId
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  BC.BillingMonth = @Month AND E.Status=N'Active' AND E.Status=N'Active'
           AND BC.Status <> N'Cancelled'
           AND (@CampusId = 0
                OR CCS.CampusId = @CampusId)
           AND (@ClassId = 0
                OR CCS.ClassId = @ClassId)
           AND (@SectionId = 0
                OR CCS.SectionId = @SectionId)
           AND (@AnyFeeType = 0
                OR EXISTS (SELECT 1
                           FROM   @FeeTypeIds AS F
                           WHERE  F.FeeTypeId = BC.FeeTypeId))
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeePaymentAllocations AS A
                                  INNER JOIN
                                  dbo.FeePayments AS FP
                                  ON FP.FeePaymentId = A.FeePaymentId
                                     AND FP.Status = N'Completed'
                           WHERE  A.BillingChargeId = BC.BillingChargeId
                           HAVING SUM(A.Amount) > 0);
    DECLARE @ReversedCount AS INT = @@ROWCOUNT;
    IF @ReversedCount > 0
        BEGIN
            DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
            INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
            VALUES                     (@OperationId, @UserId, N'FEE_POSTING_REVERSAL', N'BillingMonth=' + CONVERT (NVARCHAR (10), @Month, 120) + N'; FeeTypes=' + CASE WHEN @AnyFeeType = 1 THEN N'selected' ELSE N'all' END);
            DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT BC.BillingChargeId AS BillingChargeId,
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
                                                           @Reversed AS R
                                                           ON R.BillingChargeId = BC.BillingChargeId
                                                    FOR    JSON PATH);
            DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @Month AS BillingMonth,
                                                           @ReversedCount AS ChargesReversed,
                                                           N'Cancelled' AS Status,
                                                           @Reason AS Reason,
                                                           N'Uncollected posting reversed; no payment was allocated to any of these charges.' AS Note
                                                    FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BillingCharges', @RecordId = NULL, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT   BC.BillingChargeId,
             BC.AdmissionId,
             S.StudentName,
             FT.FeeTypeName,
             BC.NetAmount,
             ISNULL(PA.Collected, 0) AS CollectedAmount,
             N'This fee has already been collected. It cannot be reversed as an uncollected posting - use Fee Collection Correction/Refund/Reversal.' AS ActionRequired
    FROM     dbo.BillingCharges AS BC
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = BC.AdmissionId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = BC.EnrollmentId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId OUTER APPLY (SELECT SUM(A.Amount) AS Collected
                                                                   FROM   dbo.FeePaymentAllocations AS A
                                                                          INNER JOIN
                                                                          dbo.FeePayments AS FP
                                                                          ON FP.FeePaymentId = A.FeePaymentId
                                                                             AND FP.Status = N'Completed'
                                                                   WHERE  A.BillingChargeId = BC.BillingChargeId) AS PA
    WHERE    BC.BillingMonth = @Month AND E.Status=N'Active' AND E.Status=N'Active'
             AND BC.Status <> N'Cancelled'
             AND (@CampusId = 0
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@AnyFeeType = 0
                  OR EXISTS (SELECT 1
                             FROM   @FeeTypeIds AS F
                             WHERE  F.FeeTypeId = BC.FeeTypeId))
             AND EXISTS (SELECT 1
                         FROM   dbo.FeePaymentAllocations AS A
                                INNER JOIN
                                dbo.FeePayments AS FP
                                ON FP.FeePaymentId = A.FeePaymentId
                                   AND FP.Status = N'Completed'
                         WHERE  A.BillingChargeId = BC.BillingChargeId
                         HAVING SUM(A.Amount) > 0)
    ORDER BY S.StudentName, FT.FeeTypeName;
    SELECT @Month AS BillingMonth,
           @ReversedCount AS ChargesReversed,
           (SELECT COUNT(DISTINCT AdmissionId)
            FROM   @Reversed) AS StudentsAffected,
           (SELECT ISNULL(SUM(BC.NetAmount), 0)
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   @Reversed AS R
                   ON R.BillingChargeId = BC.BillingChargeId) AS ReversedAmount;
END
