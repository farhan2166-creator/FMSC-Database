-- Owner-authorized remaining generic audit retirement, 28 October 2026.
-- Preserve existing audit rows/readers, operational history, preview hashes and recovery markers.
-- Run with sqlcmd -b under the installer's outer transaction. No data cleanup is performed.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 59 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>58
 THROW 51000,'Migration 59 requires schema 58.',1;
GO

-- Optional Communication-module role adapter. Do not create the optional comm
-- schema on databases without the add-on; its reviewed installer creates the
-- module and this procedure later when explicitly installed.
IF SCHEMA_ID(N'comm') IS NOT NULL
 EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE comm.SaveRolePermissions @UserId int,@RoleId int,@PermissionIdsJson nvarchar(max) AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N''System.Roles.Manage'',N''System.Administration'';
 IF ISNULL(ISJSON(@PermissionIdsJson),0)<>1 OR LEFT(LTRIM(@PermissionIdsJson),1)<>N''['' THROW 51001,''Select a valid permission list.'',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@PermissionIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,value) IS NULL OR TRY_CONVERT(int,value)<=0) THROW 51001,''Permission IDs must be positive integers.'',1;
 DECLARE @Selected TABLE(PermissionId int NOT NULL PRIMARY KEY);
 INSERT @Selected SELECT DISTINCT CONVERT(int,value) FROM OPENJSON(@PermissionIdsJson);
 IF EXISTS(SELECT 1 FROM @Selected S LEFT JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionId IS NULL) THROW 51001,''Unknown permission selection.'',1;
 DECLARE @Optional TABLE(PermissionId int NOT NULL PRIMARY KEY,Code nvarchar(100));
 INSERT @Optional SELECT P.PermissionId,P.PermissionCode FROM @Selected S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode IN(N''Communication.View'',N''Communication.Compose'',N''Communication.Send'',N''Communication.Bulk'',N''Communication.Approve'',N''Communication.Configure'',N''Communication.History'');
 IF EXISTS(SELECT 1 FROM @Optional WHERE Code<>N''Communication.View'') AND NOT EXISTS(SELECT 1 FROM @Optional WHERE Code=N''Communication.View'') THROW 51001,''Communication actions require Communication.View.'',1;
 IF EXISTS(SELECT 1 FROM @Optional WHERE Code IN(N''Communication.Send'',N''Communication.Bulk'')) AND NOT EXISTS(SELECT 1 FROM @Optional WHERE Code=N''Communication.Compose'') THROW 51001,''Sending and bulk composition require Communication.Compose.'',1;
 IF EXISTS(SELECT 1 FROM @Optional WHERE Code=N''Communication.Approve'') AND NOT EXISTS(SELECT 1 FROM @Optional WHERE Code=N''Communication.History'') THROW 51001,''Approvers need Communication.History to review campaigns.'',1;
 DECLARE @Base nvarchar(max)=N''[''+ISNULL((SELECT STRING_AGG(CONVERT(nvarchar(max),S.PermissionId),N'','') WITHIN GROUP(ORDER BY S.PermissionId) FROM @Selected S WHERE NOT EXISTS(SELECT 1 FROM @Optional O WHERE O.PermissionId=S.PermissionId)),N'''')+N'']'';
 BEGIN TRANSACTION;
 DECLARE @RoleName nvarchar(50);SELECT @RoleName=RoleName FROM dbo.Roles WITH(UPDLOCK,HOLDLOCK) WHERE RoleId=@RoleId;
 IF ISNULL(@RoleName,N'''')<>N''Accountant'' THROW 51001,''Only Accountant permissions are editable; Administrator retains full authority.'',1;
 -- Preserve every baseline permission validation, protected-role rule.
 DECLARE @Saved TABLE(RoleId int);INSERT @Saved EXEC dbo.SaveRolePermissions @UserId,@RoleId,@Base;
 INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @RoleId,PermissionId FROM @Optional;
 INSERT comm.Audit(Actor,Operation,Detail) VALUES(@UserId,''CommunicationGrantsSaved'',N''Explicit role edit; original base permission procedure and protected-role checks retained'');
 COMMIT;
 SELECT @RoleId RoleId;
END;';
GO

-- dbo.AddStudentFeeAssignment
CREATE OR ALTER PROCEDURE dbo.AddStudentFeeAssignment @UserId int,@EnrollmentId int,@FeeTypeId int AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId)
        THROW 53110, 'The selected enrollment does not exist.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId
                          AND (BillingReady = 1 OR PlacementConfirmedAt IS NULL))
        THROW 53114, 'Confirm return admission before adding fees.', 1;
    DECLARE @CampusId AS INT, @ClassId AS INT, @AdmissionId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId;
    DECLARE @FmscCutoffDay AS INT;
    SELECT TOP (1) @FmscCutoffDay = FeeProrationCutoffDay
    FROM   dbo.SchoolProfile;
    DECLARE @FmscOpeningMonth AS DATE = CASE WHEN @FmscCutoffDay BETWEEN 1 AND 28
                                                  AND DAY(GETDATE()) >= @FmscCutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) ELSE DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1) END;
    DECLARE @BillingYearStart date,@BillingYearEnd date,@BillingEnrollmentStart date;
    SELECT @BillingYearStart=Y.StartDate,@BillingYearEnd=Y.EndDate,@BillingEnrollmentStart=E.EnrollmentDate
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId AND E.Status IN(N'Active',N'Pending');
    IF @BillingYearStart IS NULL THROW 59630,'Correct or select an active enrollment in Admission before adding fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NULL OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1)))
        SELECT @FmscOpeningMonth=DATEFROMPARTS(YEAR(COALESCE(BillingStartDate,EnrollmentDate)),MONTH(COALESCE(BillingStartDate,EnrollmentDate)),1)
        FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
    IF @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingYearStart),MONTH(@BillingYearStart),1)
       OR (@BillingYearEnd IS NOT NULL AND @FmscOpeningMonth>DATEFROMPARTS(YEAR(@BillingYearEnd),MONTH(@BillingYearEnd),1))
       OR @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingEnrollmentStart),MONTH(@BillingEnrollmentStart),1)
       THROW 59631,'The opening fee month is outside this enrollment. Correct its placement in Admission; use reviewed historical posting for an old year.',1;
    BEGIN TRANSACTION;
    -- FMSC_ACKNOWLEDGED_ADMISSION_FEE_GUARD
    IF EXISTS(
        SELECT 1 FROM dbo.Enrollments E
        JOIN dbo.StudentAdmissionFeeAcknowledgements A WITH(UPDLOCK,HOLDLOCK)
            ON A.AdmissionId=E.AdmissionId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=@FeeTypeId
        WHERE E.EnrollmentId=@EnrollmentId AND F.FeeTypeCode=N'ADMISSION')
        THROW 59971, 'This student''s admission fee is recorded as already paid before the system. It cannot be charged again.', 1;

    DECLARE @StudentFeeAssignmentId AS INT;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments
               WHERE  EnrollmentId = @EnrollmentId
                      AND FeeTypeId = @FeeTypeId
                      AND EffectiveTo IS NULL)
        BEGIN
            DECLARE @ExistingAssignmentId AS INT;
            SELECT @ExistingAssignmentId = StudentFeeAssignmentId
            FROM   dbo.StudentFeeAssignments
            WHERE  EnrollmentId = @EnrollmentId
                   AND FeeTypeId = @FeeTypeId
                   AND EffectiveTo IS NULL;
            DECLARE @HasBillingHistory AS BIT = CASE WHEN EXISTS (SELECT 1
                                                                  FROM   dbo.BillingCharges
                                                                  WHERE  StudentFeeAssignmentId = @ExistingAssignmentId) THEN 1 ELSE 0 END;
            -- A pending registration may restore an explicitly removed, unpaid
            -- opening fee in the same month. Preserve cancelled charge history.
            IF @HasBillingHistory = 0 OR (
                EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
                    WHERE E.EnrollmentId=@EnrollmentId AND E.Status=N'Pending' AND S.Status=N'Pending')
                AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@ExistingAssignmentId AND CancelledAt IS NOT NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B JOIN dbo.FeePaymentAllocations A ON A.BillingChargeId=B.BillingChargeId
                    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
                    WHERE B.StudentFeeAssignmentId=@ExistingAssignmentId AND P.Status=N'Completed' AND A.Amount>0))
                BEGIN
                    UPDATE SFA
                    SET    SFA.CancelledAt    = NULL,
                           SFA.Amount         = FC.Amount,
                           SFA.Frequency      = FC.Frequency,
                           SFA.DiscountAmount = 0,
                           SFA.UpdatedAt      = SYSDATETIME()
                    FROM   dbo.StudentFeeAssignments AS SFA
                           INNER JOIN
                           dbo.FeeConfigurations AS FC
                           ON FC.FeeTypeId = SFA.FeeTypeId
                              AND FC.CampusId = @CampusId
                              AND FC.ClassId = @ClassId
                    WHERE  SFA.StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, 'This fee is not configured for the student''s campus/class.', 1;
                        END
                    SET @StudentFeeAssignmentId = @ExistingAssignmentId;
                END
            ELSE
                BEGIN
                    DECLARE @ReactivateEffectiveFrom AS DATE = @FmscOpeningMonth;
                    DECLARE @LastBilledMonth AS DATE;
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @ReactivateEffectiveFrom <= @LastBilledMonth
                        BEGIN
                            ROLLBACK;
                            THROW 53113, 'This fee cannot be reactivated starting this month because a later month has already been billed under the previous version. Please review Fee Setup.', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @ReactivateEffectiveFrom),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId
                           AND EffectiveTo IS NULL;
                    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                    SELECT @EnrollmentId,
                           @AdmissionId,
                           FC.FeeTypeId,
                           FC.Amount,
                           0,
                           FC.Frequency,
                           N'Reactivated - new version starting current month',
                           @ReactivateEffectiveFrom,
                           NULL
                    FROM   dbo.FeeConfigurations AS FC
                    WHERE  FC.FeeTypeId = @FeeTypeId
                           AND FC.CampusId = @CampusId
                           AND FC.ClassId = @ClassId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, 'This fee is not configured for the student''s campus/class.', 1;
                        END
                    SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
                END
        END
    ELSE
        BEGIN
            DECLARE @EffectiveFromForNewRow AS DATE = @FmscOpeningMonth;
            INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
            SELECT @EnrollmentId,
                   @AdmissionId,
                   FC.FeeTypeId,
                   FC.Amount,
                   0,
                   FC.Frequency,
                   N'Added from Student Admission',
                   @EffectiveFromForNewRow,
                   NULL
            FROM   dbo.FeeConfigurations AS FC
            WHERE  FC.FeeTypeId = @FeeTypeId
                   AND FC.CampusId = @CampusId
                   AND FC.ClassId = @ClassId;
            IF @@ROWCOUNT = 0
                BEGIN
                    ROLLBACK;
                    THROW 53112, 'This fee is not configured for the student''s campus/class.', 1;
                END
            SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
        END
    DECLARE @FmscFrequency AS NVARCHAR (30), @FmscAmount AS DECIMAL (18, 2), @FmscDiscount AS DECIMAL (18, 2), @FmscFeeTypeId AS INT, @FmscEnrollmentId AS INT, @FmscAdmissionId AS INT, @FmscStartMonth AS DATE, @FmscPostMonth AS DATE;
    SELECT @FmscFrequency = Frequency,
           @FmscAmount = Amount,
           @FmscDiscount = DiscountAmount,
           @FmscFeeTypeId = FeeTypeId,
           @FmscEnrollmentId = EnrollmentId,
           @FmscAdmissionId = AdmissionId,
           @FmscStartMonth = DATEFROMPARTS(YEAR(ISNULL(EffectiveFrom, @FmscOpeningMonth)), MONTH(ISNULL(EffectiveFrom, @FmscOpeningMonth)), 1)
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    SET @FmscPostMonth = CASE WHEN @FmscStartMonth > @FmscOpeningMonth THEN @FmscStartMonth ELSE @FmscOpeningMonth END;
    IF @FmscFrequency IN (N'Monthly', N'Yearly') AND EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NOT NULL OR Status=N'Pending') AND NOT EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1))
        BEGIN
            INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
            SELECT @StudentFeeAssignmentId,
                   @FmscEnrollmentId,
                   @FmscAdmissionId,
                   @FmscFeeTypeId,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscAmount,
                   @FmscDiscount,
                   CASE WHEN @FmscFrequency = N'Yearly' THEN N'Annual' ELSE N'Monthly' END
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.BillingCharges AS BC
                               WHERE  BC.EnrollmentId = @FmscEnrollmentId
                                      AND BC.FeeTypeId = @FmscFeeTypeId
                                      AND BC.Status <> N'Cancelled'
                                      AND ((@FmscFrequency = N'Yearly'
                                            AND BC.ChargeKind = N'Annual')
                                           OR (@FmscFrequency = N'Monthly'
                                               AND BC.ChargeKind = N'Monthly'
                                               AND BC.BillingMonth = @FmscPostMonth)));
        END
    IF EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
        WHERE E.EnrollmentId=@EnrollmentId AND E.Status=N'Pending' AND S.Status=N'Pending')
        EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
COMMIT TRANSACTION;
END
GO

