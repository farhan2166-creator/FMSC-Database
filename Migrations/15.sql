-- Student assignment discounts are prospective. Bill-specific collection discounts remain separate.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStudentFeeDiscount
 @UserId int,@StudentFeeAssignmentId int,@DiscountAmount decimal(12,2),@EffectiveFrom date=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @DiscountAmount=COALESCE(@DiscountAmount,0);
 IF @DiscountAmount<0 OR @DiscountAmount<>ROUND(@DiscountAmount,0) THROW 53101,'Enter a whole-rupee discount, 0 or more.',1;
 DECLARE @From date=DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),
  @Enrollment int,@Admission int,@Fee int,@Frequency nvarchar(30),@YearEnd date,@Lock int,@Locked int;
 IF @EffectiveFrom IS NOT NULL AND @EffectiveFrom<>@From THROW 53103,'Assignment discounts start next month. Adjust existing unpaid bills in Fee Collection.',1;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  SELECT @Enrollment=A.EnrollmentId,@Admission=A.AdmissionId,@Fee=A.FeeTypeId,@Frequency=A.Frequency,
   @YearEnd=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1)
  FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
  JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE A.StudentFeeAssignmentId=@StudentFeeAssignmentId AND A.CancelledAt IS NULL AND E.Status=N'Active';
  IF @Enrollment IS NULL THROW 53102,'Select an active student fee assignment.',1;
  IF @From>@YearEnd THROW 53103,'No future month remains in this academic year.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee;
  IF @Frequency<>N'Monthly'
   THROW 53103,'Adjust an existing one-off or annual bill in Fee Collection. Recurring assignment discounts apply to monthly fees.',1;
  IF EXISTS(SELECT 1 FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
    WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND BillingMonth>=@From AND Status<>N'Cancelled')
    THROW 53104,'Future bills already exist. Review their discounts in Fee Collection before changing the assignment.',1;
  SELECT * INTO #Crossing FROM dbo.StudentFeeAssignments
   WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND CancelledAt IS NULL
    AND (EffectiveFrom IS NULL OR EffectiveFrom<@From) AND (EffectiveTo IS NULL OR EffectiveTo>=@From);
  UPDATE A SET EffectiveTo=DATEADD(month,-1,@From),UpdatedAt=SYSDATETIME()
   FROM dbo.StudentFeeAssignments A JOIN #Crossing C ON C.StudentFeeAssignmentId=A.StudentFeeAssignmentId;
  UPDATE dbo.StudentFeeAssignments SET DiscountAmount=CASE WHEN @DiscountAmount>Amount THEN Amount ELSE @DiscountAmount END,UpdatedAt=SYSDATETIME()
   WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND CancelledAt IS NULL AND EffectiveFrom>=@From AND EffectiveFrom<=@YearEnd;
  INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo)
   SELECT EnrollmentId,AdmissionId,FeeTypeId,Amount,CASE WHEN @DiscountAmount>Amount THEN Amount ELSE @DiscountAmount END,
    Frequency,Remarks,@From,EffectiveTo FROM #Crossing;
  COMMIT;
  SELECT @StudentFeeAssignmentId StudentFeeAssignmentId,@Enrollment EnrollmentId,@Admission AdmissionId,
   @From EffectiveFrom,@YearEnd EffectiveThrough;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ChangeStudentFeeAssignmentAmount_Internal
