
-- UpdateSportsCertificate
CREATE   PROCEDURE dbo.UpdateSportsCertificate
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
    DECLARE @OldValues AS NVARCHAR (MAX);
    SELECT @AdmissionId = SC.AdmissionId,
           @OldValues = (SELECT SC.SportsCertificateId,
                                SC.AdmissionId,
                                SC.SportName,
                                SC.Performance,
                                SC.CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @SportsCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @SportsCertificateId AS SportsCertificateId,
                                @AdmissionId AS AdmissionId,
                                @SportName AS SportName,
                                @Performance AS Performance,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'SPORTS_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'SportsCertificates', @RecordId = @RecordId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @SportsCertificateId AS SportsCertificateId,
           @AdmissionId AS AdmissionId;
END



