-- Run once on a DEDICATED update-service database, never on FMSC_DB.
-- No school student, academic or fee records are held by this service.
-- Execute with a privileged migration account; the running broker should have
-- only SELECT/INSERT/UPDATE on these two tables.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.UpdateSchools', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UpdateSchools
    (
        SchoolId uniqueidentifier NOT NULL CONSTRAINT PK_UpdateSchools PRIMARY KEY,
        DisplayName nvarchar(160) NOT NULL,
        AdminKeyHash binary(32) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_UpdateSchools_IsActive DEFAULT (1),
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_UpdateSchools_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc datetime2(3) NOT NULL CONSTRAINT DF_UpdateSchools_UpdatedUtc DEFAULT SYSUTCDATETIME()
    );
END;
IF OBJECT_ID(N'dbo.UpdateDevices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UpdateDevices
    (
        DeviceId uniqueidentifier NOT NULL CONSTRAINT PK_UpdateDevices PRIMARY KEY,
        SchoolId uniqueidentifier NOT NULL,
        DisplayName nvarchar(100) NOT NULL,
        DeviceKeyHash binary(32) NOT NULL,
        CreatedUtc datetime2(3) NOT NULL CONSTRAINT DF_UpdateDevices_CreatedUtc DEFAULT SYSUTCDATETIME(),
        LastSeenUtc datetime2(3) NULL,
        RevokedUtc datetime2(3) NULL,
        CONSTRAINT FK_UpdateDevices_Schools FOREIGN KEY (SchoolId) REFERENCES dbo.UpdateSchools(SchoolId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UpdateDevices') AND name = N'IX_UpdateDevices_SchoolId')
    CREATE INDEX IX_UpdateDevices_SchoolId ON dbo.UpdateDevices(SchoolId, CreatedUtc);
COMMIT;