-- dbo.AdmitHistoricalStudent
-- AdmitHistoricalStudent
CREATE OR ALTER PROCEDURE dbo.AdmitHistoricalStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @VerifiedEnrollmentDate DATE, @VerifiedStatus NVARCHAR (30), @VerifiedWithdrawalDate DATE=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF NULLIF (LTRIM(RTRIM(@AdmissionNumber)), '') IS NULL
        THROW 51726, 'Enter the admission number from the physical register.', 1;
    IF @VerifiedStatus IS NULL
       OR @VerifiedStatus NOT IN (N'Active', N'Left')
        THROW 51723, 'Confirm Active or Left for the historical record.', 1;
    IF @VerifiedEnrollmentDate IS NULL
       OR @VerifiedEnrollmentDate < @AdmissionDate
       OR @VerifiedEnrollmentDate > CONVERT (DATE, SYSDATETIME())
        THROW 51722, 'Confirm the enrollment date on or after admission, not in the future.', 1;
    IF @VerifiedWithdrawalDate IS NOT NULL
       AND (@VerifiedStatus <> N'Left'
            OR @VerifiedWithdrawalDate < @VerifiedEnrollmentDate
            OR @VerifiedWithdrawalDate > CONVERT (DATE, SYSDATETIME()))
        THROW 51724, 'Confirm a withdrawal date on or after enrollment for a Left student.', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedFeeTypeIds)
       OR EXISTS (SELECT 1
                  FROM   @AdmissionDiscounts)
        THROW 51725, 'Historical records cannot generate fees or discounts.', 1;
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @VerifiedEnrollmentDate < @YearStartDate
       OR @VerifiedEnrollmentDate > @YearEndDate
        THROW 51022, 'The verified enrollment date must fall within the selected academic year''s date range.', 1;
    DECLARE @BillingStartDate AS DATE = NULL;
    BEGIN TRANSACTION;
    EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, @VerifiedStatus, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WHERE CompletedAt IS NULL) UPDATE dbo.Students SET LegacySetupPending=1 WHERE AdmissionId=@AdmissionId;
    UPDATE dbo.Students
    SET    WithdrawalDate = @VerifiedWithdrawalDate
    WHERE  AdmissionId = @AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, LeavingDate, BillingReady)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @VerifiedEnrollmentDate, @BillingStartDate, @VerifiedStatus, @UsesTransport, N'Historical admission verified from physical school record; no fees generated.', @VerifiedWithdrawalDate, 0);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, @VerifiedStatus, N'Historical first enrollment confirmed from physical record', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, @VerifiedStatus, N'Admission', @AdmissionDate, N'Historical student status confirmed from physical record', @UserId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber;
END
GO

-- dbo.ConfirmStudentPlacement
CREATE OR ALTER PROCEDURE dbo.ConfirmStudentPlacement
 @UserId int,@EnrollmentId int,@ExpectedSnapshot varchar(64)=NULL,@Preview bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer';
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @AdmissionId int,@YearId int,@Placement int,@Start date,@BillingStart date,@End date,@Status nvarchar(30),@Confirmed datetime2(7),@Locked int;
  SELECT @AdmissionId=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @YearId=AcademicYearId,@Placement=ClassSectionId,@Start=EnrollmentDate,@BillingStart=COALESCE(BillingStartDate,EnrollmentDate),@End=LeavingDate,
   @Status=Status,@Confirmed=PlacementConfirmedAt FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  IF @AdmissionId IS NULL THROW 59641,'The enrollment no longer exists.',1;
  IF @Confirmed IS NOT NULL THROW 59642,'This placement is already confirmed. Reload Admission.',1;
  DECLARE @YearStart date,@YearEnd date;
  SELECT @YearStart=StartDate,@YearEnd=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@YearId;
  IF @Start<@YearStart OR (@YearEnd IS NOT NULL AND @Start>@YearEnd)
   THROW 59643,'Correct the enrollment start or academic year before confirming placement.',1;
  IF @BillingStart<@Start OR (@YearEnd IS NOT NULL AND @BillingStart>@YearEnd) OR (@End IS NOT NULL AND @BillingStart>@End)
   THROW 59644,'Correct billing start before confirming placement.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  DECLARE @Snapshot varchar(64),@Json nvarchar(max)=CONCAT(
   (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@YearId FOR JSON PATH,INCLUDE_NULL_VALUES));
  SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Json),2);
  IF @Preview=1
  BEGIN
   SELECT @Snapshot Snapshot,@Status Status,@BillingStart BillingStart,@Start EnrollmentStart,
    (SELECT CONCAT(Y.YearName,N' · ',C.CampusName,N' · ',CL.ClassName,N' · ',S.SectionName)
     FROM dbo.CampusClassSections CS JOIN dbo.Campuses C ON C.CampusId=CS.CampusId
     JOIN dbo.Classes CL ON CL.ClassId=CS.ClassId JOIN dbo.Sections S ON S.SectionId=CS.SectionId
     CROSS JOIN dbo.AcademicYears Y WHERE CS.ClassSectionId=@Placement AND Y.AcademicYearId=@YearId) PlacementName,
    (SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL) FeeCount,
    (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL) FeeTotal;
   COMMIT; RETURN;
  END;
  IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59645,'Placement or fees changed. Review confirmation again.',1;
  UPDATE dbo.Enrollments SET PlacementConfirmedAt=SYSDATETIME(),BillingReady=CASE WHEN @Status=N'Active' THEN 1 ELSE BillingReady END,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@EnrollmentId;
  -- Closed historical records are confirmed as history, without new billing.
  IF @Status=N'Active' EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO

-- dbo.CorrectEnrollmentStatus
CREATE OR ALTER PROCEDURE dbo.CorrectEnrollmentStatus
@UserId INT, @EnrollmentId INT=NULL, @AdmissionId INT=NULL, @NewStatus NVARCHAR (30)=NULL, @NewEnrollmentDate DATE=NULL, @NewLeavingDate DATE=NULL, @ClearLeavingDate BIT=0, @NewClassSectionId INT=NULL, @AcademicYearId INT=NULL, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
            THROW 57101, 'A reason is required for a manual correction.', 1;
        IF @ClearLeavingDate = 1
           AND @NewLeavingDate IS NOT NULL
            THROW 57102, 'Leaving date cannot both be cleared and set at the same time.', 1;
        BEGIN TRANSACTION;
        IF @EnrollmentId IS NULL
           OR @EnrollmentId = 0
            BEGIN
                IF @AdmissionId IS NULL
                   OR @AdmissionId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57103, 'A valid student is required to create an enrollment.', 1;
                    END
                DECLARE @PriorStudentStatus AS NVARCHAR (30);
                DECLARE @PriorStudentWithdrawalDate AS DATE;
                SELECT @PriorStudentStatus = Status,
                       @PriorStudentWithdrawalDate = WithdrawalDate
                FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
                WHERE  AdmissionId = @AdmissionId;
                IF @PriorStudentStatus IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57104, 'The selected student was not found.', 1;
                    END
                IF @AcademicYearId IS NULL
                   OR @AcademicYearId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57105, 'A valid academic year is required.', 1;
                    END
                IF @NewClassSectionId IS NULL
                   OR @NewClassSectionId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57106, 'A valid class section is required.', 1;
                    END
                IF @NewEnrollmentDate IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57107, 'Enrollment date is required.', 1;
                    END
                SET @NewStatus = ISNULL(NULLIF (@NewStatus, N''), N'Active');
                IF @NewStatus NOT IN (N'Active', N'Promoted', N'Left')
                    BEGIN
                        ROLLBACK;
                        THROW 57108, 'Status must be Active, Promoted or Left.', 1;
                    END
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.CampusClassSections
                               WHERE  ClassSectionId = @NewClassSectionId)
                    BEGIN
                        ROLLBACK;
                        THROW 57109, 'The selected class section is invalid.', 1;
                    END
                DECLARE @NewEffectiveLeavingDate AS DATE = CASE WHEN @ClearLeavingDate = 1 THEN NULL ELSE @NewLeavingDate END;
                IF @NewEffectiveLeavingDate IS NOT NULL
                   AND @NewEffectiveLeavingDate < @NewEnrollmentDate
                    BEGIN
                        ROLLBACK;
                        THROW 57110, 'Leaving date cannot be before the enrollment date.', 1;
                    END
                IF @NewStatus = N'Left'
                   AND @NewEffectiveLeavingDate IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57117, 'A leaving date is required when creating a Left enrollment record.', 1;
                    END
                IF @NewStatus = N'Promoted'
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                                   WHERE  AdmissionId = @AdmissionId
                                          AND Status = N'Active')
                    BEGIN
                        ROLLBACK;
                        THROW 57118, 'Marking a new enrollment Promoted requires an existing Active enrollment for this student. Create/select the destination enrollment first, or use the Promotion function.', 1;
                    END
                IF @NewStatus = N'Active'
                   AND EXISTS (SELECT 1
                               FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                               WHERE  AdmissionId = @AdmissionId
                                      AND Status = N'Active')
                    BEGIN
                        ROLLBACK;
                        THROW 57216, 'An Active enrollment already exists for this student. Select and correct the existing enrollment instead.', 1;
                    END
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, Remarks)
                VALUES                 (@AdmissionId, @AcademicYearId, @NewClassSectionId, @NewEnrollmentDate, @NewEffectiveLeavingDate, @NewStatus, @Reason);
                SET @EnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
                VALUES                       (@EnrollmentId, @AdmissionId, N'CORRECTION_CREATE', @NewEnrollmentDate, NULL, @NewClassSectionId, NULL, @NewStatus, @Reason, @UserId);
                IF @NewStatus = N'Active'
                   AND @PriorStudentStatus <> N'Active'
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = N'Active',
                               WithdrawalDate = NULL,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @PriorStudentStatus, N'Active', N'Synchronized by enrollment correction (create).', @NewEnrollmentDate, @Reason, @UserId);
                    END
COMMIT TRANSACTION;
                SELECT @EnrollmentId AS EnrollmentId,
                       @AdmissionId AS AdmissionId,
                       @NewStatus AS Status,
                       CAST (1 AS BIT) AS Changed;
                RETURN;
            END
        DECLARE @OldAdmissionId AS INT, @OldStatus AS NVARCHAR (30), @OldEnrollmentDate AS DATE, @OldLeavingDate AS DATE, @OldClassSectionId AS INT;
        SELECT @OldAdmissionId = AdmissionId,
               @OldStatus = Status,
               @OldEnrollmentDate = EnrollmentDate,
               @OldLeavingDate = LeavingDate,
               @OldClassSectionId = ClassSectionId
        FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
        WHERE  EnrollmentId = @EnrollmentId;
        IF @OldAdmissionId IS NULL
            BEGIN
                ROLLBACK;
                THROW 57111, 'The selected enrollment was not found.', 1;
            END
        DECLARE @EditPriorStudentStatus AS NVARCHAR (30);
        DECLARE @EditPriorStudentWithdrawalDate AS DATE;
        SELECT @EditPriorStudentStatus = Status,
               @EditPriorStudentWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @OldAdmissionId;
        IF @EditPriorStudentStatus IS NULL
            BEGIN
                ROLLBACK;
                THROW 57119, 'The student record for this enrollment was not found.', 1;
            END
        DECLARE @FinalStatus AS NVARCHAR (30) = ISNULL(NULLIF (@NewStatus, N''), @OldStatus);
        IF @FinalStatus NOT IN (N'Active', N'Promoted', N'Left')
            BEGIN
                ROLLBACK;
                THROW 57112, 'Status must be Active, Promoted or Left.', 1;
            END
        DECLARE @FinalEnrollmentDate AS DATE = ISNULL(@NewEnrollmentDate, @OldEnrollmentDate);
        DECLARE @FinalLeavingDate AS DATE = CASE WHEN @ClearLeavingDate = 1 THEN NULL WHEN @NewLeavingDate IS NOT NULL THEN @NewLeavingDate ELSE @OldLeavingDate END;
        DECLARE @FinalClassSectionId AS INT = ISNULL(@NewClassSectionId, @OldClassSectionId);
        IF @FinalLeavingDate IS NOT NULL
           AND @FinalLeavingDate < @FinalEnrollmentDate
            BEGIN
                ROLLBACK;
                THROW 57113, 'Leaving date cannot be before the enrollment date.', 1;
            END
        IF @FinalClassSectionId <> @OldClassSectionId
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.CampusClassSections
                           WHERE  ClassSectionId = @FinalClassSectionId)
            BEGIN
                ROLLBACK;
                THROW 57114, 'The selected class section is invalid.', 1;
            END
        IF @FinalStatus = @OldStatus
           AND @FinalEnrollmentDate = @OldEnrollmentDate
           AND ISNULL(@FinalLeavingDate, '19000101') = ISNULL(@OldLeavingDate, '19000101')
           AND @FinalClassSectionId = @OldClassSectionId
            BEGIN
