-- Blank matrix cells remove class-rate templates. Zero remains a configured rate.
-- Student assignments, posted bills, payments and recovery/history records remain.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 42 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>41
 THROW 51000,'Migration 42 requires schema 41.',1;
GO
CREATE OR ALTER PROCEDURE dbo.ApplyFeeConfigurationPlan
 @UserId int,@Plan dbo.FeeConfigurationPlanType READONLY,@ClearedConfigurations nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Configuration.Manage';
 DECLARE @Rows TABLE(RowNum int IDENTITY(1,1) PRIMARY KEY,FeeConfigurationId int NOT NULL,
  CampusId int NOT NULL,ClassId int NOT NULL,FeeTypeId int NOT NULL,Amount decimal(12,2) NOT NULL,
  Frequency nvarchar(30) NULL,Remarks nvarchar(500) NULL);
 DECLARE @Cleared TABLE(FeeConfigurationId int,CampusId int,ClassId int,FeeTypeId int,
  ExpectedAmount decimal(12,2),ExpectedFrequency nvarchar(30),ExpectedRemarks nvarchar(500));
 DECLARE @i int=1,@ConfigId int,@CampusId int,@ClassId int,@FeeTypeId int,@Amount decimal(12,2),
  @Frequency nvarchar(30),@Remarks nvarchar(500),@Total int,@Upserts int,@Clears int,@Locked int,@Message nvarchar(2048);
 BEGIN TRY
  BEGIN TRANSACTION;
  INSERT @Rows(FeeConfigurationId,CampusId,ClassId,FeeTypeId,Amount,Frequency,Remarks)
   SELECT FeeConfigurationId,CampusId,ClassId,FeeTypeId,Amount,Frequency,Remarks FROM @Plan;
  SET @ClearedConfigurations=COALESCE(NULLIF(LTRIM(RTRIM(@ClearedConfigurations)),N''),N'[]');
  IF ISJSON(@ClearedConfigurations)<>1 OR LEFT(LTRIM(@ClearedConfigurations),1)<>N'['
   THROW 53171,'The cleared-rate plan is invalid. Reload the grid and try again.',1;
  IF EXISTS(SELECT 1 FROM OPENJSON(@ClearedConfigurations) WHERE type<>5)
   THROW 53171,'The cleared-rate plan must contain class-rate records.',1;
  INSERT @Cleared
   SELECT FeeConfigurationId,CampusId,ClassId,FeeTypeId,ExpectedAmount,ExpectedFrequency,ExpectedRemarks
   FROM OPENJSON(@ClearedConfigurations) WITH(FeeConfigurationId int,CampusId int,ClassId int,FeeTypeId int,
    ExpectedAmount decimal(12,2),ExpectedFrequency nvarchar(30),ExpectedRemarks nvarchar(500));
  IF EXISTS(SELECT 1 FROM @Cleared WHERE FeeConfigurationId IS NULL OR FeeConfigurationId<=0 OR CampusId IS NULL OR CampusId<=0
    OR ClassId IS NULL OR ClassId<=0 OR FeeTypeId IS NULL OR FeeTypeId<=0 OR ExpectedAmount IS NULL OR ExpectedAmount<0)
   THROW 53171,'A cleared-rate record is incomplete. Reload the grid and try again.',1;
  SELECT @Upserts=COUNT(*) FROM @Rows;
  SELECT @Clears=COUNT(*) FROM @Cleared;
  SET @Total=@Upserts+@Clears;
  IF @Total=0 THROW 53160,'There is nothing to apply - the plan is empty.',1;
  IF EXISTS(SELECT 1 FROM @Rows WHERE Amount<0 OR Amount<>ROUND(Amount,0))
   THROW 53161,'Enter a non-negative whole-rupee class rate. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Rows R WHERE NOT EXISTS(SELECT 1 FROM dbo.Campuses C WHERE C.CampusId=R.CampusId))
   THROW 53162,'A plan row names a campus that does not exist. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Rows R WHERE NOT EXISTS(SELECT 1 FROM dbo.Classes C WHERE C.ClassId=R.ClassId))
   THROW 53163,'A plan row names a class that does not exist. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Rows R WHERE NOT EXISTS(SELECT 1 FROM dbo.FeeTypes F WHERE F.FeeTypeId=R.FeeTypeId))
   THROW 53164,'A plan row names a fee type that does not exist. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Rows GROUP BY CampusId,ClassId,FeeTypeId HAVING COUNT(*)>1)
   THROW 53166,'The plan contains duplicate class rates. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Cleared GROUP BY FeeConfigurationId HAVING COUNT(*)>1)
   OR EXISTS(SELECT 1 FROM @Cleared GROUP BY CampusId,ClassId,FeeTypeId HAVING COUNT(*)>1)
   THROW 53172,'The cleared-rate plan contains duplicates. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Rows R JOIN @Cleared C ON C.FeeConfigurationId=R.FeeConfigurationId
    OR (C.CampusId=R.CampusId AND C.ClassId=R.ClassId AND C.FeeTypeId=R.FeeTypeId))
   THROW 53172,'The same class rate cannot be saved and cleared together. Nothing was applied.',1;
  -- Hold the affected configuration identities and ranges until the whole plan commits.
  SELECT @Locked=COUNT(*) FROM dbo.FeeConfigurations F WITH(UPDLOCK,HOLDLOCK)
   WHERE EXISTS(SELECT 1 FROM @Rows R WHERE (R.CampusId=F.CampusId AND R.ClassId=F.ClassId AND R.FeeTypeId=F.FeeTypeId)
       OR R.FeeConfigurationId=F.FeeConfigurationId)
    OR EXISTS(SELECT 1 FROM @Cleared C WHERE C.FeeConfigurationId=F.FeeConfigurationId
       OR (C.CampusId=F.CampusId AND C.ClassId=F.ClassId AND C.FeeTypeId=F.FeeTypeId));
  IF EXISTS(SELECT 1 FROM @Rows R WHERE R.FeeConfigurationId<>0 AND NOT EXISTS(
    SELECT 1 FROM dbo.FeeConfigurations F WHERE F.FeeConfigurationId=R.FeeConfigurationId
     AND F.CampusId=R.CampusId AND F.ClassId=R.ClassId AND F.FeeTypeId=R.FeeTypeId))
   THROW 53165,'A class rate changed or no longer exists. Reload the grid. Nothing was applied.',1;
  IF EXISTS(SELECT 1 FROM @Cleared C WHERE NOT EXISTS(SELECT 1 FROM dbo.FeeConfigurations F
    WHERE F.FeeConfigurationId=C.FeeConfigurationId AND F.CampusId=C.CampusId AND F.ClassId=C.ClassId AND F.FeeTypeId=C.FeeTypeId
     AND F.Amount=C.ExpectedAmount AND ISNULL(F.Frequency,N'')=ISNULL(C.ExpectedFrequency,N'')
     AND ISNULL(F.Remarks,N'')=ISNULL(C.ExpectedRemarks,N'')))
   THROW 53173,'A cleared class rate changed since the grid was loaded. Reload the grid. Nothing was applied.',1;
  DELETE F FROM dbo.FeeConfigurations F JOIN @Cleared C ON C.FeeConfigurationId=F.FeeConfigurationId;
  WHILE @i<=@Upserts
  BEGIN
   SELECT @ConfigId=FeeConfigurationId,@CampusId=CampusId,@ClassId=ClassId,@FeeTypeId=FeeTypeId,
    @Amount=Amount,@Frequency=Frequency,@Remarks=Remarks FROM @Rows WHERE RowNum=@i;
   EXEC dbo.SaveFeeConfiguration @UserId=@UserId,@CampusId=@CampusId,@ClassId=@ClassId,@FeeTypeId=@FeeTypeId,
    @Amount=@Amount,@Frequency=@Frequency,@FeeConfigurationId=@ConfigId,@Remarks=@Remarks;
   SET @i=@i+1;
  END;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  IF ERROR_NUMBER() BETWEEN 53160 AND 53179 THROW;
  SET @Message=N'Nothing was applied - the whole plan was rolled back. '+ERROR_MESSAGE();
  THROW 53170,@Message,1;
 END CATCH;
 SELECT @Total TotalRows,@Total Succeeded,0 Failed,CAST(1 AS bit) AppliedAtomically;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'42',SYSDATETIME(),SUSER_SNAME(),N'Persist cleared class fee rates atomically with matrix edits');
GO
