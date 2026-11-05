-- Second small audit-retirement batch: regular-student and sports certificates.
-- Preserve permissions, validation, transactions, certificate records and returned IDs.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 33 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>32
 THROW 51000,'Migration 33 requires schema 32.',1;
GO


-- CreateRegularStudentCertificate
CREATE OR ALTER PROCEDURE dbo.CreateRegularStudentCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE, @ClassName NVARCHAR (100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58500, 'A valid student is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58501, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58502, 'Certificate date cannot be in the future.', 1;
    IF @ClassName IS NULL
       OR LTRIM(RTRIM(@ClassName)) = N''
        THROW 58503, 'Class is required.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @DateOfBirth AS DATE, @RegularStudentCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @DateOfBirth = S.DateOfBirth
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58504, 'The student was not found.', 1;
    IF @DateOfBirth IS NULL
        THROW 58505, 'This student does not have a date of birth on record. Update the student''s record before creating a certificate.', 1;
    INSERT  dbo.RegularStudentCertificates (AdmissionId, StudentName, FatherName, DateOfBirth, ClassName, CertificateDate, CreatedByUserId)
    VALUES                                (@AdmissionId, @StudentName, @FatherName, @DateOfBirth, LTRIM(RTRIM(@ClassName)), @CertificateDate, @UserId);
    SET @RegularStudentCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @RegularStudentCertificateId AS RegularStudentCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

-- UpdateRegularStudentCertificate
CREATE OR ALTER PROCEDURE dbo.UpdateRegularStudentCertificate
@UserId INT, @RegularStudentCertificateId INT, @CertificateDate DATE, @ClassName NVARCHAR (100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @RegularStudentCertificateId <= 0
        THROW 58510, 'A valid regular student certificate is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58511, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58512, 'Certificate date cannot be in the future.', 1;
    IF @ClassName IS NULL
       OR LTRIM(RTRIM(@ClassName)) = N''
        THROW 58513, 'Class is required.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT;
    SELECT @AdmissionId = RSC.AdmissionId
    FROM   dbo.RegularStudentCertificates AS RSC WITH (UPDLOCK, HOLDLOCK)
    WHERE  RSC.RegularStudentCertificateId = @RegularStudentCertificateId;
    IF @AdmissionId IS NULL
        THROW 58514, 'The regular student certificate was not found.', 1;
    UPDATE dbo.RegularStudentCertificates
    SET    ClassName       = LTRIM(RTRIM(@ClassName)),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  RegularStudentCertificateId = @RegularStudentCertificateId;
    COMMIT TRANSACTION;
    SELECT @RegularStudentCertificateId AS RegularStudentCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

-- CreateSportsCertificate
CREATE OR ALTER PROCEDURE dbo.CreateSportsCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE, @SportName NVARCHAR (200), @Performance NVARCHAR (1000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58400, 'A valid student is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58401, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58402, 'Certificate date cannot be in the future.', 1;
    IF @SportName IS NULL
       OR LTRIM(RTRIM(@SportName)) = N''
        THROW 58403, 'Sport Name is required.', 1;
    IF @Performance IS NULL
       OR LTRIM(RTRIM(@Performance)) = N''
        THROW 58404, 'Performance is required.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @SportsCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58405, 'The student was not found.', 1;
    INSERT  dbo.SportsCertificates (AdmissionId, StudentName, FatherName, SportName, Performance, CertificateDate, CreatedByUserId)
    VALUES                        (@AdmissionId, @StudentName, @FatherName, LTRIM(RTRIM(@SportName)), LTRIM(RTRIM(@Performance)), @CertificateDate, @UserId);
    SET @SportsCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @SportsCertificateId AS SportsCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

-- UpdateSportsCertificate
CREATE OR ALTER PROCEDURE dbo.UpdateSportsCertificate
@UserId INT, @SportsCertificateId INT, @CertificateDate DATE, @SportName NVARCHAR (200), @Performance NVARCHAR (1000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @SportsCertificateId <= 0
        THROW 58410, 'A valid sports certificate is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58411, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58412, 'Certificate date cannot be in the future.', 1;
    IF @SportName IS NULL
       OR LTRIM(RTRIM(@SportName)) = N''
        THROW 58413, 'Sport Name is required.', 1;
    IF @Performance IS NULL
       OR LTRIM(RTRIM(@Performance)) = N''
        THROW 58414, 'Performance is required.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT;
    SELECT @AdmissionId = SC.AdmissionId
    FROM   dbo.SportsCertificates AS SC WITH (UPDLOCK, HOLDLOCK)
    WHERE  SC.SportsCertificateId = @SportsCertificateId;
    IF @AdmissionId IS NULL
        THROW 58415, 'The sports certificate was not found.', 1;
    UPDATE dbo.SportsCertificates
    SET    SportName       = LTRIM(RTRIM(@SportName)),
           Performance     = LTRIM(RTRIM(@Performance)),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  SportsCertificateId = @SportsCertificateId;
    COMMIT TRANSACTION;
    SELECT @SportsCertificateId AS SportsCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>32',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>32',N'FROM dbo.DatabaseVersion)<>33');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'33',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only writes and snapshots in regular-student and sports certificate procedures');
GO
