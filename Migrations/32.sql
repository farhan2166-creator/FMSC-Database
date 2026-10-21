-- First small audit-retirement batch: own-account edits and no-objection certificates.
-- Preserve permissions, validation, transactions, certificate records and returned IDs.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 32 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>31
 THROW 51000,'Migration 32 requires schema 31.',1;
GO

-- ChangeOwnPassword
CREATE OR ALTER PROCEDURE dbo.ChangeOwnPassword
@UserId INT, @PasswordHash VARBINARY (64), @PasswordSalt VARBINARY (32)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    IF @PasswordHash IS NULL
       OR DATALENGTH(@PasswordHash) <> 64
        THROW 57001, 'The supplied password hash is invalid.', 1;
    IF @PasswordSalt IS NULL
       OR DATALENGTH(@PasswordSalt) <> 32
        THROW 57002, 'The supplied password salt is invalid.', 1;
    BEGIN TRANSACTION;
    UPDATE dbo.Users
    SET    PasswordHash       = @PasswordHash,
           PasswordSalt       = @PasswordSalt,
           MustChangePassword = 0,
           UpdatedAt          = SYSDATETIME()
    WHERE  UserId = @UserId;
    IF @@ROWCOUNT = 0
        THROW 57003, 'The user account is inactive or was not found.', 1;
    COMMIT TRANSACTION;
END
GO

-- RenameOwnUsername
CREATE OR ALTER PROCEDURE dbo.RenameOwnUsername
@UserId INT, @NewUsername NVARCHAR (100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    SET @NewUsername = LTRIM(RTRIM(@NewUsername));
    IF NULLIF (@NewUsername, N'') IS NULL
        THROW 57301, 'Username is required.', 1;
    IF LEN(@NewUsername) > 100
        THROW 57302, 'Username is too long (100 characters maximum).', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.Users
               WHERE  Username = @NewUsername
                      AND UserId <> @UserId)
        THROW 57303, 'That username is already in use by another account.', 1;
    BEGIN TRANSACTION;
    UPDATE dbo.Users
    SET    Username  = @NewUsername,
           UpdatedAt = SYSDATETIME()
    WHERE  UserId = @UserId;
    IF @@ROWCOUNT = 0
        THROW 57304, 'The user account is inactive or was not found.', 1;
    COMMIT TRANSACTION;
END
GO

-- CreateNoObjectionCertificate
CREATE OR ALTER PROCEDURE dbo.CreateNoObjectionCertificate
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
    COMMIT TRANSACTION;
    SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

-- UpdateNoObjectionCertificate
CREATE OR ALTER PROCEDURE dbo.UpdateNoObjectionCertificate
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
    SELECT @AdmissionId = NOC.AdmissionId
    FROM   dbo.NoObjectionCertificates AS NOC WITH (UPDLOCK, HOLDLOCK)
    WHERE  NOC.NoObjectionCertificateId = @NoObjectionCertificateId;
    IF @AdmissionId IS NULL
        THROW 58613, 'The NOC was not found.', 1;
    UPDATE dbo.NoObjectionCertificates
    SET    CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  NoObjectionCertificateId = @NoObjectionCertificateId;
    COMMIT TRANSACTION;
    SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>31',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>31',N'FROM dbo.DatabaseVersion)<>32');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'32',SYSDATETIME(),SUSER_SNAME(),N'Retire audit-only writes and snapshots in four small account and NOC procedures');
GO
