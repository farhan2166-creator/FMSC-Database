-- Exact outcome recovery for financial adjustments and bank-certificate creates.
-- Apply after migration 5, with a verified backup and application sessions closed.
-- Existing business procedures, financial records and permissions are unchanged.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Version nvarchar(20)=(SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion WITH(UPDLOCK,HOLDLOCK) ORDER BY VersionId DESC);
    IF @Version=N'6' BEGIN COMMIT TRANSACTION; RETURN; END;
    IF @Version IS NULL OR @Version<>N'5' THROW 57600,'Operation recovery migration requires database version 5.',1;
    IF OBJECT_ID(N'dbo.MutationRequests') IS NOT NULL THROW 57601,'MutationRequests already exists at version 5. Review the schema before migrating.',1;
    CREATE TABLE dbo.MutationRequests
    (
        RequestId uniqueidentifier NOT NULL CONSTRAINT PK_MutationRequests PRIMARY KEY,
        UserId int NOT NULL,
        Kind nvarchar(40) NOT NULL,
        PayloadHash binary(32) NOT NULL,
        FeePaymentId int NULL,
        BankCertificateId int NULL,
        ResultJson nvarchar(max) NOT NULL,
        CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_MutationRequests_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MutationRequests_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_MutationRequests_Payments FOREIGN KEY(FeePaymentId) REFERENCES dbo.FeePayments(FeePaymentId),
        CONSTRAINT FK_MutationRequests_BankCertificates FOREIGN KEY(BankCertificateId) REFERENCES dbo.BankAccountOpeningCertificates(BankAccountOpeningCertificateId),
        CONSTRAINT CK_MutationRequests_Kind CHECK ((Kind=N'FeeAdjustment' AND FeePaymentId IS NOT NULL AND BankCertificateId IS NULL) OR (Kind=N'BankCertificateCreate' AND BankCertificateId IS NOT NULL AND FeePaymentId IS NULL)),
        CONSTRAINT CK_MutationRequests_Result CHECK (ISJSON(ResultJson)=1)
    );
    INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'6',SYSDATETIME(),SUSER_SNAME(),N'Exact save-request markers for adjustment and bank-certificate outcome recovery.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
