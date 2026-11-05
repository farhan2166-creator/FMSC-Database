-- Third small audit-retirement batch: staff employment and bank certificates.
-- Preserve permissions, validation, transactions, certificate records and returned IDs.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 34 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>33
 THROW 51000,'Migration 34 requires schema 33.',1;
GO



-- CreateRegularLecturerCertificate
CREATE OR ALTER PROCEDURE dbo.CreateRegularLecturerCertificate
@UserId INT, @StaffId INT, @RoleTitle NVARCHAR (30), @SubjectTaught NVARCHAR (100), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @StaffId <= 0
        THROW 58700, 'A valid staff member is required.', 1;
    IF @RoleTitle IS NULL
       OR LTRIM(RTRIM(@RoleTitle)) = N''
        THROW 58701, 'Designation (Teacher/Lecturer) is required.', 1;
    IF @SubjectTaught IS NULL
       OR LTRIM(RTRIM(@SubjectTaught)) = N''
        THROW 58702, 'Subject taught is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58703, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58704, 'Certificate date cannot be in the future.', 1;
    DECLARE @StaffName AS NVARCHAR (150), @FatherName AS NVARCHAR (150), @Gender AS NVARCHAR (20), @RegularLecturerCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StaffName = S.StaffName,
           @FatherName = S.FatherName,
           @Gender = S.Gender
    FROM   dbo.Staff AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.StaffId = @StaffId;
    IF @StaffName IS NULL
        THROW 58705, 'The staff member was not found.', 1;
    INSERT  dbo.RegularLecturerCertificates (StaffId, StaffName, FatherName, Gender, RoleTitle, SubjectTaught, CertificateDate, CreatedByUserId)
    VALUES                                 (@StaffId, @StaffName, @FatherName, @Gender, LTRIM(RTRIM(@RoleTitle)), LTRIM(RTRIM(@SubjectTaught)), @CertificateDate, @UserId);
    SET @RegularLecturerCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @RegularLecturerCertificateId AS RegularLecturerCertificateId,
           @StaffId AS StaffId;
END
GO

-- UpdateRegularLecturerCertificate
CREATE OR ALTER PROCEDURE dbo.UpdateRegularLecturerCertificate
@UserId INT, @RegularLecturerCertificateId INT, @RoleTitle NVARCHAR (30), @SubjectTaught NVARCHAR (100), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @RegularLecturerCertificateId <= 0
        THROW 58710, 'A valid certificate is required.', 1;
    IF @RoleTitle IS NULL
       OR LTRIM(RTRIM(@RoleTitle)) = N''
        THROW 58711, 'Designation (Teacher/Lecturer) is required.', 1;
    IF @SubjectTaught IS NULL
       OR LTRIM(RTRIM(@SubjectTaught)) = N''
        THROW 58712, 'Subject taught is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58713, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58714, 'Certificate date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @StaffId AS INT;
    SELECT @StaffId = RLC.StaffId
    FROM   dbo.RegularLecturerCertificates AS RLC WITH (UPDLOCK, HOLDLOCK)
    WHERE  RLC.RegularLecturerCertificateId = @RegularLecturerCertificateId;
    IF @StaffId IS NULL
        THROW 58715, 'The certificate was not found.', 1;
    UPDATE dbo.RegularLecturerCertificates
    SET    RoleTitle       = LTRIM(RTRIM(@RoleTitle)),
           SubjectTaught   = LTRIM(RTRIM(@SubjectTaught)),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  RegularLecturerCertificateId = @RegularLecturerCertificateId;
    COMMIT TRANSACTION;
    SELECT @RegularLecturerCertificateId AS RegularLecturerCertificateId,
           @StaffId AS StaffId;
END
GO

