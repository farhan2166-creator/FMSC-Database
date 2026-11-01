SET NOCOUNT ON; SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'FMSC_RegistrationChecks[_]%' THROW 51000,'Registration checks require their owned clone.',1;
DECLARE @Year int=(SELECT TOP(1) AcademicYearId FROM dbo.AcademicYears WHERE CONVERT(date,GETDATE()) BETWEEN StartDate AND EndDate ORDER BY IsCurrent DESC),
 @Placement int,@Campus int,@Today date=CONVERT(date,GETDATE());
SELECT TOP(1) @Placement=P.ClassSectionId,@Campus=P.CampusId FROM dbo.CampusClassSections P
 JOIN dbo.FeeConfigurations C ON C.CampusId=P.CampusId AND C.ClassId=P.ClassId WHERE C.Amount>0 AND P.IsActive=1 ORDER BY P.ClassSectionId;
IF @Year IS NULL OR @Placement IS NULL THROW 51000,'Fixture needs a current-year placement with class rates.',1;
DECLARE @Fees dbo.FeeTypeIdList,@Discounts dbo.AdmissionFeeDiscountType;
CREATE TABLE #Admission(AdmissionId int,EnrollmentId int,RegistrationNumber nvarchar(40),AdmissionNumber varchar(40),QuickReference nvarchar(50));
INSERT #Admission EXEC dbo.AdmitStudent @UserId=1,@StudentName=N'Fictional Registration Reopen',@FatherName=N'Fictional Registration Parent',@Gender=N'Male',
 @Address=N'Fictional address',@PlaceOfBirth=N'Fictional city',@AdmissionDate=@Today,@AdmissionCampusId=@Campus,@AcademicYearId=@Year,@ClassSectionId=@Placement,
 @DateOfBirth=NULL,@BFormNumber=NULL,@Religion=NULL,@BloodGroup=NULL,@ContactNumber=NULL,@AlternateContactNumber=NULL,@PreviousSchool=NULL,@FatherOccupation=NULL,@Disability=N'No',@QuickRegistration=1,@UsesTransport=0,@SelectedFeeTypeIds=@Fees,@AdmissionDiscounts=@Discounts;
DECLARE @Student int=(SELECT AdmissionId FROM #Admission),@OldEnrollment int=(SELECT EnrollmentId FROM #Admission);
EXEC dbo.AdmitStudent @UserId=1,@StudentName=N'Fictional Registration Sibling',@FatherName=N'Fictional Registration Parent',@Gender=N'Male',
 @Address=N'Fictional address',@PlaceOfBirth=N'Fictional city',@AdmissionDate=@Today,@AdmissionCampusId=@Campus,@AcademicYearId=@Year,@ClassSectionId=@Placement,
 @DateOfBirth=NULL,@BFormNumber=NULL,@Religion=NULL,@BloodGroup=NULL,@ContactNumber=NULL,@AlternateContactNumber=NULL,@PreviousSchool=NULL,@FatherOccupation=NULL,@Disability=N'No',@QuickRegistration=1,@UsesTransport=0,@FamilyStudentId=@Student,@SelectedFeeTypeIds=@Fees,@AdmissionDiscounts=@Discounts;
CREATE TABLE #Close(Snapshot varchar(64),CurrentStatus nvarchar(30),CancelAdmission bit,BlockedCount int,FutureCount int,FutureAmount decimal(12,2),FuturePaid int,ChargeCount int,PaidReturnAmount decimal(12,2),MonthlyFeePolicy nvarchar(20),ReverseFromMonth date,Outstanding decimal(12,2));
INSERT #Close EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=@Student,@LeavingDate=@Today,@Reason=N'Fictional registration withdrawn',@Cancel=1,@Preview=1;
DECLARE @CloseHash varchar(64)=(SELECT Snapshot FROM #Close);
EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=@Student,@LeavingDate=@Today,@Reason=N'Fictional registration withdrawn',@Cancel=1,@Preview=0,@ExpectedSnapshot=@CloseHash;
DECLARE @Bills varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES)),
 @Payments varbinary(32)=HASHBYTES('SHA2_256',COALESCE((SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),
 @Allocations varbinary(32)=HASHBYTES('SHA2_256',COALESCE((SELECT * FROM dbo.FeePaymentAllocations ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),
 @Links varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.FamilyMembers ORDER BY FamilyMemberId FOR JSON PATH,INCLUDE_NULL_VALUES)),
 @OldEpisode varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@OldEnrollment FOR JSON PATH,INCLUDE_NULL_VALUES));
CREATE TABLE #Review(EnrollmentId int,Saved bit,Snapshot varchar(64),Outstanding decimal(18,2));
DECLARE @Request uniqueidentifier=NEWID(),@Hash varchar(64),@Bad uniqueidentifier=NEWID();
BEGIN TRY
 EXEC dbo.ReopenPendingRegistration 999999,@Student,@Year,@Placement,@Today,N'Returned',@Bad,1;
 THROW 51000,'Unauthorized reopening was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()=51000 THROW; END CATCH;
-- C# enforces active NEW destinations. SQL retains flexibility for historical
-- inactive placements while all review, permission and fee protections remain.
DECLARE @InactiveRequest uniqueidentifier=NEWID(),@BeforeEnrollments int=
 (SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=@Student);
