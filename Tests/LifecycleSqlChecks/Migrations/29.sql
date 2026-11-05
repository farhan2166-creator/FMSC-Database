-- Apply cutoff to the chosen billing date, independently of class placement history.
-- Existing students, assignments, charges and payments are not rewritten.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 29 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>28
 THROW 51000,'Migration 29 requires schema 28.',1;
GO
CREATE OR ALTER PROCEDURE dbo.ActivateStudentBilling
 @UserId int,@AdmissionId int,@EnrollmentId int=0,@YearId int,@PlacementId int,
 @StartDate date,@BillingStart date,@StudentName nvarchar(500),@FatherName nvarchar(500),
 @Fees nvarchar(max),@Reason nvarchar(500),@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@AdmissionFeeAlreadyPaid bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 DECLARE @Pending bit,@AdmissionDate date,@Locked int;
 SELECT @Pending=LegacySetupPending,@AdmissionDate=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @Pending IS NULL OR @Pending=0 THROW 59960,'This student is not awaiting legacy setup. Reload Admission; use restricted corrections for activated students.',1;
 IF NULLIF(LTRIM(RTRIM(@StudentName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@FatherName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 59961,'Enter student name, father name and a setup reason.',1;
 SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @EnrollmentId>0 AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND AdmissionId=@AdmissionId AND Status=N'Active' AND PlacementConfirmedAt IS NULL)
  THROW 59962,'Choose an unconfirmed active placement or add a missing placement. History cannot be overwritten here.',1;
 IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@EnrollmentId AND (Status=N'Active' OR LeavingDate IS NULL OR LeavingDate>@StartDate))
  THROW 59963,'Another placement is active or overlaps this start. Review placement history first.',1;
 IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId) OR EXISTS(SELECT 1 FROM dbo.FeePayments WHERE EnrollmentId=@EnrollmentId)
  THROW 59964,'This placement already has financial history. Use restricted corrections.',1;
 DECLARE @YearStart date,@YearEnd date,@Campus int;
 SELECT @YearStart=StartDate,@YearEnd=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@YearId;
 SELECT @Campus=CampusId FROM dbo.CampusClassSections WITH(HOLDLOCK) WHERE ClassSectionId=@PlacementId;
 IF @YearStart IS NULL OR @Campus IS NULL OR @StartDate IS NULL OR @BillingStart IS NULL OR @StartDate<@AdmissionDate OR @StartDate<@YearStart OR @StartDate>@YearEnd OR @StartDate>CONVERT(date,GETDATE()) OR @BillingStart<@StartDate OR @BillingStart>@YearEnd
  THROW 59965,'Placement and billing dates must fit the admission and academic year; placement cannot start in the future.',1;
 -- Placement is class history. Billing date alone determines the fee cutoff.
 DECLARE @MonthlyStart date=dbo.GetMonthlyBillingStartDate(@BillingStart);
 IF ISJSON(@Fees)<>1 THROW 59966,'Review valid fee rows before activation.',1;
 DECLARE @F TABLE(FeeTypeId int PRIMARY KEY,Amount decimal(12,2),DiscountAmount decimal(12,2),Frequency nvarchar(30));
 INSERT @F SELECT FeeTypeId,Amount,DiscountAmount,Frequency FROM OPENJSON(@Fees) WITH(FeeTypeId int,Amount decimal(12,2),DiscountAmount decimal(12,2),Frequency nvarchar(30));
 IF NOT EXISTS(SELECT 1 FROM @F) OR EXISTS(SELECT 1 FROM @F F LEFT JOIN dbo.FeeTypes T WITH(HOLDLOCK) ON T.FeeTypeId=F.FeeTypeId WHERE T.FeeTypeId IS NULL OR F.Amount IS NULL OR F.DiscountAmount IS NULL OR F.Frequency IS NULL OR F.Amount<0 OR F.DiscountAmount<0 OR F.DiscountAmount>F.Amount OR F.Amount<>FLOOR(F.Amount) OR F.DiscountAmount<>FLOOR(F.DiscountAmount) OR F.Frequency NOT IN(N'Monthly',N'Yearly',N'As Needed'))
  THROW 59967,'Select fees with valid frequency, whole-rupee amounts and discounts.',1;
 IF @AdmissionFeeAlreadyPaid IS NULL THROW 59970,'Choose whether admission fees were paid before system billing.',1;
 IF @AdmissionFeeAlreadyPaid=1 AND EXISTS(SELECT 1 FROM @F F JOIN dbo.FeeTypes T ON T.FeeTypeId=F.FeeTypeId WHERE T.FeeTypeCode=N'ADMISSION')
  THROW 59971,'Admission fee is marked already paid; remove it from the opening charges.',1;
 IF EXISTS(SELECT 1 FROM dbo.FeeTypes WITH(HOLDLOCK) WHERE IsMandatory=1 AND NOT (@AdmissionFeeAlreadyPaid=1 AND FeeTypeCode=N'ADMISSION') AND FeeTypeId NOT IN(SELECT FeeTypeId FROM @F))
  THROW 59968,'Include all mandatory fees before activation.',1;
 DECLARE @Raw nvarchar(max)=CONCAT(
  (SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentFeeAssignments WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeeTypes ORDER BY FeeTypeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@YearId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.CampusClassSections WHERE ClassSectionId=@PlacementId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT @EnrollmentId EnrollmentId,@YearId YearId,@PlacementId PlacementId,@StartDate StartDate,@BillingStart BillingStart,@StudentName StudentName,@FatherName FatherName,@Reason Reason,@Fees Fees,@AdmissionFeeAlreadyPaid AdmissionFeeAlreadyPaid FOR JSON PATH,INCLUDE_NULL_VALUES));
 DECLARE @Snapshot varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1 BEGIN
  SELECT @Snapshot Snapshot,
   COALESCE((SELECT SUM(Amount-DiscountAmount) FROM @F WHERE Frequency<>N'Monthly' OR (Amount>DiscountAmount AND @MonthlyStart<=@YearEnd)),0) OpeningTotal,
   (SELECT COUNT(*) FROM @F WHERE Frequency<>N'Monthly' OR (Amount>DiscountAmount AND @MonthlyStart<=@YearEnd)) ChargeCount;
  COMMIT; RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59969,'The record or setup changed. Review again before activation.',1;
 -- Clear the lock for this student only, inside the same transaction as fees and charges.
 UPDATE dbo.Students SET StudentName=LTRIM(RTRIM(@StudentName)),FatherName=LTRIM(RTRIM(@FatherName)),Status=N'Active',WithdrawalDate=NULL,LegacySetupPending=0,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 UPDATE dbo.Students SET LegacyVerifiedAt=SYSDATETIME(),LegacyVerifiedBy=@UserId WHERE AdmissionId=@AdmissionId;
 IF @EnrollmentId=0 BEGIN
  INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,Status,BillingReady,UsesTransport,PlacementConfirmedAt,Remarks)
   VALUES(@AdmissionId,@YearId,@PlacementId,@StartDate,@MonthlyStart,N'Active',1,0,SYSDATETIME(),@Reason);
  SET @EnrollmentId=CONVERT(int,SCOPE_IDENTITY());
 END ELSE BEGIN
  UPDATE dbo.Enrollments SET AcademicYearId=@YearId,ClassSectionId=@PlacementId,EnrollmentDate=@StartDate,BillingStartDate=@MonthlyStart,BillingReady=1,PlacementConfirmedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME(),Remarks=@Reason WHERE EnrollmentId=@EnrollmentId;
 END;
 UPDATE dbo.StudentFeeAssignments SET CancelledAt=COALESCE(CancelledAt,SYSDATETIME()),
  EffectiveTo=COALESCE(EffectiveTo,EffectiveFrom,@BillingStart),UpdatedAt=SYSDATETIME()
  WHERE EnrollmentId=@EnrollmentId AND (CancelledAt IS NULL OR EffectiveTo IS NULL);
 DECLARE @Month date=DATEFROMPARTS(YEAR(@MonthlyStart),MONTH(@MonthlyStart),1),
         @OneOffMonth date=DATEFROMPARTS(YEAR(@BillingStart),MONTH(@BillingStart),1);
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,EffectiveFrom,Remarks)
  SELECT @EnrollmentId,@AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @OneOffMonth END,@Reason FROM @F;
 IF @AdmissionFeeAlreadyPaid=1
  INSERT dbo.StudentAdmissionFeeAcknowledgements(AdmissionId,EnrollmentId,RecordedBy,Reason)
   VALUES(@AdmissionId,@EnrollmentId,@UserId,@Reason);
 -- Activation posts the reviewed opening fees from billing dates, never the historical class start.
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
  SELECT StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
   CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @OneOffMonth END,
   CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @BillingStart END,
   CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @BillingStart END,
   Amount,DiscountAmount,CASE WHEN Frequency=N'Monthly' THEN N'Monthly' ELSE N'Annual' END
  FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL
   AND (Frequency=N'Yearly' OR (Frequency=N'Monthly' AND Amount>DiscountAmount AND @MonthlyStart<=@YearEnd));
 -- Explicitly selected one-time fees are part of the reviewed opening bill.
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
  SELECT StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,@OneOffMonth,@BillingStart,@BillingStart,Amount,DiscountAmount,N'Manual'
  FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL AND Frequency=N'As Needed';
 EXEC dbo.ApplyStudentCredit_Internal @AdmissionId;
 COMMIT;
 SELECT @EnrollmentId EnrollmentId;
 END TRY
 BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
 END CATCH;
END;


GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>28',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>28',N'FROM dbo.DatabaseVersion)<>29');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'29',SYSDATETIME(),SUSER_SNAME(),N'Legacy activation cutoff uses chosen billing date independently of placement history');
GO