@UserId INT, @StudentFeeAssignmentId INT, @NewAmount DECIMAL (12, 2), @EffectiveFrom DATE, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 53140, 'A reason is required to change a fee amount.', 1;
    IF @NewAmount < 0
        THROW 53141, 'Amount cannot be negative.', 1;
    IF @EffectiveFrom IS NULL
        THROW 53147, 'An effective-from date is required.', 1;
    DECLARE @EnrollmentId AS INT, @AdmissionId AS INT, @FeeTypeId AS INT, @OldAmount AS DECIMAL (12, 2), @DiscountAmount AS DECIMAL (12, 2), @Frequency AS NVARCHAR (20), @Remarks AS NVARCHAR (500), @ExistingEffectiveFrom AS DATE, @ExistingEffectiveTo AS DATE, @CancelledAt AS DATETIME2;
    SELECT @EnrollmentId = EnrollmentId,
           @AdmissionId = AdmissionId,
           @FeeTypeId = FeeTypeId,
           @OldAmount = Amount,
           @DiscountAmount = DiscountAmount,
           @Frequency = Frequency,
           @Remarks = Remarks,
           @ExistingEffectiveFrom = EffectiveFrom,
           @ExistingEffectiveTo = EffectiveTo,
           @CancelledAt = CancelledAt
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    IF @EnrollmentId IS NULL
        THROW 53142, 'The selected student fee assignment does not exist.', 1;
    IF @CancelledAt IS NOT NULL
        THROW 53143, 'This fee assignment has been cancelled.', 1;
    IF @ExistingEffectiveTo IS NOT NULL
        THROW 53144, 'This fee assignment has already been closed/superseded.', 1;
    DECLARE @NormalizedEffectiveFrom AS DATE = DATEFROMPARTS(YEAR(@EffectiveFrom), MONTH(@EffectiveFrom), 1);
    DECLARE @NormalizedOldEffectiveTo AS DATE = DATEADD(MONTH, -1, @NormalizedEffectiveFrom);

    -- UNBILLED_RATE_REPLACEMENT: no extra version or obsolete scheduled action.
    IF @ExistingEffectiveFrom IS NOT NULL AND @NormalizedEffectiveFrom <= @ExistingEffectiveFrom
       AND NOT EXISTS (SELECT 1 FROM dbo.BillingCharges WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId)
    BEGIN
        BEGIN TRANSACTION;
        -- Serialize against posting or another rate edit before checking the timeline.
        IF NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments WITH (UPDLOCK, HOLDLOCK)
                       WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId AND EffectiveTo IS NULL
                         AND CancelledAt IS NULL AND Amount = @OldAmount AND EffectiveFrom = @ExistingEffectiveFrom)
            THROW 53146, 'The fee has changed. Refresh the fees and try again.', 1;
        IF EXISTS (SELECT 1 FROM dbo.BillingCharges WITH (UPDLOCK, HOLDLOCK)
                   WHERE EnrollmentId = @EnrollmentId AND FeeTypeId = @FeeTypeId
                     AND (BillingMonth >= @NormalizedEffectiveFrom OR StudentFeeAssignmentId = @StudentFeeAssignmentId))
            THROW 53148, 'Choose a month after the last month already billed for this fee.', 1;
        IF EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments
                   WHERE EnrollmentId = @EnrollmentId AND FeeTypeId = @FeeTypeId
                     AND StudentFeeAssignmentId <> @StudentFeeAssignmentId AND CancelledAt IS NULL
                     AND (EffectiveTo IS NULL OR EffectiveTo >= @NormalizedEffectiveFrom))
            THROW 53145, 'The selected month overlaps another fee period. Choose a later month.', 1;
        SET @DiscountAmount = CASE WHEN @DiscountAmount>@NewAmount THEN @NewAmount ELSE @DiscountAmount END;
        UPDATE dbo.StudentFeeAssignments
        SET Amount = @NewAmount, DiscountAmount = @DiscountAmount, EffectiveFrom = @NormalizedEffectiveFrom, UpdatedAt = SYSDATETIME()
        WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId;
        COMMIT TRANSACTION;
        SELECT StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount,
               Frequency, EffectiveFrom, EffectiveTo
        FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId;
        RETURN;
    END;
    IF @ExistingEffectiveFrom IS NOT NULL
       AND @NormalizedEffectiveFrom <= @ExistingEffectiveFrom
        THROW 53145, 'Effective-from month must be after this version''s own starting month.', 1;
    DECLARE @LastBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                        FROM   dbo.BillingCharges
                                        WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId);
    IF @LastBilledMonth IS NOT NULL
       AND @NormalizedEffectiveFrom <= @LastBilledMonth
        THROW 53148, 'Effective-from month cannot be on or before the last month already billed for this fee.', 1;
    SET @DiscountAmount = CASE WHEN @DiscountAmount>@NewAmount THEN @NewAmount ELSE @DiscountAmount END;
    BEGIN TRANSACTION;
    UPDATE dbo.StudentFeeAssignments
    SET    EffectiveTo = @NormalizedOldEffectiveTo,
           UpdatedAt   = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND EffectiveTo IS NULL;
    IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK;
            THROW 53146, 'The fee assignment could not be updated (it may have just been changed by another operation).', 1;
        END
    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
    VALUES                           (@EnrollmentId, @AdmissionId, @FeeTypeId, @NewAmount, @DiscountAmount, @Frequency, @Remarks, @NormalizedEffectiveFrom, NULL);
    DECLARE @NewStudentFeeAssignmentId AS INT = CONVERT (INT, SCOPE_IDENTITY());
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
    WHERE  StudentFeeAssignmentId = @NewStudentFeeAssignmentId;
