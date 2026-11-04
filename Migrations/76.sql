-- Credit lifecycle: posting bills must never settle them using held advance.
-- Owner decision (4 Nov 2026): use student advance only in an explicitly
-- confirmed Fee Collection transaction with a saved collection receipt.
-- Existing automatic credit settlements and fallback readers remain untouched.
-- Keep this compatibility entry point because older posting/activation procedures
-- still call it; removing callers can be done when those procedures are revised.
SET NOCOUNT ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 76 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>75
 THROW 51000,'Migration 76 requires schema 75.',1;
GO

CREATE OR ALTER PROCEDURE dbo.ApplyStudentCredit_Internal
    @AdmissionId int,
    @UserId int=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @@TRANCOUNT=0
        THROW 54900,'Credit application requires a transaction.',1;
    -- Deliberate no-op: callers can post charges but cannot settle fees here.
    -- Fee Collection alone applies chosen credit via dbo.ApplyCollectionCredit
    -- and issues dbo.IssueCollectionReceipt in the same transaction.
    -- Do not delete historical CR-GUID payments or their legacy read path.
END;
GO

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'76',SYSDATETIME(),SUSER_SNAME(),
       N'Separate fee posting from credit settlement; advance applies only in confirmed collection');
GO
