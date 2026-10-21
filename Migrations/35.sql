-- Fourth small audit-retirement batch: character and date-of-birth certificates.
-- Preserve permissions, validation, transactions, certificate records and returned IDs.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 35 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>34
 THROW 51000,'Migration 35 requires schema 34.',1;
GO




-- CreateCharacterCertificate
CREATE OR ALTER PROCEDURE dbo.CreateCharacterCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE, @RollNumber NVARCHAR (50)=NULL, @Remarks NVARCHAR (1000)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58000, 'A valid student is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58001, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58002, 'Certificate date cannot be in the future.', 1;
    IF @RollNumber IS NULL
       OR LTRIM(RTRIM(@RollNumber)) = N''
        THROW 58003, 'BISE Roll Number is required.', 1;
    IF @Remarks IS NULL
       OR LTRIM(RTRIM(@Remarks)) = N''
        THROW 58004, 'Remarks are required.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.CharacterCertificates
               WHERE  AdmissionId = @AdmissionId)
        THROW 58005, 'This student already has a character certificate.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @Gender AS NVARCHAR (20), @AcademicYearId AS INT, @LastExamYear AS INT, @CertificateRecordId AS INT, @IssueId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @Gender = S.Gender
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58006, 'The student was not found.', 1;
    SELECT   TOP (1) @AcademicYearId = E.AcademicYearId
    FROM     dbo.Enrollments AS E
    WHERE    E.AdmissionId = @AdmissionId
    ORDER BY E.EnrollmentDate DESC, E.EnrollmentId DESC;
    IF @AcademicYearId IS NULL
        THROW 58007, 'The student has no enrollment record to determine the examination year from.', 1;
    SELECT @LastExamYear = YEAR(AY.EndDate)
    FROM   dbo.AcademicYears AS AY
    WHERE  AY.AcademicYearId = @AcademicYearId;
    IF @LastExamYear IS NULL
        THROW 58008, 'The academic year for this student could not be resolved.', 1;
    INSERT  dbo.CharacterCertificates (AdmissionId, StudentName, FatherName, Gender, LastExamYear, RollNumber, Remarks, CertificateDate, CreatedByUserId)
    VALUES                           (@AdmissionId, @StudentName, @FatherName, @Gender, @LastExamYear, LTRIM(RTRIM(@RollNumber)), LTRIM(RTRIM(@Remarks)), @CertificateDate, @UserId);
    SET @CertificateRecordId = CONVERT (INT, SCOPE_IDENTITY());
    INSERT  dbo.CharacterCertificateIssues (CertificateRecordId, IssuedAt, IssuedByUserId)
    VALUES                                (@CertificateRecordId, SYSDATETIME(), @UserId);
    SET @IssueId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @CertificateRecordId AS CertificateRecordId,
           @IssueId AS CharacterCertificateIssueId,
           @AdmissionId AS AdmissionId;
END
GO

-- CreateDateOfBirthCertificate
CREATE OR ALTER PROCEDURE dbo.CreateDateOfBirthCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE
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
    IF EXISTS (SELECT 1
               FROM   dbo.DateOfBirthCertificates
               WHERE  AdmissionId = @AdmissionId)
        THROW 58503, 'This student already has a date of birth certificate.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @Gender AS NVARCHAR (20), @DateOfBirth AS DATE, @CertificateRecordId AS INT, @IssueId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @Gender = S.Gender,
           @DateOfBirth = S.DateOfBirth
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58504, 'The student was not found.', 1;
    IF @DateOfBirth IS NULL
        THROW 58505, 'The student does not have a date of birth on record. Update the student''s record first.', 1;
    INSERT  dbo.DateOfBirthCertificates (AdmissionId, StudentName, FatherName, Gender, DateOfBirth, CertificateDate, CreatedByUserId)
    VALUES                             (@AdmissionId, @StudentName, @FatherName, @Gender, @DateOfBirth, @CertificateDate, @UserId);
    SET @CertificateRecordId = CONVERT (INT, SCOPE_IDENTITY());
    INSERT  dbo.DateOfBirthCertificateIssues (CertificateRecordId, IssuedAt, IssuedByUserId)
    VALUES                                  (@CertificateRecordId, SYSDATETIME(), @UserId);
    SET @IssueId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @CertificateRecordId AS CertificateRecordId,
           @IssueId AS DateOfBirthCertificateIssueId,
           @AdmissionId AS AdmissionId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>34',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>34',N'FROM dbo.DatabaseVersion)<>35');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'35',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only writes in character and DOB certificate creation; preserve issuance history');
GO