END

GO
-- Reference only; do not execute directly on a school database.


-- GetOutstandingCharges
CREATE OR ALTER PROCEDURE dbo.GetOutstandingCharges
@UserId INT, @AdmissionId INT, @EnrollmentId INT, @AsOfDate DATE
AS
BEGIN
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    SET NOCOUNT ON;
    IF @AsOfDate IS NULL
        SET @AsOfDate = CAST (GETDATE() AS DATE);
    SELECT   BC.BillingChargeId,
             BC.StudentFeeAssignmentId,
             BC.EnrollmentId,
             BC.AdmissionId,
             FT.FeeTypeId,
             FT.FeeTypeCode,
             FT.FeeTypeName,
             BC.BillingMonth,
             BC.ChargeKind,
             BC.Remarks,
             BC.ChargeDate,
             BC.DueDate,
             BC.GrossAmount,
             BC.DiscountAmount,
             BC.NetAmount,
             BC.NetAmount - dbo.GetFeeChargeBalance(BC.BillingChargeId) AS PaidAmount,
             dbo.GetFeeChargeBalance(BC.BillingChargeId) AS Balance,
             BC.Status
    FROM     dbo.BillingCharges AS BC
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId
    WHERE    BC.AdmissionId = @AdmissionId
             AND (@EnrollmentId=0 OR BC.EnrollmentId = @EnrollmentId)
             AND BC.Status <> N'Cancelled'
             AND dbo.GetFeeChargeBalance(BC.BillingChargeId) > 0
    ORDER BY CASE WHEN FT.FeeTypeCode=N'TUITION' THEN 0 ELSE 1 END, COALESCE(BC.BillingMonth,BC.ChargeDate), BC.BillingChargeId;
END

GO
-- Reference only; do not execute directly on a school database.

CREATE OR ALTER PROCEDURE dbo.PostEnrollmentOpeningCharges @EnrollmentId int AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @BillingStartDate AS DATE, @ChargeDate AS DATE, @Status AS NVARCHAR (30), @BillingReady AS BIT;
    SELECT @BillingStartDate = E.BillingStartDate,
           @ChargeDate = COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate),
           @Status = E.Status,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @BillingStartDate IS NULL
        THROW 59401, 'Select an enrollment with an approved billing start date.', 1;
    IF @Status NOT IN(N'Active',N'Pending')
       OR @BillingReady <> 1
        THROW 59402, 'Confirm admission before posting opening charges.', 1;
    DECLARE @OpeningMonth AS DATE = DATEFROMPARTS(YEAR(@BillingStartDate), MONTH(@BillingStartDate), 1);
    IF @@TRANCOUNT > 0
        BEGIN
            DECLARE @LockResult AS INT;
            DECLARE @LockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @LockResult = sys.sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @LockResult < 0
                THROW 59403, 'Charges for this enrollment are already being posted.', 1;
        END
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @ChargeDate,
           @ChargeDate,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Annual'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Yearly'
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.ChargeKind = N'Annual'
                                  );
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @OpeningMonth,
           @OpeningMonth,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Monthly'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly'
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @OpeningMonth
                                  AND BC.ChargeKind = N'Monthly'
                                  );

    -- Explicitly selected As Needed fees belong on a pending QR opening bill.
    -- Active-student additional posting keeps its established separate workflow.
    IF @Status=N'Pending'
        INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
            BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
        SELECT A.StudentFeeAssignmentId,A.EnrollmentId,A.AdmissionId,A.FeeTypeId,
            @OpeningMonth,@ChargeDate,@ChargeDate,A.Amount,A.DiscountAmount,N'Manual'
        FROM dbo.StudentFeeAssignments A
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'As Needed'
            AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=@OpeningMonth)
            AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=@OpeningMonth)
            AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@EnrollmentId
                AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N'Manual' AND B.Status<>N'Cancelled');
