-- Larger small-procedure audit-retirement batch: eight focused procedures.
-- Preserve permissions, validation, transactions, operational history and returned results.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 37 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>36
 THROW 51000,'Migration 37 requires schema 36.',1;
GO






CREATE OR ALTER PROCEDURE dbo.VerifyLegacyStudent @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND LegacySetupPending=1)
  THROW 59730,'This is not a legacy record awaiting setup.',1;
 IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WITH(UPDLOCK,HOLDLOCK) WHERE CompletedAt IS NOT NULL)
  THROW 59731,'Legacy setup is already complete.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND (NULLIF(LTRIM(RTRIM(StudentName)),N'') IS NULL OR AdmissionNumber IS NULL))
  THROW 59732,'Complete the student name and admission number first.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Active') AND
  (SELECT COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Active')<>1
  THROW 59733,'An active student needs exactly one verified academic placement.',1;
 UPDATE dbo.Students SET LegacyVerifiedAt=SYSDATETIME(),LegacyVerifiedBy=@UserId WHERE AdmissionId=@AdmissionId;
 COMMIT;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CompleteQuickAdmission @UserId int,@AdmissionId int,@AdmissionNumber varchar(40)=NULL AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Admit';
 BEGIN TRAN;
 DECLARE @Date date,@Enrollment int;
 SELECT @Date=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Pending' AND QuickReference IS NOT NULL AND AdmissionNumber IS NULL;
 IF @Date IS NULL THROW 59701,'Select a pending registration. It may already have been completed or cancelled.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND (NULLIF(LTRIM(RTRIM(StudentName)),N'') IS NULL OR
  NULLIF(LTRIM(RTRIM(FatherName)),N'') IS NULL OR Gender IS NULL OR
  NULLIF(LTRIM(RTRIM(Address)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(PlaceOfBirth)),N'') IS NULL))
  THROW 59702,'Complete and save the required details in Quick Registration review before confirming admission.',1;
 SELECT @Enrollment=EnrollmentId FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Pending';
 IF @Enrollment IS NULL THROW 59703,'The pending academic placement could not be found.',1;
 EXEC dbo.ReserveAdmissionNumber @AdmissionNumber OUTPUT;
 DECLARE @Registration nvarchar(40);
 EXEC dbo.GetNextRegistrationNumber @RegistrationDate=@Date,@RegistrationNumber=@Registration OUTPUT;
 UPDATE dbo.Students SET AdmissionNumber=@AdmissionNumber,RegistrationNumber=@Registration,Status=N'Active',UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 UPDATE dbo.Enrollments SET Status=N'Active',PlacementConfirmedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 -- Same placement and fee assignments: opening bills already issued under the registration remain untouched.
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
 SELECT EnrollmentId,AdmissionId,N'ADMISSION',@Date,ClassSectionId,N'Pending',N'Active',N'Quick registration completed',@UserId FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment;
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,N'Pending',N'Active',N'Admission completed',@Date,@UserId);
 COMMIT;
 SELECT @AdmissionNumber AdmissionNumber;
END;
GO

-- DeleteStudentFeeAssignment
CREATE OR ALTER PROCEDURE dbo.DeleteStudentFeeAssignment
@UserId INT, @StudentFeeAssignmentId INT
AS
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
    COMMIT TRANSACTION;
END
GO

