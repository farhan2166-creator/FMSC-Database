-- Shared concession policy for Admission and Fee Collection: cap issued-bill
-- discounts at remaining dues; retain paid money, charge records and fee rates.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 54 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>53
 THROW 51000,'Migration 54 requires schema 53.',1;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStudentFeeDiscount
 @UserId int,@StudentFeeAssignmentId int,@DiscountAmount decimal(12,2),@EffectiveFrom date=NULL,@ConcessionLabel nvarchar(20)=NULL,
 @EffectiveThrough date=NULL,@ApplyToBills bit=0,@PaidReductionAction nvarchar(10)=N'Credit',
 @Reason nvarchar(500)=NULL,@Preview bit=0,@ExpectedSnapshot varchar(64)=NULL,
 @NewAmount decimal(12,2)=NULL,@UpdateAgreement bit=1,@BillingChargeId bigint=NULL,@FullFeeWaiver bit=NULL,@ApplyAvailableCredit bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @DiscountAmount=COALESCE(@DiscountAmount,0);
 IF @NewAmount IS NOT NULL AND (@NewAmount<0 OR @NewAmount<>FLOOR(@NewAmount)) THROW 53101,'Enter a non-negative whole-rupee agreed fee.',1;
 IF @UpdateAgreement=0 AND @ApplyToBills=0 THROW 53103,'Choose whether to change the agreement, issued bills, or both.',1;
 IF @DiscountAmount<0 OR @DiscountAmount<>FLOOR(@DiscountAmount) THROW 53101,'Enter a whole-rupee concession, zero or more.',1;
 IF @ConcessionLabel IS NOT NULL AND @ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived') THROW 53103,'Choose a valid concession label.',1;
 IF @PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund') THROW 53103,'Choose credit or refund for paid reductions.',1;
 DECLARE @Legacy bit=CASE WHEN @EffectiveFrom IS NULL AND @EffectiveThrough IS NULL AND @ApplyToBills=0 AND @Preview=0 AND @ExpectedSnapshot IS NULL AND @NewAmount IS NULL AND @UpdateAgreement=1 AND @BillingChargeId IS NULL AND @ApplyAvailableCredit=0 THEN 1 ELSE 0 END;
 IF @Legacy=0 AND NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 53103,'Enter the reason for this concession change.',1;
 SET @Reason=COALESCE(NULLIF(LTRIM(RTRIM(@Reason)),N''),N'Concession revised');
 DECLARE @Enrollment int,@Admission int,@Fee int,@Frequency nvarchar(30),@YearStart date,@YearEnd date,@Start date,@State nvarchar(30),
  @From date,@Through date,@Lock int,@Locked int,@Raw nvarchar(max),@Snapshot varchar(64),@Full bit;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  SELECT @Enrollment=A.EnrollmentId,@Admission=A.AdmissionId,@Fee=A.FeeTypeId,@Frequency=A.Frequency,
   @YearStart=DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1),@YearEnd=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1),
   @Start=DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1),@State=E.Status,
   @Full=CASE WHEN @ConcessionLabel IN(N'Free',N'Waived') THEN 1 ELSE COALESCE(@FullFeeWaiver,CASE WHEN @ConcessionLabel=N'Scholarship' AND @DiscountAmount=A.Amount THEN 1 ELSE 0 END) END
  FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=A.EnrollmentId
  JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE A.StudentFeeAssignmentId=@StudentFeeAssignmentId AND (A.CancelledAt IS NULL OR @UpdateAgreement=0) AND E.Status IN(N'Active',N'Pending',N'Promoted',N'Left',N'Cancelled');
  IF @Enrollment IS NULL THROW 53102,'Choose an existing fee agreement or issued bill.',1;
  IF @State NOT IN(N'Active',N'Pending') AND @UpdateAgreement=1 THROW 53103,'Closed-placement agreements stay historical. Choose issued bills only to correct an amount owed.',1;
  IF @Legacy=1 AND @Frequency<>N'Monthly' THROW 53103,'Review the annual or one-time bill explicitly before changing its terms.',1;
  SET @From=COALESCE(@EffectiveFrom,DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)));
  SET @Through=COALESCE(@EffectiveThrough,@YearEnd);
  IF DAY(@From)<>1 OR DAY(@Through)<>1 OR @From<@YearStart OR @From<@Start OR @Through>@YearEnd OR @From>@Through
   THROW 53103,'Choose first-of-month concession dates within this placement and academic year.',1;
  DECLARE @CreditResource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@Admission);
  EXEC @Lock=sys.sp_getapplock @Resource=@CreditResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'Student credit is busy. Try again.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  -- Capture the complete agreed schedule before splitting it. Values outside
  -- the chosen period, including scheduled rate changes, are copied unchanged.
  SELECT A.*,CASE WHEN A.EffectiveFrom>@From THEN A.EffectiveFrom ELSE @From END PeriodFrom,
   CASE WHEN A.EffectiveTo<@Through THEN A.EffectiveTo ELSE @Through END PeriodThrough
  INTO #Periods FROM dbo.StudentFeeAssignments A
  WHERE A.EnrollmentId=@Enrollment AND A.FeeTypeId=@Fee AND (A.CancelledAt IS NULL OR @UpdateAgreement=0)
   AND (@Frequency=N'Monthly' OR A.StudentFeeAssignmentId=@StudentFeeAssignmentId)
   AND ISNULL(A.EffectiveFrom,'19000101')<=@Through AND ISNULL(A.EffectiveTo,'99991231')>=@From;
  IF @UpdateAgreement=1 AND NOT EXISTS(SELECT 1 FROM #Periods) THROW 53103,'No fee agreement covers this period. Add or restore the fee assignment first.',1;
  SELECT B.BillingChargeId,COALESCE(B.BillingMonth,DATEFROMPARTS(YEAR(B.ChargeDate),MONTH(B.ChargeDate),1)) BillingMonth,F.FeeTypeName,B.ChargeDate,G.GrossAmount,COALESCE(C.NewGross,B.GrossAmount) OldGross,COALESCE(C.NewDiscount,B.DiscountAmount) OldDiscount,
   X.OldNet,X.Balance,
   CASE WHEN @Full=1 OR @DiscountAmount>G.GrossAmount THEN G.GrossAmount ELSE @DiscountAmount END NewDiscount,
   CONVERT(decimal(12,2),0) Difference,CONVERT(decimal(12,2),0) ReleasedPaidAmount
  INTO #Bills FROM dbo.BillingCharges B JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  OUTER APPLY(SELECT TOP(1) C.NewGross,C.NewDiscount FROM dbo.FeeBillCorrections C WHERE C.BillingChargeId=B.BillingChargeId ORDER BY C.FeeBillCorrectionId DESC) C
  CROSS APPLY(SELECT COALESCE(@NewAmount,C.NewGross,B.GrossAmount) GrossAmount) G
  CROSS APPLY(SELECT SUM(Q.NetAmount) OldNet,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance FROM dbo.BillingCharges Q
   WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
  WHERE B.EnrollmentId=@Enrollment AND B.FeeTypeId=@Fee AND B.CorrectionOfBillingChargeId IS NULL
   AND ((@Frequency=N'Monthly' AND B.ChargeKind=N'Monthly') OR (@Frequency<>N'Monthly' AND B.StudentFeeAssignmentId=@StudentFeeAssignmentId))
   AND (@BillingChargeId IS NULL OR B.BillingChargeId=@BillingChargeId)
   AND B.Status<>N'Cancelled' AND COALESCE(B.BillingMonth,DATEFROMPARTS(YEAR(B.ChargeDate),MONTH(B.ChargeDate),1)) BETWEEN @From AND @Through;
  IF @BillingChargeId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM #Bills) THROW 53103,'This bill is no longer eligible or is outside the chosen period. Reload it before reviewing.',1;
  -- Concessions forgive unpaid dues only. Existing payments stay allocated;
  -- the requested discount still applies in full to future fee-plan periods.
  IF @NewAmount IS NULL
   UPDATE #Bills SET NewDiscount=CASE
    WHEN Balance<=0 THEN OldDiscount
    WHEN NewDiscount>GrossAmount-OldNet+Balance THEN GrossAmount-OldNet+Balance
    ELSE NewDiscount END;
  UPDATE #Bills SET Difference=GrossAmount-NewDiscount-OldNet;
  UPDATE #Bills SET ReleasedPaidAmount=CASE WHEN -Difference>Balance THEN -Difference-Balance ELSE 0 END;
  IF @Legacy=1 AND EXISTS(SELECT 1 FROM #Bills) THROW 53104,'Issued bills exist. Open Fee Discount and review whether they should change.',1;
  SET @Raw=CONCAT((SELECT @UserId Actor,@StudentFeeAssignmentId Assignment,@DiscountAmount Discount,@ConcessionLabel Label,@From [From],@Through ThroughMonth,
    @ApplyToBills ApplyToBills,@PaidReductionAction PaidReductionAction,@Reason Reason,@NewAmount NewAmount,@UpdateAgreement UpdateAgreement,@BillingChargeId Bill,@Full FullFeeWaiver,@ApplyAvailableCredit ApplyAvailableCredit,CONVERT(date,GETDATE()) ReviewDate FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment FOR JSON PATH),
   (SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee ORDER BY StudentFeeAssignmentId FOR JSON PATH),
   (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@Admission ORDER BY BillingChargeId FOR JSON PATH),
   (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@Admission ORDER BY FeePaymentId FOR JSON PATH),
   (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@Admission ORDER BY FeePaymentAllocationId FOR JSON PATH),
   (SELECT * FROM dbo.StudentCreditEntries WHERE AdmissionId=@Admission ORDER BY CreditEntryId FOR JSON PATH),
   (SELECT C.* FROM dbo.FeeBillCorrections C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId WHERE B.AdmissionId=@Admission ORDER BY C.FeeBillCorrectionId FOR JSON PATH));
  SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
  DECLARE @Difference decimal(12,2)=CASE WHEN @ApplyToBills=1 THEN COALESCE((SELECT SUM(Difference) FROM #Bills),0) ELSE 0 END,
   @Released decimal(12,2)=CASE WHEN @ApplyToBills=1 THEN COALESCE((SELECT SUM(ReleasedPaidAmount) FROM #Bills),0) ELSE 0 END;
  DECLARE @CurrentCredit decimal(12,2)=COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@Admission),0),
   @CreditToUse decimal(12,2)=0,@EligibleDue decimal(12,2),@AvailableCredit decimal(12,2);
  SET @AvailableCredit=@CurrentCredit+CASE WHEN @PaidReductionAction=N'Credit' THEN @Released ELSE 0 END;
  SELECT @EligibleDue=COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@Admission AND Status<>N'Cancelled' AND ChargeDate<=CONVERT(date,GETDATE());
  IF @ApplyToBills=1 SELECT @EligibleDue=@EligibleDue+COALESCE(SUM(CASE WHEN Difference>=0 THEN Difference WHEN -Difference>Balance THEN -Balance ELSE Difference END),0) FROM #Bills WHERE ChargeDate<=CONVERT(date,GETDATE());
  IF @ApplyToBills=1 AND @ApplyAvailableCredit=1 SET @CreditToUse=CASE WHEN @AvailableCredit>@EligibleDue THEN @EligibleDue ELSE @AvailableCredit END;
  IF @Preview=1
  BEGIN
   SELECT @Snapshot Snapshot,@StudentFeeAssignmentId StudentFeeAssignmentId,@Admission AdmissionId,@Enrollment EnrollmentId,
    @From EffectiveFrom,@Through EffectiveThrough,@Difference Difference,@Released ReleasedPaidAmount,
    (SELECT COUNT(*) FROM #Bills) IssuedBills,@ApplyToBills ApplyToBills,@CurrentCredit CurrentCredit,@CreditToUse CreditToUse,@AvailableCredit-@CreditToUse RemainingCredit;
   SELECT BillingMonth [Month],FeeTypeName Fee,GrossAmount [Agreed Fee],OldNet [Current Bill],CASE WHEN @ApplyToBills=1 THEN GrossAmount-NewDiscount ELSE OldNet END [Proposed Bill],
    CASE WHEN @ApplyToBills=1 THEN Difference ELSE 0 END [Bill Change],CASE WHEN @ApplyToBills=1 THEN ReleasedPaidAmount ELSE 0 END [Paid Amount Released]
   FROM #Bills ORDER BY BillingMonth;
   SELECT PeriodFrom [From],PeriodThrough [Through],Amount [Old Fee],CASE WHEN @UpdateAgreement=1 THEN COALESCE(@NewAmount,Amount) ELSE Amount END [New Fee],DiscountAmount [Old Concession],
    CASE WHEN @UpdateAgreement=0 THEN DiscountAmount WHEN @Full=1 OR @DiscountAmount>COALESCE(@NewAmount,Amount) THEN COALESCE(@NewAmount,Amount) ELSE @DiscountAmount END [New Concession] FROM #Periods ORDER BY PeriodFrom;
   COMMIT;RETURN;
  END;
  IF @Legacy=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot) THROW 53104,'Fees or payments changed after review. Review the concession again; nothing was saved.',1;
  IF @UpdateAgreement=1 AND @Frequency=N'Monthly'
  BEGIN
  DECLARE @Id int,@L date,@R date,@OldFrom date,@OldTo date,@Amount decimal(12,2),@OldDiscount decimal(12,2),@OldLabel nvarchar(20),@Remarks nvarchar(500);
  DECLARE Periods CURSOR LOCAL FAST_FORWARD FOR SELECT StudentFeeAssignmentId,PeriodFrom,PeriodThrough,EffectiveFrom,EffectiveTo,Amount,DiscountAmount,ConcessionLabel,Remarks FROM #Periods ORDER BY PeriodFrom;
  OPEN Periods;FETCH NEXT FROM Periods INTO @Id,@L,@R,@OldFrom,@OldTo,@Amount,@OldDiscount,@OldLabel,@Remarks;
  WHILE @@FETCH_STATUS=0
  BEGIN
   DECLARE @PeriodAmount decimal(12,2)=COALESCE(@NewAmount,@Amount);
   IF @OldFrom IS NULL OR @OldFrom<@L
   BEGIN
    UPDATE dbo.StudentFeeAssignments SET EffectiveTo=DATEADD(month,-1,@L),UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@Id;
    INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
    VALUES(@Enrollment,@Admission,@Fee,@PeriodAmount,CASE WHEN @Full=1 OR @DiscountAmount>@PeriodAmount THEN @PeriodAmount ELSE @DiscountAmount END,N'Monthly',@Reason,@L,CASE WHEN @R=@YearEnd AND @OldTo IS NULL THEN NULL ELSE @R END,@ConcessionLabel);
   END
   ELSE UPDATE dbo.StudentFeeAssignments SET EffectiveFrom=@L,EffectiveTo=CASE WHEN @R=@YearEnd AND @OldTo IS NULL THEN NULL ELSE @R END,
    Amount=@PeriodAmount,DiscountAmount=CASE WHEN @Full=1 OR @DiscountAmount>@PeriodAmount THEN @PeriodAmount ELSE @DiscountAmount END,ConcessionLabel=@ConcessionLabel,Remarks=@Reason,UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@Id;
   IF @R<@YearEnd AND (@OldTo IS NULL OR @OldTo>@R)
    INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
    VALUES(@Enrollment,@Admission,@Fee,@Amount,@OldDiscount,N'Monthly',@Remarks,DATEADD(month,1,@R),@OldTo,@OldLabel);
   FETCH NEXT FROM Periods INTO @Id,@L,@R,@OldFrom,@OldTo,@Amount,@OldDiscount,@OldLabel,@Remarks;
  END;
  CLOSE Periods;DEALLOCATE Periods;
  END;
  IF @UpdateAgreement=1 AND @Frequency<>N'Monthly'
   UPDATE dbo.StudentFeeAssignments SET Amount=COALESCE(@NewAmount,Amount),
    DiscountAmount=CASE WHEN @Full=1 OR @DiscountAmount>COALESCE(@NewAmount,Amount) THEN COALESCE(@NewAmount,Amount) ELSE @DiscountAmount END,
    ConcessionLabel=@ConcessionLabel,Remarks=@Reason,UpdatedAt=SYSDATETIME() WHERE StudentFeeAssignmentId=@StudentFeeAssignmentId;
  IF @ApplyToBills=1
  BEGIN
   DECLARE @Bill bigint,@Gross decimal(12,2),@NewDiscount decimal(12,2),@OldNet decimal(12,2),@Balance decimal(12,2),@Request uniqueidentifier;
   DECLARE @Result TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
   DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId,GrossAmount,NewDiscount,OldNet,Balance FROM #Bills WHERE Difference<>0 OR GrossAmount<>OldGross OR NewDiscount<>OldDiscount ORDER BY CASE WHEN Difference<0 THEN 0 ELSE 1 END,BillingMonth,BillingChargeId;
   OPEN Bills;FETCH NEXT FROM Bills INTO @Bill,@Gross,@NewDiscount,@OldNet,@Balance;
   WHILE @@FETCH_STATUS=0
   BEGIN
    SET @Request=NEWID();
    -- The complete preview is locked; use the balance after our own earlier
    -- corrections. Explicit credit application runs once after all corrections.
    SELECT @Balance=SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges
     WHERE (BillingChargeId=@Bill OR CorrectionOfBillingChargeId=@Bill) AND Status<>N'Cancelled';
    INSERT @Result EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,@Gross,@PaidReductionAction,@Reason,@Request,@OldNet,@Balance,@NewDiscount,0;
    FETCH NEXT FROM Bills INTO @Bill,@Gross,@NewDiscount,@OldNet,@Balance;
   END;
   CLOSE Bills;DEALLOCATE Bills;
   IF @ApplyAvailableCredit=1 EXEC dbo.ApplyStudentCredit_Internal @Admission,@UserId;
  END;
  COMMIT;
  SELECT @StudentFeeAssignmentId StudentFeeAssignmentId,@Admission AdmissionId,@Enrollment EnrollmentId,@From EffectiveFrom,@Through EffectiveThrough,
   @Difference Difference,@Released ReleasedPaidAmount;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;

GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'54',SYSDATETIME(),SUSER_SNAME(),N'Cap concessions on issued bills at unpaid dues; preserve paid allocations and apply requested discounts to the selected fee-plan months');
GO