COMMIT TRANSACTION;
                SELECT @EnrollmentId AS EnrollmentId,
                       @OldAdmissionId AS AdmissionId,
                       @OldStatus AS Status,
                       CAST (0 AS BIT) AS Changed;
                RETURN;
            END
        IF @FinalEnrollmentDate <> @OldEnrollmentDate
           AND (EXISTS (SELECT 1
                        FROM   dbo.BillingCharges
                        WHERE  EnrollmentId = @EnrollmentId)
                OR EXISTS (SELECT 1
                           FROM   dbo.FeePayments
                           WHERE  EnrollmentId = @EnrollmentId)
                OR EXISTS (SELECT 1
                           FROM   dbo.EnrollmentHistory
                           WHERE  EnrollmentId = @EnrollmentId))
            BEGIN
                ROLLBACK;
                THROW 57116, 'This enrollment already has billing, payment or status history, so its enrollment (start) date cannot be changed by an ordinary correction. Correct the dependent records first, or create a new enrollment with the correct start date.', 1;
            END
        IF @FinalStatus = N'Left'
           AND @FinalLeavingDate IS NULL
            BEGIN
                ROLLBACK;
                THROW 57117, 'A leaving date is required when setting an enrollment to Left.', 1;
            END
        IF @FinalStatus = N'Promoted'
           AND @OldStatus <> N'Promoted'
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @OldAdmissionId
                                  AND Status = N'Active'
                                  AND EnrollmentId <> @EnrollmentId)
            BEGIN
                ROLLBACK;
                THROW 57118, 'Marking this enrollment Promoted requires an existing Active enrollment for this student. Create/select the destination enrollment first, or use the Promotion function.', 1;
            END
        IF @FinalLeavingDate IS NOT NULL
           AND (@OldLeavingDate IS NULL
                OR @FinalLeavingDate <> @OldLeavingDate)
            BEGIN
                DECLARE @MaxBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                                   FROM   dbo.BillingCharges
                                                   WHERE  EnrollmentId = @EnrollmentId
                                                          AND Status <> N'Cancelled');
                IF @MaxBilledMonth IS NOT NULL
                   AND DATEFROMPARTS(YEAR(@FinalLeavingDate), MONTH(@FinalLeavingDate), 1) < @MaxBilledMonth
                    BEGIN
                        ROLLBACK;
                        THROW 57120, 'This enrollment already has billing charges for a later month than the requested leaving date. Cancel/correct those charges first, or choose a leaving date in or after the last billed month.', 1;
                    END
            END
        IF @FinalStatus = N'Active'
           AND EXISTS (SELECT 1
                       FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                       WHERE  AdmissionId = @OldAdmissionId
                              AND Status = N'Active'
                              AND EnrollmentId <> @EnrollmentId)
            BEGIN
                ROLLBACK;
                THROW 57216, 'An Active enrollment already exists for this student. Select and correct the existing enrollment instead.', 1;
            END
        UPDATE dbo.Enrollments
        SET    Status         = @FinalStatus,
               EnrollmentDate = @FinalEnrollmentDate,
               LeavingDate    = @FinalLeavingDate,
               ClassSectionId = @FinalClassSectionId,
               UpdatedAt      = SYSDATETIME(),
               Remarks        = COALESCE (NULLIF (@Reason, N''), Remarks)
        WHERE  EnrollmentId = @EnrollmentId;
        IF @@ROWCOUNT <> 1
            BEGIN
                ROLLBACK;
                THROW 57115, 'The enrollment could not be updated.', 1;
            END
        INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
        VALUES                       (@EnrollmentId, @OldAdmissionId, N'CORRECTION', CONVERT (DATE, SYSDATETIME()), @OldClassSectionId, @FinalClassSectionId, @OldStatus, @FinalStatus, @Reason, @UserId);
        DECLARE @EditStudentStatusSynced AS BIT = 0;
        DECLARE @NewStudentStatus AS NVARCHAR (30) = @EditPriorStudentStatus;
        DECLARE @NewStudentWithdrawalDate AS DATE = @EditPriorStudentWithdrawalDate;
        IF @FinalStatus = N'Active'
           AND @EditPriorStudentStatus <> N'Active'
            BEGIN
                SET @NewStudentStatus = N'Active';
                SET @NewStudentWithdrawalDate = NULL;
                SET @EditStudentStatusSynced = 1;
            END
        ELSE
            IF @OldStatus = N'Active'
               AND @FinalStatus = N'Left'
                BEGIN
                    SET @NewStudentStatus = N'Left';
                    SET @NewStudentWithdrawalDate = @FinalLeavingDate;
                    SET @EditStudentStatusSynced = 1;
                END
        IF @EditStudentStatusSynced = 1
            BEGIN
                UPDATE dbo.Students
                SET    Status         = @NewStudentStatus,
                       WithdrawalDate = @NewStudentWithdrawalDate,
                       UpdatedAt      = SYSDATETIME()
                WHERE  AdmissionId = @OldAdmissionId;
                INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                VALUES                          (@OldAdmissionId, @EditPriorStudentStatus, @NewStudentStatus, N'Synchronized by enrollment correction.', ISNULL(@NewStudentWithdrawalDate, CONVERT (DATE, SYSDATETIME())), @Reason, @UserId);
            END
COMMIT TRANSACTION;
        SELECT @EnrollmentId AS EnrollmentId,
               @OldAdmissionId AS AdmissionId,
               @FinalStatus AS Status,
               CAST (1 AS BIT) AS Changed;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;
        THROW;
    END CATCH
END
GO

-- dbo.CorrectStudentStatus
-- CorrectStudentStatus
CREATE OR ALTER PROCEDURE [dbo].[CorrectStudentStatus]
@UserId INT, @AdmissionId INT, @NewStatus NVARCHAR (30), @NewWithdrawalDate DATE=NULL, @ClearWithdrawalDate BIT=0, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @AdmissionId <= 0
        THROW 57001, 'A valid student is required.', 1;
    IF @NewStatus NOT IN (N'Active', N'Left')
        THROW 57002, 'Status must be Active or Left.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 57003, 'A reason is required for a manual correction.', 1;
    IF @ClearWithdrawalDate = 1
       AND @NewWithdrawalDate IS NOT NULL
        THROW 57004, 'Withdrawal date cannot both be cleared and set at the same time.', 1;
    BEGIN TRANSACTION;
    DECLARE @OldStatus AS NVARCHAR (30), @OldWithdrawalDate AS DATE;
    SELECT @OldStatus = Status,
           @OldWithdrawalDate = WithdrawalDate
    FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @AdmissionId;
    IF @OldStatus IS NULL
        BEGIN
            ROLLBACK;
            THROW 57005, 'The selected student was not found.', 1;
        END
    DECLARE @FinalWithdrawalDate AS DATE = CASE WHEN @ClearWithdrawalDate = 1 THEN NULL WHEN @NewWithdrawalDate IS NOT NULL THEN @NewWithdrawalDate ELSE @OldWithdrawalDate END;
    IF @FinalWithdrawalDate > CONVERT (DATE, SYSDATETIME())
        BEGIN
            ROLLBACK;
            THROW 57006, 'Withdrawal date cannot be in the future.', 1;
        END
    IF @OldStatus = @NewStatus
       AND ISNULL(@OldWithdrawalDate, '19000101') = ISNULL(@FinalWithdrawalDate, '19000101')
        BEGIN
            COMMIT TRANSACTION;
            SELECT @AdmissionId AS AdmissionId,
                   @OldStatus AS Status,
                   @OldWithdrawalDate AS WithdrawalDate,
                   CAST (0 AS BIT) AS Changed;
            RETURN;
        END
    IF @NewStatus = N'Active'
       AND @OldStatus = N'Left'
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @AdmissionId
                                  AND Status = N'Active')
                BEGIN
                    ROLLBACK;
                    THROW 57008, 'This student has no active enrollment. Use Re-Admit Student to bring them back into a class with a proper enrollment date, rather than correcting status directly.', 1;
                END
        END
    UPDATE dbo.Students
    SET    Status         = @NewStatus,
           WithdrawalDate = @FinalWithdrawalDate,
           UpdatedAt      = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    IF @@ROWCOUNT <> 1
        BEGIN
            ROLLBACK;
            THROW 57007, 'The student status could not be updated.', 1;
        END
    DECLARE @ClosedEnrollmentId AS INT;
    DECLARE @ClosedClassSectionId AS INT;
    DECLARE @EnrollmentLeavingDate AS DATE = ISNULL(@FinalWithdrawalDate, CONVERT (DATE, SYSDATETIME()));
    IF @NewStatus = N'Left'
        BEGIN
            SELECT   TOP (1) @ClosedEnrollmentId = E.EnrollmentId,
                             @ClosedClassSectionId = E.ClassSectionId
            FROM     dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
            WHERE    E.AdmissionId = @AdmissionId
                     AND E.Status = N'Active'
            ORDER BY E.EnrollmentId DESC;
            IF @ClosedEnrollmentId IS NOT NULL
                BEGIN
                    DECLARE @MaxBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                                       FROM   dbo.BillingCharges
                                                       WHERE  EnrollmentId = @ClosedEnrollmentId
                                                              AND Status <> N'Cancelled');
                    IF @MaxBilledMonth IS NOT NULL
                       AND DATEFROMPARTS(YEAR(@EnrollmentLeavingDate), MONTH(@EnrollmentLeavingDate), 1) < @MaxBilledMonth
                        BEGIN
                            ROLLBACK;
                            THROW 57009, 'This student already has billing charges for a later month than the requested withdrawal date. Cancel/correct those charges first, or choose a withdrawal date in or after the last billed month.', 1;
                        END
                    UPDATE dbo.Enrollments
                    SET    Status      = N'Left',
                           LeavingDate = @EnrollmentLeavingDate,
                           Remarks     = @Reason,
                           UpdatedAt   = SYSDATETIME()
                    WHERE  EnrollmentId = @ClosedEnrollmentId
                           AND Status = N'Active';
                    IF @@ROWCOUNT = 1
                        BEGIN
                            INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
                            VALUES                       (@ClosedEnrollmentId, @AdmissionId, N'LEAVE', @EnrollmentLeavingDate, @ClosedClassSectionId, NULL, N'Active', N'Left', N'Closed automatically by Student Status Console correction. ' + @Reason, @UserId);
                        END
                END
        END
    IF @OldStatus <> @NewStatus
        BEGIN
            INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
            VALUES                          (@AdmissionId, @OldStatus, @NewStatus, N'Console correction', ISNULL(@FinalWithdrawalDate, CONVERT (DATE, SYSDATETIME())), @Reason, @UserId);
        END
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @NewStatus AS Status,
           @FinalWithdrawalDate AS WithdrawalDate,
           CAST (1 AS BIT) AS Changed;
END
GO

