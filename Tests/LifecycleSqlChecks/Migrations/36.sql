-- Fifth audit-retirement batch: provisional certificates.
-- Preserve permissions, validation, transactions, certificate records and returned IDs.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 36 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>35
 THROW 51000,'Migration 36 requires schema 35.',1;
GO





-- CreateProvisionalCertificate
CREATE OR ALTER PROCEDURE dbo.CreateProvisionalCertificate
@UserId INT, @AdmissionId INT, @ExamType NVARCHAR (10), @ExamYear INT, @BoardName NVARCHAR (150), @RollNumber NVARCHAR (30), @MarksObtained INT, @MarksOutOf INT, @Grade NVARCHAR (10), @OtherSubjects NVARCHAR (300), @CertificateDate DATE, @SubjectIds NVARCHAR (MAX)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58800, 'A valid student is required.', 1;
    IF @ExamType NOT IN (N'SSC', N'HSSC1', N'HSSC2')
        THROW 58801, 'A valid exam type is required.', 1;
    IF @ExamYear IS NULL
       OR @ExamYear < 1990
       OR @ExamYear > YEAR(SYSDATETIME()) + 1
        THROW 58802, 'A valid exam year is required.', 1;
    IF @BoardName IS NULL
       OR LTRIM(RTRIM(@BoardName)) = N''
        THROW 58803, 'Board name is required.', 1;
    IF @RollNumber IS NULL
       OR LTRIM(RTRIM(@RollNumber)) = N''
        THROW 58804, 'Roll number is required.', 1;
    IF @MarksObtained IS NULL
       OR @MarksObtained < 0
        THROW 58805, 'Marks obtained is required.', 1;
    IF @MarksOutOf IS NULL
       OR @MarksOutOf <= 0
        THROW 58806, 'Marks out of is required.', 1;
    IF @MarksObtained > @MarksOutOf
        THROW 58807, 'Marks obtained cannot exceed marks out of.', 1;
    IF @CertificateDate IS NULL
        THROW 58808, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58809, 'Certificate date cannot be in the future.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @Gender AS NVARCHAR (20), @DateOfBirth AS DATE, @ProvisionalCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @Gender = S.Gender,
           @DateOfBirth = S.DateOfBirth
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58810, 'The student was not found.', 1;
    INSERT  dbo.ProvisionalCertificates (AdmissionId, StudentName, FatherName, Gender, DateOfBirth, ExamType, ExamYear, BoardName, RollNumber, MarksObtained, MarksOutOf, Grade, OtherSubjects, CertificateDate, CreatedByUserId)
    VALUES                             (@AdmissionId, @StudentName, @FatherName, @Gender, @DateOfBirth, @ExamType, @ExamYear, LTRIM(RTRIM(@BoardName)), LTRIM(RTRIM(@RollNumber)), @MarksObtained, @MarksOutOf, NULLIF (LTRIM(RTRIM(@Grade)), N''), NULLIF (LTRIM(RTRIM(@OtherSubjects)), N''), @CertificateDate, @UserId);
    SET @ProvisionalCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    IF @SubjectIds IS NOT NULL
       AND LTRIM(RTRIM(@SubjectIds)) <> N''
        BEGIN
            INSERT dbo.ProvisionalCertificateSubjects (ProvisionalCertificateId, SubjectId, SubjectName, DisplayOrder)
            SELECT @ProvisionalCertificateId,
                   SU.SubjectId,
                   SU.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY SU.SubjectName, SU.SubjectId)
            FROM   STRING_SPLIT (@SubjectIds, N',') AS SPL
                   INNER JOIN
                   dbo.Subjects AS SU
                   ON SU.SubjectId = TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value)))
            WHERE  TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value))) IS NOT NULL;
        END
    COMMIT TRANSACTION;
    SELECT @ProvisionalCertificateId AS ProvisionalCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