-- SetStudentAdmissionNumber
CREATE OR ALTER PROCEDURE dbo.SetStudentAdmissionNumber
@UserId INT, @AdmissionId INT, @AdmissionNumber VARCHAR (40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @@TRANCOUNT = 0
        THROW 59205, 'Save the admission number together with student details.', 1;
    SET @AdmissionNumber = dbo.NormalizeAdmissionNumber(@AdmissionNumber);
    IF @AdmissionNumber IS NULL
        THROW 59203, 'Use a positive admission number with an optional letter suffix, such as 1 or 1a.', 1;
    DECLARE @Last AS BIGINT;
    SELECT @Last = LastNumber
    FROM   dbo.AdmissionNumberCounter WITH (UPDLOCK, HOLDLOCK)
    WHERE  CounterId = 1;
    IF @Last IS NULL THROW 59202, 'The admission number counter is missing.', 1;
    DECLARE @OldNumber AS VARCHAR (40);
    SELECT @OldNumber = AdmissionNumber
    FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @AdmissionId;
    IF @OldNumber IS NULL
        THROW 51710, 'The selected student was not found.', 1;
    IF @OldNumber = @AdmissionNumber
        RETURN;
    IF EXISTS (SELECT 1
               FROM   dbo.Students
               WHERE  AdmissionNumber = @AdmissionNumber
                      AND AdmissionId <> @AdmissionId)
        THROW 59204, 'This admission number is already assigned to another student. Choose a different number.', 1;
    UPDATE dbo.Students
    SET    AdmissionNumber = @AdmissionNumber,
           UpdatedAt       = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    -- All allocators lock the counter first, so renumbering and allocation stay serialized.
    -- Letter suffixes contribute their numeric base: 6590a belongs to register number 6590.
    DECLARE @HighestAssigned BIGINT;
    SELECT @HighestAssigned = ISNULL(MAX(TRY_CONVERT(BIGINT,
        LEFT(AdmissionNumber, PATINDEX('%[^0-9]%', AdmissionNumber + 'x') - 1))), 0)
    FROM dbo.Students;
    UPDATE dbo.AdmissionNumberCounter SET LastNumber = @HighestAssigned WHERE CounterId = 1;
END
GO

-- SetEnrollmentBillingStartDate
CREATE OR ALTER PROCEDURE dbo.SetEnrollmentBillingStartDate
@UserId INT, @EnrollmentId INT, @BillingStartDate DATE, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF @BillingStartDate IS NULL
        THROW 58001, 'A billing start date is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 58002, 'A reason is required for this change.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT, @EnrollmentDate AS DATE, @OldBillingStartDate AS DATE, @Status AS NVARCHAR (30);
    SELECT @AdmissionId = E.AdmissionId,
           @EnrollmentDate = E.EnrollmentDate,
           @OldBillingStartDate = E.BillingStartDate,
           @Status = E.Status
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @AdmissionId IS NULL
        BEGIN
            ROLLBACK;
            THROW 58003, 'The selected enrollment does not exist.', 1;
        END
    IF @BillingStartDate < @EnrollmentDate
        BEGIN
            ROLLBACK;
            THROW 58004, 'Billing start date cannot be earlier than the enrollment date.', 1;
        END
    UPDATE dbo.Enrollments
    SET    BillingStartDate = @BillingStartDate,
           UpdatedAt        = SYSDATETIME()
    WHERE  EnrollmentId = @EnrollmentId;
    COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @OldBillingStartDate AS OldBillingStartDate,
           @BillingStartDate AS NewBillingStartDate;
END
GO

-- SaveRole
CREATE OR ALTER PROCEDURE dbo.SaveRole
@UserId INT, @RoleName NVARCHAR (50), @RoleId INT=0, @Description NVARCHAR (250)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAnyPermission @UserId, N'System.Roles.Manage', N'System.Administration';
    IF @RoleId<=0 OR LTRIM(RTRIM(ISNULL(@RoleName,N'')))<>N'Accountant'
       OR NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE RoleId=@RoleId AND RoleName=N'Accountant')
        THROW 57410,'Only the existing Accountant role may be edited. Administrator always has full authority; creating or renaming roles is disabled.',1;
    IF NULLIF (LTRIM(RTRIM(@RoleName)), N'') IS NULL
        THROW 57110, 'Role name is required.', 1;
    IF @RoleId <> 0
       AND EXISTS (SELECT 1
                   FROM   dbo.Roles
                   WHERE  RoleId = @RoleId
                          AND RoleName = N'Administrator')
        BEGIN
            THROW 57228, 'The Administrator role is protected.', 1;
        END
    IF EXISTS (SELECT 1
               FROM   dbo.Roles
               WHERE  RoleName = LTRIM(RTRIM(@RoleName))
                      AND RoleId <> @RoleId)
        BEGIN
            THROW 57229, 'A role with this name already exists.', 1;
        END
    BEGIN TRANSACTION;
    IF @RoleId = 0
        BEGIN
            INSERT  dbo.Roles (RoleName, Description)
            VALUES           (LTRIM(RTRIM(@RoleName)), NULLIF (LTRIM(RTRIM(@Description)), N''));
            SET @RoleId = CONVERT (INT, SCOPE_IDENTITY());
        END
    ELSE
        BEGIN
            UPDATE dbo.Roles
            SET    RoleName    = LTRIM(RTRIM(@RoleName)),
                   Description = NULLIF (LTRIM(RTRIM(@Description)), N'')
            WHERE  RoleId = @RoleId;
            IF @@ROWCOUNT = 0
                THROW 57111, 'The selected role was not found.', 1;
        END
    COMMIT TRANSACTION;
    SELECT @RoleId AS RoleId;