-- dbo.CreateLeaveCertificate
-- CreateLeaveCertificate
CREATE OR ALTER PROCEDURE dbo.CreateLeaveCertificate
@UserId INT, @AdmissionId INT, @LeaveDate DATE, @ClassPromotedTo NVARCHAR (300)=NULL, @AcademicRecord NVARCHAR (400)=NULL, @AttendanceRecord NVARCHAR (400)=NULL, @Remarks NVARCHAR (1000)=NULL, @CertificateType NVARCHAR (20)=N'School', @KeepLaterCharges BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Leave';
    IF @AdmissionId <= 0
        THROW 57000, 'A valid student is required.', 1;
    IF @LeaveDate IS NULL
        THROW 57001, 'Leave date is required.', 1;
    IF @LeaveDate > CONVERT (DATE, SYSDATETIME())
        THROW 57002, 'Leave date cannot be in the future.', 1;
    IF @ClassPromotedTo IS NULL
       OR LTRIM(RTRIM(@ClassPromotedTo)) = N''
        THROW 57009, 'Class promoted to is required.', 1;
    IF @AcademicRecord IS NULL
       OR LTRIM(RTRIM(@AcademicRecord)) = N''
        THROW 57010, 'Academic record is required.', 1;
    IF @AttendanceRecord IS NULL
       OR LTRIM(RTRIM(@AttendanceRecord)) = N''
        THROW 57011, 'Attendance record is required.', 1;
    IF @Remarks IS NULL
       OR LTRIM(RTRIM(@Remarks)) = N''
        THROW 57012, 'Remarks are required.', 1;
    SET @CertificateType = NULLIF (LTRIM(RTRIM(@CertificateType)), N'');
    IF @CertificateType IS NULL
        SET @CertificateType = N'School';
    IF @CertificateType NOT IN (N'School', N'College')
        THROW 57014, 'Certificate type must be School or College.', 1;
    DECLARE @StudentName AS NVARCHAR (300), @FatherName AS NVARCHAR (300), @DateOfBirth AS DATE, @AdmissionDate AS DATE, @StudentStatus AS NVARCHAR (60), @EnrollmentId AS INT, @ClassSectionId AS INT, @ClassStudied AS NVARCHAR (300), @LeaveRecordId AS INT, @IssueId AS INT, @MaxBilledMonth AS DATE, @IsRetroactive AS BIT = 0;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @DateOfBirth = S.DateOfBirth,
           @AdmissionDate = S.AdmissionDate,
           @StudentStatus = S.Status
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 57003, 'The student was not found.', 1;
    IF @StudentStatus = N'Left'
        SET @IsRetroactive = 1;
    ELSE
        IF @StudentStatus <> N'Active'
            THROW 57008, 'The student is not currently active and cannot be issued a new leave certificate.', 1;
    DECLARE @CheckEnrollmentId AS INT;
    IF @IsRetroactive = 1
        SELECT   TOP (1) @CheckEnrollmentId = E.EnrollmentId
        FROM     dbo.Enrollments AS E
        WHERE    E.AdmissionId = @AdmissionId
                 AND E.Status = N'Left'
        ORDER BY E.EnrollmentId DESC;
    ELSE
        SELECT   TOP (1) @CheckEnrollmentId = E.EnrollmentId
        FROM     dbo.Enrollments AS E
        WHERE    E.AdmissionId = @AdmissionId
                 AND E.Status = N'Active'
        ORDER BY E.EnrollmentId DESC;
    IF @CheckEnrollmentId IS NOT NULL
       AND EXISTS (SELECT 1
                   FROM   dbo.LeaveCertificates
                   WHERE  AdmissionId = @AdmissionId
                          AND CertificateType = @CertificateType
                          AND EnrollmentId = @CheckEnrollmentId)
        THROW 57013, 'A leaving certificate of this type has already been issued for this student''s current leaving record.', 1;
    IF @AdmissionDate IS NOT NULL
       AND @LeaveDate < @AdmissionDate
        THROW 57005, 'Leave date cannot be earlier than the admission date.', 1;
    IF @IsRetroactive = 1
        BEGIN
            SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                             @ClassSectionId = E.ClassSectionId,
                             @ClassStudied = C.ClassName
            FROM     dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
                     INNER JOIN
                     dbo.CampusClassSections AS CCS
                     ON CCS.ClassSectionId = E.ClassSectionId
                     INNER JOIN
                     dbo.Classes AS C
                     ON C.ClassId = CCS.ClassId
            WHERE    E.AdmissionId = @AdmissionId
                     AND E.Status = N'Left'
            ORDER BY E.EnrollmentId DESC;
            IF @EnrollmentId IS NULL
                THROW 57004, 'No closed enrollment was found for this student to attach the certificate to.', 1;
            IF EXISTS (SELECT 1
                       FROM   dbo.Enrollments
                       WHERE  EnrollmentId = @EnrollmentId
                              AND @LeaveDate < EnrollmentDate)
                THROW 57016, 'Leave date cannot be earlier than the verified enrollment date.', 1;
            SELECT @MaxBilledMonth = (SELECT MAX(BillingMonth)
                                      FROM   dbo.BillingCharges
                                      WHERE  EnrollmentId = @EnrollmentId
                                             AND Status <> N'Cancelled');
            IF @MaxBilledMonth IS NOT NULL
               AND @KeepLaterCharges=0 AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth
                THROW 57015, 'This enrollment already has billing charges for a later month than the requested leave date. Cancel/correct those charges first, or choose a leave date in or after the last billed month.', 1;
            UPDATE dbo.Students
            SET    WithdrawalDate = @LeaveDate,
                   UpdatedAt      = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId;
            UPDATE dbo.Enrollments
            SET    LeavingDate = @LeaveDate,
                   Remarks     = @Remarks,
                   UpdatedAt   = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId;
            INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
            VALUES                       (@EnrollmentId, @AdmissionId, N'LEAVE_CERTIFICATE_RETROACTIVE', @LeaveDate, @ClassSectionId, NULL, N'Left', N'Left', @Remarks, @UserId);
            INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
            VALUES                          (@AdmissionId, N'Left', N'Left', N'Leave certificate issued retroactively', @LeaveDate, @Remarks, @UserId);
        END
    ELSE
        BEGIN
            SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                             @ClassSectionId = E.ClassSectionId,
                             @ClassStudied = C.ClassName
            FROM     dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
                     INNER JOIN
                     dbo.CampusClassSections AS CCS
                     ON CCS.ClassSectionId = E.ClassSectionId
                     INNER JOIN
                     dbo.Classes AS C
                     ON C.ClassId = CCS.ClassId
            WHERE    E.AdmissionId = @AdmissionId
                     AND E.Status = N'Active'
            ORDER BY E.EnrollmentId DESC;
            IF @EnrollmentId IS NULL
                THROW 57004, 'No active enrollment exists for this student.', 1;
            IF EXISTS (SELECT 1
                       FROM   dbo.Enrollments
                       WHERE  EnrollmentId = @EnrollmentId
                              AND @LeaveDate < EnrollmentDate)
                THROW 57016, 'Leave date cannot be earlier than the verified enrollment date.', 1;
            SELECT @MaxBilledMonth = (SELECT MAX(BillingMonth)
                                      FROM   dbo.BillingCharges
                                      WHERE  EnrollmentId = @EnrollmentId
                                             AND Status <> N'Cancelled');
            IF @MaxBilledMonth IS NOT NULL
               AND @KeepLaterCharges=0 AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth
                THROW 57015, 'This enrollment already has billing charges for a later month than the requested leave date. Cancel/correct those charges first, or choose a leave date in or after the last billed month.', 1;
            UPDATE dbo.Enrollments
            SET    Status      = N'Left',
                   LeavingDate = @LeaveDate,
                   Remarks     = @Remarks,
                   UpdatedAt   = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId
                   AND Status = N'Active';
            IF @@ROWCOUNT <> 1
                THROW 57006, 'The active enrollment could not be updated.', 1;
            UPDATE dbo.Students
            SET    Status         = N'Left',
                   WithdrawalDate = @LeaveDate,
                   UpdatedAt      = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId
                   AND Status = N'Active';
            IF @@ROWCOUNT <> 1
                THROW 57007, 'The student status could not be updated.', 1;
            INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
            VALUES                       (@EnrollmentId, @AdmissionId, N'LEAVE', @LeaveDate, @ClassSectionId, NULL, N'Active', N'Left', @Remarks, @UserId);
            INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
            VALUES                          (@AdmissionId, N'Active', N'Left', N'Leave', @LeaveDate, @Remarks, @UserId);
        END
    INSERT  dbo.LeaveCertificates (AdmissionId, CertificateType, EnrollmentId, StudentName, FatherName, DateOfBirth, DateOfAdmission, ClassStudied, ClassPromotedTo, LeaveDate, AcademicRecord, AttendanceRecord, Remarks, CreatedByUserId)
    VALUES                       (@AdmissionId, @CertificateType, @EnrollmentId, @StudentName, @FatherName, @DateOfBirth, @AdmissionDate, @ClassStudied, @ClassPromotedTo, @LeaveDate, @AcademicRecord, @AttendanceRecord, @Remarks, @UserId);
    SET @LeaveRecordId = CONVERT (INT, SCOPE_IDENTITY());
    INSERT  dbo.LeaveCertificateIssues (LeaveRecordId, IssuedAt, IssuedByUserId, IssueType)
    VALUES                            (@LeaveRecordId, SYSDATETIME(), @UserId, N'Original');
    SET @IssueId = CONVERT (INT, SCOPE_IDENTITY());
COMMIT TRANSACTION;
    SELECT @LeaveRecordId AS LeaveRecordId,
           @IssueId AS LeaveCertificateIssueId,
           @AdmissionId AS AdmissionId,
           @CertificateType AS CertificateType,
           @EnrollmentId AS EnrollmentId,
           @LeaveDate AS LeaveDate;
END
GO