-- UpdateProvisionalCertificate
CREATE OR ALTER PROCEDURE dbo.UpdateProvisionalCertificate
@UserId INT, @ProvisionalCertificateId INT, @ExamType NVARCHAR (10), @ExamYear INT, @BoardName NVARCHAR (150), @RollNumber NVARCHAR (30), @MarksObtained INT, @MarksOutOf INT, @Grade NVARCHAR (10), @OtherSubjects NVARCHAR (300), @CertificateDate DATE, @SubjectIds NVARCHAR (MAX)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @ProvisionalCertificateId <= 0
        THROW 58820, 'A valid certificate is required.', 1;
    IF @ExamType NOT IN (N'SSC', N'HSSC1', N'HSSC2')
        THROW 58821, 'A valid exam type is required.', 1;
    IF @ExamYear IS NULL
       OR @ExamYear < 1990
       OR @ExamYear > YEAR(SYSDATETIME()) + 1
        THROW 58822, 'A valid exam year is required.', 1;
    IF @BoardName IS NULL
       OR LTRIM(RTRIM(@BoardName)) = N''
        THROW 58823, 'Board name is required.', 1;
    IF @RollNumber IS NULL
       OR LTRIM(RTRIM(@RollNumber)) = N''
        THROW 58824, 'Roll number is required.', 1;
    IF @MarksObtained IS NULL
       OR @MarksObtained < 0
        THROW 58825, 'Marks obtained is required.', 1;
    IF @MarksOutOf IS NULL
       OR @MarksOutOf <= 0
        THROW 58826, 'Marks out of is required.', 1;
    IF @MarksObtained > @MarksOutOf
        THROW 58827, 'Marks obtained cannot exceed marks out of.', 1;
    IF @CertificateDate IS NULL
        THROW 58828, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58829, 'Certificate date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT;
    SELECT @AdmissionId = PC.AdmissionId
    FROM   dbo.ProvisionalCertificates AS PC WITH (UPDLOCK, HOLDLOCK)
    WHERE  PC.ProvisionalCertificateId = @ProvisionalCertificateId;
    IF @AdmissionId IS NULL
        THROW 58830, 'The certificate was not found.', 1;
    UPDATE dbo.ProvisionalCertificates
    SET    ExamType        = @ExamType,
           ExamYear        = @ExamYear,
           BoardName       = LTRIM(RTRIM(@BoardName)),
           RollNumber      = LTRIM(RTRIM(@RollNumber)),
           MarksObtained   = @MarksObtained,
           MarksOutOf      = @MarksOutOf,
           Grade           = NULLIF (LTRIM(RTRIM(@Grade)), N''),
           OtherSubjects   = NULLIF (LTRIM(RTRIM(@OtherSubjects)), N''),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    DECLARE @SavedSubjectOrder TABLE (
        SubjectId    INT,
        DisplayOrder INT);
    INSERT @SavedSubjectOrder
    SELECT SubjectId,
           DisplayOrder
    FROM   dbo.ProvisionalCertificateSubjects
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    DELETE dbo.ProvisionalCertificateSubjects
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    IF @SubjectIds IS NOT NULL
       AND LTRIM(RTRIM(@SubjectIds)) <> N''
        BEGIN
            INSERT dbo.ProvisionalCertificateSubjects (ProvisionalCertificateId, SubjectId, SubjectName, DisplayOrder)
            SELECT @ProvisionalCertificateId,
                   SU.SubjectId,
                   SU.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY COALESCE (SO.DisplayOrder, 2147483647), SU.SubjectName, SU.SubjectId)
            FROM   STRING_SPLIT (@SubjectIds, N',') AS SPL
                   INNER JOIN
                   dbo.Subjects AS SU
                   ON SU.SubjectId = TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value)))
                   LEFT OUTER JOIN
                   @SavedSubjectOrder AS SO
                   ON SO.SubjectId = SU.SubjectId
            WHERE  TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value))) IS NOT NULL;
        END
    COMMIT TRANSACTION;
    SELECT @ProvisionalCertificateId AS ProvisionalCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>35',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>35',N'FROM dbo.DatabaseVersion)<>36');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'36',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only snapshots and writes in provisional certificates; preserve subjects and order');
GO
