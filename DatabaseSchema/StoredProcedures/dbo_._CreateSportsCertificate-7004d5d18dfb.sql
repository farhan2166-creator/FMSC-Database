-- Module body only. See companion XML for signatures and relationships.

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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @SportsCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @SportsCertificateId AS SportsCertificateId,
                                @AdmissionId AS AdmissionId,
                                @StudentName AS StudentName,
                                @FatherName AS FatherName,
                                @SportName AS SportName,
                                @Performance AS Performance,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'SPORTS_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'SportsCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @SportsCertificateId AS SportsCertificateId,
           @AdmissionId AS AdmissionId;
END