-- dbo.CreateReviewedLeaveCertificate
CREATE OR ALTER PROCEDURE dbo.CreateReviewedLeaveCertificate
    @UserId int,@AdmissionId int,@EnrollmentId int,@LeaveDate date,
    @ClassPromotedTo nvarchar(300),@AcademicRecord nvarchar(400),@AttendanceRecord nvarchar(400),
    @Remarks nvarchar(1000),@CertificateType nvarchar(20)=N'School',
    @ExpectedBalance decimal(18,2),@ReviewedCharges nvarchar(max)=N'[]',@ReverseChargeIds nvarchar(max)=N'[]'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId,N'Students.Leave';
    EXEC dbo.RequirePermission @UserId,N'Students.View';
    IF ISJSON(@ReviewedCharges)<>1 OR ISJSON(@ReverseChargeIds)<>1
        THROW 57031,'Reload the fee review before confirming.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Locked int,@LockResult int,@Resource nvarchar(255)=N'FMSC.GenerateEnrollmentCharges.'+CONVERT(nvarchar(20),@EnrollmentId);
        SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
        IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK)
            WHERE AdmissionId=@AdmissionId AND EnrollmentId=@EnrollmentId AND Status IN(N'Active',N'Left'))
            THROW 57032,'The enrollment changed. Reload the student and review again.',1;
        IF @EnrollmentId<>(SELECT TOP(1) EnrollmentId FROM dbo.Enrollments
            WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Left') ORDER BY CASE WHEN Status=N'Active' THEN 0 ELSE 1 END,EnrollmentId DESC)
            THROW 57032,'The enrollment changed. Reload the student and review again.',1;
        EXEC @LockResult=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=5000;
        IF @LockResult<0 THROW 57033,'Fees are being updated elsewhere. Try again.',1;
        SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
        SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId WHERE B.AdmissionId=@AdmissionId;
        IF @ExpectedBalance IS NULL OR @ExpectedBalance<>(SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0)
            FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled')
            THROW 57034,'The balance changed. Review fees again before creating the certificate.',1;
        DECLARE @Month date=DATEFROMPARTS(YEAR(@LeaveDate),MONTH(@LeaveDate),1);
        DECLARE @Actual TABLE(BillingChargeId bigint PRIMARY KEY,NetAmount decimal(18,2),Status nvarchar(40),Balance decimal(18,2));
        INSERT @Actual SELECT BillingChargeId,NetAmount,Status,dbo.GetFeeChargeBalance(BillingChargeId)
            FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId AND Status<>N'Cancelled'
            AND (dbo.GetFeeChargeBalance(BillingChargeId)>0 OR BillingMonth>@Month OR (BillingMonth IS NULL AND ChargeDate>@LeaveDate));
        DECLARE @Reviewed TABLE(BillingChargeId bigint PRIMARY KEY,NetAmount decimal(18,2),Status nvarchar(40),Balance decimal(18,2));
        INSERT @Reviewed SELECT BillingChargeId,NetAmount,Status,Balance FROM OPENJSON(@ReviewedCharges)
            WITH(BillingChargeId bigint,NetAmount decimal(18,2),Status nvarchar(40),Balance decimal(18,2));
        IF EXISTS(SELECT * FROM @Actual EXCEPT SELECT * FROM @Reviewed)
            OR EXISTS(SELECT * FROM @Reviewed EXCEPT SELECT * FROM @Actual)
            THROW 57035,'Later charges changed. Review fees again before creating the certificate.',1;
        DECLARE @Reverse TABLE(BillingChargeId bigint PRIMARY KEY);
        INSERT @Reverse SELECT TRY_CONVERT(bigint,[value]) FROM OPENJSON(@ReverseChargeIds);
        IF EXISTS(SELECT 1 FROM @Reverse R LEFT JOIN @Actual A ON A.BillingChargeId=R.BillingChargeId
            WHERE A.BillingChargeId IS NULL OR A.Status<>N'Open' OR A.Balance<>A.NetAmount)
            THROW 57036,'Only reviewed, wholly uncollected charges on the current enrollment can be reversed.',1;
        IF EXISTS(SELECT 1 FROM @Reverse R JOIN dbo.FeePaymentAllocations A ON A.BillingChargeId=R.BillingChargeId
            JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId WHERE P.Status=N'Completed' AND A.Amount>0)
            THROW 57036,'A selected charge has a payment. Use the existing correction or refund process.',1;
        DECLARE @ChargeId bigint,@Reason nvarchar(500)=LEFT(N'Leave certificate: '+ISNULL(@Remarks,N''),500);
        DECLARE reverse_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM @Reverse;
        OPEN reverse_cursor;
        FETCH NEXT FROM reverse_cursor INTO @ChargeId;
        WHILE @@FETCH_STATUS=0
        BEGIN
            EXEC dbo.ReverseBillingCharge @UserId=@UserId,@BillingChargeId=@ChargeId,@Reason=@Reason;
            FETCH NEXT FROM reverse_cursor INTO @ChargeId;
        END;
        CLOSE reverse_cursor;
        DEALLOCATE reverse_cursor;
        EXEC dbo.CreateLeaveCertificate @UserId=@UserId,@AdmissionId=@AdmissionId,@LeaveDate=@LeaveDate,
            @ClassPromotedTo=@ClassPromotedTo,@AcademicRecord=@AcademicRecord,@AttendanceRecord=@AttendanceRecord,
            @Remarks=@Remarks,@CertificateType=@CertificateType,@KeepLaterCharges=1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- dbo.FinishLegacyAdmissionSetup
CREATE OR ALTER PROCEDURE dbo.FinishLegacyAdmissionSetup @UserId int,@GoLiveDate date,@BillingStart date,@PlacementStart date=NULL,
 @Preview bit=1,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF @GoLiveDate IS NULL OR @GoLiveDate>CONVERT(date,GETDATE()) OR @BillingStart IS NULL OR DAY(@BillingStart)<>1
  THROW 59740,'Choose a go-live date up to today and a billing start month.',1;
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WITH(UPDLOCK,HOLDLOCK) WHERE CompletedAt IS NOT NULL)
  THROW 59741,'Legacy setup has already been completed. It cannot be run again.',1;
 DECLARE @Locked int,@Snapshot varchar(64),@Raw nvarchar(max),@Blocked int;
 SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE LegacySetupPending=1;
 SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 CREATE TABLE #Issues(AdmissionId int NOT NULL,Reason nvarchar(500) NOT NULL);
 INSERT #Issues SELECT AdmissionId,N'Open Admission, review and mark the legacy record verified'
 FROM dbo.Students WHERE LegacySetupPending=1 AND Status=N'Active' AND LegacyVerifiedAt IS NULL;
 INSERT #Issues SELECT AdmissionId,N'An active student needs exactly one academic placement; correct placement history in Admission'
 FROM dbo.Students S WHERE LegacySetupPending=1 AND Status=N'Active' AND
  (SELECT COUNT(*) FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active')<>1;
 SELECT E.EnrollmentId,E.AdmissionId,COALESCE(@PlacementStart,E.EnrollmentDate) StartDate,@BillingStart BillingStart INTO #Ready
 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE E.Status=N'Active' AND S.LegacySetupPending=1 AND S.LegacyVerifiedAt IS NOT NULL;
 INSERT #Issues SELECT R.AdmissionId,CONCAT(N'Placement/billing dates must fit ',Y.YearName,N' (',CONVERT(nvarchar(10),Y.StartDate,120),N' to ',CONVERT(nvarchar(10),Y.EndDate,120),N'), admission date and go-live date')
 FROM #Ready R JOIN dbo.Enrollments E ON E.EnrollmentId=R.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  JOIN dbo.Students S ON S.AdmissionId=R.AdmissionId WHERE R.StartDate NOT BETWEEN Y.StartDate AND Y.EndDate OR R.StartDate<S.AdmissionDate OR R.StartDate>@GoLiveDate
   OR @BillingStart<DATEFROMPARTS(YEAR(R.StartDate),MONTH(R.StartDate),1) OR @BillingStart>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1);
 INSERT #Issues SELECT R.AdmissionId,CONCAT(N'Missing mandatory fee: ',F.FeeTypeName,N'. Add/review it in Admission')
 FROM #Ready R CROSS JOIN dbo.FeeTypes F WHERE F.IsMandatory=1 AND NOT EXISTS(
   SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=R.EnrollmentId AND A.FeeTypeId=F.FeeTypeId AND A.CancelledAt IS NULL);
 -- No date overwrite may invalidate an existing charge/payment or overlap historical placement.
 IF @PlacementStart IS NOT NULL INSERT #Issues SELECT R.AdmissionId,N'The replacement placement date conflicts with existing bills or placement history. Keep the recorded date or review the record in Admission'
 FROM #Ready R WHERE EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=R.EnrollmentId
   AND B.Status<>N'Cancelled' AND B.ChargeDate<R.StartDate) OR EXISTS(SELECT 1 FROM dbo.Enrollments H WHERE H.AdmissionId=R.AdmissionId AND H.EnrollmentId<>R.EnrollmentId
   AND (H.LeavingDate IS NULL OR H.LeavingDate>=R.StartDate));
 SELECT @Blocked=COUNT(*) FROM #Issues;
 SET @Raw=CONCAT(CONVERT(varchar(10),@GoLiveDate,120),CONVERT(varchar(10),@BillingStart,120),CONVERT(varchar(10),@PlacementStart,120),
  (SELECT * FROM dbo.Students WHERE LegacySetupPending=1 ORDER BY AdmissionId FOR JSON PATH),
  (SELECT E.* FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY E.EnrollmentId FOR JSON PATH),
  (SELECT A.* FROM dbo.StudentFeeAssignments A JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY A.StudentFeeAssignmentId FOR JSON PATH),
  (SELECT B.* FROM dbo.BillingCharges B JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY B.BillingChargeId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1 BEGIN
  SELECT @Snapshot Snapshot,@Blocked BlockedCount,(SELECT COUNT(*) FROM #Ready R WHERE NOT EXISTS(SELECT 1 FROM #Issues I WHERE I.AdmissionId=R.AdmissionId)) ActiveStudents,
   (SELECT COUNT(*) FROM dbo.Students WHERE LegacySetupPending=1 AND Status=N'Active' AND LegacyVerifiedAt IS NULL) UnverifiedStudents,
   (SELECT STRING_AGG(CONVERT(nvarchar(max),CONCAT(D.AdmissionNumber,N' · ',D.StudentName,N' · ',D.Reason)),CHAR(13)+CHAR(10))
    FROM(SELECT TOP(200) S.AdmissionNumber,S.StudentName,I.Reason FROM #Issues I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId ORDER BY S.AdmissionId,I.Reason) D) ReviewDetails;
  SELECT S.AdmissionNumber,S.StudentName,R.StartDate PlacementStartDate,R.BillingStart FROM #Ready R JOIN dbo.Students S ON S.AdmissionId=R.AdmissionId;
  COMMIT;RETURN;
 END;
 IF @Blocked>0 THROW 59742,'Legacy setup has unresolved records, missing fee assignments or conflicting dates. Review Data Review and Admission first.',1;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59743,'Records changed after review. Preview Go Live again.',1;
 -- Remove the legacy lock only within this atomic activation; opening charges still use the usual deduplication.
 UPDATE dbo.Students SET LegacySetupPending=0 WHERE LegacySetupPending=1;
 UPDATE E SET EnrollmentDate=R.StartDate,BillingStartDate=R.BillingStart,BillingReady=1,PlacementConfirmedAt=COALESCE(E.PlacementConfirmedAt,SYSDATETIME()),UpdatedAt=SYSDATETIME()
 FROM dbo.Enrollments E JOIN #Ready R ON R.EnrollmentId=E.EnrollmentId;
 DECLARE @Enrollment int;
 DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT EnrollmentId FROM #Ready;
 OPEN C;FETCH NEXT FROM C INTO @Enrollment;
 WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.PostEnrollmentOpeningCharges @Enrollment;FETCH NEXT FROM C INTO @Enrollment;END;
 CLOSE C;DEALLOCATE C;
 UPDATE dbo.LegacyAdmissionSetup SET CompletedAt=SYSDATETIME(),CompletedBy=@UserId,GoLiveDate=@GoLiveDate,BillingStart=@BillingStart WHERE SetupId=1;
 COMMIT;
END;
GO

-- dbo.ReconcileEnrollmentDates
CREATE OR ALTER PROCEDURE dbo.ReconcileEnrollmentDates
 @UserId int, @EnrollmentId int, @EnrollmentDate date, @BillingStartDate date,
 @Reason nvarchar(500), @AllowLegacyExceptions bit=0,
 @Preview bit=1, @ExpectedSnapshot nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 57400,'Enter a reason for this correction.',1;
 IF @EnrollmentDate IS NULL OR @EnrollmentDate>CONVERT(date,SYSDATETIME())
  THROW 57401,'Enter an enrollment start that is not in the future.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @AdmissionId int,@Ready bit,@Return datetime2,@YearStart date,@YearEnd date,@FirstAdmission date,
   @Status nvarchar(30),@ClassSectionId int,@Locked int,@LockResult int;
  SELECT @AdmissionId=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  IF @AdmissionId IS NULL THROW 57402,'The enrollment no longer exists.',1;
  SELECT @FirstAdmission=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Ready=E.BillingReady,@Return=E.ReturnConfirmedAt,@Status=E.Status,@ClassSectionId=E.ClassSectionId,
   @YearStart=Y.StartDate,@YearEnd=Y.EndDate
   FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@EnrollmentId;
  IF @Status<>N'Active' THROW 57403,'Closed enrollments are view only.',1;
  IF @Ready=1 AND @BillingStartDate IS NULL THROW 57404,'Billing start is required for a confirmed enrollment.',1;
  IF @BillingStartDate<@EnrollmentDate OR @BillingStartDate>@YearEnd
   THROW 57405,'Billing start must be on or after enrollment start and within the academic year.',1;
  IF @AllowLegacyExceptions=0 AND (@EnrollmentDate<@FirstAdmission OR @EnrollmentDate<@YearStart OR @EnrollmentDate>@YearEnd
   OR EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@EnrollmentId
     AND ISNULL(LeavingDate,'99991231')>@EnrollmentDate))
   THROW 57406,'The start precedes admission, overlaps another enrollment, or falls outside its academic year.',1;
  DECLARE @LockResource nvarchar(255)=N'FMSC.GenerateEnrollmentCharges.'+CONVERT(nvarchar(20),@EnrollmentId);
  EXEC @LockResult=sys.sp_getapplock @Resource=@LockResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 57407,'Billing is being changed elsewhere. Reload and try again.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId WHERE B.EnrollmentId=@EnrollmentId;
  DECLARE @Snapshot nvarchar(max)=(SELECT
   JSON_QUERY((SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Enrollments,
   JSON_QUERY((SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Assignments,
   JSON_QUERY((SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES)) Charges,
   JSON_QUERY((SELECT A.* FROM dbo.FeePaymentAllocations A JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
    WHERE B.EnrollmentId=@EnrollmentId ORDER BY A.FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES)) Allocations
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  IF @Preview=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot)
   THROW 57408,'Records changed after the preview. Review the reconciliation again.',1;
  DECLARE @Opening date=DATEFROMPARTS(YEAR(@BillingStartDate),MONTH(@BillingStartDate),1),
   @AsOf date=CASE WHEN CONVERT(date,SYSDATETIME())<@YearEnd THEN CONVERT(date,SYSDATETIME()) ELSE @YearEnd END;
  -- Retain valid months already posted ahead of today within this academic year.
  SELECT @AsOf=CASE WHEN MAX(BillingMonth)>@AsOf THEN MAX(BillingMonth) ELSE @AsOf END
   FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId AND ChargeKind=N'Monthly' AND Status<>N'Cancelled' AND BillingMonth<=@YearEnd;
  DECLARE @CorrectedReturn datetime2=CASE WHEN @Return IS NOT NULL AND @Return<@EnrollmentDate THEN CONVERT(datetime2,@EnrollmentDate) ELSE @Return END;
  DECLARE @Expected TABLE(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,ChargeKind nvarchar(20),
   ChargeDate date,GrossAmount decimal(18,2),DiscountAmount decimal(18,2));
  IF @Ready=1
  BEGIN
   ;WITH Months AS (
    SELECT @Opening BillingMonth WHERE @Opening<=@AsOf
    UNION ALL SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE DATEADD(month,1,BillingMonth)<=@AsOf)
   INSERT @Expected
   SELECT F.StudentFeeAssignmentId,F.FeeTypeId,M.BillingMonth,N'Monthly',M.BillingMonth,F.Amount,F.DiscountAmount
    FROM Months M JOIN dbo.StudentFeeAssignments F ON F.EnrollmentId=@EnrollmentId
    WHERE F.CancelledAt IS NULL AND F.Frequency=N'Monthly'
     AND (F.EffectiveFrom IS NULL OR M.BillingMonth>=F.EffectiveFrom)
     AND (F.EffectiveTo IS NULL OR M.BillingMonth<=F.EffectiveTo) OPTION(MAXRECURSION 1200);
   INSERT @Expected
   SELECT F.StudentFeeAssignmentId,F.FeeTypeId,@Opening,N'Annual',COALESCE(CONVERT(date,@CorrectedReturn),@EnrollmentDate),
    COALESCE(B.GrossAmount,F.Amount),COALESCE(B.DiscountAmount,F.DiscountAmount)
    FROM dbo.StudentFeeAssignments F OUTER APPLY(SELECT TOP(1) GrossAmount,DiscountAmount FROM dbo.BillingCharges
     WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=F.FeeTypeId AND ChargeKind=N'Annual' AND Status<>N'Cancelled' ORDER BY BillingChargeId) B
    WHERE F.EnrollmentId=@EnrollmentId AND F.CancelledAt IS NULL AND F.Frequency=N'Yearly'
     AND (F.EffectiveFrom IS NULL OR @Opening>=F.EffectiveFrom) AND (F.EffectiveTo IS NULL OR @Opening<=F.EffectiveTo);
  END;
  IF EXISTS(SELECT 1 FROM @Expected GROUP BY FeeTypeId,BillingMonth,ChargeKind HAVING COUNT(*)>1)
   THROW 57409,'Fee-assignment periods overlap. Correct the assignments before reconciling.',1;
  DECLARE @Cancel TABLE(BillingChargeId bigint PRIMARY KEY);
  INSERT @Cancel SELECT B.BillingChargeId FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=@EnrollmentId AND B.Status<>N'Cancelled' AND B.ChargeKind IN(N'Monthly',N'Annual')
    AND NOT EXISTS(SELECT 1 FROM @Expected X WHERE X.FeeTypeId=B.FeeTypeId AND X.BillingMonth=B.BillingMonth AND X.ChargeKind=B.ChargeKind
     AND (B.ChargeKind<>N'Annual' OR B.ChargeDate=X.ChargeDate));
  DECLARE @Blocked int=(SELECT COUNT(*) FROM @Cancel C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId
   WHERE B.Status<>N'Open' OR EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations WHERE BillingChargeId=B.BillingChargeId));
  DECLARE @Add TABLE(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,ChargeKind nvarchar(20),ChargeDate date,
   GrossAmount decimal(18,2),DiscountAmount decimal(18,2));
  INSERT @Add SELECT X.* FROM @Expected X WHERE NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=X.FeeTypeId AND B.ChargeKind=X.ChargeKind
    AND B.BillingMonth=X.BillingMonth AND B.Status<>N'Cancelled' AND NOT EXISTS(SELECT 1 FROM @Cancel C WHERE C.BillingChargeId=B.BillingChargeId));
  DECLARE @CancelCount int=(SELECT COUNT(*) FROM @Cancel),@AddCount int=(SELECT COUNT(*) FROM @Add),
   @CancelledAmount decimal(18,2)=COALESCE((SELECT SUM(B.NetAmount) FROM @Cancel C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId),0),
   @AddedAmount decimal(18,2)=COALESCE((SELECT SUM(GrossAmount-DiscountAmount) FROM @Add),0);
  IF @Preview=1
  BEGIN
   COMMIT;
   SELECT @Snapshot Snapshot,@CancelCount CancelCount,@AddCount AddCount,@CancelledAmount CancelledAmount,@AddedAmount AddedAmount,
    @Blocked BlockedCount,@AsOf ThroughDate,@CorrectedReturn ReturnDate;
   RETURN;
  END;
  IF @Blocked>0 THROW 57410,'Affected charges have payment history. Reverse or refund those payments in Student Fees before reconciling; no records were changed.',1;
  UPDATE dbo.Enrollments SET EnrollmentDate=@EnrollmentDate,BillingStartDate=@BillingStartDate,
   ReturnConfirmedAt=@CorrectedReturn,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@EnrollmentId;
  UPDATE B SET Status=N'Cancelled',Remarks=LEFT(COALESCE(B.Remarks+N' | ',N'')+N'Enrollment reconciliation: '+@Reason,1000)
   FROM dbo.BillingCharges B JOIN @Cancel C ON C.BillingChargeId=B.BillingChargeId;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
   SELECT StudentFeeAssignmentId,@EnrollmentId,@AdmissionId,FeeTypeId,BillingMonth,ChargeDate,ChargeDate,GrossAmount,DiscountAmount,ChargeKind,
    LEFT(N'Enrollment reconciliation: '+@Reason,1000) FROM @Add;
  INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   VALUES(@EnrollmentId,@AdmissionId,N'CORRECTION',CONVERT(date,SYSDATETIME()),@ClassSectionId,@ClassSectionId,@Status,@Status,
    LEFT(N'Dates and billing reconciled: '+@Reason,500),@UserId);
  COMMIT;
  SELECT @CancelCount CancelledCharges,@AddCount AddedCharges;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO

-- dbo.RepairLeaveCertificateEnrollmentLinks
-- RepairLeaveCertificateEnrollmentLinks
CREATE OR ALTER PROCEDURE dbo.RepairLeaveCertificateEnrollmentLinks
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT L.LeaveRecordId,
               L.AdmissionId,
               L.EnrollmentId AS OldEnrollmentId,
               L.LeaveDate,
               L.ClassStudied
        INTO   #Broken
        FROM   dbo.LeaveCertificates AS L WITH (UPDLOCK, HOLDLOCK)
               LEFT OUTER JOIN
               dbo.Enrollments AS E
               ON E.EnrollmentId = L.EnrollmentId
        WHERE  E.EnrollmentId IS NULL
               OR E.AdmissionId <> L.AdmissionId;
        SELECT B.LeaveRecordId,
               E.EnrollmentId
        INTO   #Candidates
        FROM   #Broken AS B
               INNER JOIN
               dbo.Enrollments AS E
               ON E.AdmissionId = B.AdmissionId
                  AND E.Status = N'Left'
                  AND E.LeavingDate = B.LeaveDate
               INNER JOIN
               dbo.CampusClassSections AS CCS
               ON CCS.ClassSectionId = E.ClassSectionId
               INNER JOIN
               dbo.Classes AS C
               ON C.ClassId = CCS.ClassId
        WHERE  LTRIM(RTRIM(C.ClassName)) = LTRIM(RTRIM(B.ClassStudied));
        IF EXISTS (SELECT   B.LeaveRecordId
                   FROM     #Broken AS B
                            LEFT OUTER JOIN
                            #Candidates AS C
                            ON C.LeaveRecordId = B.LeaveRecordId
                   GROUP BY B.LeaveRecordId
                   HAVING   COUNT(C.EnrollmentId) <> 1)
            THROW 51730, 'A broken certificate link has no unique enrollment matching its student, class and leave date. Nothing was repaired.', 1;
        DECLARE @Count AS INT = (SELECT COUNT(*)
                                 FROM   #Broken);
        IF @Count = 0
            BEGIN
                COMMIT TRANSACTION;
                SELECT 0 AS RepairedLinks;
                RETURN;
            END
        UPDATE L
        SET    EnrollmentId = C.EnrollmentId
        FROM   dbo.LeaveCertificates AS L
               INNER JOIN
               #Candidates AS C
               ON C.LeaveRecordId = L.LeaveRecordId;
COMMIT TRANSACTION;
        SELECT @Count AS RepairedLinks;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
END
GO

-- dbo.RepairLifecycleDates
CREATE OR ALTER PROCEDURE dbo.RepairLifecycleDates
 @UserId int,@AdmissionId int,@ExpectedSnapshot nvarchar(max),@Changes nvarchar(max),@Reason nvarchar(500),
 @AllowLegacyExceptions bit=0,@KeepFinancialHistory bit=0,
 @CertificateChanges nvarchar(max)=N'[]',@UpdateWithdrawal bit=0,@NewWithdrawalDate date=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 57200,'A reason is required for this repair.',1;
 IF ISJSON(@Changes)<>1 THROW 57201,'Reload the date repair before saving.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Locked int,@Actual nvarchar(max),@AdmissionDate date,@Status nvarchar(30),@Withdrawal date;
  SELECT @Status=Status,@AdmissionDate=AdmissionDate,@Withdrawal=WithdrawalDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @Status IS NULL THROW 57201,'The student no longer exists.',1;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Locked=COUNT(*) FROM dbo.LeaveCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@Actual OUTPUT;
  IF @ExpectedSnapshot IS NULL OR @Actual<>@ExpectedSnapshot THROW 57202,'Records changed while this repair was open. Reload and review again.',1;
  DECLARE @Dates TABLE(EnrollmentId int PRIMARY KEY,EnrollmentDate date NOT NULL,LeavingDate date NULL,BillingStartDate date NULL);
  INSERT @Dates SELECT EnrollmentId,EnrollmentDate,LeavingDate,BillingStartDate FROM OPENJSON(@Changes)
   WITH(EnrollmentId int,EnrollmentDate date,LeavingDate date,BillingStartDate date);
  IF EXISTS(SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId EXCEPT SELECT EnrollmentId FROM @Dates)
    OR EXISTS(SELECT EnrollmentId FROM @Dates EXCEPT SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId)
   THROW 57203,'Review every enrollment belonging to this student together.',1;
  IF EXISTS(SELECT 1 FROM @Dates WHERE LeavingDate<EnrollmentDate)
   THROW 57204,'A leaving date cannot precede its enrollment start. Correct the start date as well if it is wrong.',1;
  IF EXISTS(SELECT 1 FROM @Dates WHERE EnrollmentDate>CONVERT(date,SYSDATETIME()) OR LeavingDate>CONVERT(date,SYSDATETIME()))
   THROW 57205,'Historical enrollment and leaving dates cannot be in the future.',1;
  IF EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId
     WHERE E.Status=N'Active' AND D.LeavingDate IS NOT NULL)
   THROW 57206,'An active enrollment cannot have a leaving date. Correct its status separately if necessary.',1;
  IF EXISTS(SELECT 1 FROM @Dates D JOIN dbo.LeaveCertificates L ON L.EnrollmentId=D.EnrollmentId WHERE D.LeavingDate IS NULL)
   THROW 57207,'An enrollment with a leave certificate needs a leaving date.',1;
  IF @AllowLegacyExceptions=0 AND (EXISTS(SELECT 1 FROM @Dates D WHERE D.EnrollmentDate<@AdmissionDate)
    OR EXISTS(SELECT 1 FROM @Dates A JOIN @Dates B ON A.EnrollmentId<B.EnrollmentId
      WHERE A.EnrollmentDate<=ISNULL(B.LeavingDate,'99991231') AND B.EnrollmentDate<=ISNULL(A.LeavingDate,'99991231'))
    OR EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
      WHERE D.EnrollmentDate<Y.StartDate OR D.EnrollmentDate>Y.EndDate OR D.LeavingDate>Y.EndDate))
   THROW 57208,'Dates overlap, precede admission, or fall outside an academic year. Correct them or explicitly acknowledge a legacy exception.',1;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @KeepFinancialHistory=0 AND EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId)
   THROW 57209,'Explicitly acknowledge that existing bills and payments retain their original dates and amounts.',1;
  DECLARE @Before nvarchar(max)=@Actual,@Changed int;
  UPDATE E SET EnrollmentDate=D.EnrollmentDate,LeavingDate=D.LeavingDate,BillingStartDate=D.BillingStartDate,UpdatedAt=SYSDATETIME()
   FROM dbo.Enrollments E JOIN @Dates D ON D.EnrollmentId=E.EnrollmentId
   WHERE E.EnrollmentDate<>D.EnrollmentDate OR ISNULL(E.LeavingDate,'19000101')<>ISNULL(D.LeavingDate,'19000101')
     OR ISNULL(E.BillingStartDate,'19000101')<>ISNULL(D.BillingStartDate,'19000101');
  SET @Changed=@@ROWCOUNT;
  DECLARE @SelectedCertificates TABLE(LeaveRecordId int PRIMARY KEY,LeaveDate date NOT NULL);
  IF ISJSON(@CertificateChanges)<>1 THROW 57210,'Reload and review the certificate corrections.',1;
  INSERT @SelectedCertificates SELECT LeaveRecordId,LeaveDate FROM OPENJSON(@CertificateChanges)
   WITH(LeaveRecordId int,LeaveDate date);
  IF EXISTS(SELECT 1 FROM @SelectedCertificates C LEFT JOIN dbo.LeaveCertificates L ON L.LeaveRecordId=C.LeaveRecordId
      WHERE L.LeaveRecordId IS NULL OR L.AdmissionId<>@AdmissionId)
   THROW 57211,'A selected certificate does not belong to this student.',1;
  IF EXISTS(SELECT 1 FROM @SelectedCertificates WHERE LeaveDate>CONVERT(date,SYSDATETIME()))
   THROW 57212,'A historical certificate leave date cannot be in the future.',1;
  IF @AllowLegacyExceptions=0 AND EXISTS(SELECT 1 FROM @SelectedCertificates C JOIN dbo.LeaveCertificates L ON L.LeaveRecordId=C.LeaveRecordId
      JOIN @Dates D ON D.EnrollmentId=L.EnrollmentId WHERE D.LeavingDate IS NULL OR C.LeaveDate<>D.LeavingDate)
   THROW 57213,'A selected certificate date differs from its enrollment leave date. Correct both, or explicitly acknowledge a legacy exception.',1;
  UPDATE L SET LeaveDate=C.LeaveDate FROM dbo.LeaveCertificates L JOIN @SelectedCertificates C ON C.LeaveRecordId=L.LeaveRecordId WHERE L.LeaveDate<>C.LeaveDate;
  DECLARE @Certificates int=@@ROWCOUNT,@FinalWithdrawal date=@Withdrawal;
  IF @UpdateWithdrawal=1
  BEGIN
   IF @NewWithdrawalDate>CONVERT(date,SYSDATETIME()) THROW 57214,'A withdrawal date cannot be in the future.',1;
   IF @Status=N'Active' AND @NewWithdrawalDate IS NOT NULL THROW 57215,'An active student cannot have a withdrawal date. Correct the student status separately.',1;
   IF @AllowLegacyExceptions=0 AND @NewWithdrawalDate IS NOT NULL AND (@NewWithdrawalDate<@AdmissionDate OR
      NOT EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE E.Status=N'Left' AND D.LeavingDate=@NewWithdrawalDate))
    THROW 57217,'The withdrawal date does not match a departure. Review the enrollment dates, or explicitly acknowledge a legacy exception.',1;
   SET @FinalWithdrawal=@NewWithdrawalDate;
   UPDATE dbo.Students SET WithdrawalDate=@FinalWithdrawal,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId
     AND ISNULL(WithdrawalDate,'19000101')<>ISNULL(@FinalWithdrawal,'19000101');
   IF @@ROWCOUNT>0
    INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,Remarks,CreatedBy)
     VALUES(@AdmissionId,@Status,@Status,N'Withdrawal date repaired',ISNULL(@FinalWithdrawal,CONVERT(date,SYSDATETIME())),@Reason,@UserId);
  END;
  INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   SELECT E.EnrollmentId,@AdmissionId,N'CORRECTION',CONVERT(date,SYSDATETIME()),E.ClassSectionId,E.ClassSectionId,E.Status,E.Status,
    LEFT(N'Lifecycle dates reviewed: '+@Reason,500),@UserId FROM dbo.Enrollments E JOIN @Dates D ON D.EnrollmentId=E.EnrollmentId
    WHERE EXISTS(SELECT 1 FROM OPENJSON(@Before,'$.Enrollments') WITH(EnrollmentId int,EnrollmentDate date,LeavingDate date,BillingStartDate date) O
      WHERE O.EnrollmentId=E.EnrollmentId AND (O.EnrollmentDate<>D.EnrollmentDate OR ISNULL(O.LeavingDate,'19000101')<>ISNULL(D.LeavingDate,'19000101')
        OR ISNULL(O.BillingStartDate,'19000101')<>ISNULL(D.BillingStartDate,'19000101')));
  COMMIT;
  SELECT @Changed EnrollmentsChanged,@Certificates CertificatesUpdated,@FinalWithdrawal WithdrawalDate;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO

-- dbo.ReverseBillingCharge
-- ReverseBillingCharge
CREATE OR ALTER PROCEDURE dbo.ReverseBillingCharge
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
GO

-- dbo.ReverseMonthlyFees
CREATE OR ALTER PROCEDURE dbo.ReverseMonthlyFees
 @UserId int,@BillingMonth date,@Reason nvarchar(500),@CampusId int=0,@ClassId int=0,@SectionId int=0,@FeeTypeIds dbo.FeeTypeIdList READONLY,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 54741,'Enter a reason for reversing the whole month.',1;
 IF @CampusId<>0 OR @ClassId<>0 OR @SectionId<>0 OR EXISTS(SELECT 1 FROM @FeeTypeIds)
  THROW 54805,'Month reversal applies to the entire school and all fee types. Partial filters are not allowed.',1;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @YearId int,@Snapshot varchar(64);
 EXEC dbo.RequireWholeMonthReversal @UserId,@BillingMonth,@YearId OUTPUT,@Snapshot OUTPUT;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 54806,'The posting changed after preview. Preview the whole month again; nothing was reversed.',1;
 DECLARE @Reversed TABLE(BillingChargeId bigint PRIMARY KEY,AdmissionId int,Amount decimal(12,2));
 UPDATE B SET Status=N'Cancelled',Remarks=LEFT(COALESCE(B.Remarks+N' | ',N'')+N'Whole-month posting reversal: '+@Reason,500)
 OUTPUT inserted.BillingChargeId,inserted.AdmissionId,inserted.NetAmount INTO @Reversed
 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
 WHERE E.AcademicYearId=@YearId AND B.BillingMonth=@BillingMonth AND B.Status<>N'Cancelled';
 DECLARE @Count int=(SELECT COUNT(*) FROM @Reversed);
 COMMIT;
 SELECT @BillingMonth BillingMonth,@Count ChargesReversed,(SELECT COUNT(DISTINCT AdmissionId) FROM @Reversed) StudentsAffected,(SELECT SUM(Amount) FROM @Reversed) ReversedAmount;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO

-- dbo.SaveRolePermissions
-- SaveRolePermissions
CREATE OR ALTER PROCEDURE dbo.SaveRolePermissions
@UserId INT, @RoleId INT, @PermissionIdsJson NVARCHAR (MAX)
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles.Manage', N'System.Administration';
    IF @RoleId <= 0
        THROW 57300, 'A valid role is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Roles
                   WHERE  RoleId = @RoleId)
        THROW 57301, 'The selected role was not found.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Roles
               WHERE  RoleId = @RoleId
                      AND RoleName = N'Administrator')
        THROW 57302, 'Administrator permissions are managed automatically.', 1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE RoleId=@RoleId AND RoleName=N'Accountant')
        THROW 57411,'Only Accountant permissions may be changed; Administrator always has full authority.',1;
    IF ISNULL(ISJSON(@PermissionIdsJson),0) <> 1 OR LEFT(LTRIM(@PermissionIdsJson),1)<>N'['
        THROW 57303, 'The supplied permission list is not valid JSON.', 1;
    IF EXISTS(SELECT 1 FROM OPENJSON(@PermissionIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,[value]) IS NULL OR TRY_CONVERT(int,[value])<=0)
        THROW 57412,'Permission selections must be an array of positive integer IDs.',1;
    DECLARE @SelectedPermissions TABLE (
        PermissionId INT NOT NULL PRIMARY KEY);
    INSERT INTO @SelectedPermissions (PermissionId)
    SELECT DISTINCT TRY_CONVERT (INT, [value])
    FROM   OPENJSON (@PermissionIdsJson)
    WHERE  TRY_CONVERT (INT, [value]) IS NOT NULL;
    IF EXISTS (SELECT 1
               FROM   @SelectedPermissions AS SP
                      LEFT OUTER JOIN
                      dbo.Permissions AS P
                      ON P.PermissionId = SP.PermissionId
               WHERE  P.PermissionId IS NULL OR P.PermissionCode NOT IN(N'Backup.Create',N'Backup.Restore',N'Backup.View',N'Calendar.Manage',N'Certificates.Create',N'Certificates.Print',N'Enrollment.Manage',N'Fees.Adjust',N'Fees.Collect',N'Fees.Configuration.Manage',N'Fees.Reports.View',N'Fees.Statement.View',N'Fees.View',N'Reports.View',N'Staff.Manage',N'Staff.View',N'Students.Administer',N'Students.Admit',N'Students.Family',N'Students.Leave',N'Students.View',N'System.Administration',N'System.Audit',N'System.Roles',N'System.Roles.Manage',N'System.Setup',N'System.Users',N'System.Users.Manage'))
        THROW 57304, 'One or more selected permissions are invalid.', 1;
    IF NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N'System.Administration')
       AND (
        EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N'System.Users.Manage')
        AND (NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N'System.Users')
          OR NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N'System.Roles'))
        OR EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode IN(N'System.Roles.Manage',N'System.Users'))
        AND NOT EXISTS(SELECT 1 FROM @SelectedPermissions S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode=N'System.Roles'))
        THROW 57414,'User and role management require their viewing permissions. Select the required viewing access before saving.',1;
    BEGIN TRANSACTION;
    DELETE dbo.RolePermissions
    WHERE  RoleId = @RoleId;
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @RoleId,
           PermissionId
    FROM   @SelectedPermissions;
