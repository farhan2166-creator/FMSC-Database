-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.TransferEnrollmentClass
@UserId INT, @EnrollmentId INT, @NewClassSectionId INT, @TransferDate DATE, @Remarks NVARCHAR (500)=NULL, @FeePolicy NVARCHAR (20)=NULL, @FeeSyncReason NVARCHAR (500)=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @EnrollmentId <= 0
        THROW 52301, 'A valid enrollment is required.', 1;
    IF @NewClassSectionId <= 0
        THROW 52302, 'A valid destination class/section is required.', 1;
    IF @TransferDate IS NULL
        THROW 52303, 'Transfer date is required.', 1;
    IF @TransferDate > CONVERT (DATE, SYSDATETIME())
        THROW 52304, 'Transfer date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @EnrollmentStartDate AS DATE;
    DECLARE @AdmissionId AS INT, @OldClassSectionId AS INT, @AcademicYearId AS INT, @OldCampusId AS INT, @NewCampusId AS INT, @OldClassId AS INT, @NewClassId AS INT, @OldStatus AS NVARCHAR (30);
    SELECT @EnrollmentStartDate=E.EnrollmentDate,
           @AdmissionId = E.AdmissionId,
           @OldClassSectionId = E.ClassSectionId,
           @AcademicYearId = E.AcademicYearId,
           @OldStatus = E.Status,
           @OldCampusId = CCS.CampusId,
           @OldClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 52305, 'The selected enrollment is not active or does not exist.', 1;
    IF @TransferDate < @EnrollmentStartDate
        THROW 59308,'Transfer date cannot be before the enrollment date.',1;
    SELECT @NewCampusId = CCS.CampusId,
           @NewClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @NewClassSectionId;
    IF @NewCampusId IS NULL
        THROW 52306, 'The destination class/section is invalid.', 1;
    IF @OldCampusId <> @NewCampusId
        THROW 52307, 'Transfer Class cannot move a student to another campus. Use the appropriate enrollment workflow.', 1;
    IF @OldClassSectionId = @NewClassSectionId
        THROW 52308, 'The destination class/section is the same as the current placement.', 1;
    UPDATE dbo.Enrollments
    SET    ClassSectionId = @NewClassSectionId,
           UpdatedAt      = SYSDATETIME(),
           Remarks        = COALESCE (NULLIF (@Remarks, N''), Remarks)
    WHERE  EnrollmentId = @EnrollmentId
           AND Status = N'Active';
    IF @@ROWCOUNT <> 1
        THROW 52309, 'The active enrollment could not be transferred.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'TRANSFER_CLASS', @TransferDate, @OldClassSectionId, @NewClassSectionId, @OldStatus, N'Active', @Remarks, @UserId);

    IF @FeePolicy IS NOT NULL
        BEGIN
            EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
            DECLARE @FeeLockResult AS INT;
            DECLARE @FeeLockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @FeeLockResult = sys.sp_getapplock @Resource = @FeeLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @FeeLockResult < 0
                THROW 52310, 'This enrollment is currently being billed or updated elsewhere. Please try again.', 1;
            EXECUTE dbo.SyncTransferStudentFees_Internal @UserId = @UserId, @EnrollmentId = @EnrollmentId, @FeePolicy = @FeePolicy, @Reason = @FeeSyncReason, @EffectiveDate = @TransferDate, @FeeEffectiveMonth=@FeeEffectiveMonth, @ReplaceScheduledFees=@ReplaceScheduledFees,@PaidReductionAction=@PaidReductionAction;
        END

COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @NewClassSectionId AS NewClassSectionId;
END
