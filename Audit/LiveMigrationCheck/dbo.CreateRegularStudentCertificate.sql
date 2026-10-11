
-- CreateRegularStudentCertificate
CREATE   PROCEDURE dbo.CreateRegularStudentCertificate
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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @RegularStudentCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @RegularStudentCertificateId AS RegularStudentCertificateId,
                                @AdmissionId AS AdmissionId,
                                @StudentName AS StudentName,
                                @FatherName AS FatherName,
                                @DateOfBirth AS DateOfBirth,
                                @ClassName AS ClassName,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'REGULAR_STUDENT_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'RegularStudentCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RegularStudentCertificateId AS RegularStudentCertificateId,
           @AdmissionId AS AdmissionId;
END