COMMIT TRANSACTION;
    SELECT @RoleId AS RoleId;
END
GO

-- dbo.SaveStudentDetails
-- SaveStudentDetails
CREATE OR ALTER PROCEDURE dbo.SaveStudentDetails
@UserId INT, @AdmissionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500)=NULL, @DateOfBirth DATE=NULL, @Gender NVARCHAR (20)=NULL, @BFormNumber NVARCHAR (50)=NULL, @Religion NVARCHAR (50)=NULL, @BloodGroup NVARCHAR (10)=NULL, @ContactNumber NVARCHAR (30)=NULL, @AlternateContactNumber NVARCHAR (30)=NULL, @Address NVARCHAR (500)=NULL, @PreviousSchool NVARCHAR (500)=NULL, @PlaceOfBirth NVARCHAR (500)=NULL, @FatherOccupation NVARCHAR (250)=NULL, @Disability NVARCHAR (3)=N'No', @UsesTransport BIT=NULL, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Pending') EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer'; ELSE EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51701, 'The selected student was not found.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51702, 'Student name is required.', 1;
    DECLARE @AdmissionDate AS DATE;
    SELECT @AdmissionDate = AdmissionDate
    FROM   dbo.Students
    WHERE  AdmissionId = @AdmissionId;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51703, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability IS NULL
       OR @Disability NOT IN (N'Yes', N'No')
        THROW 51704, 'Disability must be Yes or No.', 1;
    DECLARE @OldStudentName AS NVARCHAR (500), @OldFatherName AS NVARCHAR (500), @OldDateOfBirth AS DATE, @OldGender AS NVARCHAR (20), @OldBFormNumber AS NVARCHAR (50), @OldReligion AS NVARCHAR (50), @OldBloodGroup AS NVARCHAR (10), @OldContactNumber AS NVARCHAR (30), @OldAlternateContactNumber AS NVARCHAR (30), @OldAddress AS NVARCHAR (500), @OldPreviousSchool AS NVARCHAR (500), @OldPlaceOfBirth AS NVARCHAR (500), @OldFatherOccupation AS NVARCHAR (250), @OldDisability AS NVARCHAR (3), @OldBiseEnrolmentNumber AS NVARCHAR (50), @OldCasteTribe AS NVARCHAR (100);
    SELECT @OldStudentName = StudentName,
           @OldFatherName = FatherName,
           @OldDateOfBirth = DateOfBirth,
           @OldGender = Gender,
           @OldBFormNumber = BFormNumber,
           @OldReligion = Religion,
           @OldBloodGroup = BloodGroup,
           @OldContactNumber = ContactNumber,
           @OldAlternateContactNumber = AlternateContactNumber,
           @OldAddress = Address,
           @OldPreviousSchool = PreviousSchool,
           @OldPlaceOfBirth = PlaceOfBirth,
           @OldFatherOccupation = FatherOccupation,
           @OldDisability = Disability,
           @OldBiseEnrolmentNumber = BiseEnrolmentNumber,
           @OldCasteTribe = CasteTribe
    FROM   dbo.Students
    WHERE  AdmissionId = @AdmissionId;
    DECLARE @NewStudentName AS NVARCHAR (500), @NewFatherName AS NVARCHAR (500), @NewBFormNumber AS NVARCHAR (50), @NewContactNumber AS NVARCHAR (30), @NewAlternateContactNumber AS NVARCHAR (30), @NewAddress AS NVARCHAR (500), @NewPreviousSchool AS NVARCHAR (500), @NewPlaceOfBirth AS NVARCHAR (500), @NewFatherOccupation AS NVARCHAR (250), @NewBiseEnrolmentNumber AS NVARCHAR (50), @NewCasteTribe AS NVARCHAR (100);
    SET @NewStudentName = LTRIM(RTRIM(@StudentName));
    SET @NewFatherName = NULLIF (LTRIM(RTRIM(@FatherName)), N'');
    SET @NewBFormNumber = NULLIF (LTRIM(RTRIM(@BFormNumber)), N'');
    SET @NewContactNumber = NULLIF (LTRIM(RTRIM(@ContactNumber)), N'');
    SET @NewAlternateContactNumber = NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N'');
    SET @NewAddress = NULLIF (LTRIM(RTRIM(@Address)), N'');
    SET @NewPreviousSchool = NULLIF (LTRIM(RTRIM(@PreviousSchool)), N'');
    SET @NewPlaceOfBirth = NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N'');
    SET @NewFatherOccupation = NULLIF (LTRIM(RTRIM(@FatherOccupation)), N'');
    SET @NewBiseEnrolmentNumber = NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N'');
    SET @NewCasteTribe = NULLIF (LTRIM(RTRIM(@CasteTribe)), N'');
    DECLARE @StudentChanged AS BIT = 0;
    IF ISNULL(@OldStudentName, N'') <> ISNULL(@NewStudentName, N'')
       OR ISNULL(@OldFatherName, N'') <> ISNULL(@NewFatherName, N'')
       OR ISNULL(@OldDateOfBirth, '19000101') <> ISNULL(@DateOfBirth, '19000101')
       OR ISNULL(@OldGender, N'') <> ISNULL(@Gender, N'')
       OR ISNULL(@OldBFormNumber, N'') <> ISNULL(@NewBFormNumber, N'')
       OR ISNULL(@OldReligion, N'') <> ISNULL(@Religion, N'')
       OR ISNULL(@OldBloodGroup, N'') <> ISNULL(@BloodGroup, N'')
       OR ISNULL(@OldContactNumber, N'') <> ISNULL(@NewContactNumber, N'')
       OR ISNULL(@OldAlternateContactNumber, N'') <> ISNULL(@NewAlternateContactNumber, N'')
       OR ISNULL(@OldAddress, N'') <> ISNULL(@NewAddress, N'')
       OR ISNULL(@OldPreviousSchool, N'') <> ISNULL(@NewPreviousSchool, N'')
       OR ISNULL(@OldPlaceOfBirth, N'') <> ISNULL(@NewPlaceOfBirth, N'')
       OR ISNULL(@OldFatherOccupation, N'') <> ISNULL(@NewFatherOccupation, N'')
       OR ISNULL(@OldDisability, N'') <> ISNULL(@Disability, N'')
       OR ISNULL(@OldBiseEnrolmentNumber, N'') <> ISNULL(@NewBiseEnrolmentNumber, N'')
       OR ISNULL(@OldCasteTribe, N'') <> ISNULL(@NewCasteTribe, N'')
        BEGIN
            SET @StudentChanged = 1;
        END
    DECLARE @EnrollmentId AS INT = NULL, @OldUsesTransport AS BIT = NULL;
    SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                     @OldUsesTransport = E.UsesTransport
    FROM     dbo.Enrollments AS E
    WHERE    E.AdmissionId = @AdmissionId
    ORDER BY CASE WHEN E.Status = N'Active' THEN 0 ELSE 1 END, E.EnrollmentDate DESC, E.EnrollmentId DESC;
    DECLARE @TransportChanged AS BIT = 0;
    IF @UsesTransport IS NOT NULL
       AND @EnrollmentId IS NOT NULL
       AND (@OldUsesTransport IS NULL
            OR @OldUsesTransport <> @UsesTransport)
        BEGIN
            SET @TransportChanged = 1;
        END
    IF @StudentChanged = 0
       AND @TransportChanged = 0
        BEGIN
            SELECT @AdmissionId AS AdmissionId;
            RETURN;
        END
    BEGIN TRANSACTION;
    IF @StudentChanged = 1
        BEGIN
            UPDATE dbo.Students
            SET    StudentName            = @NewStudentName,
                   FatherName             = @NewFatherName,
                   DateOfBirth            = @DateOfBirth,
                   Gender                 = @Gender,
                   BFormNumber            = @NewBFormNumber,
                   Religion               = @Religion,
                   BloodGroup             = @BloodGroup,
                   ContactNumber          = @NewContactNumber,
                   AlternateContactNumber = @NewAlternateContactNumber,
                   Address                = @NewAddress,
                   PreviousSchool         = @NewPreviousSchool,
                   PlaceOfBirth           = @NewPlaceOfBirth,
                   FatherOccupation       = @NewFatherOccupation,
                   Disability             = @Disability,
                   BiseEnrolmentNumber    = @NewBiseEnrolmentNumber,
                   CasteTribe             = @NewCasteTribe,
                   UpdatedAt              = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId;
            IF @@ROWCOUNT = 0
                THROW 51705, 'The selected student record was not found.', 1;
        END
    IF @TransportChanged = 1
        BEGIN
            UPDATE dbo.Enrollments
            SET    UsesTransport = @UsesTransport,
                   UpdatedAt     = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId;
            IF @@ROWCOUNT = 0
                THROW 51707, 'The student enrollment could not be updated.', 1;
        END
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId;
END
GO