END
GO

-- SaveSchoolProfile
CREATE OR ALTER PROCEDURE dbo.SaveSchoolProfile
@UserId INT, @SchoolName NVARCHAR (200), @AddressLine NVARCHAR (500), @City NVARCHAR (100), @ContactNumber NVARCHAR (30), @Email NVARCHAR (150), @LogoImage VARBINARY (MAX), @LogoFileName NVARCHAR (255), @LogoContentType NVARCHAR (100), @FeeProrationCutoffDay INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    IF @SchoolName IS NULL
       OR LTRIM(RTRIM(@SchoolName)) = N''
        THROW 59000, 'School name is required.', 1;
    IF @FeeProrationCutoffDay IS NOT NULL
       AND @FeeProrationCutoffDay <> 0
       AND @FeeProrationCutoffDay NOT BETWEEN 1 AND 28
        THROW 59001, 'The fee proration cutoff day must be between 1 and 28, or left blank.', 1;
    IF @FeeProrationCutoffDay = 0
        SET @FeeProrationCutoffDay = NULL;
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1
               FROM   dbo.SchoolProfile
               WHERE  SchoolProfileId = 1)
        BEGIN
            UPDATE dbo.SchoolProfile
            SET    SchoolName            = LTRIM(RTRIM(@SchoolName)),
                   AddressLine           = NULLIF (LTRIM(RTRIM(@AddressLine)), N''),
                   City                  = NULLIF (LTRIM(RTRIM(@City)), N''),
                   ContactNumber         = NULLIF (LTRIM(RTRIM(@ContactNumber)), N''),
                   Email                 = NULLIF (LTRIM(RTRIM(@Email)), N''),
                   LogoImage             = @LogoImage,
                   LogoFileName          = NULLIF (LTRIM(RTRIM(@LogoFileName)), N''),
                   LogoContentType       = NULLIF (LTRIM(RTRIM(@LogoContentType)), N''),
                   FeeProrationCutoffDay = @FeeProrationCutoffDay,
                   UpdatedAt             = SYSDATETIME(),
                   UpdatedByUserId       = @UserId
            WHERE  SchoolProfileId = 1;
        END
    ELSE
        BEGIN
            INSERT  dbo.SchoolProfile (SchoolProfileId, SchoolName, AddressLine, City, ContactNumber, Email, LogoImage, LogoFileName, LogoContentType, FeeProrationCutoffDay, CreatedByUserId, UpdatedByUserId)
            VALUES                   (1, LTRIM(RTRIM(@SchoolName)), NULLIF (LTRIM(RTRIM(@AddressLine)), N''), NULLIF (LTRIM(RTRIM(@City)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Email)), N''), @LogoImage, NULLIF (LTRIM(RTRIM(@LogoFileName)), N''), NULLIF (LTRIM(RTRIM(@LogoContentType)), N''), @FeeProrationCutoffDay, @UserId, @UserId);
        END
    COMMIT TRANSACTION;
    SELECT 1 AS SchoolProfileId;