END

GO
-- Reference only; do not execute directly on a school database.


-- ProcessFeeTransaction
CREATE OR ALTER PROCEDURE dbo.ProcessFeeTransaction
@GroupReceiptNumber NVARCHAR (40)=NULL, @AdmissionId INT, @EnrollmentId INT, @TransactionType NVARCHAR (20), @Amount DECIMAL (12, 2), @TransactionDate DATE, @PaymentMethod NVARCHAR (30), @ReferenceNumber NVARCHAR (100), @Remarks NVARCHAR (500), @FeePaymentId INT, @BillingChargeId BIGINT, @Allocations dbo.FeePaymentAllocationType READONLY, @UserId INT
AS
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
    IF @TransactionType<>N'Collection' EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
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
            IF @DiscountTotal>0 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
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
SELECT @NewBalance=COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled';
COMMIT TRANSACTION;
    SELECT @ResultPaymentId AS FeePaymentId,
           @ReceiptNumber AS ReceiptNumber,
           @TransactionType AS TransactionType,
           @Amount AS Amount,
           @NewBalance AS Balance;
END

GO
CREATE OR ALTER PROCEDURE dbo.ProcessFamilyFeeTransaction
    @TransactionType NVARCHAR(20),
    @TransactionDate DATE,
    @PaymentMethod   NVARCHAR(30),
    @ReferenceNumber NVARCHAR(100),
    @Remarks         NVARCHAR(500),
    @Allocations     dbo.FamilyFeeAllocationType READONLY,
    @UserId          INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Fees.Collect';

    SET @TransactionType =
        LTRIM(RTRIM(@TransactionType));

    IF @TransactionType NOT IN
    (
        N'Collection',
        N'Correction',
        N'Refund',
        N'Reversal'
    )
        THROW 59217, 'Invalid fee transaction type.', 1;


    /* ========================================================================
       VALIDATE THE WHOLE FAMILY BEFORE ANYTHING IS WRITTEN

       Every check below covers the entire batch at once. Validating up front is
       not what makes the operation atomic - the transaction is - but it means a
       family with a bad line is refused with a sentence that names the problem
       instead of failing after some children have already been processed.
       ======================================================================== */

    IF NOT EXISTS
    (
        SELECT 1
        FROM @Allocations
    )
        THROW 59218, 'No fee allocations were supplied, so there is nothing to collect.', 1;


    /* A row with no student, no enrollment or no charge cannot be attributed to
       anybody. This catches a malformed batch rather than a data problem. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations
        WHERE AdmissionId <= 0
           OR EnrollmentId <= 0
           OR BillingChargeId <= 0
    )
        THROW 59219, 'A family collection line is missing its student, enrollment or charge. Nothing has been posted.', 1;


    /* Every student. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations A
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Students S
            WHERE S.AdmissionId = A.AdmissionId
        )
    )
        THROW 59220, 'One of the students in this family collection no longer exists. Nothing has been posted.', 1;


    /* Every enrollment, and that it belongs to the student it is filed under.
       A mismatch here would post a charge against the wrong child. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations A
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Enrollments E
            WHERE E.EnrollmentId = A.EnrollmentId
              AND E.AdmissionId = A.AdmissionId
        )
    )
        THROW 59221, 'One of the enrollments in this family collection does not belong to its student. Nothing has been posted.', 1;


    /* Every charge, and that it belongs to that student's enrollment. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations A
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.BillingCharges BC
            WHERE BC.BillingChargeId = A.BillingChargeId
              AND BC.AdmissionId = A.AdmissionId
              AND BC.EnrollmentId = A.EnrollmentId
        )
    )
        THROW 59222, 'One of the fee charges in this family collection does not belong to its student. Nothing has been posted.', 1;


    /* ========================================================================
       GROUP THE BATCH BY STUDENT, IN A FIXED ORDER

       ROW_NUMBER rather than IDENTITY so the order is the ORDER BY and not an
       implementation detail: the sequence decides the order in which the engine
       takes its locks, which is what keeps two concurrent family collections
       from deadlocking on each other.
       ======================================================================== */

    DECLARE @Groups TABLE
    (
        Seq          INT NOT NULL PRIMARY KEY,
        AdmissionId  INT NOT NULL,
        EnrollmentId INT NOT NULL
    );

    INSERT INTO @Groups (Seq, AdmissionId, EnrollmentId)
    SELECT
        ROW_NUMBER() OVER (ORDER BY AdmissionId, EnrollmentId),
        AdmissionId,
        EnrollmentId
    FROM
    (
        SELECT DISTINCT
            AdmissionId,
            EnrollmentId
        FROM @Allocations
    ) D;


    /* dbo.ProcessFeeTransaction requires @Amount to equal the sum of that call's
       POSITIVE allocations and rejects a non-positive amount (54011). A student
       whose lines are discounts only would fail inside the loop, after the
       transaction had opened; refusing it here says which situation it is. */
    DECLARE @Receipts TABLE
    (
        AdmissionId   INT           NOT NULL,
        EnrollmentId  INT           NOT NULL,
        FeePaymentId  INT           NULL,
        ReceiptNumber NVARCHAR(40)  NULL,
        Amount        DECIMAL(12,2) NULL,
        Balance       DECIMAL(12,2) NULL
    );

    /* dbo.ProcessFeeTransaction returns exactly these five columns. The names
       and types are declared to match it, because INSERT ... EXEC matches a
       result set to its target by position and data type. */
    DECLARE @OneResult TABLE
    (
        FeePaymentId    INT,
        ReceiptNumber   NVARCHAR(40),
        TransactionType NVARCHAR(20),
        Amount          DECIMAL(12,2),
        Balance         DECIMAL(12,2)
    );

    DECLARE @One dbo.FeePaymentAllocationType;

    DECLARE @Seq INT = 1;
    DECLARE @GroupCount INT =
        (SELECT COUNT(*) FROM @Groups);

    DECLARE @AdmissionId INT;
    DECLARE @EnrollmentId INT;
    DECLARE @StudentAmount DECIMAL(12,2);
    DECLARE @Processed INT = 0;

    /* The engine's own nullable inputs. Assigned to variables rather than
       written as NULL in the EXEC, because EXEC parameter values must be
       constants or variables. */
    DECLARE @NoPaymentId INT = NULL;
    DECLARE @NoChargeId BIGINT = NULL;


    /* ONE transaction for the whole family. See the header: this is the only
       boundary that writes, and XACT_ABORT ON in both procedures means any
       failure inside the engine rolls all of it back. */
    BEGIN TRANSACTION;

    /* FIX 70b: ONE receipt number for the whole collection, taken here so a
       family collection does not consume one number per child and leave gaps in
       the series. Inside the transaction, so a rollback gives the number back. */
    DECLARE @FamilyReceiptNumber NVARCHAR(40);

    IF EXISTS(SELECT 1 FROM @Allocations WHERE Amount>0)
    EXEC dbo.GetNextFeeReceiptNumber
        @ReceiptDate = @TransactionDate,
        @ReceiptNumber = @FamilyReceiptNumber OUTPUT;

    WHILE @Seq <= @GroupCount
    BEGIN
        SELECT
            @AdmissionId = AdmissionId,
            @EnrollmentId = EnrollmentId
        FROM @Groups
        WHERE Seq = @Seq;

        /* One child's allocations, in the shape the engine takes. */
        DELETE FROM @One;

        INSERT INTO @One (BillingChargeId, Amount)
        SELECT
            BillingChargeId,
            Amount
        FROM @Allocations
        WHERE AdmissionId = @AdmissionId
          AND EnrollmentId = @EnrollmentId;

        SELECT
            @StudentAmount = ISNULL(SUM(Amount),0)
        FROM @One
        WHERE Amount > 0;

        DELETE FROM @OneResult;

        INSERT INTO @OneResult
        (
            FeePaymentId,
            ReceiptNumber,
            TransactionType,
            Amount,
            Balance
        )
        EXEC dbo.ProcessFeeTransaction
            @AdmissionId = @AdmissionId,
            @EnrollmentId = @EnrollmentId,
            @TransactionType = @TransactionType,
            @Amount = @StudentAmount,
            @TransactionDate = @TransactionDate,
            @PaymentMethod = @PaymentMethod,
            @ReferenceNumber = @ReferenceNumber,
            @Remarks = @Remarks,
            @GroupReceiptNumber = @FamilyReceiptNumber,
            @FeePaymentId = @NoPaymentId,
            @BillingChargeId = @NoChargeId,
            @Allocations = @One,
            @UserId = @UserId;

        INSERT INTO @Receipts
        (
            AdmissionId,
            EnrollmentId,
            FeePaymentId,
            ReceiptNumber,
            Amount,
            Balance
        )
        SELECT
            @AdmissionId,
            @EnrollmentId,
            FeePaymentId,
            ReceiptNumber,
            Amount,
            Balance
        FROM @OneResult;

        SET @Processed = @Processed + 1;
        SET @Seq = @Seq + 1;
    END;


    /* ========================================================================
       PROVE THE FAMILY IS WHOLE BEFORE COMMITTING

       These four run inside the transaction, so a collection that did not
       produce one payment and one receipt per child rolls itself back instead of
       committing something half-formed. They are the difference between "the
       loop finished" and "the family was collected".
       ======================================================================== */

    IF @Processed <> @GroupCount
        THROW 59224, 'Not every student in the family collection produced a payment, so the whole collection has been rolled back.', 1;

    IF (SELECT COUNT(*) FROM @Receipts) <> @GroupCount
        THROW 59225, 'Not every student in the family collection produced a payment row, so the whole collection has been rolled back.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @Receipts
        WHERE Amount>0 AND (ReceiptNumber IS NULL
           OR LTRIM(RTRIM(ReceiptNumber)) = '')
    )
        THROW 59226, 'A student in the family collection received no receipt number, so the whole collection has been rolled back.', 1;

    /* FIX 70b/70: a family Collection is issued ONE receipt number that every
       child's payment shares (ProcessFeeTransaction stamps @GroupReceiptNumber
       and sets IsGroupPayment = 1; UQ_FeePayments_Receipt_Single is filtered to
       IsGroupPayment = 0 for exactly this reason; GetFeeReceiptGroup prints the
       whole family from that one number).

       This check used to demand the opposite - that no two children share a
       number - so every family of two or more children was rolled back with
       59227. What it must prove now is that every child's payment carries the
       family's number and nothing else. */
    IF @TransactionType = N'Collection'
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM @Receipts
            WHERE Amount>0 AND ReceiptNumber <> @FamilyReceiptNumber
        )
            THROW 59227, 'A student in the family collection was not given the family receipt number, so the whole collection has been rolled back.', 1;
    END
    ELSE
    BEGIN
        /* Any other type keeps its own number per payment (ProcessFeeTransaction
           allocates them itself), so there the numbers must all differ. */
        IF EXISTS
        (
            SELECT ReceiptNumber
            FROM @Receipts
            GROUP BY ReceiptNumber
            HAVING COUNT(*) > 1
        )
            THROW 59227, 'Two students in the family collection were given the same receipt number, so the whole collection has been rolled back.', 1;
    END;


    IF EXISTS
    (
        SELECT 1
        FROM @Receipts
        WHERE Amount>0 AND FeePaymentId IS NULL
    )
        THROW 59228, 'A student in the family collection has no payment record, so the whole collection has been rolled back.', 1;


    /* The one commit that writes. Everything above is provisional until here. */
    COMMIT TRANSACTION;


    /* ========================================================================
       RETURN RESULT - one row per child, in a fixed order, so the caller can
       report each child's own receipt number.
       ======================================================================== */

    SELECT
        AdmissionId,
        EnrollmentId,
        FeePaymentId,
        ReceiptNumber,
        Amount,
        Balance
    FROM @Receipts
    ORDER BY AdmissionId;
END;

GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'15')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'15',SYSDATETIME(),SUSER_SNAME(),N'Prospective assignment discounts; fee reductions cap discounts to fee amount.');
GO