-- dbo.SaveStudentDetailsWithFirstEnrollment
-- SaveStudentDetailsWithFirstEnrollment
CREATE OR ALTER PROCEDURE dbo.SaveStudentDetailsWithFirstEnrollment
@UserId INT, @AdmissionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500)=NULL, @DateOfBirth DATE=NULL, @Gender NVARCHAR (20)=NULL, @BFormNumber NVARCHAR (50)=NULL, @Religion NVARCHAR (50)=NULL, @BloodGroup NVARCHAR (10)=NULL, @ContactNumber NVARCHAR (30)=NULL, @AlternateContactNumber NVARCHAR (30)=NULL, @Address NVARCHAR (500)=NULL, @PreviousSchool NVARCHAR (500)=NULL, @PlaceOfBirth NVARCHAR (500)=NULL, @FatherOccupation NVARCHAR (250)=NULL, @Disability NVARCHAR (3)=N'No', @UsesTransport BIT=NULL, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @Discounts dbo.StudentFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @CreateFirstEnrollment BIT=0, @PlacementCampusId INT=NULL, @PlacementAcademicYearId INT=NULL, @PlacementClassSectionId INT=NULL, @PlacementEnrollmentDate DATE=NULL, @PlacementStatus NVARCHAR (30)=NULL, @PlacementLeavingDate DATE=NULL, @PlacementUsesTransport BIT=0, @CompleteRegistration bit=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51710, 'The selected student was not found.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts
               WHERE  DiscountAmount < 0)
        THROW 51711, 'A fee discount cannot be negative. Nothing was changed.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts AS D
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.StudentFeeAssignments AS SFA
                                         LEFT OUTER JOIN
                                         dbo.Enrollments AS E
                                         ON E.EnrollmentId = SFA.EnrollmentId
                                  WHERE  SFA.StudentFeeAssignmentId = D.StudentFeeAssignmentId
                                         AND (SFA.AdmissionId = @AdmissionId
                                              OR E.AdmissionId = @AdmissionId)))
        THROW 51712, 'A fee discount in this save does not belong to the student being saved. Nothing was changed.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Pending') EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer'; ELSE EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        DECLARE @StoredStatus AS NVARCHAR (30), @StoredAdmissionDate AS DATE, @StoredWithdrawalDate AS DATE;
        SELECT @StoredStatus = Status,
               @StoredAdmissionDate = AdmissionDate,
               @StoredWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @AdmissionId;
        IF @StoredStatus IS NULL
            THROW 51710, 'The selected student was not found.', 1;
        IF @CreateFirstEnrollment = 1
            BEGIN
                EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
                IF EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @AdmissionId)
                    THROW 51720, 'This student already has an enrollment. Reload the record and use enrollment corrections.', 1;
                IF @PlacementCampusId IS NULL
                   OR @PlacementAcademicYearId IS NULL
                   OR @PlacementClassSectionId IS NULL
                    THROW 51721, 'Confirm the campus, academic year, class and section.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.Campuses
                               WHERE  CampusId = @PlacementCampusId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.AcademicYears
                                  WHERE  AcademicYearId = @PlacementAcademicYearId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.CampusClassSections
                                  WHERE  ClassSectionId = @PlacementClassSectionId
                                         AND CampusId = @PlacementCampusId)
                    THROW 51721, 'The verified placement is not a valid campus, year and class section.', 1;
                IF @PlacementEnrollmentDate IS NULL
                   OR @PlacementEnrollmentDate < @StoredAdmissionDate
                   OR @PlacementEnrollmentDate > CONVERT (DATE, SYSDATETIME())
                    THROW 51722, 'Enter the confirmed enrollment date, on or after admission and not in the future.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.AcademicYears
                               WHERE  AcademicYearId = @PlacementAcademicYearId
                                      AND @PlacementEnrollmentDate BETWEEN StartDate AND EndDate)
                    THROW 51722, 'The enrollment date must fall within the selected academic year.', 1;
                IF @PlacementStatus IS NULL
                   OR @PlacementStatus NOT IN (N'Active', N'Left')
                    THROW 51723, 'Choose Active or Left for the verified enrollment.', 1;
                IF @StoredStatus = N'Left'
                   AND @PlacementStatus <> N'Left'
                    THROW 51723, 'An already-left student needs a historical Left enrollment. Use Re-Admit to return them.', 1;
                IF @PlacementStatus = N'Active'
                   AND (@PlacementLeavingDate IS NOT NULL
                        OR @StoredWithdrawalDate IS NOT NULL)
                    THROW 51724, 'An active enrollment cannot have a withdrawal date.', 1;
                IF @StoredWithdrawalDate IS NOT NULL
                   AND @PlacementLeavingDate IS NOT NULL
                   AND @StoredWithdrawalDate <> @PlacementLeavingDate
                    THROW 51724, 'The confirmed withdrawal date differs from the stored date. Correct the stored date through the status workflow first.', 1;
                SET @PlacementLeavingDate = COALESCE (@PlacementLeavingDate, @StoredWithdrawalDate);
                IF @PlacementLeavingDate IS NOT NULL
                   AND (@PlacementLeavingDate < @PlacementEnrollmentDate
                        OR @PlacementLeavingDate > CONVERT (DATE, SYSDATETIME()))
                    THROW 51724, 'Withdrawal date must be on or after enrollment and not in the future.', 1;
                IF EXISTS (SELECT 1
                           FROM   @Discounts)
                    THROW 51725, 'First placement cannot include discounts from another enrollment.', 1;
            END
        IF @AdmissionNumber IS NOT NULL AND @CompleteRegistration=0
            EXECUTE dbo.SetStudentAdmissionNumber @UserId, @AdmissionId, @AdmissionNumber;
        EXECUTE dbo.SaveStudentDetails @UserId = @UserId, @AdmissionId = @AdmissionId, @StudentName = @StudentName, @FatherName = @FatherName, @DateOfBirth = @DateOfBirth, @Gender = @Gender, @BFormNumber = @BFormNumber, @Religion = @Religion, @BloodGroup = @BloodGroup, @ContactNumber = @ContactNumber, @AlternateContactNumber = @AlternateContactNumber, @Address = @Address, @PreviousSchool = @PreviousSchool, @PlaceOfBirth = @PlaceOfBirth, @FatherOccupation = @FatherOccupation, @Disability = @Disability, @UsesTransport = @UsesTransport, @BiseEnrolmentNumber = @BiseEnrolmentNumber, @CasteTribe = @CasteTribe;
        IF EXISTS (SELECT 1
                   FROM   @Discounts)
            BEGIN
                DECLARE @StudentFeeAssignmentId AS INT, @DiscountAmount AS DECIMAL (12, 2);
                DECLARE DiscountRows CURSOR LOCAL FAST_FORWARD
                    FOR SELECT   StudentFeeAssignmentId,
                                 DiscountAmount
                        FROM     @Discounts
                        ORDER BY StudentFeeAssignmentId;
                OPEN DiscountRows;
                FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                WHILE @@FETCH_STATUS = 0
                    BEGIN
                        EXECUTE dbo.SaveStudentFeeDiscount @UserId = @UserId, @StudentFeeAssignmentId = @StudentFeeAssignmentId, @DiscountAmount = @DiscountAmount;
                        FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                    END
                CLOSE DiscountRows;
                DEALLOCATE DiscountRows;
            END
        IF @CreateFirstEnrollment = 1
            BEGIN
                DECLARE @NewEnrollmentId AS INT;
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, UsesTransport, Remarks, BillingStartDate, BillingReady)
                VALUES                 (@AdmissionId, @PlacementAcademicYearId, @PlacementClassSectionId, @PlacementEnrollmentDate, @PlacementLeavingDate, @PlacementStatus, @PlacementUsesTransport, N'First placement verified from physical school record on Admission form; no fees generated.', NULL, 0);
                SET @NewEnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, NewClassSectionId, NewStatus, Remarks, UserId)
                VALUES                       (@NewEnrollmentId, @AdmissionId, N'VERIFIED_FIRST_PLACEMENT', CONVERT (DATE, SYSDATETIME()), @PlacementClassSectionId, @PlacementStatus, N'Confirmed from physical school record.', @UserId);
                IF @StoredStatus <> @PlacementStatus
                   OR ISNULL(@StoredWithdrawalDate, '00010101') <> ISNULL(@PlacementLeavingDate, '00010101')
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = @PlacementStatus,
                               WithdrawalDate = @PlacementLeavingDate,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @StoredStatus, @PlacementStatus, N'Physical record verification', COALESCE (@PlacementLeavingDate, @PlacementEnrollmentDate), N'First verified enrollment recorded on Admission form.', @UserId);
                    END
            END
