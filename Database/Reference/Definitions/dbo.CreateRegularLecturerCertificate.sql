SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- CreateRegularLecturerCertificate
CREATE   PROCEDURE dbo.CreateRegularLecturerCertificate
@UserId INT, @StaffId INT, @RoleTitle NVARCHAR (30), @SubjectTaught NVARCHAR (100), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @StaffId <= 0
        THROW 58700, 'A valid staff member is required.', 1;
    IF @RoleTitle IS NULL
       OR LTRIM(RTRIM(@RoleTitle)) = N''
        THROW 58701, 'Designation (Teacher/Lecturer) is required.', 1;
    IF @SubjectTaught IS NULL
       OR LTRIM(RTRIM(@SubjectTaught)) = N''
        THROW 58702, 'Subject taught is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58703, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58704, 'Certificate date cannot be in the future.', 1;
    DECLARE @StaffName AS NVARCHAR (150), @FatherName AS NVARCHAR (150), @Gender AS NVARCHAR (20), @RegularLecturerCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StaffName = S.StaffName,
           @FatherName = S.FatherName,
           @Gender = S.Gender
    FROM   dbo.Staff AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.StaffId = @StaffId;
    IF @StaffName IS NULL
        THROW 58705, 'The staff member was not found.', 1;
    INSERT  dbo.RegularLecturerCertificates (StaffId, StaffName, FatherName, Gender, RoleTitle, SubjectTaught, CertificateDate, CreatedByUserId)
    VALUES                                 (@StaffId, @StaffName, @FatherName, @Gender, LTRIM(RTRIM(@RoleTitle)), LTRIM(RTRIM(@SubjectTaught)), @CertificateDate, @UserId);
    SET @RegularLecturerCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @RegularLecturerCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @RegularLecturerCertificateId AS RegularLecturerCertificateId,
                                @StaffId AS StaffId,
                                @StaffName AS StaffName,
                                @FatherName AS FatherName,
                                @Gender AS Gender,
                                @RoleTitle AS RoleTitle,
                                @SubjectTaught AS SubjectTaught,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'REGULAR_LECTURER_CERTIFICATE', CONVERT (NVARCHAR (200), @StaffId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'RegularLecturerCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @RegularLecturerCertificateId AS RegularLecturerCertificateId,
           @StaffId AS StaffId;
END



GO
