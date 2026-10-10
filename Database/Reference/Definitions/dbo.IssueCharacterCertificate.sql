SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   4. dbo.IssueCharacterCertificate ("Issue Again")
   ===================================================================== */

CREATE   PROCEDURE dbo.IssueCharacterCertificate
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
        THROW 58100, 'A valid character certificate is required.', 1;

    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.CharacterCertificates
        WHERE CertificateRecordId = @CertificateRecordId
    )
        THROW 58101, 'The character certificate was not found.', 1;

    DECLARE
        @LastIssueId INT,
        @LastCollectedAt DATETIME2;

    SELECT TOP (1)
        @LastIssueId = CCI.CharacterCertificateIssueId,
        @LastCollectedAt = CCI.CollectedAt
    FROM dbo.CharacterCertificateIssues AS CCI WITH (UPDLOCK, HOLDLOCK)
    WHERE CCI.CertificateRecordId = @CertificateRecordId
    ORDER BY
        CCI.IssuedAt DESC,
        CCI.CharacterCertificateIssueId DESC;

    IF @LastIssueId IS NOT NULL
       AND @LastCollectedAt IS NULL
        THROW 58102, 'The currently issued certificate has not been collected yet.', 1;

    INSERT dbo.CharacterCertificateIssues
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
        @IssueId AS CharacterCertificateIssueId,
        @CertificateRecordId AS CertificateRecordId;
END;

GO
