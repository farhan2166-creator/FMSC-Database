-- Owner decision, 31 October 2026: used years retain their dates; names,
-- remarks and the Current selection remain editable. No existing rows rewritten.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 63 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>62
 THROW 51000,'Migration 63 requires schema 62.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetAcademicYears @UserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequireAuthenticatedUser @UserId;
 SELECT Y.*,CONVERT(bit,CASE WHEN
  EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AcademicYearId=Y.AcademicYearId) OR
  EXISTS(SELECT 1 FROM dbo.PromotionBatches P WHERE P.SourceAcademicYearId=Y.AcademicYearId OR P.DestinationAcademicYearId=Y.AcademicYearId)
  THEN 1 ELSE 0 END) AS DatesLocked
 FROM dbo.AcademicYears Y ORDER BY Y.StartDate DESC,Y.AcademicYearId DESC;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SaveAcademicYear
 @AcademicYearId int,@YearName nvarchar(50),@StartDate date,@EndDate date,
 @IsCurrent bit,@Remarks nvarchar(500),@UserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Calendar.Manage';
 SET @YearName=LTRIM(RTRIM(@YearName));
 IF @YearName IS NULL OR LEN(@YearName)<>9 OR SUBSTRING(@YearName,5,1)<>N'-'
  OR LEFT(@YearName,4) COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%'
  OR RIGHT(@YearName,4) COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%'
  OR TRY_CONVERT(int,LEFT(@YearName,4))<1000
  OR TRY_CONVERT(int,RIGHT(@YearName,4))<>TRY_CONVERT(int,LEFT(@YearName,4))+1
  THROW 56110,'Enter consecutive four-digit years, for example 2025-2026.',1;
 IF @StartDate IS NULL OR @EndDate IS NULL OR @EndDate<@StartDate
  THROW 56111,'Academic year end date cannot be earlier than its start date; both dates are required.',1;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  IF @OwnTransaction=1 BEGIN TRAN;
  -- HOLDLOCK keeps the reviewed calendar and usage stable through the write.
  DECLARE @OldStart date,@OldEnd date;
  IF @AcademicYearId<>0
  BEGIN
   SELECT @OldStart=StartDate,@OldEnd=EndDate FROM dbo.AcademicYears WITH(UPDLOCK,HOLDLOCK) WHERE AcademicYearId=@AcademicYearId;
   IF @OldStart IS NULL THROW 56113,'The selected academic year was not found.',1;
   IF (@OldStart<>@StartDate OR @OldEnd<>@EndDate) AND (
    EXISTS(SELECT 1 FROM dbo.Enrollments WITH(HOLDLOCK) WHERE AcademicYearId=@AcademicYearId) OR
    EXISTS(SELECT 1 FROM dbo.PromotionBatches WITH(HOLDLOCK) WHERE SourceAcademicYearId=@AcademicYearId OR DestinationAcademicYearId=@AcademicYearId))
    THROW 56116,'This academic year has student or promotion history. Its Start and End dates are locked; Name, Remarks and Current can still be changed.',1;
  END;
  IF EXISTS(SELECT 1 FROM dbo.AcademicYears WITH(UPDLOCK,HOLDLOCK) WHERE YearName=@YearName AND AcademicYearId<>@AcademicYearId)
   THROW 56112,'An academic year with this name already exists.',1;
  IF EXISTS(SELECT 1 FROM dbo.AcademicYears WITH(UPDLOCK,HOLDLOCK) WHERE AcademicYearId<>@AcademicYearId AND StartDate<=@EndDate AND EndDate>=@StartDate)
   THROW 56115,'These dates overlap an existing academic year. Academic years cannot cover the same calendar dates; adjust the start or end date.',1;
  IF @IsCurrent=1 UPDATE dbo.AcademicYears SET IsCurrent=0,UpdatedAt=SYSDATETIME() WHERE AcademicYearId<>@AcademicYearId AND IsCurrent=1;
  IF @AcademicYearId=0
  BEGIN
   INSERT dbo.AcademicYears(YearName,StartDate,EndDate,IsCurrent,Remarks) VALUES(@YearName,@StartDate,@EndDate,@IsCurrent,NULLIF(LTRIM(RTRIM(@Remarks)),N''));
   SET @AcademicYearId=CONVERT(int,SCOPE_IDENTITY());
  END
  ELSE UPDATE dbo.AcademicYears SET YearName=@YearName,StartDate=@StartDate,EndDate=@EndDate,IsCurrent=@IsCurrent,
   Remarks=NULLIF(LTRIM(RTRIM(@Remarks)),N''),UpdatedAt=SYSDATETIME() WHERE AcademicYearId=@AcademicYearId;
  IF @OwnTransaction=1 COMMIT;
  SELECT @AcademicYearId AS AcademicYearId;
 END TRY
 BEGIN CATCH
  IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'63',SYSDATETIME(),SUSER_SNAME(),N'Lock used academic-year dates; validate consecutive year names');
GO
