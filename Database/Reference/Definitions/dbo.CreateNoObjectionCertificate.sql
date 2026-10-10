SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- CreateNoObjectionCertificate
CREATE   PROCEDURE dbo.CreateNoObjectionCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58600, 'A valid student is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58601, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58602, 'Certificate date cannot be in the future.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @NoObjectionCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58603, 'The student was not found.', 1;
    INSERT  dbo.NoObjectionCertificates (AdmissionId, StudentName, FatherName, CertificateDate, CreatedByUserId)
    VALUES                             (@AdmissionId, @StudentName, @FatherName, @CertificateDate, @UserId);
    SET @NoObjectionCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @NoObjectionCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
                                @AdmissionId AS AdmissionId,
                                @StudentName AS StudentName,
                                @FatherName AS FatherName,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'NO_OBJECTION_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'NoObjectionCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
           @AdmissionId AS AdmissionId;
END



GO
