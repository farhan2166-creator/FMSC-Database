-- Historical placements remain usable for verified history, without new billing.
-- Preserve all business history, payments, allocations and recovery markers.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 61 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>60
 THROW 51000,'Migration 61 requires schema 60.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentDetails
(
    @UserId int,
    @AdmissionId int
)
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.View';

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Students
        WHERE AdmissionId = @AdmissionId
    )
        THROW 51700, 'The selected student was not found.', 1;

    SELECT
        S.AdmissionId,
        S.AdmissionNumber,
        S.RegistrationNumber, S.QuickReference, S.LegacySetupPending, S.LegacyVerifiedAt,
        S.BiseEnrolmentNumber,
        S.CasteTribe,
        S.StudentName,
        S.FatherName,
        S.DateOfBirth,
        S.Gender,
        S.BFormNumber,
        S.Religion,
        S.BloodGroup,
        S.ContactNumber,
        S.AlternateContactNumber,
        S.Address,
        S.PreviousSchool,
        S.PlaceOfBirth,
        S.FatherOccupation,
        S.Disability,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL) THEN 1 ELSE 0 END) IsOnHold, S.Status,
        S.WithdrawalDate,
        S.AdmissionDate,

        S.AdmissionCampusId,
        AC.CampusName AS AdmissionCampusName,

        E.EnrollmentId,
        E.EnrollmentDate,
        E.AcademicYearId,
        AY.YearName, AY.StartDate AS PlacementYearStart, AY.EndDate AS PlacementYearEnd, E.BillingReady,
        E.ClassSectionId,
        E.UsesTransport,
        E.Status AS EnrollmentStatus,

        CCS.CampusId AS CurrentCampusId,
        CC.CampusName AS CurrentCampusName,

        C.ClassId,
        C.ClassCode,
        C.ClassName,

        SEC.SectionId,
        SEC.SectionName

    FROM dbo.Students AS S

    INNER JOIN dbo.Campuses AS AC
        ON AC.CampusId = S.AdmissionCampusId

    OUTER APPLY
    (
        SELECT TOP (1) *
        FROM dbo.Enrollments AS EE
        WHERE EE.AdmissionId = S.AdmissionId
        ORDER BY
            CASE WHEN EE.Status = N'Active' THEN 0 ELSE 1 END,
            EE.EnrollmentDate DESC,
            EE.EnrollmentId DESC
    ) AS E

    LEFT JOIN dbo.AcademicYears AS AY
        ON AY.AcademicYearId = E.AcademicYearId


    LEFT JOIN dbo.CampusClassSections AS CCS
        ON CCS.ClassSectionId = E.ClassSectionId

    LEFT JOIN dbo.Campuses AS CC
        ON CC.CampusId = CCS.CampusId

    LEFT JOIN dbo.Classes AS C
        ON C.ClassId = CCS.ClassId

    LEFT JOIN dbo.Sections AS SEC
        ON SEC.SectionId = CCS.SectionId

    WHERE S.AdmissionId = @AdmissionId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentEnrollmentsForLifecycle
    @UserId INT,
    @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Matches the permission already required by three of this page's
    -- four operations (TransferEnrollmentClassSection/Class/Campus);
    -- Re-Admit itself is gated separately by dbo.ReAdmitStudent's own
    -- Students.Administer check when it runs.
    EXEC dbo.RequirePermission
        @UserId,
        N'Enrollment.Manage';

    IF @AdmissionId <= 0
        THROW 52001, 'A valid student is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Students
        WHERE AdmissionId = @AdmissionId
    )
        THROW 52002, 'The selected student was not found.', 1;

    -- No fee-balance filtering, unlike dbo.GetStudentEnrollments - every
    -- enrollment the student has ever had is relevant here, so the page
    -- can always show a Left student's last known placement.
    SELECT
        E.EnrollmentId,
        E.AdmissionId,
        E.AcademicYearId,
        AY.YearName, AY.StartDate AS PlacementYearStart, AY.EndDate AS PlacementYearEnd,
        E.ClassSectionId,
        CCS.CampusId, CMP.CampusName, C.ClassId, SEC.SectionId,
        C.ClassName,
        SEC.SectionName,
        E.EnrollmentDate,
        E.LeavingDate, E.BillingStartDate, E.PlacementConfirmedAt, E.BillingReady,
        E.Status

    FROM dbo.Enrollments E

    INNER JOIN dbo.AcademicYears AY
        ON AY.AcademicYearId = E.AcademicYearId

    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId

    INNER JOIN dbo.Campuses CMP ON CMP.CampusId = CCS.CampusId
    INNER JOIN dbo.Classes C
        ON C.ClassId = CCS.ClassId

    INNER JOIN dbo.Sections SEC
        ON SEC.SectionId = CCS.SectionId

    WHERE E.AdmissionId = @AdmissionId

    ORDER BY
        CASE
            WHEN E.Status = N'Active' THEN 0
            ELSE 1
        END,

        E.EnrollmentId DESC;
END;
GO


-- dbo.TransferEnrollmentClassSection
-- TransferEnrollmentClassSection
CREATE OR ALTER PROCEDURE dbo.TransferEnrollmentClassSection
@UserId INT, @EnrollmentId INT, @NewClassSectionId INT, @TransferDate DATE, @Remarks NVARCHAR (500)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @EnrollmentId <= 0
        THROW 52105, 'A valid enrollment is required.', 1;
    IF @NewClassSectionId <= 0
        THROW 52106, 'A valid destination class/section is required.', 1;
    IF @TransferDate IS NULL
        THROW 52107, 'Transfer date is required.', 1;
    IF @TransferDate > CONVERT (DATE, SYSDATETIME())
        THROW 52101, 'Transfer date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @EnrollmentStartDate AS DATE;
    DECLARE @AdmissionId AS INT, @OldClassSectionId AS INT, @AcademicYearId AS INT, @OldCampusId AS INT, @OldClassId AS INT, @NewCampusId AS INT, @NewClassId AS INT;
    SELECT @EnrollmentStartDate=E.EnrollmentDate,
           @AdmissionId = E.AdmissionId,
           @OldClassSectionId = E.ClassSectionId,
           @AcademicYearId = E.AcademicYearId,
           @OldCampusId = CCS.CampusId,
           @OldClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 52102, 'The selected enrollment is not active or does not exist.', 1;
    IF @TransferDate < @EnrollmentStartDate
        THROW 59308,'Transfer date cannot be before the enrollment date.',1;
    DECLARE @PlacementYearStart date,@PlacementYearEnd date,@PlacementReady bit;
    SELECT @PlacementYearStart=Y.StartDate,@PlacementYearEnd=Y.EndDate,@PlacementReady=E.BillingReady
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId;
    IF @PlacementYearStart IS NULL OR @TransferDate<@PlacementYearStart OR @TransferDate>@PlacementYearEnd
        THROW 59640,'Transfer date must be within the recorded academic year. Promote or prepare the current placement for a transfer now.',1;
    IF @PlacementYearEnd<CONVERT(date,GETDATE()) AND NULLIF(LTRIM(RTRIM(@Remarks)),N'') IS NULL
        THROW 59641,'Enter a reason for recording this historical transfer.',1;
    SELECT @NewCampusId = CCS.CampusId,
           @NewClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @NewClassSectionId;
    IF @NewCampusId IS NULL
        THROW 52103, 'The destination class/section does not exist or is inactive.', 1;
    IF @OldClassSectionId = @NewClassSectionId
        THROW 52104, 'The destination class/section is the same as the current placement.', 1;
    IF @NewCampusId <> @OldCampusId
       OR @NewClassId <> @OldClassId
        THROW 52109, 'Transfer Section must stay within the current campus and class. Use Transfer Class or Transfer Campus instead.', 1;
    UPDATE dbo.Enrollments
    SET    ClassSectionId = @NewClassSectionId,
           UpdatedAt      = SYSDATETIME(),
           Remarks        = COALESCE (NULLIF (LTRIM(RTRIM(@Remarks)), N''), Remarks)
    WHERE  EnrollmentId = @EnrollmentId
           AND Status = N'Active';
    IF @@ROWCOUNT <> 1
        THROW 52108, 'The active enrollment could not be transferred.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'TRANSFER', @TransferDate, @OldClassSectionId, @NewClassSectionId, N'Active', N'Active', NULLIF (LTRIM(RTRIM(@Remarks)), N''), @UserId);
COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @NewClassSectionId AS NewClassSectionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.TransferEnrollmentClass
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
    DECLARE @PlacementYearStart date,@PlacementYearEnd date,@PlacementReady bit;
    SELECT @PlacementYearStart=Y.StartDate,@PlacementYearEnd=Y.EndDate,@PlacementReady=E.BillingReady
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId;
    IF @PlacementYearStart IS NULL OR @TransferDate<@PlacementYearStart OR @TransferDate>@PlacementYearEnd
        THROW 59640,'Transfer date must be within the recorded academic year. Promote or prepare the current placement for a transfer now.',1;
    IF @PlacementYearEnd<CONVERT(date,GETDATE()) AND NULLIF(LTRIM(RTRIM(@Remarks)),N'') IS NULL
        THROW 59641,'Enter a reason for recording this historical transfer.',1;
    IF @FeePolicy IS NOT NULL AND (@PlacementReady=0 OR CONVERT(date,GETDATE()) NOT BETWEEN @PlacementYearStart AND @PlacementYearEnd)
        THROW 59642,'Historical or nonbilling transfers preserve existing fees. Complete the current placement and billing setup before changing fee plans.',1;
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
GO

CREATE OR ALTER PROCEDURE dbo.TransferEnrollmentCampus
@UserId INT, @EnrollmentId INT, @NewClassSectionId INT, @TransferDate DATE, @Remarks NVARCHAR (500), @FeePolicy NVARCHAR (20)=NULL, @FeeSyncReason NVARCHAR (500)=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @EnrollmentId <= 0
        THROW 52501, 'A valid enrollment is required.', 1;
    IF @NewClassSectionId <= 0
        THROW 52502, 'A valid destination campus, class and section are required.', 1;
    IF @TransferDate IS NULL
        THROW 52503, 'Transfer date is required.', 1;
    IF @TransferDate > CONVERT (DATE, SYSDATETIME())
        THROW 52504, 'Transfer date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @EnrollmentStartDate AS DATE;
    DECLARE @AdmissionId AS INT, @OldClassSectionId AS INT, @AcademicYearId AS INT, @OldCampusId AS INT, @NewCampusId AS INT, @OldStatus AS NVARCHAR (30);
    SELECT @EnrollmentStartDate=E.EnrollmentDate,
           @AdmissionId = E.AdmissionId,
           @OldClassSectionId = E.ClassSectionId,
           @AcademicYearId = E.AcademicYearId,
           @OldStatus = E.Status,
           @OldCampusId = CCS.CampusId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 52505, 'The selected enrollment is not active or does not exist.', 1;
    IF @TransferDate < @EnrollmentStartDate
        THROW 59308,'Transfer date cannot be before the enrollment date.',1;
    DECLARE @PlacementYearStart date,@PlacementYearEnd date,@PlacementReady bit;
    SELECT @PlacementYearStart=Y.StartDate,@PlacementYearEnd=Y.EndDate,@PlacementReady=E.BillingReady
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId;
    IF @PlacementYearStart IS NULL OR @TransferDate<@PlacementYearStart OR @TransferDate>@PlacementYearEnd
        THROW 59640,'Transfer date must be within the recorded academic year. Promote or prepare the current placement for a transfer now.',1;
    IF @PlacementYearEnd<CONVERT(date,GETDATE()) AND NULLIF(LTRIM(RTRIM(@Remarks)),N'') IS NULL
        THROW 59641,'Enter a reason for recording this historical transfer.',1;
    IF @FeePolicy IS NOT NULL AND (@PlacementReady=0 OR CONVERT(date,GETDATE()) NOT BETWEEN @PlacementYearStart AND @PlacementYearEnd)
        THROW 59642,'Historical or nonbilling transfers preserve existing fees. Complete the current placement and billing setup before changing fee plans.',1;
    SELECT @NewCampusId = CCS.CampusId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @NewClassSectionId;
    IF @NewCampusId IS NULL
        THROW 52506, 'The destination class/section does not exist or is inactive.', 1;
    IF @OldCampusId = @NewCampusId
        THROW 52507, 'The destination campus is the same as the student''s current campus. Use Transfer Class or Transfer Section instead.', 1;
    IF @OldClassSectionId = @NewClassSectionId
        THROW 52508, 'The destination class/section is the same as the current placement.', 1;
    UPDATE dbo.Enrollments
    SET    ClassSectionId = @NewClassSectionId,
           UpdatedAt      = SYSDATETIME(),
           Remarks        = COALESCE (NULLIF (@Remarks, N''), Remarks)
    WHERE  EnrollmentId = @EnrollmentId
           AND Status = N'Active';
    IF @@ROWCOUNT <> 1
        THROW 52509, 'The active enrollment could not be transferred.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'TRANSFER_CAMPUS', @TransferDate, @OldClassSectionId, @NewClassSectionId, @OldStatus, N'Active', @Remarks, @UserId);

    IF @FeePolicy IS NOT NULL
        BEGIN
            EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
            DECLARE @FeeLockResult AS INT;
            DECLARE @FeeLockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @FeeLockResult = sys.sp_getapplock @Resource = @FeeLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @FeeLockResult < 0
                THROW 52510, 'This enrollment is currently being billed or updated elsewhere. Please try again.', 1;
            EXECUTE dbo.SyncTransferStudentFees_Internal @UserId = @UserId, @EnrollmentId = @EnrollmentId, @FeePolicy = @FeePolicy, @Reason = @FeeSyncReason, @EffectiveDate = @TransferDate, @FeeEffectiveMonth=@FeeEffectiveMonth, @ReplaceScheduledFees=@ReplaceScheduledFees,@PaidReductionAction=@PaidReductionAction;
        END
    
COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @NewClassSectionId AS NewClassSectionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.SyncTransferStudentFees_Internal
@UserId INT, @EnrollmentId INT, @FeePolicy NVARCHAR (20), @Reason NVARCHAR (500), @EffectiveDate DATE=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    IF @FeePolicy NOT IN (N'NEXT_MONTH', N'CURRENT_MONTH', N'CUSTOM_MONTH',N'SCHOOL_CUTOFF')
        THROW 59300, 'Invalid fee synchronization policy.', 1;
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 59301, 'A reason is required to synchronize fees after a transfer.', 1;
    DECLARE @AdmissionId AS INT, @CampusId AS INT, @ClassId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 59302, 'The selected enrollment does not exist or is not active.', 1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
       WHERE E.EnrollmentId=@EnrollmentId AND E.BillingReady=1 AND CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate)
        THROW 59642,'Historical or nonbilling transfers preserve existing fees. Complete the current placement and billing setup before changing fee plans.',1;
    -- Same-year transfer fees are limited to tuition and transport already in use.
    DECLARE @TransferFees TABLE(FeeTypeId int NOT NULL PRIMARY KEY);
    INSERT @TransferFees SELECT FT.FeeTypeId FROM dbo.FeeTypes FT
    WHERE FT.FeeTypeCode=N'TUITION' OR (FT.IsTransportLinked=1 AND FT.FeeTypeCode<>N'ADMISSION'
      AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=@EnrollmentId
        AND A.FeeTypeId=FT.FeeTypeId AND A.Frequency=N'Monthly' AND A.CancelledAt IS NULL AND A.EffectiveTo IS NULL));
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeConfigurations WITH (HOLDLOCK)
                   WHERE  CampusId = @CampusId
                          AND ClassId = @ClassId
                          AND Frequency=N'Monthly' AND FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees))
        THROW 59306, 'No destination fees are configured. Keep existing fees or complete Fee Setup first.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments AS SFA
                      INNER JOIN
                      dbo.FeeTypes AS FT
                      ON FT.FeeTypeId = SFA.FeeTypeId
               WHERE  SFA.EnrollmentId = @EnrollmentId
                      AND SFA.EffectiveTo IS NULL
                      AND SFA.CancelledAt IS NULL
                      AND FT.IsMandatory = 1
                      AND SFA.Frequency=N'Monthly' AND SFA.FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)
                      AND NOT EXISTS (SELECT 1
                                      FROM   dbo.FeeConfigurations AS FC WITH (HOLDLOCK)
                                      WHERE  FC.CampusId = @CampusId
                                             AND FC.ClassId = @ClassId
                                             AND FC.FeeTypeId = SFA.FeeTypeId AND FC.Frequency=N'Monthly'))
        THROW 59307, 'Required destination fees are missing. Keep existing fees or complete Fee Setup first.', 1;
    DECLARE @Today AS DATE = COALESCE(@EffectiveDate,CAST(GETDATE() AS DATE));
    DECLARE @CurrentMonthStart AS DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @TargetMonth AS DATE = CASE @FeePolicy WHEN N'CURRENT_MONTH' THEN @CurrentMonthStart WHEN N'CUSTOM_MONTH' THEN DATEFROMPARTS(YEAR(@FeeEffectiveMonth),MONTH(@FeeEffectiveMonth),1) ELSE DATEADD(MONTH, 1, @CurrentMonthStart) END;

    DECLARE @CutoffDate date=dbo.GetMonthlyBillingStartDate(@Today);
    DECLARE @CutoffMonth date=DATEFROMPARTS(YEAR(@CutoffDate),MONTH(@CutoffDate),1);
    IF @FeePolicy=N'SCHOOL_CUTOFF' SET @TargetMonth=@CutoffMonth;
    IF @TargetMonth<@CutoffMonth THROW 59327,'The selected month is before the School Profile cutoff permits the new class rate.',1;
    IF @TargetMonth IS NULL OR @TargetMonth<@CurrentMonthStart
        THROW 59327,'Choose an effective month on or after the transfer month.',1;
    DECLARE @InitialLockedRows int;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    IF OBJECT_ID('tempdb..#FeeSyncPlan') IS NOT NULL
        DROP TABLE #FeeSyncPlan;
    CREATE TABLE #FeeSyncPlan (
        FeeTypeId             INT             NOT NULL,
        CurrentAssignmentId   INT             NULL,
        CurrentDiscountAmount DECIMAL (12, 2) NULL,
        CurrentRemarks        NVARCHAR (500)  NULL,
        DestAmount            DECIMAL (12, 2) NULL,
        DestFrequency         NVARCHAR (20)   NULL,
        PlanAction            NVARCHAR (20)   NOT NULL
    );
    WITH CurrentOpen
    AS   (SELECT StudentFeeAssignmentId,
                 FeeTypeId,
                 Amount,
                 Frequency,
                 DiscountAmount,
                 Remarks
          FROM   dbo.StudentFeeAssignments
          WHERE  EnrollmentId = @EnrollmentId
                 AND EffectiveTo IS NULL
                 AND CancelledAt IS NULL
                 AND Frequency=N'Monthly' AND FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)),
         DestConfig
    AS   (SELECT FC.FeeConfigurationId,
                 FC.FeeTypeId,
                 FC.Amount,
                 FC.Frequency
          FROM   dbo.FeeConfigurations AS FC
                 INNER JOIN
                 dbo.FeeTypes AS FT
                 ON FT.FeeTypeId = FC.FeeTypeId
          WHERE  FC.CampusId = @CampusId
                 AND FC.ClassId = @ClassId
                 AND FC.Frequency=N'Monthly' AND FC.FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)
                 -- A transfer must not undo an explicit cancellation.
                 AND NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments CX
                                 WHERE CX.EnrollmentId=@EnrollmentId AND CX.FeeTypeId=FC.FeeTypeId
                                   AND CX.EffectiveTo IS NULL AND CX.CancelledAt IS NOT NULL)
                 AND (FT.FeeTypeCode=N'TUITION'
                      OR (FT.IsTransportLinked = 1
                          AND EXISTS (SELECT 1
                                      FROM   CurrentOpen AS CO
                                      WHERE  CO.FeeTypeId = FC.FeeTypeId))))
    INSERT #FeeSyncPlan (FeeTypeId, CurrentAssignmentId, CurrentDiscountAmount, CurrentRemarks, DestAmount, DestFrequency, PlanAction)
    SELECT COALESCE (CO.FeeTypeId, DC.FeeTypeId),
           CO.StudentFeeAssignmentId,
           CO.DiscountAmount,
           CO.Remarks,
           DC.Amount,
           DC.Frequency,
           CASE WHEN CO.StudentFeeAssignmentId IS NOT NULL
                     AND DC.FeeConfigurationId IS NOT NULL
                     AND (CO.Amount <> DC.Amount
                          OR CO.Frequency <> DC.Frequency
                          OR (@ReplaceScheduledFees=1 AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments S
                              WHERE S.EnrollmentId=@EnrollmentId AND S.FeeTypeId=CO.FeeTypeId AND S.CancelledAt IS NULL
                              AND S.StudentFeeAssignmentId<>CO.StudentFeeAssignmentId AND ISNULL(S.EffectiveTo,'99991231')>=@TargetMonth))) THEN N'CHANGE' WHEN CO.StudentFeeAssignmentId IS NULL
                                                                               AND DC.FeeConfigurationId IS NOT NULL THEN N'ADD' WHEN CO.StudentFeeAssignmentId IS NOT NULL
                                                                                                                                      AND DC.FeeConfigurationId IS NULL THEN N'DISCONTINUE' ELSE N'NONE' END
    FROM   CurrentOpen AS CO
           FULL OUTER JOIN
           DestConfig AS DC
           ON DC.FeeTypeId = CO.FeeTypeId;

    -- TRANSFER_TIMELINE_GUARD: validate all versions and bills under the caller transaction.
    IF @@TRANCOUNT=0 THROW 59321,'Transfer fee changes require a transaction.',1;
    DECLARE @LockedRows int;
    SELECT @LockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    SELECT @LockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments
        WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL
          AND EffectiveFrom>EffectiveTo AND Frequency=N'Monthly' AND FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees))
        THROW 59322,'An existing fee period ends before it starts. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A JOIN dbo.StudentFeeAssignments B
        ON A.EnrollmentId=B.EnrollmentId AND A.FeeTypeId=B.FeeTypeId
        AND A.StudentFeeAssignmentId<B.StudentFeeAssignmentId
        AND A.Frequency=N'Monthly' AND B.Frequency=N'Monthly' AND A.FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND B.CancelledAt IS NULL
        AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(B.EffectiveTo,'99991231')
        AND ISNULL(B.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231'))
        THROW 59323,'Existing fee periods overlap. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    DECLARE @CorrectionBill bigint,@DestinationAmount decimal(12,2),@Request uniqueidentifier;
    DECLARE @CorrectionResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
    DECLARE CorrectBills CURSOR LOCAL FAST_FORWARD FOR
      SELECT B.BillingChargeId,CASE WHEN P.PlanAction=N'DISCONTINUE' THEN 0 ELSE P.DestAmount END
      FROM #FeeSyncPlan P JOIN dbo.BillingCharges B ON B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=P.FeeTypeId
      WHERE P.PlanAction<>N'NONE' AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL
        AND B.Status<>N'Cancelled' AND B.BillingMonth>=@TargetMonth ORDER BY B.BillingChargeId;
    OPEN CorrectBills;FETCH NEXT FROM CorrectBills INTO @CorrectionBill,@DestinationAmount;
    WHILE @@FETCH_STATUS=0 BEGIN
      SET @Request=NEWID();
      INSERT @CorrectionResult EXEC dbo.AdjustBillingChargeRate @UserId,@CorrectionBill,@DestinationAmount,@PaidReductionAction,@Reason,@Request;
      FETCH NEXT FROM CorrectBills INTO @CorrectionBill,@DestinationAmount;
    END;
    CLOSE CorrectBills;DEALLOCATE CorrectBills;
    DECLARE @LastIssued date=(SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B JOIN #FeeSyncPlan P ON P.FeeTypeId=B.FeeTypeId
      WHERE B.EnrollmentId=@EnrollmentId AND P.PlanAction<>N'NONE' AND B.ChargeKind=N'Monthly' AND B.Status<>N'Cancelled');
    IF @LastIssued>=@TargetMonth SET @TargetMonth=DATEADD(month,1,@LastIssued);
    -- An unbilled version already starting on the permitted month needs a rate edit, not schedule replacement.
    UPDATE A SET Amount=P.DestAmount,DiscountAmount=P.CurrentDiscountAmount,UpdatedAt=SYSDATETIME()
     FROM dbo.StudentFeeAssignments A JOIN #FeeSyncPlan P ON P.CurrentAssignmentId=A.StudentFeeAssignmentId
     WHERE P.PlanAction=N'CHANGE' AND A.EffectiveFrom=@TargetMonth
       AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId);
    UPDATE P SET PlanAction=N'NONE' FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=P.CurrentAssignmentId
     WHERE P.PlanAction=N'CHANGE' AND A.EffectiveFrom=@TargetMonth AND A.Amount=P.DestAmount
       AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId);
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A
        ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=P.FeeTypeId
        WHERE @ReplaceScheduledFees=0 AND P.PlanAction<>N'NONE' AND A.CancelledAt IS NULL
        AND ((A.StudentFeeAssignmentId=P.CurrentAssignmentId AND A.EffectiveFrom>=@TargetMonth)
          OR (A.StudentFeeAssignmentId<>ISNULL(P.CurrentAssignmentId,-1)
            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth)))
        THROW 59325,'A changed fee has a scheduled rate or waiver. Keep current fees, then explicitly review that schedule in Fee Changes.',1;
    UPDATE P SET CurrentDiscountAmount=CASE WHEN A.ConcessionLabel IS NOT NULL AND A.DiscountAmount=A.Amount THEN P.DestAmount
      WHEN P.CurrentDiscountAmount>P.DestAmount THEN P.DestAmount ELSE P.CurrentDiscountAmount END
    FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=P.CurrentAssignmentId WHERE P.DestAmount IS NOT NULL;
    IF NOT EXISTS (SELECT 1
                   FROM   #FeeSyncPlan
                   WHERE  PlanAction <> N'NONE')
        RETURN;
    DECLARE @PlanFeeTypeId AS INT, @PlanAssignmentId AS INT, @PlanDiscount AS DECIMAL (12, 2), @PlanRemarks AS NVARCHAR (500), @PlanDestAmount AS DECIMAL (12, 2), @PlanDestFrequency AS NVARCHAR (20), @PlanAction AS NVARCHAR (20), @PlanFeeTypeName AS NVARCHAR (100);
    DECLARE @PlanConcession nvarchar(20);
    DECLARE @LastBilledMonth AS DATE, @NewAssignmentId AS INT, @DiscontinueEffectiveTo AS DATE;
    DECLARE plan_cursor CURSOR LOCAL FAST_FORWARD
        FOR SELECT P.FeeTypeId,
                   P.CurrentAssignmentId,
                   P.CurrentDiscountAmount,
                   P.CurrentRemarks,
                   P.DestAmount,
                   P.DestFrequency,
                   P.PlanAction,
                   FT.FeeTypeName
            FROM   #FeeSyncPlan AS P
                   INNER JOIN
                   dbo.FeeTypes AS FT
                   ON FT.FeeTypeId = P.FeeTypeId
            WHERE  P.PlanAction <> N'NONE';
    OPEN plan_cursor;
    FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
    WHILE @@FETCH_STATUS = 0
        BEGIN

                    IF @ReplaceScheduledFees=1 AND @PlanAction IN(N'CHANGE',N'ADD',N'DISCONTINUE')
                    BEGIN
                        UPDATE A SET EffectiveTo=CASE WHEN A.EffectiveFrom IS NULL OR A.EffectiveFrom<@TargetMonth
                                THEN DATEADD(MONTH,-1,@TargetMonth) ELSE COALESCE(A.EffectiveTo,A.EffectiveFrom) END,
                            CancelledAt=CASE WHEN A.EffectiveFrom>=@TargetMonth THEN SYSDATETIME() ELSE A.CancelledAt END,
                            UpdatedAt=SYSDATETIME()
                        FROM dbo.StudentFeeAssignments A
                        WHERE A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=@PlanFeeTypeId AND A.CancelledAt IS NULL
                            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth;
                    END;
            SET @PlanConcession=(SELECT ConcessionLabel FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@PlanAssignmentId);
            IF @PlanAction = N'CHANGE'
                BEGIN
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @PlanAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @TargetMonth <= @LastBilledMonth
                        BEGIN
                            CLOSE plan_cursor;
                            DEALLOCATE plan_cursor;
                            THROW 59304, 'The destination fees could not be applied from the selected month because that month has already been billed for one or more fees. Choose "next month" instead, or keep the existing fees, then adjust manually in Fee Setup if needed.', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @TargetMonth),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @PlanAssignmentId
                           AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);
                    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo, ConcessionLabel)
                    VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, @PlanDiscount, @PlanDestFrequency, @PlanRemarks, @TargetMonth, NULL, @PlanConcession);
                    SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());

                END
            ELSE
                IF @PlanAction = N'ADD'
                    BEGIN
                        INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                        VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, 0, @PlanDestFrequency, N'Added via transfer fee sync', @TargetMonth, NULL);
                        SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());

                    END
                ELSE
                    IF @PlanAction = N'DISCONTINUE'
                        BEGIN
                            SET @DiscontinueEffectiveTo = DATEADD(MONTH, -1, @TargetMonth);
                            SELECT @LastBilledMonth = MAX(BillingMonth)
                            FROM   dbo.BillingCharges
                            WHERE  StudentFeeAssignmentId = @PlanAssignmentId;
                            IF @LastBilledMonth IS NOT NULL
                               AND @DiscontinueEffectiveTo < @LastBilledMonth
                                BEGIN
                                    CLOSE plan_cursor;
                                    DEALLOCATE plan_cursor;
                                    THROW 59305, 'One or more fees that no longer apply at the destination could not be closed because a later month has already been billed for them. Please review those fees manually in Fee Setup.', 1;
                                END
                            UPDATE dbo.StudentFeeAssignments
                            SET    EffectiveTo = @DiscontinueEffectiveTo,
                                   UpdatedAt   = SYSDATETIME()
                            WHERE  StudentFeeAssignmentId = @PlanAssignmentId
                                   AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);

                        END
            FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
        END
    CLOSE plan_cursor;
    DEALLOCATE plan_cursor;

