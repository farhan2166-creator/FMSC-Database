-- Session-local fixtures: no school tables, procedures, or records are changed.
SET NOCOUNT ON;
CREATE TABLE #SchoolProfile(SchoolName nvarchar(100),AddressLine nvarchar(100),ContactNumber nvarchar(30));
CREATE TABLE #Campuses(CampusId int,CampusName nvarchar(100));
CREATE TABLE #Classes(ClassId int,ClassName nvarchar(100));
CREATE TABLE #Sections(SectionId int);
CREATE TABLE #CampusClassSections(CampusId int,ClassId int,SectionId int,IsActive bit);
CREATE TABLE #AcademicYears(IsCurrent bit,StartDate date,EndDate date);
CREATE TABLE #FeeTypes(FeeTypeId int,FeeTypeName nvarchar(100),IsMandatory bit,IsTransportLinked bit);
CREATE TABLE #FeeConfigurations(CampusId int,ClassId int,FeeTypeId int,Amount decimal(18,2));
CREATE TABLE #Issues(Area nvarchar(50),Issue nvarchar(500),PageKey nvarchar(40));
GO
CREATE PROCEDURE #RequirePermission @UserId int,@Permission nvarchar(100) AS
BEGIN
    IF @UserId=0 OR (@UserId=2 AND @Permission<>N'Fees.Configuration.Manage')
        THROW 50003,'Permission denied by fixture.',1;
END;
GO
-- PROCEDURE
GO
INSERT #SchoolProfile VALUES(N'School',N'Address',N'123');
INSERT #Campuses VALUES(1,N'Campus Boys'),(2,N'Campus Girls');
INSERT #Classes VALUES(1,N'One'),(2,N'Two');
INSERT #Sections VALUES(1),(2);
INSERT #AcademicYears VALUES(1,DATEADD(day,-1,GETDATE()),DATEADD(day,1,GETDATE()));
INSERT #FeeTypes VALUES(1,N'Tuition',1,0),(2,N'Transport',1,1),(3,N'Optional',0,0);
INSERT #CampusClassSections VALUES(1,1,1,0),(2,2,1,1),(2,2,2,1);

INSERT #Issues EXEC #GetSetupReadiness 1;
IF EXISTS(SELECT 1 FROM #Issues WHERE Issue LIKE N'Campus Boys%')
    THROW 51000,'Inactive Boys assignments must not require fee rates.',1;
IF (SELECT COUNT(*) FROM #Issues WHERE Area=N'Fee rates' AND Issue LIKE N'Campus Girls / Two:%')<>1
    THROW 51000,'Active sections must produce one missing tuition warning per campus/class.',1;
IF (SELECT COUNT(*) FROM #Issues)<>1
    THROW 51000,'Transport and optional fees must not produce required-rate warnings.',1;
PRINT 'PASS: inactive Boys excluded; active Girls missing tuition shown once across sections; optional and transport excluded.';

INSERT #FeeConfigurations VALUES(2,2,1,0);
TRUNCATE TABLE #Issues;
INSERT #Issues EXEC #GetSetupReadiness 1;
IF EXISTS(SELECT 1 FROM #Issues) THROW 51000,'An intentionally zero fee must satisfy readiness.',1;
PRINT 'PASS: an intentionally free active fee satisfies readiness.';

UPDATE #CampusClassSections SET IsActive=0;
TRUNCATE TABLE #Issues;
INSERT #Issues EXEC #GetSetupReadiness 1;
IF (SELECT COUNT(*) FROM #Issues)<>1 OR NOT EXISTS(SELECT 1 FROM #Issues WHERE Area=N'Placement' AND PageKey=N'Assignments')
    THROW 51000,'Inactive-only placements must require active placement setup without fee warnings.',1;
PRINT 'PASS: inactive-only placements require assignment setup and produce no fee warnings.';

UPDATE #CampusClassSections SET IsActive=1 WHERE CampusId=1;
TRUNCATE TABLE #Issues;
INSERT #Issues EXEC #GetSetupReadiness 2;
IF (SELECT COUNT(*) FROM #Issues)<>1 OR NOT EXISTS(SELECT 1 FROM #Issues WHERE Area=N'Fee rates' AND Issue LIKE N'Campus Boys / One:%')
    THROW 51000,'Reactivating Boys must require its missing fee, including for fee-only users.',1;
PRINT 'PASS: reactivated Boys assignment requires its missing rate for a fee-only user.';

BEGIN TRY
    EXEC #GetSetupReadiness 0;
    THROW 51000,'A user without setup permissions must be denied.',1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()<>50003 THROW;
END CATCH;
PRINT 'PASS: users without setup access remain denied.';
