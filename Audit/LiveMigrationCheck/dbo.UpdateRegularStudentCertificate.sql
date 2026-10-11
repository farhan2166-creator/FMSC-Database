
-- UpdateRegularStudentCertificate
CREATE   PROCEDURE dbo.UpdateRegularStudentCertificate
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
    DECLARE @OldValues AS NVARCHAR (MAX);
    SELECT @AdmissionId = RSC.AdmissionId,
           @OldValues = (SELECT RSC.RegularStudentCertificateId,
                                RSC.AdmissionId,
                                RSC.ClassName,
                                RSC.CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @RegularStudentCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @RegularStudentCertificateId AS RegularStudentCertificateId,
                                @AdmissionId AS AdmissionId,
                                @ClassName AS ClassName,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'REGULAR_STUDENT_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'RegularStudentCertificates', @RecordId = @RecordId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RegularStudentCertificateId AS RegularStudentCertificateId,
           @AdmissionId AS AdmissionId;
END