END
GO

CREATE OR ALTER PROCEDURE dbo.CloseStudentAdmission @UserId int,@AdmissionId int,@LeavingDate date,@Reason nvarchar(500),
 @Cancel bit=0,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@ReverseFutureUnpaid bit=0,@PaidReductionAction nvarchar(10)=N'Credit',@MonthlyFeePolicy nvarchar(20)=N'SchoolCutoff',@ReverseFromMonth date=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59710,'Enter the reason for leaving or cancellation.',1;
 IF @LeavingDate IS NULL OR @LeavingDate>CONVERT(date,GETDATE()) THROW 59711,'Choose a leaving date that is not in the future.',1;
 IF @MonthlyFeePolicy IS NULL OR @MonthlyFeePolicy NOT IN(N'SchoolCutoff',N'KeepIssued',N'FromMonth') THROW 59721,'Choose how existing monthly fees should be handled.',1;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Enrollment int,@Start date,@Snapshot varchar(64),@Raw nvarchar(max),@Lock int;
 SELECT @State=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Enrollment=EnrollmentId,@Start=EnrollmentDate FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending');
 IF @Enrollment IS NULL OR @State NOT IN(N'Active',N'Pending') THROW 59712,'This record has no open placement to close.',1;
 IF @Cancel=1 AND (@State<>N'Pending' OR EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND AdmissionNumber IS NOT NULL))
  THROW 59720,'An officially admitted student cannot be cancelled. Use Mark as Left for departure, or correct details and fees in their respective workspaces.',1;
 IF @State=N'Pending' AND @Cancel=0 THROW 59713,'Use Cancel Registration for a pending registration.',1;
 DECLARE @PlacementYearEnd date=(SELECT Y.EndDate FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@Enrollment);
 IF @PlacementYearEnd IS NULL OR @LeavingDate>@PlacementYearEnd
  THROW 59643,'Leaving date must be within the recorded academic year. Review historical departure dates or prepare the current placement first.',1;
 IF @LeavingDate<@Start THROW 59714,'Leaving date cannot be before the placement start.',1;
 SELECT @Lock=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment) THROW 59715,'This placement already has a leaving certificate. Review its recorded departure in Admission.',1;
 IF @Cancel=1 AND EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.AdmissionId=@AdmissionId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)
  THROW 59716,'Payments exist. Review refunds or reversals in Fee Corrections before cancellation; history must be retained. For an admitted student who attended, use Mark as Left.',1;
 IF @Cancel=1 AND (EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE AdmissionId=@AdmissionId) OR
   EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@Enrollment))
  THROW 59717,'This admission has academic or certificate history. Use Mark as Left rather than cancellation.',1;
 DECLARE @MonthlyBoundary date=dbo.GetMonthlyBillingStartDate(@LeavingDate);
 SET @MonthlyBoundary=DATEFROMPARTS(YEAR(@MonthlyBoundary),MONTH(@MonthlyBoundary),1);
 IF @MonthlyFeePolicy=N'KeepIssued' SET @MonthlyBoundary='99991201';
 IF @MonthlyFeePolicy=N'FromMonth'
 BEGIN
  IF @ReverseFromMonth IS NULL OR DAY(@ReverseFromMonth)<>1 OR @ReverseFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
   OR @ReverseFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1))
   THROW 59721,'Choose a first month to waive, from the placement start through the month after leaving.',1;
  SET @MonthlyBoundary=@ReverseFromMonth;
 END;
 DECLARE @FutureCount int,@FutureAmount decimal(12,2),@FuturePaid int,@Blocked int;
 SELECT B.BillingChargeId,B.BillingMonth,X.NetAmount,X.Balance INTO #ClosureBills
 FROM dbo.BillingCharges B CROSS APPLY(SELECT SUM(Q.NetAmount) NetAmount,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance
  FROM dbo.BillingCharges Q WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
 WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled' AND B.ChargeKind=N'Monthly'
  AND B.CorrectionOfBillingChargeId IS NULL AND B.BillingMonth>=@MonthlyBoundary AND X.NetAmount>0;
 SELECT @FutureCount=COUNT(*),@FutureAmount=COALESCE(SUM(Balance),0),
  @FuturePaid=COALESCE(SUM(CASE WHEN NetAmount>Balance THEN 1 ELSE 0 END),0) FROM #ClosureBills;
 IF @Cancel=0 AND @FutureCount>0 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @Blocked=CASE WHEN @Cancel=0 AND (@PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund')) THEN 1 ELSE 0 END;
 DECLARE @PaidReturnAmount decimal(12,2)=(SELECT COALESCE(SUM(NetAmount-Balance),0) FROM #ClosureBills);
 SET @Raw=CONCAT(@UserId,CONVERT(varchar(10),@LeavingDate,120),@Reason,CONVERT(char(1),@Cancel),@MonthlyFeePolicy,CONVERT(varchar(10),@MonthlyBoundary,120),@PaidReductionAction,(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@State CurrentStatus,@Cancel CancelAdmission,@Blocked BlockedCount,@FutureCount FutureCount,@FutureAmount FutureAmount,@FuturePaid FuturePaid,COUNT(*) ChargeCount,@PaidReturnAmount PaidReturnAmount,@MonthlyFeePolicy MonthlyFeePolicy,@MonthlyBoundary ReverseFromMonth,
   COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled';
  COMMIT;RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59719,'The record changed. Review the closure again.',1;
 IF @Blocked>0 THROW 59718,'Review charges after the leaving month. Choose reversal for unpaid future charges, or resolve paid charges in Fee Corrections first.',1;
 IF @Cancel=1
 BEGIN
  EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
  DECLARE @Charge bigint; DECLARE @Reverse TABLE(BillingChargeId bigint,AdmissionId int,FeeTypeName nvarchar(100),BillingMonth date,AmountReversed decimal(12,2),Reason nvarchar(500),Status nvarchar(20));
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled'
   AND (@Cancel=1 OR COALESCE(BillingMonth,ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1));
  OPEN C;FETCH NEXT FROM C INTO @Charge;
  WHILE @@FETCH_STATUS=0 BEGIN INSERT @Reverse EXEC dbo.ReverseBillingCharge @UserId,@Charge,@Reason;FETCH NEXT FROM C INTO @Charge;END;
  CLOSE C;DEALLOCATE C;
 END;
 IF @Cancel=0 AND @FutureCount>0
 BEGIN
  DECLARE @MonthlyCharge bigint,@CorrectionRequest uniqueidentifier;
  DECLARE @MonthlyResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
  DECLARE MonthlyCharges CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM #ClosureBills ORDER BY BillingChargeId;
  OPEN MonthlyCharges;FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  WHILE @@FETCH_STATUS=0 BEGIN
   SET @CorrectionRequest=NEWID();
   INSERT @MonthlyResult EXEC dbo.AdjustBillingChargeRate @UserId,@MonthlyCharge,0,@PaidReductionAction,@Reason,@CorrectionRequest;
   FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  END;
  CLOSE MonthlyCharges;DEALLOCATE MonthlyCharges;
 END;
 DECLARE @NewStatus nvarchar(30)=CASE WHEN @Cancel=1 THEN N'Cancelled' ELSE N'Left' END;
 UPDATE dbo.Enrollments SET Status=@NewStatus,LeavingDate=@LeavingDate,BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=@NewStatus,WithdrawalDate=@LeavingDate,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId) VALUES(@Enrollment,@AdmissionId,N'STATUS_CHANGE',@LeavingDate,@State,@NewStatus,@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,@State,@NewStatus,@Reason,@LeavingDate,@UserId);

 COMMIT;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStudentPostingDecision
 @UserId int,@AdmissionId int,@Action nvarchar(10),@EffectiveDate date,@Reason nvarchar(500),
 @RequestId uniqueidentifier,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,
 @MonthlyFeePolicy nvarchar(20)=N'SchoolCutoff',@WaiveFromMonth date=NULL,@PaidReductionAction nvarchar(10)=N'Credit',
 @BillingFromMonth date=NULL,@RestoreBills nvarchar(max)=N'[]',@ApplyAvailableCredit bit=0,
 @TargetEnrollmentId int=NULL,@AdoptLeft bit=0,@ChargeAnnualFees bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF @Action IS NULL OR @Action NOT IN(N'Hold',N'Resume') OR @RequestId IS NULL OR @RequestId='00000000-0000-0000-0000-000000000000'
  OR @EffectiveDate IS NULL OR @EffectiveDate>CONVERT(date,GETDATE()) OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 59802,'Choose a valid effective date and enter a reason.',1;
 IF ISJSON(@RestoreBills)<>1 OR LEFT(LTRIM(@RestoreBills),1)<>N'[' THROW 59803,'Review the fees selected for restoration.',1;
 DECLARE @Restore TABLE(BillingChargeId bigint PRIMARY KEY);
 INSERT @Restore SELECT TRY_CONVERT(bigint,value) FROM OPENJSON(@RestoreBills);
 DECLARE @Payload varbinary(32)=HASHBYTES('SHA2_256',CONCAT(@UserId,N'|',@AdmissionId,N'|',@Action,N'|',CONVERT(char(10),@EffectiveDate,120),N'|',@Reason,N'|',
  @MonthlyFeePolicy,N'|',CONVERT(char(10),@WaiveFromMonth,120),N'|',@PaidReductionAction,N'|',CONVERT(char(10),@BillingFromMonth,120),N'|',
  @ApplyAvailableCredit,N'|',@TargetEnrollmentId,N'|',@AdoptLeft,N'|',@ChargeAnnualFees,N'|',(SELECT BillingChargeId FROM @Restore ORDER BY BillingChargeId FOR JSON PATH)));
 DECLARE @Own bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Lock int,@Count int,@Status nvarchar(30),@Enrollment int,@Start date,@Ready bit,@Hold int,@HoldDate date,@Adopted bit,
  @Year int,@YearStart date,@YearEnd date,@Boundary date,@Snapshot varchar(64),@Raw nvarchar(max),@Result nvarchar(max);
 EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 59800,'Billing is busy. Review the decision again shortly.',1;
 IF EXISTS(SELECT 1 FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId AND PayloadHash=@Payload AND UserId=@UserId AND AdmissionId=@AdmissionId)
   THROW 59801,'This request belongs to a different decision. Check the original outcome.',1;
  SELECT ResultJson FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId; COMMIT; RETURN;
 END;
 DECLARE @CreditResource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@AdmissionId);
 EXEC @Lock=sys.sp_getapplock @Resource=@CreditResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 59800,'The student account is busy. Review again shortly.',1;
 SELECT @Status=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND LegacySetupPending=0;
 IF @Status IS NULL THROW 59804,'Complete student billing setup before using hold or resume.',1;
 SELECT @Count=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Hold=HoldId,@HoldDate=HoldDate,@Adopted=AdoptedLeft FROM dbo.StudentPostingHolds WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND ResumeDate IS NULL;
 CREATE TABLE #Changes(BillingChargeId bigint PRIMARY KEY,Gross decimal(12,2),Discount decimal(12,2),Net decimal(12,2),Balance decimal(12,2),SourceHoldId int NULL);
 CREATE TABLE #NewBills(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,Amount decimal(12,2),Discount decimal(12,2),ChargeKind nvarchar(20),PRIMARY KEY(StudentFeeAssignmentId,BillingMonth));
 IF @Action=N'Hold'
 BEGIN
  IF @Hold IS NOT NULL THROW 59805,'This student is already on hold. Use Lift Hold / Resume.',1;
  IF (@AdoptLeft=0 AND @Status<>N'Active') OR (@AdoptLeft=1 AND @Status<>N'Left')
   THROW 59806,'Review an active student, or explicitly review an existing Left record as a temporary hold.',1;
  SELECT TOP(1) @Enrollment=EnrollmentId,@Start=EnrollmentDate,@Ready=BillingReady
   FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND Status=CASE WHEN @AdoptLeft=1 THEN N'Left' ELSE N'Active' END ORDER BY EnrollmentId DESC;
  IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
      WHERE E.EnrollmentId=@Enrollment AND CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate
        AND @EffectiveDate BETWEEN E.EnrollmentDate AND Y.EndDate)
   THROW 59644,'Hold requires a placement in the current academic year. Use Placement History to correct an earlier period.',1;
  IF @Enrollment IS NULL OR @EffectiveDate<@Start OR EXISTS(SELECT 1 FROM dbo.StudentPostingHolds WHERE AdmissionId=@AdmissionId AND ResumeDate>@EffectiveDate)
   THROW 59807,'The hold date must fit the placement and follow the previous resume.',1;
  IF @MonthlyFeePolicy IS NULL OR @MonthlyFeePolicy NOT IN(N'SchoolCutoff',N'KeepIssued',N'FromMonth')
   OR @PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund') THROW 59808,'Choose how issued fees and released payments should be handled.',1;
  SET @Boundary=DATEFROMPARTS(YEAR(dbo.GetMonthlyBillingStartDate(@EffectiveDate)),MONTH(dbo.GetMonthlyBillingStartDate(@EffectiveDate)),1);
  IF @MonthlyFeePolicy=N'KeepIssued' SET @Boundary='99991201';
  IF @MonthlyFeePolicy=N'FromMonth'
  BEGIN
   IF @WaiveFromMonth IS NULL OR DAY(@WaiveFromMonth)<>1 OR @WaiveFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
    OR @WaiveFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@EffectiveDate),MONTH(@EffectiveDate),1))
    THROW 59808,'Choose a waiver month within the placement, no later than the month after the hold.',1;
   SET @Boundary=@WaiveFromMonth;
  END;
  INSERT #Changes(BillingChargeId,Gross,Discount,Net,Balance)
  SELECT B.BillingChargeId,COALESCE(C.NewGross,B.GrossAmount),COALESCE(C.NewGross,B.GrossAmount)-X.Net,X.Net,X.Balance
  FROM dbo.BillingCharges B
  OUTER APPLY(SELECT TOP(1) NewGross,NewDiscount FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
  CROSS APPLY(SELECT SUM(NetAmount) Net,SUM(dbo.GetFeeChargeBalance(BillingChargeId)) Balance FROM dbo.BillingCharges Q
   WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
  WHERE B.EnrollmentId=@Enrollment AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL AND B.Status<>N'Cancelled' AND B.BillingMonth>=@Boundary AND X.Net>0;
 END
 ELSE
 BEGIN
  IF @Hold IS NULL THROW 59809,'There is no active hold. Reload the student before continuing.',1;
  IF @EffectiveDate<@HoldDate THROW 59810,'The resume date cannot be before the hold date.',1;
  SET @Enrollment=COALESCE(NULLIF(@TargetEnrollmentId,0),(SELECT EnrollmentId FROM dbo.StudentPostingHolds WHERE HoldId=@Hold));
  SELECT @Start=E.EnrollmentDate,@Year=E.AcademicYearId,@YearStart=Y.StartDate,@YearEnd=Y.EndDate
   FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
   WHERE E.EnrollmentId=@Enrollment AND E.AdmissionId=@AdmissionId AND (E.Status=N'Active' OR (E.Status=N'Left' AND E.EnrollmentId=(SELECT EnrollmentId FROM dbo.StudentPostingHolds WHERE HoldId=@Hold))) AND E.PlacementConfirmedAt IS NOT NULL;
  IF CONVERT(date,GETDATE()) NOT BETWEEN @YearStart AND @YearEnd OR @YearStart IS NULL OR @YearEnd IS NULL
   THROW 59645,'Resume requires a placement in the current academic year. Prepare that placement first.',1;
  IF @Start IS NULL OR @EffectiveDate<@Start OR @EffectiveDate>@YearEnd OR @BillingFromMonth IS NULL OR DAY(@BillingFromMonth)<>1
   OR @BillingFromMonth<DATEFROMPARTS(YEAR(@YearStart),MONTH(@YearStart),1) OR @BillingFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
   OR @BillingFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@EffectiveDate),MONTH(@EffectiveDate),1)) OR @BillingFromMonth>@YearEnd
   THROW 59811,'Choose a confirmed placement covering the return date and an applicable first billing month. For a new academic year, prepare its placement first.',1;
  IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND Status=N'Active' AND EnrollmentId<>@Enrollment)
   THROW 59811,'Select the current active placement. A previous placement cannot replace it.',1;
  IF EXISTS(SELECT 1 FROM @Restore R WHERE NOT EXISTS(
   SELECT 1 FROM dbo.StudentPostingHoldBills HB JOIN dbo.StudentPostingHolds H ON H.HoldId=HB.HoldId
    JOIN dbo.BillingCharges B ON B.BillingChargeId=HB.BillingChargeId
   WHERE H.AdmissionId=@AdmissionId AND HB.BillingChargeId=R.BillingChargeId AND HB.RestoredRequestId IS NULL AND B.Status<>N'Cancelled'
    AND (SELECT TOP(1) RequestId FROM dbo.FeeBillCorrections WHERE BillingChargeId=R.BillingChargeId ORDER BY FeeBillCorrectionId DESC)=HB.CorrectionRequestId))
   THROW 59812,'A selected waived fee changed. Reload the waived fees and review again.',1;
  INSERT #Changes SELECT R.BillingChargeId,HB.OriginalGross,HB.OriginalDiscount,X.Net,X.Balance,HB.HoldId
   FROM @Restore R JOIN dbo.StudentPostingHoldBills HB ON HB.BillingChargeId=R.BillingChargeId
   JOIN dbo.StudentPostingHolds H ON H.HoldId=HB.HoldId AND H.AdmissionId=@AdmissionId
   CROSS APPLY(SELECT COALESCE(SUM(NetAmount),0) Net,COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Balance FROM dbo.BillingCharges Q
    WHERE (Q.BillingChargeId=R.BillingChargeId OR Q.CorrectionOfBillingChargeId=R.BillingChargeId) AND Q.Status<>N'Cancelled') X
   WHERE HB.RestoredRequestId IS NULL AND (SELECT TOP(1) RequestId FROM dbo.FeeBillCorrections WHERE BillingChargeId=R.BillingChargeId ORDER BY FeeBillCorrectionId DESC)=HB.CorrectionRequestId;
  ;WITH Months AS(SELECT @BillingFromMonth M WHERE @BillingFromMonth<=@EffectiveDate UNION ALL SELECT DATEADD(month,1,M) FROM Months WHERE DATEADD(month,1,M)<=@EffectiveDate)
  INSERT #NewBills SELECT A.StudentFeeAssignmentId,A.FeeTypeId,M.M,A.Amount,A.DiscountAmount,N'Monthly'
   FROM Months M JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@Enrollment
   WHERE A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
    AND (A.EffectiveFrom IS NULL OR M.M>=A.EffectiveFrom) AND (A.EffectiveTo IS NULL OR M.M<=A.EffectiveTo)
    AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=@AdmissionId AND H.ResumeDate IS NOT NULL AND M.M>=DATEFROMPARTS(YEAR(H.HoldDate),MONTH(H.HoldDate),1) AND M.M<H.BillingFromMonth)
    AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.FeeTypeId=A.FeeTypeId AND B.BillingMonth=M.M AND B.ChargeKind=N'Monthly')
   OPTION(MAXRECURSION 1200);
  IF @ChargeAnnualFees=1 INSERT #NewBills
   SELECT A.StudentFeeAssignmentId,A.FeeTypeId,@BillingFromMonth,A.Amount,A.DiscountAmount,N'Annual'
   FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=@Enrollment AND A.CancelledAt IS NULL AND A.Frequency=N'Yearly' AND A.Amount>A.DiscountAmount
    AND (A.EffectiveFrom IS NULL OR @BillingFromMonth>=A.EffectiveFrom) AND (A.EffectiveTo IS NULL OR @BillingFromMonth<=A.EffectiveTo)
    AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N'Annual');
 END;
 IF EXISTS(SELECT 1 FROM #NewBills GROUP BY FeeTypeId,BillingMonth,ChargeKind HAVING COUNT(*)>1)
  THROW 59815,'Resolve overlapping fee agreements before resuming billing. Nothing was saved.',1;
 IF EXISTS(SELECT 1 FROM #Changes) OR EXISTS(SELECT 1 FROM #NewBills) EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF EXISTS(SELECT 1 FROM #NewBills) EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @ApplyAvailableCredit=1 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 DECLARE @Outstanding decimal(12,2)=(SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled'),
  @Waived decimal(12,2)=CASE WHEN @Action=N'Hold' THEN (SELECT COALESCE(SUM(Balance),0) FROM #Changes) ELSE 0 END,
  @Released decimal(12,2)=CASE WHEN @Action=N'Hold' THEN (SELECT COALESCE(SUM(Net-Balance),0) FROM #Changes) ELSE 0 END,
  @Restored decimal(12,2)=CASE WHEN @Action=N'Resume' THEN (SELECT COALESCE(SUM(Gross-Discount-Net),0) FROM #Changes) ELSE 0 END,
  @New decimal(12,2)=(SELECT COALESCE(SUM(Amount-Discount),0) FROM #NewBills),
  @Credit decimal(12,2)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId),@CreditUsed decimal(12,2)=0;
 DECLARE @CreditEligible decimal(12,2)=(SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled' AND ChargeDate<=CONVERT(date,GETDATE()));
 SET @CreditEligible=@CreditEligible+@New+CASE WHEN @Action=N'Resume' THEN (SELECT COALESCE(SUM(C.Gross-C.Discount-C.Net),0) FROM #Changes C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId WHERE B.ChargeDate<=CONVERT(date,GETDATE())) ELSE 0 END;
 IF @Action=N'Resume' AND @ApplyAvailableCredit=1 SET @CreditUsed=CASE WHEN @Credit>@CreditEligible THEN @CreditEligible ELSE @Credit END;
 SET @Raw=CONCAT(CONVERT(varchar(64),@Payload,2),
  (SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentFeeAssignments WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentPostingHolds WHERE AdmissionId=@AdmissionId ORDER BY HoldId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM #Changes ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM #NewBills ORDER BY BillingMonth,FeeTypeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@Outstanding Outstanding,@Waived Waived,@Released ReleasedPaid,@Restored Restored,@New NewCharges,@Credit AvailableCredit,@CreditUsed CreditToApply,
   @Outstanding-@Waived+@Restored+@New-@CreditUsed ResultingOutstanding,
   (SELECT B.BillingMonth,F.FeeTypeName,C.Gross-C.Discount Amount,C.Balance Due FROM #Changes C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId ORDER BY B.BillingMonth,F.FeeTypeName FOR JSON PATH) ChangedFees,
   (SELECT N.BillingMonth,F.FeeTypeName,N.Amount-N.Discount Amount FROM #NewBills N JOIN dbo.FeeTypes F ON F.FeeTypeId=N.FeeTypeId ORDER BY N.BillingMonth,F.FeeTypeName FOR JSON PATH) AddedFees;
  COMMIT; RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59813,'The student or account changed. Review the decision again; nothing was saved.',1;
 IF @Action=N'Hold'
 BEGIN
  INSERT dbo.StudentPostingHolds(AdmissionId,EnrollmentId,HoldDate,Reason,CreatedBy,MonthlyFeePolicy,WaiveFromMonth,PaidReductionAction,PreviousBillingReady,AdoptedLeft)
   VALUES(@AdmissionId,@Enrollment,@EffectiveDate,@Reason,@UserId,@MonthlyFeePolicy,@Boundary,@PaidReductionAction,@Ready,@AdoptLeft);
  SET @Hold=CONVERT(int,SCOPE_IDENTITY());
 END;
 -- Restore through the existing financial correction path. Never restore old
 -- payment allocations: refunds stay refunded; credit is a separate choice.
 DECLARE @Bill bigint,@Gross decimal(12,2),@Discount decimal(12,2),@Correction uniqueidentifier,@BillHold int;
 DECLARE @Corrections TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
 DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId,Gross,Discount,SourceHoldId FROM #Changes ORDER BY BillingChargeId;
 OPEN Bills; FETCH NEXT FROM Bills INTO @Bill,@Gross,@Discount,@BillHold;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @Correction=NEWID();
  IF @Action=N'Hold'
  BEGIN
   INSERT @Corrections EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,0,@PaidReductionAction,@Reason,@Correction,@NewDiscount=0,@ApplyAvailableCredit=0;
   INSERT dbo.StudentPostingHoldBills(HoldId,BillingChargeId,OriginalGross,OriginalDiscount,CorrectionRequestId) VALUES(@Hold,@Bill,@Gross,@Discount,@Correction);
  END
  ELSE
  BEGIN
   INSERT @Corrections EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,@Gross,N'Credit',@Reason,@Correction,@NewDiscount=@Discount,@ApplyAvailableCredit=0;
   UPDATE dbo.StudentPostingHoldBills SET RestoredRequestId=@Correction WHERE HoldId=@BillHold AND BillingChargeId=@Bill;
  END;
  FETCH NEXT FROM Bills INTO @Bill,@Gross,@Discount,@BillHold;
 END;
 CLOSE Bills; DEALLOCATE Bills;
 IF @Action=N'Hold'
  UPDATE dbo.Enrollments SET BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 ELSE
 BEGIN
  UPDATE dbo.StudentPostingHolds SET ResumeDate=@EffectiveDate,BillingFromMonth=@BillingFromMonth,ResumedEnrollmentId=@Enrollment,ResumeReason=@Reason,ResumedBy=@UserId,ResumedAt=SYSDATETIME() WHERE HoldId=@Hold;
  -- Owner decision: placement start is class history, never the billing policy.
  UPDATE dbo.Enrollments SET Status=N'Active',BillingReady=1,LeavingDate=CASE WHEN Status=N'Left' THEN NULL ELSE LeavingDate END,
   BillingStartDate=CASE WHEN BillingStartDate IS NULL OR BillingStartDate>@BillingFromMonth THEN @BillingFromMonth ELSE BillingStartDate END,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
  IF @Status=N'Left' UPDATE dbo.Students SET Status=N'Active',WithdrawalDate=NULL,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
   SELECT StudentFeeAssignmentId,@Enrollment,@AdmissionId,FeeTypeId,BillingMonth,CASE WHEN ChargeKind=N'Annual' THEN @EffectiveDate ELSE BillingMonth END,CASE WHEN ChargeKind=N'Annual' THEN @EffectiveDate ELSE BillingMonth END,Amount,Discount,ChargeKind,LEFT(CONCAT(N'Reviewed resume: ',@Reason),500) FROM #NewBills;
  IF @ApplyAvailableCredit=1 EXEC dbo.ApplyStudentCredit_Internal @AdmissionId,@UserId;
 END;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId)
  VALUES(@Enrollment,@AdmissionId,CASE WHEN @Action=N'Hold' THEN N'POSTING_HOLD' ELSE N'POSTING_RESUME' END,@EffectiveDate,@Status,CASE WHEN @Action=N'Resume' AND @Status=N'Left' THEN N'Active' ELSE @Status END,@Reason,@UserId);
 IF @Action=N'Resume' AND @Status=N'Left' INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,N'Left',N'Active',@Reason,@EffectiveDate,@UserId);
 SET @Result=(SELECT @RequestId RequestId,@AdmissionId AdmissionId,@Hold HoldId,@Action Action,@Enrollment EnrollmentId,@EffectiveDate EffectiveDate,
  @Waived Waived,@Released ReleasedPaid,@Restored Restored,@New NewCharges,@CreditUsed CreditApplied,
  (SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled') Outstanding FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 INSERT dbo.StudentPostingDecisions(RequestId,AdmissionId,HoldId,Action,UserId,PayloadHash,ResultJson) VALUES(@RequestId,@AdmissionId,@Hold,@Action,@UserId,@Payload,@Result);
 COMMIT; SELECT @Result ResultJson;
 END TRY BEGIN CATCH IF @Own=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'61',SYSDATETIME(),SUSER_SNAME(),N'Bound historical lifecycle dates; preserve nonbilling fee plans; require current placements for hold and resume');
GO
