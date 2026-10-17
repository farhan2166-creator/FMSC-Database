-- Synthetic fixture, only accepted on the uniquely owned billing-test catalog.
IF DB_NAME() NOT LIKE N'FMSC_BillingChecks[_]%' OR NOT EXISTS(
 SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_BillingChecksOwner')
 THROW 51000,'This fixture may only run on an owned disposable billing database.',1;
IF EXISTS(SELECT 1 FROM dbo.Students) OR EXISTS(SELECT 1 FROM dbo.Users)
 THROW 51000,'The billing fixture requires an empty schema-only database.',1;
SET NOCOUNT ON;
INSERT dbo.Roles(RoleName) VALUES(N'Administrator');
INSERT dbo.Users(Username,RoleId) VALUES(N'fictional_billing_admin',1);
INSERT dbo.Campuses(CampusCode,CampusName,CampusType) VALUES(N'TEST',N'Fictional Campus',N'School');
INSERT dbo.AcademicPrograms(ProgramCode,ProgramName,ProgramType) VALUES(N'TEST',N'Fictional Program',N'School');
INSERT dbo.Classes(ClassCode,ClassName,AcademicProgramId) VALUES(N'TEST',N'Fictional Class',1);
INSERT dbo.Sections(SectionCode,SectionName) VALUES(N'A',N'Fictional Section');
INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId) VALUES(1,1,1);
DECLARE @Month date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),@Start date;
SET @Start=DATEADD(month,-2,@Month);
INSERT dbo.AcademicYears(YearName,StartDate,EndDate,IsCurrent) VALUES(N'Fictional Year',@Start,DATEADD(day,-1,DATEADD(month,10,@Month)),1);
INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,DefaultFrequency) VALUES(N'TUITION',N'Tuition',N'Monthly'),(N'TRANSPORT',N'Transport',N'Monthly');
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,Status,LegacySetupPending)
 VALUES('1001',N'TEST-1001',@Start,1,N'Ready Example',N'Active',0),('1002',N'TEST-1002',@Start,1,N'Legacy Example',N'Active',1),
 ('1003',N'TEST-1003',@Start,1,N'Missing Start Example',N'Active',0),('1004',N'TEST-1004',@Start,1,N'Missing Placement Example',N'Active',0);
INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,Status,BillingReady,PlacementConfirmedAt)
 VALUES(1,1,1,@Start,@Start,N'Active',1,SYSDATETIME()),(2,1,1,@Start,@Start,N'Active',1,SYSDATETIME()),
 (3,1,1,@Start,NULL,N'Active',1,SYSDATETIME());
INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency)
 VALUES(1,1,1,10000,0,N'Monthly'),(1,1,2,2000,0,N'Monthly'),
 (2,2,1,10000,0,N'Monthly'),(3,3,1,10000,0,N'Monthly');