UPDATE dbo.CampusClassSections SET IsActive=0 WHERE ClassSectionId=@Placement AND IsActive=1;
IF @@ROWCOUNT<>1 THROW 51000,'Could not prepare an inactive fixture placement.',1;
INSERT #Review EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Inactive historical preview',@InactiveRequest,1;
IF NOT EXISTS(SELECT 1 FROM #Review WHERE Saved=0 AND Snapshot IS NOT NULL)
 THROW 51000,'SQL refused an inactive historical preview.',1;
BEGIN TRY
 EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Inactive historical preview',@InactiveRequest,0,NULL;
 THROW 51000,'Reopening without its reviewed snapshot was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59845 THROW; END CATCH;
IF EXISTS(SELECT 1 FROM dbo.RegistrationReopenRequests WHERE RequestId=@InactiveRequest)
 OR (SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=@Student)<>@BeforeEnrollments
 OR NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@Student AND Status=N'Cancelled')
 THROW 51000,'Inactive preview changed saved registration history.',1;
UPDATE dbo.CampusClassSections SET IsActive=1 WHERE ClassSectionId=@Placement;
IF @@ROWCOUNT<>1 THROW 51000,'Could not restore the fixture placement.',1;
TRUNCATE TABLE #Review;
PRINT 'PASS: inactive historical preview allowed; SQL still requires reviewed snapshot on apply.';
INSERT #Review EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned',@Request,1;
SELECT @Hash=Snapshot FROM #Review;
IF EXISTS(SELECT 1 FROM dbo.RegistrationReopenRequests WHERE RequestId=@Request) OR NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@Student AND Status=N'Cancelled') THROW 51000,'Preview wrote registration state.',1;
UPDATE dbo.Students SET StudentName=N'Fictional revised name' WHERE AdmissionId=@Student;
BEGIN TRY
 EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned',@Request,0,@Hash;
 THROW 51000,'Stale reopening was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59845 THROW; END CATCH;
TRUNCATE TABLE #Review;
INSERT #Review EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned',@Request,1;
SELECT @Hash=Snapshot FROM #Review;
TRUNCATE TABLE #Review;
INSERT #Review EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned',@Request,0,@Hash;
DECLARE @NewEnrollment int=(SELECT EnrollmentId FROM #Review);
IF @NewEnrollment=@OldEnrollment OR NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@NewEnrollment AND Status=N'Pending') OR
 NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@Student AND Status=N'Pending' AND AdmissionNumber IS NULL) THROW 51000,'Reopening lost its identity or new pending placement.',1;
EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned',@Request,0,@Hash;
IF (SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId=@Student)<>2 THROW 51000,'Request replay duplicated placement.',1;
BEGIN TRY
 EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Different details',@Request,0,@Hash;
 THROW 51000,'Changed request payload was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59841 THROW; END CATCH;
IF @Bills<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
 @Payments<>HASHBYTES('SHA2_256',COALESCE((SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) OR
 @Allocations<>HASHBYTES('SHA2_256',COALESCE((SELECT * FROM dbo.FeePaymentAllocations ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')) OR
 @Links<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.FamilyMembers ORDER BY FamilyMemberId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
 @OldEpisode<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@OldEnrollment FOR JSON PATH,INCLUDE_NULL_VALUES))
 THROW 51000,'Reopening changed retained financial, family or cancellation records.',1;
PRINT 'PASS: cancelled identity reopened with a new pending placement; original cancelled placement, all bills/payments/allocations and family links preserved. Preview, stale review, permissions and request replay guarded.';
-- Re-cancellation must remain available when earlier episodes were cancelled too.
TRUNCATE TABLE #Close;
INSERT #Close EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=@Student,@LeavingDate=@Today,@Reason=N'Fictional second withdrawal',@Cancel=1,@Preview=1;
SELECT @CloseHash=Snapshot FROM #Close;
EXEC dbo.CloseStudentAdmission @UserId=1,@AdmissionId=@Student,@LeavingDate=@Today,@Reason=N'Fictional second withdrawal',@Cancel=1,@Preview=0,@ExpectedSnapshot=@CloseHash;
TRUNCATE TABLE #Review; SET @Request=NEWID();
INSERT #Review EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned again',@Request,1;
SELECT @Hash=Snapshot FROM #Review;
EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Returned again',@Request,0,@Hash;
DECLARE @Official varchar(40)=(SELECT CONVERT(varchar(40),COALESCE(MAX(AdmissionNumberBase),0)+1) FROM dbo.Students);
EXEC dbo.CompleteQuickAdmission @UserId=1,@AdmissionId=@Student,@AdmissionNumber=@Official;
IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@Student AND Status=N'Active' AND AdmissionNumber IS NOT NULL AND RegistrationNumber IS NOT NULL) THROW 51000,'Reopened registration could not confirm admission.',1;
BEGIN TRY
 EXEC dbo.ReopenPendingRegistration 1,@Student,@Year,@Placement,@Today,N'Cannot reopen admitted student',@Bad,1;
 THROW 51000,'Officially admitted student was allowed reopening.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>59842 THROW; END CATCH;
PRINT 'PASS: repeated cancel/reopen retains each episode; existing confirmation assigns official numbers; admitted students cannot be reopened.';