END
GO

-- SaveFeeConfiguration
CREATE OR ALTER PROCEDURE dbo.SaveFeeConfiguration
@UserId INT, @CampusId INT, @ClassId INT, @FeeTypeId INT, @Amount DECIMAL (12, 2), @Frequency NVARCHAR (30), @FeeConfigurationId INT, @Remarks NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @CampusId)
        THROW 56310, 'Please select an campus.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Classes
                   WHERE  ClassId = @ClassId)
        THROW 56312, 'Please select an class.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeTypes
                   WHERE  FeeTypeId = @FeeTypeId)
        THROW 56313, 'Please select an fee type.', 1;
    IF @Amount < 0
        THROW 56314, 'Fee amount cannot be negative.', 1;
    DECLARE @ResultId AS INT;
    DECLARE @NewRemarks AS NVARCHAR (500) = NULLIF (LTRIM(RTRIM(@Remarks)), N'');
    BEGIN TRANSACTION;
    IF @FeeConfigurationId = 0
        BEGIN
            INSERT  dbo.FeeConfigurations (CampusId, ClassId, FeeTypeId, Amount, Frequency, Remarks)
            VALUES                       (@CampusId, @ClassId, @FeeTypeId, @Amount, @Frequency, @NewRemarks);
            SET @ResultId = CONVERT (INT, SCOPE_IDENTITY());
        END
    ELSE
        BEGIN
            DECLARE @OldCampusId AS INT, @OldClassId AS INT, @OldFeeTypeId AS INT, @OldAmount AS DECIMAL (12, 2), @OldFrequency AS NVARCHAR (30), @OldRemarks AS NVARCHAR (500);
            SELECT @OldCampusId = CampusId,
                   @OldClassId = ClassId,
                   @OldFeeTypeId = FeeTypeId,
                   @OldAmount = Amount,
                   @OldFrequency = Frequency,
                   @OldRemarks = Remarks
            FROM   dbo.FeeConfigurations WITH (UPDLOCK, HOLDLOCK)
            WHERE  FeeConfigurationId = @FeeConfigurationId;
            IF @@ROWCOUNT = 0
                THROW 56315, 'The selected fee configuration was not found.', 1;
            SET @ResultId = @FeeConfigurationId;
            IF @OldCampusId = @CampusId
               AND @OldClassId = @ClassId
               AND @OldFeeTypeId = @FeeTypeId
               AND @OldAmount = @Amount
               AND ISNULL(@OldFrequency, N'') = ISNULL(@Frequency, N'')
               AND ISNULL(@OldRemarks, N'') = ISNULL(@NewRemarks, N'')
                BEGIN
                    UPDATE dbo.FeeConfigurations
                    SET    UpdatedAt = SYSDATETIME()
                    WHERE  FeeConfigurationId = @FeeConfigurationId;
                END
            ELSE
                BEGIN
                    UPDATE dbo.FeeConfigurations
                    SET    CampusId  = @CampusId,
                           ClassId   = @ClassId,
                           FeeTypeId = @FeeTypeId,
                           Amount    = @Amount,
                           Frequency = @Frequency,
                           Remarks   = @NewRemarks,
                           UpdatedAt = SYSDATETIME()
                    WHERE  FeeConfigurationId = @FeeConfigurationId;
                END
        END
    COMMIT TRANSACTION;
    SELECT @ResultId AS FeeConfigurationId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>36',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>36',N'FROM dbo.DatabaseVersion)<>37');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'37',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only work in eight small account, setup, admission and fee procedures');
GO
