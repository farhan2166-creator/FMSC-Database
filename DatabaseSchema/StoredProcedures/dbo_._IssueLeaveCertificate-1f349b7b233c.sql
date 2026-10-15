-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.Leave';

    IF @LeaveRecordId <= 0
        THROW 57400,
            'A valid leave certificate is required.',
            1;

    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.LeaveCertificates
        WHERE LeaveRecordId = @LeaveRecordId
    )
        THROW 57401,
            'The leave certificate was not found.',
            1;

    DECLARE
        @LastIssueId INT,
        @LastCollectedAt DATETIME2;

    SELECT TOP (1)
        @LastIssueId = LCI.LeaveCertificateIssueId,
        @LastCollectedAt = LCI.CollectedAt
    FROM dbo.LeaveCertificateIssues AS LCI WITH (UPDLOCK, HOLDLOCK)
    WHERE LCI.LeaveRecordId = @LeaveRecordId
    ORDER BY
        LCI.IssuedAt DESC,
        LCI.LeaveCertificateIssueId DESC;

    IF @LastIssueId IS NOT NULL
       AND @LastCollectedAt IS NULL
        THROW 57402,
            'The currently issued certificate has not been collected yet.',
            1;

    -- NEW: only 1 Original (created by CreateLeaveCertificate) + 2
    -- Duplicates are allowed per leave certificate. A 3rd "issue again"
    -- attempt is refused.
    DECLARE @DuplicateCount INT;

    SELECT @DuplicateCount = COUNT(*)
    FROM dbo.LeaveCertificateIssues
    WHERE LeaveRecordId = @LeaveRecordId
      AND IssueType = N'Duplicate';

    IF @DuplicateCount >= 2
        THROW 57403,
            'This certificate has already been issued as a duplicate twice (the maximum allowed). No further copies can be issued.',
            1;

    INSERT dbo.LeaveCertificateIssues
    (
        LeaveRecordId,
        IssuedAt,
        IssuedByUserId,
        Remarks,
        IssueType
    )
    VALUES
    (
        @LeaveRecordId,
        SYSDATETIME(),
        @UserId,
        @Remarks,
        N'Duplicate'
    );

    DECLARE @IssueId INT =
        CONVERT(INT, SCOPE_IDENTITY());

    COMMIT;

    SELECT
        @IssueId AS LeaveCertificateIssueId,
        @LeaveRecordId AS LeaveRecordId;
END;
