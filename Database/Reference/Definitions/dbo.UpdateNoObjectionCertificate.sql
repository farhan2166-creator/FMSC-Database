SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- UpdateNoObjectionCertificate
CREATE   PROCEDURE dbo.UpdateNoObjectionCertificate
@UserId INT, @NoObjectionCertificateId INT, @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @NoObjectionCertificateId <= 0
        THROW 58610, 'A valid NOC is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58611, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58612, 'Certificate date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT;
    DECLARE @OldValues AS NVARCHAR (MAX);
    SELECT @AdmissionId = NOC.AdmissionId,
           @OldValues = (SELECT NOC.NoObjectionCertificateId,
                                NOC.AdmissionId,
                                NOC.CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
    FROM   dbo.NoObjectionCertificates AS NOC WITH (UPDLOCK, HOLDLOCK)
    WHERE  NOC.NoObjectionCertificateId = @NoObjectionCertificateId;
    IF @AdmissionId IS NULL
        THROW 58613, 'The NOC was not found.', 1;
    UPDATE dbo.NoObjectionCertificates
    SET    CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  NoObjectionCertificateId = @NoObjectionCertificateId;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @NoObjectionCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
                                @AdmissionId AS AdmissionId,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'NO_OBJECTION_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'NoObjectionCertificates', @RecordId = @RecordId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
           @AdmissionId AS AdmissionId;
END



GO
