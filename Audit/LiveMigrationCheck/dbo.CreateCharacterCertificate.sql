
-- CreateCharacterCertificate
CREATE   PROCEDURE dbo.CreateCharacterCertificate
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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @CertificateRecordId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @CertificateRecordId AS CertificateRecordId,
                                @IssueId AS CharacterCertificateIssueId,
                                @AdmissionId AS AdmissionId,
                                @StudentName AS StudentName,
                                @FatherName AS FatherName,
                                @LastExamYear AS LastExamYear,
                                @RollNumber AS RollNumber,
                                @Remarks AS Remarks,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'CHARACTER_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'CharacterCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @CertificateRecordId AS CertificateRecordId,
           @IssueId AS CharacterCertificateIssueId,
           @AdmissionId AS AdmissionId;
END



