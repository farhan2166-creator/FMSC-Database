-- Disposable database copy. Run after the test runner's --write-sql output, in the same sqlcmd session.
-- Synthetic school records are created in a transaction and rolled back. No real records are changed.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
CREATE PROCEDURE #BalanceFixture @U int OUTPUT,@Campus int OUTPUT,@Y1 int OUTPUT,@Y2 int OUTPUT,@E1 int OUTPUT,@E2 int OUTPUT,@Fee int OUTPUT,@Place int OUTPUT
AS
BEGIN
 DECLARE @Code nvarchar(10)=LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),10),@Class int,@Section int,@Student int;
 SELECT TOP(1) @U=U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';
 SELECT TOP(1) @Class=ClassId,@Section=SectionId FROM dbo.CampusClassSections;
 IF @U IS NULL OR @Class IS NULL THROW 57800,'Tests require an administrator and catalog placement.',1;
 INSERT dbo.Campuses(CampusCode,CampusName) VALUES(@Code,N'Audit test campus'); SET @Campus=SCOPE_IDENTITY();
 INSERT dbo.AcademicYears(YearName,StartDate,EndDate) VALUES(@Code+N'-1','20250101','20251231');SET @Y1=SCOPE_IDENTITY();
 INSERT dbo.AcademicYears(YearName,StartDate,EndDate) VALUES(@Code+N'-2','20260101','20261231');SET @Y2=SCOPE_IDENTITY();
 INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId,IsActive) VALUES(@Campus,@Class,@Section,1);SET @Place=SCOPE_IDENTITY();
 INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName) VALUES(@Code,N'Audit test monthly fee');SET @Fee=SCOPE_IDENTITY();
 INSERT dbo.Students(AdmissionDate,AdmissionCampusId,StudentName,AdmissionNumber,Status)
 VALUES('20250101',@Campus,N'Audit test older year',CONVERT(varchar(20),9000000000+ABS(CONVERT(bigint,CHECKSUM(NEWID())))),N'Active');SET @Student=SCOPE_IDENTITY();
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,BillingReady,Status)
 VALUES(@Student,@Y1,@Place,'20250101','20250101',1,N'Active');SET @E1=SCOPE_IDENTITY();
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,Frequency) VALUES(@E1,@Student,@Fee,100,N'Monthly');
 INSERT dbo.Students(AdmissionDate,AdmissionCampusId,StudentName,AdmissionNumber,Status)
 VALUES('20260101',@Campus,N'Audit test selected year',CONVERT(varchar(20),9000000000+ABS(CONVERT(bigint,CHECKSUM(NEWID())))),N'Active');SET @Student=SCOPE_IDENTITY();
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,BillingReady,Status)
 VALUES(@Student,@Y2,@Place,'20260101','20260101',1,N'Active');SET @E2=SCOPE_IDENTITY();
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,Frequency) VALUES(@E2,@Student,@Fee,100,N'Monthly');
END;
GO
BEGIN TRANSACTION;
DECLARE @FixtureUser int,@FixtureCampus int,@Y1 int,@Y2 int,@E1 int,@E2 int,@Fee int,@Place int;
EXEC #BalanceFixture @FixtureUser OUTPUT,@FixtureCampus OUTPUT,@Y1 OUTPUT,@Y2 OUTPUT,@E1 OUTPUT,@E2 OUTPUT,@Fee OUTPUT,@Place OUTPUT;
INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,GrossAmount,ChargeKind)
 SELECT A.StudentFeeAssignmentId,E.EnrollmentId,E.AdmissionId,@Fee,'20260501','20260501',100,K.Kind
 FROM dbo.Enrollments E JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=E.EnrollmentId
 CROSS JOIN(VALUES(N'Monthly'),(N'Annual'),(N'Manual')) K(Kind) WHERE E.EnrollmentId IN(@E1,@E2);
UPDATE dbo.BillingCharges SET Status=N'Cancelled' WHERE EnrollmentId=@E1 AND ChargeKind=N'Annual';
UPDATE dbo.BillingCharges SET DiscountAmount=10 WHERE EnrollmentId=@E2 AND ChargeKind=N'Annual';
UPDATE dbo.Enrollments SET Status=N'Left',LeavingDate='20260601' WHERE EnrollmentId=@E1;
DECLARE @OtherCampus int,@OtherPlace int,@OtherClass int,@OtherSection int,@Admission int,@Partial bigint,@Payment int,
 @Code nvarchar(10)=LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),10);
INSERT dbo.Campuses(CampusCode,CampusName) VALUES(@Code,N'Balance test other campus'); SET @OtherCampus=SCOPE_IDENTITY();
SELECT @OtherClass=ClassId,@OtherSection=SectionId FROM dbo.CampusClassSections WHERE ClassSectionId=@Place;
SELECT @OtherClass=COALESCE((SELECT TOP(1) ClassId FROM dbo.Classes WHERE ClassId<>@OtherClass),@OtherClass),
 @OtherSection=COALESCE((SELECT TOP(1) SectionId FROM dbo.Sections WHERE SectionId<>@OtherSection),@OtherSection);
INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId,IsActive) VALUES(@OtherCampus,@OtherClass,@OtherSection,1);SET @OtherPlace=SCOPE_IDENTITY();
UPDATE dbo.Enrollments SET ClassSectionId=@OtherPlace WHERE EnrollmentId=@E2;
SELECT @Admission=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@E2;
SELECT @Partial=BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E2 AND ChargeKind=N'Monthly';
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,CreatedByUserId)
 VALUES(@Admission,@E2,'20260502',@Code+N'-collect',40,N'Cash',@FixtureUser);SET @Payment=SCOPE_IDENTITY();
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Partial,40);
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,CreatedByUserId,RelatedFeePaymentId)
 VALUES(@Admission,@E2,'20260503',@Code+N'-refund',-10,N'Refund',@FixtureUser,@Payment);SET @Payment=SCOPE_IDENTITY();
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Partial,-10);
INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,CreatedByUserId,Status)
 VALUES(@Admission,@E2,'20260504',@Code+N'-reversed',25,N'Cash',@FixtureUser,N'Reversed');SET @Payment=SCOPE_IDENTITY();
INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,@Admission,@Partial,25);
UPDATE dbo.BillingCharges SET Status=N'PartiallyPaid' WHERE BillingChargeId=@Partial;
DECLARE @User int;
SELECT TOP(1) @User=U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';
IF @User IS NULL THROW 57820,'Checks require an administrator account.',1;
CREATE TABLE #Actual(CampusId int,CampusName nvarchar(max),CampusOrder int,
 ClassId int,ClassName nvarchar(max),ClassOrder int,SectionId int,SectionName nvarchar(max),SectionOrder int,
 AdmissionId int,AdmissionNumber nvarchar(max),StudentName nvarchar(max),FatherName nvarchar(max),Outstanding decimal(38,2));
INSERT #Actual EXEC #ClassOutstanding @UserId=@User;
IF NOT EXISTS(SELECT 1 FROM #Actual WHERE CampusId=@FixtureCampus AND Outstanding=200)
 OR NOT EXISTS(SELECT 1 FROM #Actual WHERE CampusId=@OtherCampus AND Outstanding=260)
 THROW 57825,'Cancellation, discount, payment, refund, reversed-payment or historical balance handling changed.',1;
PRINT 'PASS authoritative balances include discounts, signed completed allocations and closed enrollments; ignore cancelled charges and reversed payments';
DECLARE @Campus int,@Class int,@Section int,@Year int;
SELECT TOP(1) @Campus=CampusId,@Class=ClassId,@Section=SectionId FROM #Actual;
SELECT TOP(1) @Year=E.AcademicYearId FROM dbo.BillingCharges BC JOIN dbo.Enrollments E ON E.EnrollmentId=BC.EnrollmentId
 JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
 WHERE CCS.CampusId=@Campus AND CCS.ClassId=@Class AND CCS.SectionId=@Section
 AND BC.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BC.BillingChargeId)>0;
IF @Campus IS NULL OR @Year IS NULL THROW 57821,'Checks require existing outstanding charges.',1;
DECLARE @Cases TABLE(Id int,Campus int,Year int,Class int,Section int);
INSERT @Cases VALUES(1,0,0,0,0),(2,@Campus,0,0,0),(3,0,@Year,0,0),(4,0,0,@Class,0),
 (5,0,0,0,@Section),(6,@Campus,0,@Class,@Section),(7,@Campus,@Year,@Class,@Section);
DECLARE @Case int=1,@C int,@Y int,@Cl int,@S int,@Expected decimal(38,2),@Returned decimal(38,2);
WHILE @Case<=7
BEGIN
 SELECT @C=Campus,@Y=Year,@Cl=Class,@S=Section FROM @Cases WHERE Id=@Case;
 TRUNCATE TABLE #Actual;
 INSERT #Actual EXEC #ClassOutstanding @UserId=@User,@CampusId=@C,@AcademicYearId=@Y,@ClassId=@Cl,@SectionId=@S;
 SELECT @Expected=COALESCE(SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId)),0)
 FROM dbo.BillingCharges BC
 WHERE BC.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BC.BillingChargeId)>0
 AND EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
     WHERE E.EnrollmentId=BC.EnrollmentId AND (@C=0 OR CCS.CampusId=@C) AND (@Y=0 OR E.AcademicYearId=@Y)
       AND (@Cl=0 OR CCS.ClassId=@Cl) AND (@S=0 OR CCS.SectionId=@S));
 SELECT @Returned=COALESCE(SUM(Outstanding),0) FROM #Actual;
 IF @Returned<>@Expected THROW 57822,'Report total does not match authoritative filtered charge balances.',1;
 IF EXISTS(SELECT 1 FROM #Actual GROUP BY CampusId,ClassId,SectionId,AdmissionId HAVING COUNT(*)<>1)
    THROW 57823,'A student appears more than once in one placement group.',1;
 IF EXISTS(SELECT 1 FROM #Actual WHERE Outstanding<=0 OR (@C<>0 AND CampusId<>@C)
     OR (@Cl<>0 AND ClassId<>@Cl) OR (@S<>0 AND SectionId<>@S))
    THROW 57824,'Report eligibility or scope does not match filters.',1;
 PRINT CONCAT('PASS balance query case ',@Case,': exact filtered total and one student line per placement');
 SET @Case+=1;
END;
DROP TABLE #Actual;
ROLLBACK;
GO