IF @CompleteRegistration=1 EXEC dbo.CompleteQuickAdmission @UserId,@AdmissionId,@AdmissionNumber;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
    SELECT @AdmissionId AS AdmissionId;
END
GO

-- dbo.SaveUser
-- SaveUser
CREATE OR ALTER PROCEDURE dbo.SaveUser
@UserId INT, @Username NVARCHAR (100), @RoleId INT, @TargetUserId INT=0, @MustChangePassword BIT=1, @PasswordHash VARBINARY (64)=NULL, @PasswordSalt VARBINARY (32)=NULL, @RoleIdsJson NVARCHAR (MAX)=NULL
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Users.Manage', N'System.Administration';
    DECLARE @IsNewUser AS BIT = CASE WHEN @TargetUserId = 0 THEN 1 ELSE 0 END;
    IF NULLIF (LTRIM(RTRIM(@Username)), N'') IS NULL
        THROW 57101, 'Username is required.', 1;
    DECLARE @AdministratorRoleId AS INT;
    SELECT   TOP (1) @AdministratorRoleId = RoleId
    FROM     dbo.Roles
    WHERE    RoleName = N'Administrator'
    ORDER BY RoleId;
    IF @AdministratorRoleId IS NULL
        THROW 57222, 'The Administrator role is not available.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users AS U
               WHERE  U.UserId = @TargetUserId
                      AND (U.RoleId = @AdministratorRoleId OR EXISTS(SELECT 1 FROM dbo.UserRoles UR
                           WHERE UR.UserId=U.UserId AND UR.RoleId=@AdministratorRoleId)))
        BEGIN
            THROW 57223, 'The Administrator account is protected and cannot be edited as a normal user.', 1;
        END
    DECLARE @SelectedRoles TABLE (
        RoleId INT NOT NULL PRIMARY KEY);
    IF NULLIF (LTRIM(RTRIM(@RoleIdsJson)), N'') IS NOT NULL
        BEGIN
            IF ISNULL(ISJSON(@RoleIdsJson),0) <> 1 OR LEFT(LTRIM(@RoleIdsJson),1)<>N'['
                THROW 57224, 'The supplied role list is not valid JSON.', 1;
            IF EXISTS(SELECT 1 FROM OPENJSON(@RoleIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,[value]) IS NULL OR TRY_CONVERT(int,[value])<=0)
                THROW 57413,'Role selections must be an array of positive integer IDs.',1;
            INSERT INTO @SelectedRoles (RoleId)
            SELECT DISTINCT TRY_CONVERT (INT, [value])
            FROM   OPENJSON (@RoleIdsJson)
            WHERE  TRY_CONVERT (INT, [value]) IS NOT NULL;
        END
    ELSE
        IF @RoleId IS NOT NULL
           AND @RoleId > 0
            BEGIN
                INSERT  INTO @SelectedRoles (RoleId)
                VALUES                     (@RoleId);
            END
    IF EXISTS (SELECT 1
               FROM   @SelectedRoles AS SR
                      LEFT OUTER JOIN
                      dbo.Roles AS R
                      ON R.RoleId = SR.RoleId
               WHERE  R.RoleId IS NULL OR R.RoleName NOT IN(N'Administrator',N'Accountant'))
        THROW 57226, 'One or more selected roles are invalid.', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedRoles AS SR
               WHERE  SR.RoleId = @AdministratorRoleId)
        THROW 57227, 'The Administrator role is reserved for the system administrator account.', 1;
    BEGIN TRANSACTION;
    IF @TargetUserId = 0
        BEGIN
            IF @PasswordHash IS NULL
               OR @PasswordSalt IS NULL
                THROW 57102, 'A password is required when creating a user.', 1;
            DECLARE @PrimaryRoleId AS INT;
            SELECT   TOP (1) @PrimaryRoleId = RoleId
            FROM     @SelectedRoles
            ORDER BY RoleId;
            IF @PrimaryRoleId IS NULL
                BEGIN
                    SELECT   TOP (1) @PrimaryRoleId = RoleId
                    FROM     dbo.Roles
                    WHERE    RoleId <> @AdministratorRoleId
                    ORDER BY RoleId;
                    IF @PrimaryRoleId IS NULL
                        THROW 57228, 'At least one non-Administrator role must exist in the system before a user can be created.', 1;
                END
            INSERT  dbo.Users (Username, PasswordHash, PasswordSalt, RoleId, MustChangePassword)
            VALUES           (LTRIM(RTRIM(@Username)), @PasswordHash, @PasswordSalt, @PrimaryRoleId, @MustChangePassword);
            SET @TargetUserId = CONVERT (INT, SCOPE_IDENTITY());
        END
    ELSE
        BEGIN
            UPDATE dbo.Users
            SET    Username           = LTRIM(RTRIM(@Username)),
                   RoleId             = COALESCE ((SELECT   TOP (1) RoleId
                                                   FROM     @SelectedRoles
                                                   ORDER BY RoleId), RoleId),
                   MustChangePassword = @MustChangePassword,
                   PasswordHash       = COALESCE (@PasswordHash, PasswordHash),
                   PasswordSalt       = COALESCE (@PasswordSalt, PasswordSalt),
                   UpdatedAt          = SYSDATETIME()
            WHERE  UserId = @TargetUserId;
            IF @@ROWCOUNT = 0
                THROW 57104, 'The selected user account was not found.', 1;
        END
    DELETE dbo.UserRoles
    WHERE  UserId = @TargetUserId;
    INSERT INTO dbo.UserRoles (UserId, RoleId)
    SELECT @TargetUserId,
           RoleId
    FROM   @SelectedRoles;
COMMIT TRANSACTION;
    SELECT @TargetUserId AS UserId;
END
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

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'59',SYSDATETIME(),SUSER_SNAME(),N'Retire remaining generic audit writers; preserve operational history, preview guards and existing audit readers');
GO
