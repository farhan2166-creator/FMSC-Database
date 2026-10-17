-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.SaveStudentFeeDiscount
 @UserId int,@StudentFeeAssignmentId int,@DiscountAmount decimal(12,2),@EffectiveFrom date=NULL,@ConcessionLabel nvarchar(20)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF @ConcessionLabel IS NOT NULL AND @ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived') THROW 53103,'Choose a valid concession label.',1;
 SET @DiscountAmount=COALESCE(@DiscountAmount,0);
 IF @DiscountAmount<0 OR @DiscountAmount<>ROUND(@DiscountAmount,0) THROW 53101,'Enter a whole-rupee discount, 0 or more.',1;
 DECLARE @FullConcession bit=CASE WHEN @ConcessionLabel IN(N'Free',N'Waived') OR (@ConcessionLabel=N'Scholarship' AND @DiscountAmount=(SELECT Amount FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@StudentFeeAssignmentId)) THEN 1 ELSE 0 END;
 DECLARE @From date=DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),
  @Enrollment int,@Admission int,@Fee int,@Frequency nvarchar(30),@YearEnd date,@Lock int,@Locked int;
 IF @EffectiveFrom IS NOT NULL AND @EffectiveFrom<>@From THROW 53103,'Assignment discounts start next month. Adjust existing unpaid bills in Fee Collection.',1;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  SELECT @Enrollment=A.EnrollmentId,@Admission=A.AdmissionId,@Fee=A.FeeTypeId,@Frequency=A.Frequency,
   @YearEnd=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1)
  FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
  JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE A.StudentFeeAssignmentId=@StudentFeeAssignmentId AND A.CancelledAt IS NULL AND E.Status IN(N'Active',N'Pending');
  IF @Enrollment IS NULL THROW 53102,'Select an active or pending student fee assignment.',1;
  IF @From>@YearEnd THROW 53103,'No future month remains in this academic year.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee;
  IF @Frequency<>N'Monthly'
   THROW 53103,'Adjust an existing one-off or annual bill in Fee Collection. Recurring assignment discounts apply to monthly fees.',1;
  IF EXISTS(SELECT 1 FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
    WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND BillingMonth>=@From AND Status<>N'Cancelled')
    THROW 53104,'Future bills already exist. Review their discounts in Fee Collection before changing the assignment.',1;
  SELECT * INTO #Crossing FROM dbo.StudentFeeAssignments
   WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND CancelledAt IS NULL
    AND (EffectiveFrom IS NULL OR EffectiveFrom<@From) AND (EffectiveTo IS NULL OR EffectiveTo>=@From);
  UPDATE A SET EffectiveTo=DATEADD(month,-1,@From),UpdatedAt=SYSDATETIME()
   FROM dbo.StudentFeeAssignments A JOIN #Crossing C ON C.StudentFeeAssignmentId=A.StudentFeeAssignmentId;
  UPDATE dbo.StudentFeeAssignments SET ConcessionLabel=@ConcessionLabel,DiscountAmount=CASE WHEN @FullConcession=1 OR @DiscountAmount>Amount THEN Amount ELSE @DiscountAmount END,UpdatedAt=SYSDATETIME()
   WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND CancelledAt IS NULL AND EffectiveFrom>=@From AND EffectiveFrom<=@YearEnd;
  INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
   SELECT EnrollmentId,AdmissionId,FeeTypeId,Amount,CASE WHEN @FullConcession=1 OR @DiscountAmount>Amount THEN Amount ELSE @DiscountAmount END,
    Frequency,Remarks,@From,EffectiveTo,@ConcessionLabel FROM #Crossing;
  COMMIT;
  SELECT @StudentFeeAssignmentId StudentFeeAssignmentId,@Enrollment EnrollmentId,@Admission AdmissionId,
   @From EffectiveFrom,@YearEnd EffectiveThrough;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
