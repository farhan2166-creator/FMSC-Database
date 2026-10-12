-- Optional one-time cleanup for an existing FMSC database or a disposable test copy.
-- Select the intended database explicitly before running. This does not touch school data.
-- Deploy the updated application's compatibility contract before this cleanup.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 59994, 'Select the intended FMSC database before running this cleanup.', 1;
IF OBJECT_ID(N'dbo.DatabaseVersion', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Students', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Enrollments', N'U') IS NULL
    THROW 59994, 'This database does not have the expected FMSC schema.', 1;
IF ISNULL((SELECT TOP (1) CONVERT(nvarchar(50), VersionNumber)
    FROM dbo.DatabaseVersion ORDER BY VersionId DESC), N'') <> N'3'
    THROW 59994, 'Review this cleanup against the selected database schema version first.', 1;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @ProcedureId int = OBJECT_ID(N'dbo.GetStudentAnnualFeeLedger', N'P');
    IF @ProcedureId IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies
                   WHERE referenced_id = @ProcedureId AND referencing_id <> @ProcedureId)
            THROW 59994, 'Another SQL object uses the legacy ledger. Review the dependency before removal.', 1;
        DROP PROCEDURE dbo.GetStudentAnnualFeeLedger;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
