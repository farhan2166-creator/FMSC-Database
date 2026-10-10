SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- CreateDateOfBirthCertificate
CREATE   PROCEDURE dbo.CreateDateOfBirthCertificate
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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @CertificateRecordId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @CertificateRecordId AS CertificateRecordId,
                                @IssueId AS DateOfBirthCertificateIssueId,
                                @AdmissionId AS AdmissionId,
                                @StudentName AS StudentName,
                                @FatherName AS FatherName,
                                @DateOfBirth AS DateOfBirth,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'DATE_OF_BIRTH_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'DateOfBirthCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @CertificateRecordId AS CertificateRecordId,
           @IssueId AS DateOfBirthCertificateIssueId,
           @AdmissionId AS AdmissionId;
END



GO
