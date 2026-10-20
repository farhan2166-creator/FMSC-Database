-- Run only against a disposable database restored from a development snapshot.
-- Successful activation cases roll back; error cases are preview-only.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
DECLARE @Student int=(SELECT TOP(1) S.AdmissionId FROM dbo.Students S
 WHERE S.LegacySetupPending=1 AND S.AdmissionDate<='20260401'
 AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId)
 AND NOT EXISTS(SELECT 1 FROM dbo.StudentAdmissionFeeAcknowledgements A WHERE A.AdmissionId=S.AdmissionId));
IF @Student IS NULL THROW 51000,'A pending student without placement is required in the disposable snapshot.',1;
DECLARE @Year int=(SELECT TOP(1) AcademicYearId FROM dbo.AcademicYears WHERE IsCurrent=1),
 @Placement int=(SELECT MIN(ClassSectionId) FROM dbo.CampusClassSections),
 @User int=(SELECT MIN(UserId) FROM dbo.Users),
 @Fees nvarchar(max)=N'[{"FeeTypeId":2,"Amount":400,"DiscountAmount":0,"Frequency":"Monthly"},{"FeeTypeId":1,"Amount":4000,"DiscountAmount":0,"Frequency":"Yearly"},{"FeeTypeId":5,"Amount":250,"DiscountAmount":0,"Frequency":"As Needed"}]';
DECLARE @Cases table(Id int IDENTITY,StartDate date,BillingStart date,ExpectedMonth date,Cutoff int,YearEnd date,Discounted bit);
INSERT @Cases VALUES
 ('20261015','20261015','20261001',16,NULL,0),
 ('20261016','20261016','20261101',16,NULL,0),
 ('20261020','20261020','20261101',16,NULL,0),
 ('20260401','20261020','20261101',16,NULL,0),
 ('20261020','20261020','20261001',NULL,NULL,0),
 ('20261020','20261020','20261101',16,NULL,1),
 ('20261020','20261020','20261101',16,'20261031',0),
 ('20260401','20261015','20261001',16,NULL,0);
DECLARE @Id int=1,@Start date,@Billing date,@Expected date,@Cutoff int,@End date,@Discounted bit,
 @Effective date,@Enrollment int,@Hash varchar(64),@Total decimal(12,2),@Count int,@Audits bigint;
DECLARE @Preview table(Snapshot varchar(64),OpeningTotal decimal(12,2),ChargeCount int);
WHILE @Id<=8
BEGIN
 SELECT @Start=StartDate,@Billing=BillingStart,@Expected=ExpectedMonth,@Cutoff=Cutoff,@End=YearEnd,@Discounted=Discounted FROM @Cases WHERE Id=@Id;
 BEGIN TRAN;
 UPDATE dbo.SchoolProfile SET FeeProrationCutoffDay=@Cutoff;
 SET @Effective=dbo.GetMonthlyBillingStartDate(@Billing);
 IF @End IS NOT NULL UPDATE dbo.AcademicYears SET EndDate=@End WHERE AcademicYearId=@Year;
 DECLARE @CaseFees nvarchar(max)=CASE WHEN @Discounted=1 THEN REPLACE(@Fees,N'"DiscountAmount":0,"Frequency":"Monthly"',N'"DiscountAmount":400,"Frequency":"Monthly"') ELSE @Fees END;
 SELECT @Audits=COUNT_BIG(*) FROM dbo.AuditOperations;
 DELETE @Preview;
 INSERT @Preview EXEC dbo.ActivateStudentBilling @UserId=@User,@AdmissionId=@Student,@YearId=@Year,@PlacementId=@Placement,
  @StartDate=@Start,@BillingStart=@Billing,@StudentName=N'Cutoff test',@FatherName=N'Test parent',@Fees=@CaseFees,@Reason=N'Disposable cutoff validation';
 SELECT @Hash=Snapshot,@Total=OpeningTotal,@Count=ChargeCount FROM @Preview;
 IF @Total<>4250+CASE WHEN @Discounted=1 OR @End IS NOT NULL THEN 0 ELSE 400 END
  THROW 51011,'Preview does not contain the expected monthly and one-off fees.',1;
 EXEC dbo.ActivateStudentBilling @UserId=@User,@AdmissionId=@Student,@YearId=@Year,@PlacementId=@Placement,
  @StartDate=@Start,@BillingStart=@Billing,@StudentName=N'Cutoff test',@FatherName=N'Test parent',@Fees=@CaseFees,@Reason=N'Disposable cutoff validation',@Preview=0,@ExpectedSnapshot=@Hash;
 SELECT @Enrollment=EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@Student AND Status=N'Active';
 IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment AND EnrollmentDate=@Start AND BillingStartDate=@Effective)
  THROW 51001,'Placement or billing date mismatch.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND Frequency=N'Monthly' AND EffectiveFrom=@Expected)
  THROW 51002,'Monthly assignment uses the wrong month.',1;
 IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Monthly' AND BillingMonth<>@Expected)
  THROW 51003,'An earlier monthly charge was created.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Annual' AND ChargeDate=@Billing AND NetAmount=4000)
  THROW 51004,'Admission fee did not use the selected billing date.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Manual' AND ChargeDate=@Billing AND NetAmount=250)
  THROW 51005,'Selected one-time fee did not use the billing date.',1;
 IF (SELECT COALESCE(SUM(NetAmount),0) FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment)<>@Total
  OR (SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment)<>@Count
  THROW 51006,'Opening preview differs from committed charges.',1;
 IF (@Discounted=1 OR @End IS NOT NULL) AND EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Monthly')
  THROW 51007,'Waived or outside-year monthly charge was created.',1;
 IF @Audits<>(SELECT COUNT_BIG(*) FROM dbo.AuditOperations) THROW 51008,'Activation wrote to the retired audit system.',1;
 ROLLBACK;
 PRINT CONCAT('PASS: placement cutoff case ',@Id,'; preview, monthly/one-off dates, charges and rollback.');
 SET @Id+=1;
END;

IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@Student) THROW 51010,'Test placement was not rolled back.',1;
PRINT 'PASS: historical and current class start dates do not change the billing cutoff; all changes rolled back.';
