SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.MarkLeaveCertificateCollected
    @UserId INT,
    @LeaveCertificateIssueId INT,
    @CollectedBy NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.Leave';

    IF @LeaveCertificateIssueId <= 0
        THROW 57500,
            'A valid certificate issue is required.',
            1;

    IF NULLIF(LTRIM(RTRIM(@CollectedBy)), N'') IS NULL
        THROW 57501,
            'The name of the person collecting the certificate is required.',
            1;

    UPDATE dbo.LeaveCertificateIssues
    SET
        CollectedAt = SYSDATETIME(),
        CollectedBy = LTRIM(RTRIM(@CollectedBy))
    WHERE LeaveCertificateIssueId = @LeaveCertificateIssueId
      AND CollectedAt IS NULL;

    IF @@ROWCOUNT <> 1
        THROW 57502,
            'The certificate issue was not found or has already been collected.',
            1;

    SELECT
        LeaveCertificateIssueId,
        LeaveRecordId,
        IssuedAt,
        IssuedByUserId,
        CollectedAt,
        CollectedBy
    FROM dbo.LeaveCertificateIssues
    WHERE LeaveCertificateIssueId = @LeaveCertificateIssueId;
END;

GO
