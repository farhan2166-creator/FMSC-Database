
/* =====================================================================
   5. dbo.MarkDateOfBirthCertificateCollected
   ===================================================================== */

CREATE   PROCEDURE dbo.MarkDateOfBirthCertificateCollected
    @UserId INT,
    @DateOfBirthCertificateIssueId INT,
    @CollectedBy NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    IF @DateOfBirthCertificateIssueId <= 0
        THROW 58700, 'A valid certificate issue is required.', 1;

    IF NULLIF(LTRIM(RTRIM(@CollectedBy)), N'') IS NULL
        THROW 58701, 'The name of the person collecting the certificate is required.', 1;

    UPDATE dbo.DateOfBirthCertificateIssues
    SET
        CollectedAt = SYSDATETIME(),
        CollectedBy = LTRIM(RTRIM(@CollectedBy))
    WHERE DateOfBirthCertificateIssueId = @DateOfBirthCertificateIssueId
      AND CollectedAt IS NULL;

    IF @@ROWCOUNT <> 1
        THROW 58702, 'The certificate issue was not found or has already been collected.', 1;

    SELECT
        DateOfBirthCertificateIssueId,
        CertificateRecordId,
        IssuedAt,
        IssuedByUserId,
        CollectedAt,
        CollectedBy
    FROM dbo.DateOfBirthCertificateIssues
    WHERE DateOfBirthCertificateIssueId = @DateOfBirthCertificateIssueId;
END;

