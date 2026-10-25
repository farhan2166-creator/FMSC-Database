-- Run only through Tools/TestAdmissionFeeSelection.ps1 against its owned restored database.
SET NOCOUNT ON; SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'FMSC_AdmissionFeeChecks[_]%'
 THROW 54800,'An owned admission-fee fixture is required.',1;
CREATE TABLE #Context(UserId int,CampusId int,ClassSectionId int,ClassId int,YearId int,TuitionId int,AdmissionFeeId int);
INSERT #Context
SELECT TOP(1) U.UserId,P.CampusId,P.ClassSectionId,P.ClassId,Y.AcademicYearId,T.FeeTypeId,A.FeeTypeId
FROM dbo.CampusClassSections P JOIN dbo.FeeConfigurations CT ON CT.CampusId=P.CampusId AND CT.ClassId=P.ClassId
JOIN dbo.FeeTypes T ON T.FeeTypeId=CT.FeeTypeId AND T.FeeTypeCode=N'TUITION'
JOIN dbo.FeeConfigurations CA ON CA.CampusId=P.CampusId AND CA.ClassId=P.ClassId
JOIN dbo.FeeTypes A ON A.FeeTypeId=CA.FeeTypeId AND A.FeeTypeCode=N'ADMISSION'
CROSS JOIN dbo.AcademicYears Y CROSS JOIN dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId
WHERE P.IsActive=1 AND CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate AND R.RoleName=N'Administrator'
ORDER BY Y.IsCurrent DESC,P.ClassSectionId,U.UserId;
IF NOT EXISTS(SELECT 1 FROM #Context) THROW 54800,'Fixture needs a current academic year and a class with tuition and admission rates.',1;
CREATE TABLE #AdmissionSequence(NextNumber bigint);
INSERT #AdmissionSequence VALUES(CONVERT(bigint,CONCAT('6',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20000101'),SYSUTCDATETIME()))));
GO
CREATE PROCEDURE dbo.FixtureCheckAdmissionFees @KeepTuition bit,@KeepAdmission bit,@UseSelection bit=1
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @User int,@Campus int,@Placement int,@Year int,@Tuition int,@AdmissionFee int;
 SELECT @User=UserId,@Campus=CampusId,@Placement=ClassSectionId,@Year=YearId,@Tuition=TuitionId,@AdmissionFee=AdmissionFeeId FROM #Context;
 DECLARE @Fees dbo.FeeTypeIdList,@Discounts dbo.AdmissionFeeDiscountType;
 IF @KeepTuition=1 INSERT @Fees VALUES(@Tuition);
 IF @KeepAdmission=1 INSERT @Fees VALUES(@AdmissionFee);
 UPDATE #AdmissionSequence SET NextNumber=NextNumber+1;
 DECLARE @Today date=CONVERT(date,GETDATE()),@Number varchar(40)=(SELECT CONVERT(varchar(40),NextNumber) FROM #AdmissionSequence);
 CREATE TABLE #Saved(AdmissionId int,EnrollmentId int,RegistrationNumber nvarchar(40),AdmissionNumber varchar(40),QuickReference nvarchar(100));
 INSERT #Saved EXEC dbo.AdmitStudent
  @UserId=@User,@AdmissionDate=@Today,@AdmissionCampusId=@Campus,@AcademicYearId=@Year,@ClassSectionId=@Placement,
  @StudentName=N'Fee selection fixture',@FatherName=N'Fixture parent',@DateOfBirth='2010-01-02',@PlaceOfBirth=N'Fixture',@Gender=N'Male',
  @BFormNumber=NULL,@Religion=NULL,@BloodGroup=NULL,@ContactNumber=NULL,@AlternateContactNumber=NULL,@Address=N'Fixture',@PreviousSchool=NULL,
  @FatherOccupation=NULL,@Disability=N'No',@UsesTransport=1,@SelectedFeeTypeIds=@Fees,@AdmissionDiscounts=@Discounts,@AdmissionNumber=@Number,@UseSelectedFeeTypes=@UseSelection;
 DECLARE @Admission int=(SELECT AdmissionId FROM #Saved),@Enrollment int=(SELECT EnrollmentId FROM #Saved);
 IF @UseSelection=0
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Tuition)
   OR NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND FeeTypeId=@AdmissionFee)
   THROW 54808,'An existing admission caller lost its default fees.',1;
  RETURN;
 END;
 IF (SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment)<>CONVERT(int,@KeepTuition)+CONVERT(int,@KeepAdmission)
  THROW 54801,'Admission assigned fees outside the submitted selection.',1;
 IF @KeepTuition=0 AND (EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Tuition)
  OR EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Tuition))
  THROW 54802,'Removed tuition was assigned or charged.',1;
 IF @KeepAdmission=0 AND (EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND FeeTypeId=@AdmissionFee)
  OR EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND FeeTypeId=@AdmissionFee))
  THROW 54803,'Removed admission fee was assigned or charged.',1;
 IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments S JOIN dbo.FeeTypes F ON F.FeeTypeId=S.FeeTypeId WHERE EnrollmentId=@Enrollment AND F.IsTransportLinked=1)
  THROW 54804,'Unselected transport was automatically assigned.',1;
 IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment AND EnrollmentDate<>@Today)
  THROW 54805,'Fee selection changed the new-admission placement date.',1;
 IF EXISTS(SELECT 1 FROM dbo.FeePayments WHERE AdmissionId=@Admission)
  THROW 54806,'Admission fee selection created a payment.',1;
END;
GO
EXEC dbo.FixtureCheckAdmissionFees 1,1;
EXEC dbo.FixtureCheckAdmissionFees 1,0;
EXEC dbo.FixtureCheckAdmissionFees 0,1;
EXEC dbo.FixtureCheckAdmissionFees 0,0;
EXEC dbo.FixtureCheckAdmissionFees 0,0,0;
-- Omitted fees must not require configured rates either.
DELETE C FROM dbo.FeeConfigurations C JOIN #Context X ON X.CampusId=C.CampusId AND X.ClassId=C.ClassId WHERE C.FeeTypeId IN(X.TuitionId,X.AdmissionFeeId);
EXEC dbo.FixtureCheckAdmissionFees 0,0;
DECLARE @Before int=(SELECT COUNT(*) FROM dbo.Students),@Rejected bit=0;
BEGIN TRY EXEC dbo.FixtureCheckAdmissionFees 1,0; END TRY BEGIN CATCH IF ERROR_NUMBER()=51019 SET @Rejected=1; ELSE THROW; END CATCH;
IF @Rejected=0 OR @Before<>(SELECT COUNT(*) FROM dbo.Students) THROW 54807,'An unconfigured selected fee was not rejected cleanly.',1;
PRINT 'PASS: retained, removed and empty admission fee selections; no forced tuition, admission or transport; unconfigured selection validation; dates and payments preserved.';
