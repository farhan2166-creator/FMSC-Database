-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.ChangeStudentFeeAssignmentAmount_Internal
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
    DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END,@BillingLock int;
    BEGIN TRY
    BEGIN TRANSACTION;
    EXEC @BillingLock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
    IF @BillingLock<0 THROW 54745,'Billing is running. Try again shortly.',1;
    DECLARE @ConcessionLabel nvarchar(20)=(SELECT ConcessionLabel FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@StudentFeeAssignmentId);
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
    FROM   dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
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
        SET @DiscountAmount = CASE WHEN @ConcessionLabel IS NOT NULL AND @DiscountAmount=@OldAmount THEN @NewAmount WHEN @DiscountAmount>@NewAmount THEN @NewAmount ELSE @DiscountAmount END;
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
    SET @DiscountAmount = CASE WHEN @ConcessionLabel IS NOT NULL AND @DiscountAmount=@OldAmount THEN @NewAmount WHEN @DiscountAmount>@NewAmount THEN @NewAmount ELSE @DiscountAmount END;

    UPDATE dbo.StudentFeeAssignments
    SET    EffectiveTo = @NormalizedOldEffectiveTo,
           UpdatedAt   = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND EffectiveTo IS NULL;
    IF @@ROWCOUNT = 0
        BEGIN
            THROW 53146, 'The fee assignment could not be updated (it may have just been changed by another operation).', 1;
        END
    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo, ConcessionLabel)
    VALUES                           (@EnrollmentId, @AdmissionId, @FeeTypeId, @NewAmount, @DiscountAmount, @Frequency, @Remarks, @NormalizedEffectiveFrom, NULL, @ConcessionLabel);
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
    END TRY
    BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END