-- CreateBankAccountOpeningCertificate
CREATE OR ALTER PROCEDURE dbo.CreateBankAccountOpeningCertificate
@UserId INT, @StaffId INT, @Designation NVARCHAR (100), @BankName NVARCHAR (100), @BranchName NVARCHAR (150), @MonthlyIncome DECIMAL (18, 2)=NULL, @Purpose NVARCHAR (200), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @StaffId <= 0
        THROW 58800, 'A valid staff member is required.', 1;
    IF @Designation IS NULL
       OR LTRIM(RTRIM(@Designation)) = N''
        THROW 58801, 'Designation is required.', 1;
    IF @BankName IS NULL
       OR LTRIM(RTRIM(@BankName)) = N''
        THROW 58802, 'Bank name is required.', 1;
    IF @BranchName IS NULL
       OR LTRIM(RTRIM(@BranchName)) = N''
        THROW 58803, 'Branch name is required.', 1;
    IF @Purpose IS NULL
       OR LTRIM(RTRIM(@Purpose)) = N''
        THROW 58804, 'Purpose is required.', 1;
    IF @MonthlyIncome IS NOT NULL
       AND @MonthlyIncome < 0
        THROW 58805, 'Monthly income cannot be negative.', 1;
    IF @CertificateDate IS NULL
        THROW 58806, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58807, 'Certificate date cannot be in the future.', 1;
    DECLARE @StaffName AS NVARCHAR (150), @FatherName AS NVARCHAR (150), @Gender AS NVARCHAR (20), @CNIC AS NVARCHAR (20), @BankAccountOpeningCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StaffName = S.StaffName,
           @FatherName = S.FatherName,
           @Gender = S.Gender,
           @CNIC = S.CNIC
    FROM   dbo.Staff AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.StaffId = @StaffId;
    IF @StaffName IS NULL
        THROW 58808, 'The staff member was not found.', 1;
    INSERT  dbo.BankAccountOpeningCertificates (StaffId, StaffName, FatherName, Gender, CNIC, Designation, BankName, BranchName, MonthlyIncome, Purpose, CertificateDate, CreatedByUserId)
    VALUES                                    (@StaffId, @StaffName, @FatherName, @Gender, @CNIC, LTRIM(RTRIM(@Designation)), LTRIM(RTRIM(@BankName)), LTRIM(RTRIM(@BranchName)), NULLIF (@MonthlyIncome, 0), LTRIM(RTRIM(@Purpose)), @CertificateDate, @UserId);
    SET @BankAccountOpeningCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @BankAccountOpeningCertificateId AS BankAccountOpeningCertificateId,
           @StaffId AS StaffId;
END
GO

-- UpdateBankAccountOpeningCertificate
CREATE OR ALTER PROCEDURE dbo.UpdateBankAccountOpeningCertificate
@UserId INT, @BankAccountOpeningCertificateId INT, @Designation NVARCHAR (100), @BankName NVARCHAR (100), @BranchName NVARCHAR (150), @MonthlyIncome DECIMAL (18, 2)=NULL, @Purpose NVARCHAR (200), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @BankAccountOpeningCertificateId <= 0
        THROW 58810, 'A valid certificate is required.', 1;
    IF @Designation IS NULL
       OR LTRIM(RTRIM(@Designation)) = N''
        THROW 58811, 'Designation is required.', 1;
    IF @BankName IS NULL
       OR LTRIM(RTRIM(@BankName)) = N''
        THROW 58812, 'Bank name is required.', 1;
    IF @BranchName IS NULL
       OR LTRIM(RTRIM(@BranchName)) = N''
        THROW 58813, 'Branch name is required.', 1;
    IF @Purpose IS NULL
       OR LTRIM(RTRIM(@Purpose)) = N''
        THROW 58814, 'Purpose is required.', 1;
    IF @MonthlyIncome IS NOT NULL
       AND @MonthlyIncome < 0
        THROW 58815, 'Monthly income cannot be negative.', 1;
    IF @CertificateDate IS NULL
        THROW 58816, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58817, 'Certificate date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @StaffId AS INT;
    SELECT @StaffId = BAC.StaffId
    FROM   dbo.BankAccountOpeningCertificates AS BAC WITH (UPDLOCK, HOLDLOCK)
    WHERE  BAC.BankAccountOpeningCertificateId = @BankAccountOpeningCertificateId;
    IF @StaffId IS NULL
        THROW 58818, 'The certificate was not found.', 1;
    UPDATE dbo.BankAccountOpeningCertificates
    SET    Designation     = LTRIM(RTRIM(@Designation)),
           BankName        = LTRIM(RTRIM(@BankName)),
           BranchName      = LTRIM(RTRIM(@BranchName)),
           MonthlyIncome   = NULLIF (@MonthlyIncome, 0),
           Purpose         = LTRIM(RTRIM(@Purpose)),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  BankAccountOpeningCertificateId = @BankAccountOpeningCertificateId;
    COMMIT TRANSACTION;
    SELECT @BankAccountOpeningCertificateId AS BankAccountOpeningCertificateId,
           @StaffId AS StaffId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>33',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>33',N'FROM dbo.DatabaseVersion)<>34');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'34',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only writes and snapshots in staff employment and bank certificate procedures');
GO
