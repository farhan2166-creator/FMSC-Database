SET NOCOUNT ON; SET XACT_ABORT ON;
CREATE TABLE #AcademicYears(AcademicYearId int IDENTITY PRIMARY KEY,YearName nvarchar(50),StartDate date,EndDate date,IsCurrent bit,Remarks nvarchar(500),UpdatedAt datetime2);
CREATE TABLE #Enrollments(AcademicYearId int,Status nvarchar(20));
CREATE TABLE #PromotionBatches(SourceAcademicYearId int,DestinationAcademicYearId int);
INSERT #AcademicYears(YearName,StartDate,EndDate,IsCurrent) VALUES
 (N'2021-2022','20210401','20220331',0),(N'2022-2023','20220401','20230331',0),
 (N'2023-2024','20230401','20240331',0),(N'2024-2025','20240401','20250331',0),
 (N'2025-2026','20250401','20260331',1);
INSERT #Enrollments VALUES(1,N'Left');
INSERT #PromotionBatches VALUES(3,4);
GO
CREATE PROCEDURE #RequirePermission @UserId int,@Permission nvarchar(100)
AS IF @UserId<>1 THROW 51000,'Permission denied.',1;
GO
CREATE PROCEDURE #RequireAuthenticatedUser @UserId int
AS IF @UserId<>1 THROW 51000,'Authentication required.',1;
GO
-- PRODUCTION PROCEDURES
CREATE TABLE #ReadYears(AcademicYearId int,YearName nvarchar(50),StartDate date,EndDate date,IsCurrent bit,Remarks nvarchar(500),UpdatedAt datetime2,DatesLocked bit);
INSERT #ReadYears EXEC #GetAcademicYears @UserId=1;
IF (SELECT COUNT(*) FROM #ReadYears WHERE DatesLocked=1)<>3 OR EXISTS(SELECT 1 FROM #ReadYears WHERE AcademicYearId IN(2,5) AND DatesLocked=1)
 THROW 51000,'Reader must lock historical enrolments and both promotion-year references only.',1;
PRINT 'PASS: reader locks historical enrolment, promotion source and promotion destination; unused years stay editable.';
GO
CREATE PROCEDURE #ExpectLocked @Id int,@Start date,@End date
AS
BEGIN
 DECLARE @Name nvarchar(50)=(SELECT YearName FROM #AcademicYears WHERE AcademicYearId=@Id);
 BEGIN TRY
  EXEC #SaveAcademicYear @Id,@Name,@Start,@End,1,N'Refused change',1;
  THROW 51000,'Used-year date change was accepted.',1;
 END TRY
 BEGIN CATCH
  IF ERROR_NUMBER()<>56116 THROW;
 END CATCH;
 IF @@TRANCOUNT<>0 THROW 51000,'Refused save leaked a transaction.',1;
END;
GO
EXEC #ExpectLocked 1,'20210402','20220331';
EXEC #ExpectLocked 1,'20210401','20220330';
EXEC #ExpectLocked 3,'20230402','20240331';
EXEC #ExpectLocked 4,'20240401','20250330';
IF EXISTS(SELECT 1 FROM #AcademicYears WHERE AcademicYearId=5 AND IsCurrent<>1)
 THROW 51000,'Rejected save changed the Current year.',1;
PRINT 'PASS: direct/stale saves cannot change either used date or partially change Current.';
-- Allowed corrections retain both dates and all historical links.
EXEC #SaveAcademicYear 1,N'2020-2021','20210401','20220331',1,N'Corrected label',1;
IF NOT EXISTS(SELECT 1 FROM #AcademicYears WHERE AcademicYearId=1 AND YearName=N'2020-2021' AND Remarks=N'Corrected label' AND IsCurrent=1 AND StartDate='20210401' AND EndDate='20220331')
 OR (SELECT COUNT(*) FROM #AcademicYears WHERE IsCurrent=1)<>1
 OR (SELECT COUNT(*) FROM #Enrollments)<>1 OR (SELECT COUNT(*) FROM #PromotionBatches)<>1
 THROW 51000,'Allowed label/remarks/Current correction did not preserve history.',1;
EXEC #SaveAcademicYear 2,N'2022-2023','20220402','20230330',0,NULL,1;
IF NOT EXISTS(SELECT 1 FROM #AcademicYears WHERE AcademicYearId=2 AND StartDate='20220402' AND EndDate='20230330')
 THROW 51000,'Unused-year dates should remain editable.',1;
EXEC #SaveAcademicYear 0,N'2026-2027','20260401','20270331',0,NULL,1;
IF (SELECT COUNT(*) FROM #AcademicYears)<>6 THROW 51000,'Creating a valid year failed.',1;
PRINT 'PASS: used-year name/remarks/Current corrections, unused-year date edits and new-year creation work.';
-- A year that was unused when read may gain history before Save.
INSERT #Enrollments VALUES(2,N'Active');
EXEC #ExpectLocked 2,'20220403','20230330';
PRINT 'PASS: newly added usage refuses a stale unlocked edit.';
DECLARE @BadNames TABLE(Name nvarchar(50));
INSERT @BadNames VALUES(N'2021-20224444'),(N'2021'),(N'2021-2023'),(N'2022-2021'),(N'abcd-efgh'),(N''),(NULL);
DECLARE @Bad nvarchar(50);
DECLARE Names CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @BadNames;
OPEN Names; FETCH NEXT FROM Names INTO @Bad;
WHILE @@FETCH_STATUS=0
BEGIN
 BEGIN TRY
  EXEC #SaveAcademicYear 0,@Bad,'20280401','20290331',0,NULL,1;
  THROW 51000,'Invalid name was accepted by SQL.',1;
 END TRY BEGIN CATCH IF ERROR_NUMBER()<>56110 THROW; END CATCH;
 FETCH NEXT FROM Names INTO @Bad;
END;
CLOSE Names; DEALLOCATE Names;
BEGIN TRY
 EXEC #SaveAcademicYear 0,N'2028-2029','20280401','20290331',0,NULL,2;
 THROW 51001,'Permission check was bypassed.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51000 THROW; END CATCH;
BEGIN TRY
 EXEC #SaveAcademicYear 0,N'2028-2029','20250401','20260331',0,NULL,1;
 THROW 51000,'Overlapping dates were accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>56115 THROW; END CATCH;
BEGIN TRY
 EXEC #SaveAcademicYear 0,N'2026-2027','20280401','20290331',0,NULL,1;
 THROW 51000,'Duplicate name was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>56112 THROW; END CATCH;
IF @@TRANCOUNT<>0 THROW 51000,'Tests leaked a transaction.',1;
PRINT 'PASS: malformed names, permissions, overlapping dates and duplicate names remain guarded. Only session-local synthetic data was used.';
