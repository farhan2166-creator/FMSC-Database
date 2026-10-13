-- Quick Registration save requests: exact recovery after a lost commit reply.
-- Apply after migration 3, with a verified backup and application sessions closed.
-- Student creation and its request marker are committed together by the C# writer.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Version nvarchar(20)=(SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion WITH(UPDLOCK,HOLDLOCK) ORDER BY VersionId DESC);
    IF @Version=N'4' BEGIN COMMIT TRANSACTION; RETURN; END;
    IF @Version IS NULL OR @Version<>N'3'
        THROW 57500,'Quick Registration recovery migration requires database version 3.',1;
    IF OBJECT_ID(N'dbo.QuickRegistrationRequests') IS NOT NULL
        THROW 57501,'QuickRegistrationRequests already exists at version 3. Review the schema before migrating.',1;
    IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope')
        THROW 57502,'Registration recovery scope already exists at version 3. Review the schema before migrating.',1;
    CREATE TABLE dbo.QuickRegistrationRequests
    (
        RequestId uniqueidentifier NOT NULL CONSTRAINT PK_QuickRegistrationRequests PRIMARY KEY,
        UserId int NOT NULL,
        PayloadHash binary(32) NOT NULL,
        AdmissionId int NOT NULL CONSTRAINT UQ_QuickRegistrationRequests_AdmissionId UNIQUE,
        CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_QuickRegistrationRequests_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_QuickRegistrationRequests_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_QuickRegistrationRequests_Students FOREIGN KEY(AdmissionId) REFERENCES dbo.Students(AdmissionId)
    );
    -- Stable across connection aliases and restarts; another database installation
    -- receives a different identity. Backups retain their database lineage.
    DECLARE @Scope uniqueidentifier=NEWID();
    EXEC sys.sp_addextendedproperty @name=N'FMSC_QuickRegistrationScope',@value=@Scope;
    INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'4',SYSDATETIME(),SUSER_SNAME(),N'Exact Quick Registration save-request recovery and duplicate-request protection.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
