SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   4. dbo.IssueDateOfBirthCertificate ("Issue Again")
   ===================================================================== */

CREATE   PROCEDURE dbo.IssueDateOfBirthCertificate
    @UserId INT,
    @CertificateRecordId INT,
    @Remarks NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    IF @CertificateRecordId <= 0
        THROW 58600, 'A valid date of birth certificate is required.', 1;

    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.DateOfBirthCertificates
        WHERE CertificateRecordId = @CertificateRecordId
    )
        THROW 58601, 'The date of birth certificate was not found.', 1;

    DECLARE
        @LastIssueId INT,
        @LastCollectedAt DATETIME2;

    SELECT TOP (1)
        @LastIssueId = DBI.DateOfBirthCertificateIssueId,
        @LastCollectedAt = DBI.CollectedAt
    FROM dbo.DateOfBirthCertificateIssues AS DBI WITH (UPDLOCK, HOLDLOCK)
    WHERE DBI.CertificateRecordId = @CertificateRecordId
    ORDER BY
        DBI.IssuedAt DESC,
        DBI.DateOfBirthCertificateIssueId DESC;

    IF @LastIssueId IS NOT NULL
       AND @LastCollectedAt IS NULL
        THROW 58602, 'The currently issued certificate has not been collected yet.', 1;

    INSERT dbo.DateOfBirthCertificateIssues
    (
        CertificateRecordId,
        IssuedAt,
        IssuedByUserId,
        Remarks
    )
    VALUES
    (
        @CertificateRecordId,
        SYSDATETIME(),
        @UserId,
        @Remarks
    );

    DECLARE @IssueId INT =
        CONVERT(INT, SCOPE_IDENTITY());

    COMMIT;

    SELECT
        @IssueId AS DateOfBirthCertificateIssueId,
        @CertificateRecordId AS CertificateRecordId;
END;

GO
