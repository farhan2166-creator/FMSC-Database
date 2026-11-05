-- Delete only unused programs. No cascading class/student/history changes.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 72 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>71
 THROW 51000,'Migration 72 requires schema 71.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetAcademicProgramUsageCount
 @UserId int,@AcademicProgramId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'System.Setup';
 SELECT COUNT(*) FROM dbo.Classes WHERE AcademicProgramId=@AcademicProgramId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.DeleteAcademicProgram
 @UserId int,@AcademicProgramId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'System.Setup';
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @OrderLock int;
  EXEC @OrderLock=sys.sp_getapplock @Resource=N'SetupOrder:AcademicPrograms',
   @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
  IF @OrderLock<0 THROW 57401,'Unable to lock program ordering. Please try again.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.AcademicPrograms WITH(UPDLOCK,HOLDLOCK) WHERE AcademicProgramId=@AcademicProgramId)
   THROW 56422,'The selected program was not found. Refresh the list.',1;
  IF EXISTS(SELECT 1 FROM dbo.Classes WITH(UPDLOCK,HOLDLOCK) WHERE AcademicProgramId=@AcademicProgramId)
   THROW 56423,'This program has linked classes and cannot be deleted.',1;
  DELETE dbo.AcademicPrograms WHERE AcademicProgramId=@AcademicProgramId;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  IF ERROR_NUMBER()=547 THROW 56423,'This program is in use and cannot be deleted.',1;
  THROW;
 END CATCH
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'72',SYSDATETIME(),SUSER_SNAME(),N'Allow deletion of unused academic programs; preserve all linked classes and history');
GO
