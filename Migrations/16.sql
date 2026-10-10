-- Include the saved billing start in the lifecycle enrollment editor.
SET NOCOUNT ON;
DECLARE @definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.GetStudentEnrollmentsForLifecycle'));
IF @definition IS NULL THROW 57300,'GetStudentEnrollmentsForLifecycle is missing.',1;
IF @definition NOT LIKE '%E.BillingStartDate%'
BEGIN
 SET @definition=REPLACE(@definition,N'E.LeavingDate,',N'E.LeavingDate, E.BillingStartDate,');
 SET @definition=REPLACE(@definition,N'CREATE PROCEDURE',N'CREATE OR ALTER PROCEDURE');
 EXEC sys.sp_executesql @definition;
END;
GO
