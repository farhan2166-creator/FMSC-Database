-- Temporary fee-posting holds are independent of departure and certificates.
-- Existing Left records are deliberately NOT converted by this migration.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 30 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>29
 THROW 51000,'Migration 30 requires schema 29.',1;
GO
CREATE TABLE dbo.StudentPostingHolds(
 HoldId int IDENTITY PRIMARY KEY, AdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),
 EnrollmentId int NOT NULL REFERENCES dbo.Enrollments(EnrollmentId), HoldDate date NOT NULL,
 Reason nvarchar(500) NOT NULL, CreatedBy int NOT NULL REFERENCES dbo.Users(UserId),
 CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(), MonthlyFeePolicy nvarchar(20) NOT NULL,
 WaiveFromMonth date NULL, PaidReductionAction nvarchar(10) NOT NULL,
 PreviousBillingReady bit NOT NULL, AdoptedLeft bit NOT NULL DEFAULT 0,
 ResumeDate date NULL, BillingFromMonth date NULL, ResumedEnrollmentId int NULL REFERENCES dbo.Enrollments(EnrollmentId),
 ResumeReason nvarchar(500) NULL, ResumedBy int NULL REFERENCES dbo.Users(UserId), ResumedAt datetime2 NULL,
 CONSTRAINT CK_StudentPostingHolds_Dates CHECK((ResumeDate IS NULL AND BillingFromMonth IS NULL AND ResumedEnrollmentId IS NULL AND ResumedBy IS NULL AND ResumedAt IS NULL)
  OR (ResumeDate>=HoldDate AND BillingFromMonth IS NOT NULL AND DAY(BillingFromMonth)=1 AND ResumedEnrollmentId IS NOT NULL AND ResumedBy IS NOT NULL AND ResumedAt IS NOT NULL))
);
CREATE UNIQUE INDEX UX_StudentPostingHolds_Active ON dbo.StudentPostingHolds(AdmissionId) WHERE ResumeDate IS NULL;
CREATE TABLE dbo.StudentPostingHoldBills(
 HoldId int NOT NULL REFERENCES dbo.StudentPostingHolds(HoldId),
 BillingChargeId bigint NOT NULL REFERENCES dbo.BillingCharges(BillingChargeId),
 OriginalGross decimal(12,2) NOT NULL, OriginalDiscount decimal(12,2) NOT NULL,
 CorrectionRequestId uniqueidentifier NOT NULL REFERENCES dbo.FeeBillCorrections(RequestId),
 RestoredRequestId uniqueidentifier NULL REFERENCES dbo.FeeBillCorrections(RequestId),
 PRIMARY KEY(HoldId,BillingChargeId)
);
CREATE TABLE dbo.StudentPostingDecisions(
 RequestId uniqueidentifier NOT NULL PRIMARY KEY, AdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),
 HoldId int NOT NULL REFERENCES dbo.StudentPostingHolds(HoldId), Action nvarchar(10) NOT NULL,
 UserId int NOT NULL REFERENCES dbo.Users(UserId), PayloadHash varbinary(32) NOT NULL,
 ResultJson nvarchar(max) NOT NULL, CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),
 CONSTRAINT CK_StudentPostingDecisions_Action CHECK(Action IN(N'Hold',N'Resume')),
 CONSTRAINT CK_StudentPostingDecisions_Json CHECK(ISJSON(ResultJson)=1)
);
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentPostingHoldContext @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Students.View';
 SELECT E.EnrollmentId,E.AcademicYearId,E.ClassSectionId,E.EnrollmentDate,E.BillingStartDate,E.Status EnrollmentStatus,
  Y.YearName,Y.StartDate YearStart,Y.EndDate YearEnd,Y.IsCurrent,
  CONCAT(Y.YearName,N' / ',C.CampusName,N' / ',CL.ClassName,N' - ',SEC.SectionName,N' (',E.Status,N')') Placement,
  S.Status StudentStatus,H.HoldId,H.HoldDate,H.Reason HoldReason,H.AdoptedLeft
 FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId
 JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
 JOIN dbo.CampusClassSections CS ON CS.ClassSectionId=E.ClassSectionId JOIN dbo.Campuses C ON C.CampusId=CS.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=CS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CS.SectionId
 LEFT JOIN dbo.StudentPostingHolds H ON H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL
 WHERE S.AdmissionId=@AdmissionId ORDER BY Y.IsCurrent DESC,E.EnrollmentId DESC;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentPostingHoldBills @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Students.View';
 SELECT HB.BillingChargeId,F.FeeTypeName,B.BillingMonth,HB.OriginalGross-HB.OriginalDiscount RestoreAmount,
  H.HoldId,HB.RestoredRequestId,
  CONVERT(bit,CASE WHEN HB.RestoredRequestId IS NULL AND B.Status<>N'Cancelled' AND
   (SELECT TOP(1) RequestId FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC)=HB.CorrectionRequestId THEN 1 ELSE 0 END) CanRestore
 FROM dbo.StudentPostingHolds H JOIN dbo.StudentPostingHoldBills HB ON HB.HoldId=H.HoldId
 JOIN dbo.BillingCharges B ON B.BillingChargeId=HB.BillingChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
 WHERE H.AdmissionId=@AdmissionId AND HB.RestoredRequestId IS NULL
  AND EXISTS(SELECT 1 FROM dbo.StudentPostingHolds ActiveHold WHERE ActiveHold.AdmissionId=@AdmissionId AND ActiveHold.ResumeDate IS NULL)
  AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHoldBills Later WHERE Later.BillingChargeId=HB.BillingChargeId AND Later.HoldId>HB.HoldId)
 ORDER BY B.BillingMonth,F.FeeTypeName;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentPostingDecisionOutcome @UserId int,@AdmissionId int,@RequestId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Lock int;
 EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 59800,'The decision is still processing. Check its outcome again shortly.',1;
 IF EXISTS(SELECT 1 FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId AND (AdmissionId<>@AdmissionId OR UserId<>@UserId))
  THROW 59801,'The saved decision belongs to another student or operator.',1;
 SELECT RequestId,AdmissionId,HoldId,Action,ResultJson,CONVERT(varchar(64),PayloadHash,2) PayloadHash
 FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId;
 COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStudentPostingDecision
 @UserId int,@AdmissionId int,@Action nvarchar(10),@EffectiveDate date,@Reason nvarchar(500),
 @RequestId uniqueidentifier,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,
 @MonthlyFeePolicy nvarchar(20)=N'SchoolCutoff',@WaiveFromMonth date=NULL,@PaidReductionAction nvarchar(10)=N'Credit',
 @BillingFromMonth date=NULL,@RestoreBills nvarchar(max)=N'[]',@ApplyAvailableCredit bit=0,
 @TargetEnrollmentId int=NULL,@AdoptLeft bit=0,@ChargeAnnualFees bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF @Action IS NULL OR @Action NOT IN(N'Hold',N'Resume') OR @RequestId IS NULL OR @RequestId='00000000-0000-0000-0000-000000000000'
  OR @EffectiveDate IS NULL OR @EffectiveDate>CONVERT(date,GETDATE()) OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 59802,'Choose a valid effective date and enter a reason.',1;
 IF ISJSON(@RestoreBills)<>1 OR LEFT(LTRIM(@RestoreBills),1)<>N'[' THROW 59803,'Review the fees selected for restoration.',1;
 DECLARE @Restore TABLE(BillingChargeId bigint PRIMARY KEY);
 INSERT @Restore SELECT TRY_CONVERT(bigint,value) FROM OPENJSON(@RestoreBills);
 DECLARE @Payload varbinary(32)=HASHBYTES('SHA2_256',CONCAT(@UserId,N'|',@AdmissionId,N'|',@Action,N'|',CONVERT(char(10),@EffectiveDate,120),N'|',@Reason,N'|',
  @MonthlyFeePolicy,N'|',CONVERT(char(10),@WaiveFromMonth,120),N'|',@PaidReductionAction,N'|',CONVERT(char(10),@BillingFromMonth,120),N'|',
  @ApplyAvailableCredit,N'|',@TargetEnrollmentId,N'|',@AdoptLeft,N'|',@ChargeAnnualFees,N'|',(SELECT BillingChargeId FROM @Restore ORDER BY BillingChargeId FOR JSON PATH)));
 DECLARE @Own bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Lock int,@Count int,@Status nvarchar(30),@Enrollment int,@Start date,@Ready bit,@Hold int,@HoldDate date,@Adopted bit,
  @Year int,@YearStart date,@YearEnd date,@Boundary date,@Snapshot varchar(64),@Raw nvarchar(max),@Result nvarchar(max);
 EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 59800,'Billing is busy. Review the decision again shortly.',1;
 IF EXISTS(SELECT 1 FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId AND PayloadHash=@Payload AND UserId=@UserId AND AdmissionId=@AdmissionId)
   THROW 59801,'This request belongs to a different decision. Check the original outcome.',1;
  SELECT ResultJson FROM dbo.StudentPostingDecisions WHERE RequestId=@RequestId; COMMIT; RETURN;
 END;
 DECLARE @CreditResource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@AdmissionId);
 EXEC @Lock=sys.sp_getapplock @Resource=@CreditResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 59800,'The student account is busy. Review again shortly.',1;
 SELECT @Status=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND LegacySetupPending=0;
 IF @Status IS NULL THROW 59804,'Complete student billing setup before using hold or resume.',1;
 SELECT @Count=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Count=COUNT(*) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Hold=HoldId,@HoldDate=HoldDate,@Adopted=AdoptedLeft FROM dbo.StudentPostingHolds WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND ResumeDate IS NULL;
 CREATE TABLE #Changes(BillingChargeId bigint PRIMARY KEY,Gross decimal(12,2),Discount decimal(12,2),Net decimal(12,2),Balance decimal(12,2),SourceHoldId int NULL);
 CREATE TABLE #NewBills(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,Amount decimal(12,2),Discount decimal(12,2),ChargeKind nvarchar(20),PRIMARY KEY(StudentFeeAssignmentId,BillingMonth));
 IF @Action=N'Hold'
 BEGIN
  IF @Hold IS NOT NULL THROW 59805,'This student is already on hold. Use Lift Hold / Resume.',1;
  IF (@AdoptLeft=0 AND @Status<>N'Active') OR (@AdoptLeft=1 AND @Status<>N'Left')
   THROW 59806,'Review an active student, or explicitly review an existing Left record as a temporary hold.',1;
  SELECT TOP(1) @Enrollment=EnrollmentId,@Start=EnrollmentDate,@Ready=BillingReady
   FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND Status=CASE WHEN @AdoptLeft=1 THEN N'Left' ELSE N'Active' END ORDER BY EnrollmentId DESC;
  IF @Enrollment IS NULL OR @EffectiveDate<@Start OR EXISTS(SELECT 1 FROM dbo.StudentPostingHolds WHERE AdmissionId=@AdmissionId AND ResumeDate>@EffectiveDate)
   THROW 59807,'The hold date must fit the placement and follow the previous resume.',1;
  IF @MonthlyFeePolicy IS NULL OR @MonthlyFeePolicy NOT IN(N'SchoolCutoff',N'KeepIssued',N'FromMonth')
   OR @PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund') THROW 59808,'Choose how issued fees and released payments should be handled.',1;
  SET @Boundary=DATEFROMPARTS(YEAR(dbo.GetMonthlyBillingStartDate(@EffectiveDate)),MONTH(dbo.GetMonthlyBillingStartDate(@EffectiveDate)),1);
  IF @MonthlyFeePolicy=N'KeepIssued' SET @Boundary='99991201';
  IF @MonthlyFeePolicy=N'FromMonth'
  BEGIN
   IF @WaiveFromMonth IS NULL OR DAY(@WaiveFromMonth)<>1 OR @WaiveFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
    OR @WaiveFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@EffectiveDate),MONTH(@EffectiveDate),1))
    THROW 59808,'Choose a waiver month within the placement, no later than the month after the hold.',1;
   SET @Boundary=@WaiveFromMonth;
  END;
  INSERT #Changes(BillingChargeId,Gross,Discount,Net,Balance)
  SELECT B.BillingChargeId,COALESCE(C.NewGross,B.GrossAmount),COALESCE(C.NewGross,B.GrossAmount)-X.Net,X.Net,X.Balance
  FROM dbo.BillingCharges B
  OUTER APPLY(SELECT TOP(1) NewGross,NewDiscount FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
  CROSS APPLY(SELECT SUM(NetAmount) Net,SUM(dbo.GetFeeChargeBalance(BillingChargeId)) Balance FROM dbo.BillingCharges Q
   WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
  WHERE B.EnrollmentId=@Enrollment AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL AND B.Status<>N'Cancelled' AND B.BillingMonth>=@Boundary AND X.Net>0;
 END
 ELSE
 BEGIN
  IF @Hold IS NULL THROW 59809,'There is no active hold. Reload the student before continuing.',1;
  IF @EffectiveDate<@HoldDate THROW 59810,'The resume date cannot be before the hold date.',1;
  SET @Enrollment=COALESCE(NULLIF(@TargetEnrollmentId,0),(SELECT EnrollmentId FROM dbo.StudentPostingHolds WHERE HoldId=@Hold));
  SELECT @Start=E.EnrollmentDate,@Year=E.AcademicYearId,@YearStart=Y.StartDate,@YearEnd=Y.EndDate
   FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
   WHERE E.EnrollmentId=@Enrollment AND E.AdmissionId=@AdmissionId AND (E.Status=N'Active' OR (E.Status=N'Left' AND E.EnrollmentId=(SELECT EnrollmentId FROM dbo.StudentPostingHolds WHERE HoldId=@Hold))) AND E.PlacementConfirmedAt IS NOT NULL;
  IF @Start IS NULL OR @EffectiveDate<@Start OR @EffectiveDate>@YearEnd OR @BillingFromMonth IS NULL OR DAY(@BillingFromMonth)<>1
   OR @BillingFromMonth<DATEFROMPARTS(YEAR(@YearStart),MONTH(@YearStart),1) OR @BillingFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
   OR @BillingFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@EffectiveDate),MONTH(@EffectiveDate),1)) OR @BillingFromMonth>@YearEnd
   THROW 59811,'Choose a confirmed placement covering the return date and an applicable first billing month. For a new academic year, prepare its placement first.',1;
  IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND Status=N'Active' AND EnrollmentId<>@Enrollment)
   THROW 59811,'Select the current active placement. A previous placement cannot replace it.',1;
  IF EXISTS(SELECT 1 FROM @Restore R WHERE NOT EXISTS(
   SELECT 1 FROM dbo.StudentPostingHoldBills HB JOIN dbo.StudentPostingHolds H ON H.HoldId=HB.HoldId
    JOIN dbo.BillingCharges B ON B.BillingChargeId=HB.BillingChargeId
   WHERE H.AdmissionId=@AdmissionId AND HB.BillingChargeId=R.BillingChargeId AND HB.RestoredRequestId IS NULL AND B.Status<>N'Cancelled'
    AND (SELECT TOP(1) RequestId FROM dbo.FeeBillCorrections WHERE BillingChargeId=R.BillingChargeId ORDER BY FeeBillCorrectionId DESC)=HB.CorrectionRequestId))
   THROW 59812,'A selected waived fee changed. Reload the waived fees and review again.',1;
  INSERT #Changes SELECT R.BillingChargeId,HB.OriginalGross,HB.OriginalDiscount,X.Net,X.Balance,HB.HoldId
   FROM @Restore R JOIN dbo.StudentPostingHoldBills HB ON HB.BillingChargeId=R.BillingChargeId
   JOIN dbo.StudentPostingHolds H ON H.HoldId=HB.HoldId AND H.AdmissionId=@AdmissionId
   CROSS APPLY(SELECT COALESCE(SUM(NetAmount),0) Net,COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Balance FROM dbo.BillingCharges Q
    WHERE (Q.BillingChargeId=R.BillingChargeId OR Q.CorrectionOfBillingChargeId=R.BillingChargeId) AND Q.Status<>N'Cancelled') X
   WHERE HB.RestoredRequestId IS NULL AND (SELECT TOP(1) RequestId FROM dbo.FeeBillCorrections WHERE BillingChargeId=R.BillingChargeId ORDER BY FeeBillCorrectionId DESC)=HB.CorrectionRequestId;
  ;WITH Months AS(SELECT @BillingFromMonth M WHERE @BillingFromMonth<=@EffectiveDate UNION ALL SELECT DATEADD(month,1,M) FROM Months WHERE DATEADD(month,1,M)<=@EffectiveDate)
  INSERT #NewBills SELECT A.StudentFeeAssignmentId,A.FeeTypeId,M.M,A.Amount,A.DiscountAmount,N'Monthly'
   FROM Months M JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@Enrollment
   WHERE A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
    AND (A.EffectiveFrom IS NULL OR M.M>=A.EffectiveFrom) AND (A.EffectiveTo IS NULL OR M.M<=A.EffectiveTo)
    AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=@AdmissionId AND H.ResumeDate IS NOT NULL AND M.M>=DATEFROMPARTS(YEAR(H.HoldDate),MONTH(H.HoldDate),1) AND M.M<H.BillingFromMonth)
    AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.FeeTypeId=A.FeeTypeId AND B.BillingMonth=M.M AND B.ChargeKind=N'Monthly')
   OPTION(MAXRECURSION 1200);
  IF @ChargeAnnualFees=1 INSERT #NewBills
   SELECT A.StudentFeeAssignmentId,A.FeeTypeId,@BillingFromMonth,A.Amount,A.DiscountAmount,N'Annual'
   FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=@Enrollment AND A.CancelledAt IS NULL AND A.Frequency=N'Yearly' AND A.Amount>A.DiscountAmount
    AND (A.EffectiveFrom IS NULL OR @BillingFromMonth>=A.EffectiveFrom) AND (A.EffectiveTo IS NULL OR @BillingFromMonth<=A.EffectiveTo)
    AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N'Annual');
 END;
 IF EXISTS(SELECT 1 FROM #NewBills GROUP BY FeeTypeId,BillingMonth,ChargeKind HAVING COUNT(*)>1)
  THROW 59815,'Resolve overlapping fee agreements before resuming billing. Nothing was saved.',1;
 IF EXISTS(SELECT 1 FROM #Changes) OR EXISTS(SELECT 1 FROM #NewBills) EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF EXISTS(SELECT 1 FROM #NewBills) EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @ApplyAvailableCredit=1 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 DECLARE @Outstanding decimal(12,2)=(SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled'),
  @Waived decimal(12,2)=CASE WHEN @Action=N'Hold' THEN (SELECT COALESCE(SUM(Balance),0) FROM #Changes) ELSE 0 END,
  @Released decimal(12,2)=CASE WHEN @Action=N'Hold' THEN (SELECT COALESCE(SUM(Net-Balance),0) FROM #Changes) ELSE 0 END,
  @Restored decimal(12,2)=CASE WHEN @Action=N'Resume' THEN (SELECT COALESCE(SUM(Gross-Discount-Net),0) FROM #Changes) ELSE 0 END,
  @New decimal(12,2)=(SELECT COALESCE(SUM(Amount-Discount),0) FROM #NewBills),
  @Credit decimal(12,2)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId),@CreditUsed decimal(12,2)=0;
 DECLARE @CreditEligible decimal(12,2)=(SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled' AND ChargeDate<=CONVERT(date,GETDATE()));
 SET @CreditEligible=@CreditEligible+@New+CASE WHEN @Action=N'Resume' THEN (SELECT COALESCE(SUM(C.Gross-C.Discount-C.Net),0) FROM #Changes C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId WHERE B.ChargeDate<=CONVERT(date,GETDATE())) ELSE 0 END;
 IF @Action=N'Resume' AND @ApplyAvailableCredit=1 SET @CreditUsed=CASE WHEN @Credit>@CreditEligible THEN @CreditEligible ELSE @Credit END;
 SET @Raw=CONCAT(CONVERT(varchar(64),@Payload,2),
  (SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentFeeAssignments WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentPostingHolds WHERE AdmissionId=@AdmissionId ORDER BY HoldId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM #Changes ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM #NewBills ORDER BY BillingMonth,FeeTypeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@Outstanding Outstanding,@Waived Waived,@Released ReleasedPaid,@Restored Restored,@New NewCharges,@Credit AvailableCredit,@CreditUsed CreditToApply,
   @Outstanding-@Waived+@Restored+@New-@CreditUsed ResultingOutstanding,
   (SELECT B.BillingMonth,F.FeeTypeName,C.Gross-C.Discount Amount,C.Balance Due FROM #Changes C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId ORDER BY B.BillingMonth,F.FeeTypeName FOR JSON PATH) ChangedFees,
   (SELECT N.BillingMonth,F.FeeTypeName,N.Amount-N.Discount Amount FROM #NewBills N JOIN dbo.FeeTypes F ON F.FeeTypeId=N.FeeTypeId ORDER BY N.BillingMonth,F.FeeTypeName FOR JSON PATH) AddedFees;
  COMMIT; RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59813,'The student or account changed. Review the decision again; nothing was saved.',1;
 IF @Action=N'Hold'
 BEGIN
  INSERT dbo.StudentPostingHolds(AdmissionId,EnrollmentId,HoldDate,Reason,CreatedBy,MonthlyFeePolicy,WaiveFromMonth,PaidReductionAction,PreviousBillingReady,AdoptedLeft)
   VALUES(@AdmissionId,@Enrollment,@EffectiveDate,@Reason,@UserId,@MonthlyFeePolicy,@Boundary,@PaidReductionAction,@Ready,@AdoptLeft);
  SET @Hold=CONVERT(int,SCOPE_IDENTITY());
 END;
 -- Restore through the existing financial correction path. Never restore old
 -- payment allocations: refunds stay refunded; credit is a separate choice.
 DECLARE @Bill bigint,@Gross decimal(12,2),@Discount decimal(12,2),@Correction uniqueidentifier,@BillHold int;
 DECLARE @Corrections TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
 DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId,Gross,Discount,SourceHoldId FROM #Changes ORDER BY BillingChargeId;
 OPEN Bills; FETCH NEXT FROM Bills INTO @Bill,@Gross,@Discount,@BillHold;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @Correction=NEWID();
  IF @Action=N'Hold'
  BEGIN
   INSERT @Corrections EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,0,@PaidReductionAction,@Reason,@Correction,@NewDiscount=0,@ApplyAvailableCredit=0;
   INSERT dbo.StudentPostingHoldBills(HoldId,BillingChargeId,OriginalGross,OriginalDiscount,CorrectionRequestId) VALUES(@Hold,@Bill,@Gross,@Discount,@Correction);
  END
  ELSE
  BEGIN
   INSERT @Corrections EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,@Gross,N'Credit',@Reason,@Correction,@NewDiscount=@Discount,@ApplyAvailableCredit=0;
   UPDATE dbo.StudentPostingHoldBills SET RestoredRequestId=@Correction WHERE HoldId=@BillHold AND BillingChargeId=@Bill;
  END;
  FETCH NEXT FROM Bills INTO @Bill,@Gross,@Discount,@BillHold;
 END;
 CLOSE Bills; DEALLOCATE Bills;
 IF @Action=N'Hold'
  UPDATE dbo.Enrollments SET BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 ELSE
 BEGIN
  UPDATE dbo.StudentPostingHolds SET ResumeDate=@EffectiveDate,BillingFromMonth=@BillingFromMonth,ResumedEnrollmentId=@Enrollment,ResumeReason=@Reason,ResumedBy=@UserId,ResumedAt=SYSDATETIME() WHERE HoldId=@Hold;
  -- Owner decision: placement start is class history, never the billing policy.
  UPDATE dbo.Enrollments SET Status=N'Active',BillingReady=1,LeavingDate=CASE WHEN Status=N'Left' THEN NULL ELSE LeavingDate END,
   BillingStartDate=CASE WHEN BillingStartDate IS NULL OR BillingStartDate>@BillingFromMonth THEN @BillingFromMonth ELSE BillingStartDate END,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
  IF @Status=N'Left' UPDATE dbo.Students SET Status=N'Active',WithdrawalDate=NULL,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
   SELECT StudentFeeAssignmentId,@Enrollment,@AdmissionId,FeeTypeId,BillingMonth,CASE WHEN ChargeKind=N'Annual' THEN @EffectiveDate ELSE BillingMonth END,CASE WHEN ChargeKind=N'Annual' THEN @EffectiveDate ELSE BillingMonth END,Amount,Discount,ChargeKind,LEFT(CONCAT(N'Reviewed resume: ',@Reason),500) FROM #NewBills;
  IF @ApplyAvailableCredit=1 EXEC dbo.ApplyStudentCredit_Internal @AdmissionId,@UserId;
 END;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId)
  VALUES(@Enrollment,@AdmissionId,CASE WHEN @Action=N'Hold' THEN N'POSTING_HOLD' ELSE N'POSTING_RESUME' END,@EffectiveDate,@Status,CASE WHEN @Action=N'Resume' AND @Status=N'Left' THEN N'Active' ELSE @Status END,@Reason,@UserId);
 IF @Action=N'Resume' AND @Status=N'Left' INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,N'Left',N'Active',@Reason,@EffectiveDate,@UserId);
 SET @Result=(SELECT @RequestId RequestId,@AdmissionId AdmissionId,@Hold HoldId,@Action Action,@Enrollment EnrollmentId,@EffectiveDate EffectiveDate,
  @Waived Waived,@Released ReleasedPaid,@Restored Restored,@New NewCharges,@CreditUsed CreditApplied,
  (SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled') Outstanding FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 INSERT dbo.StudentPostingDecisions(RequestId,AdmissionId,HoldId,Action,UserId,PayloadHash,ResultJson) VALUES(@RequestId,@AdmissionId,@Hold,@Action,@UserId,@Payload,@Result);
 COMMIT; SELECT @Result ResultJson;
 END TRY BEGIN CATCH IF @Own=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER FUNCTION dbo.fn_MonthlyBillingCandidates(@Month date,@AcademicYearId int)
RETURNS TABLE
AS RETURN
 SELECT E.EnrollmentId,E.AdmissionId,E.AcademicYearId,E.ClassSectionId,
        A.StudentFeeAssignmentId,A.FeeTypeId,A.Amount,A.DiscountAmount,
        Live.BillingChargeId,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BillingCharges B
          WHERE B.EnrollmentId=E.EnrollmentId AND B.FeeTypeId=A.FeeTypeId
            AND B.BillingMonth=@Month AND B.ChargeKind=N'Monthly' AND B.Status=N'Cancelled') THEN 1 ELSE 0 END) PreviouslyCancelled,
        CONVERT(nvarchar(200),CASE
          WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL) THEN N'On hold â€” use Lift Hold / Resume'
          WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NOT NULL AND @Month>=DATEFROMPARTS(YEAR(H.HoldDate),MONTH(H.HoldDate),1) AND @Month<H.BillingFromMonth) THEN N'No fee for this month under the reviewed resume decision'
          WHEN S.Status<>N'Active' THEN N'Student is not active'
          WHEN S.LegacySetupPending=1 THEN N'Complete legacy billing setup'
          WHEN E.Status<>N'Active' THEN N'Placement is not active'
          WHEN E.BillingReady=0 THEN N'Complete billing setup or confirm return'
          WHEN E.PlacementConfirmedAt IS NULL THEN N'Confirm academic placement'
          WHEN E.BillingStartDate IS NULL THEN N'Set approved billing start date'
          WHEN E.BillingStartDate<DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1) THEN N'Review billing start before placement start'
          WHEN @Month<DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1) THEN N'Billing has not started'
          WHEN @Month<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
            OR @Month>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1) THEN N'Outside academic year'
          WHEN E.LeavingDate IS NOT NULL AND @Month>
            CASE WHEN P.FeeProrationCutoffDay BETWEEN 1 AND 28 AND DAY(E.LeavingDate)<P.FeeProrationCutoffDay
              THEN DATEADD(month,-1,DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1))
              ELSE DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1) END THEN N'After monthly billing ends'
          WHEN A.EffectiveFrom IS NOT NULL AND @Month<A.EffectiveFrom THEN N'Fee starts later'
          WHEN A.EffectiveTo IS NOT NULL AND @Month>A.EffectiveTo THEN N'Fee period ended'
          ELSE NULL END) BlockReason
 FROM dbo.StudentFeeAssignments A
 JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
 JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
 JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
 OUTER APPLY(SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId) P
 OUTER APPLY(SELECT TOP(1) B.BillingChargeId FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=E.EnrollmentId AND B.FeeTypeId=A.FeeTypeId
     AND B.BillingMonth=@Month AND B.ChargeKind=N'Monthly' AND B.Status<>N'Cancelled'
   ORDER BY B.BillingChargeId) Live
 WHERE A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
   AND (@AcademicYearId=0 OR E.AcademicYearId=@AcademicYearId);

GO
-- A final SQL safeguard covers direct and alternate posting paths. Settling dues,
-- financial corrections and recorded refunds remain available during a hold.
CREATE OR ALTER TRIGGER dbo.GuardStudentPostingHolds ON dbo.BillingCharges AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
  WHERE (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId OR I.AdmissionId<>D.AdmissionId
   OR I.GrossAmount>D.GrossAmount OR I.DiscountAmount<D.DiscountAmount OR I.BillingMonth<>D.BillingMonth OR (D.Status=N'Cancelled' AND I.Status<>N'Cancelled'))
  AND (EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WITH(UPDLOCK,HOLDLOCK) WHERE H.AdmissionId=I.AdmissionId AND H.ResumeDate IS NULL)
   OR (I.ChargeKind=N'Monthly' AND EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=I.AdmissionId AND H.ResumeDate IS NOT NULL
    AND I.BillingMonth>=DATEFROMPARTS(YEAR(H.HoldDate),MONTH(H.HoldDate),1) AND I.BillingMonth<H.BillingFromMonth)))
  AND NOT EXISTS(SELECT 1 FROM dbo.FeeBillCorrections C WHERE C.RequestId=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'FMSC.BillCorrection'))
   AND C.BillingChargeId=COALESCE(I.CorrectionOfBillingChargeId,I.BillingChargeId)))
  THROW 59814,'Fee posting is on hold, or this month was excluded by the resume decision. Use the reviewed hold/resume or bill-correction process.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetAdditionalFeeCandidates
 @UserId int,@AcademicYearId int,@CampusId int=0,@ClassId int=0,@SectionId int=0,@ChargeDate date=NULL
AS
BEGIN
 SET NOCOUNT ON; SET @ChargeDate=COALESCE(@ChargeDate,CONVERT(date,GETDATE()));
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @ChargeDate>CONVERT(date,GETDATE()) THROW 53021,'Enter a charge date that is not in the future.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId AND IsCurrent=1)
  THROW 53040,'Additional fees must use the current academic year. Reload the page if it has changed.',1;
 SELECT E.EnrollmentId,S.AdmissionNumber,S.RegistrationNumber,S.StudentName,S.FatherName,C.CampusName,CL.ClassName,SEC.SectionName,E.BillingStartDate
 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
 JOIN dbo.CampusClassSections CS ON CS.ClassSectionId=E.ClassSectionId JOIN dbo.Campuses C ON C.CampusId=CS.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=CS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CS.SectionId
 WHERE E.AcademicYearId=@AcademicYearId AND (@ClassId=0 OR CS.ClassId=@ClassId) AND (@CampusId=0 OR CS.CampusId=@CampusId) AND (@SectionId=0 OR CS.SectionId=@SectionId)
  AND E.Status=N'Active' AND E.BillingReady=1 AND S.Status=N'Active' AND E.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
  AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL)
  AND @ChargeDate>=COALESCE(CONVERT(date,E.ReturnConfirmedAt),E.EnrollmentDate)
  AND EXISTS(SELECT 1 FROM dbo.AcademicYears Y WHERE Y.AcademicYearId=E.AcademicYearId AND @ChargeDate BETWEEN Y.StartDate AND Y.EndDate)
 ORDER BY C.CampusName,CL.DisplayOrder,SEC.DisplayOrder,S.AdmissionNumberBase,S.AdmissionNumberSuffix,S.StudentName;
END;
GO
-- Preserve the existing student read contracts and add a separate hold indicator.
DECLARE @Name sysname,@Definition nvarchar(max),@Position int;
DECLARE Readers CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM (VALUES(N'GetStudents'),(N'GetStudentDetails')) P(Name);
OPEN Readers; FETCH NEXT FROM Readers INTO @Name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @Definition=OBJECT_DEFINITION(OBJECT_ID(N'dbo.'+@Name));
 SET @Position=CHARINDEX(N'S.Status,',@Definition);
 IF @Position=0 OR @Definition IS NULL THROW 51000,'Unexpected student reader definition; hold indicator was not installed.',1;
 SET @Definition=STUFF(@Definition,@Position,0,N'CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL) THEN 1 ELSE 0 END) IsOnHold, ');
 SET @Position=PATINDEX(N'%PROCEDURE%',UPPER(@Definition));
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,@Position,LEN(@Definition)); EXEC sys.sp_executesql @Definition;
 FETCH NEXT FROM Readers INTO @Name;
END;
CLOSE Readers; DEALLOCATE Readers;
GO
CREATE OR ALTER FUNCTION dbo.fn_ExpectedRecurringChargeMonths(@EnrollmentId int,@AsOfDate date)
RETURNS TABLE AS RETURN
 WITH Numbers AS(SELECT 0 N UNION ALL SELECT N+1 FROM Numbers WHERE N<23),
 EnrollmentWindow AS(
  SELECT E.EnrollmentId,DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1) StartMonth,
   DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1) YearStart,
   DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1) YearEnd,
   DATEFROMPARTS(YEAR(@AsOfDate),MONTH(@AsOfDate),1) AsOfMonth,
   CASE WHEN E.LeavingDate IS NULL THEN NULL ELSE
    DATEADD(month,-1,DATEFROMPARTS(YEAR(dbo.GetMonthlyBillingStartDate(E.LeavingDate)),MONTH(dbo.GetMonthlyBillingStartDate(E.LeavingDate)),1)) END LastMonth
  FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE E.EnrollmentId=@EnrollmentId AND E.BillingStartDate IS NOT NULL AND E.BillingReady=1
   AND E.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
   AND E.Status IN(N'Active',N'Promoted',N'Completed',N'Left'))
 SELECT A.StudentFeeAssignmentId,A.FeeTypeId,M.BillingMonth FROM EnrollmentWindow W CROSS JOIN Numbers N
 CROSS APPLY(SELECT DATEADD(month,N.N,W.YearStart) BillingMonth) M
 JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=W.EnrollmentId
 WHERE M.BillingMonth>=W.StartMonth AND M.BillingMonth<=W.YearEnd AND M.BillingMonth<=W.AsOfMonth
  AND (W.LastMonth IS NULL OR M.BillingMonth<=W.LastMonth)
  AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
  AND (A.EffectiveFrom IS NULL OR M.BillingMonth>=A.EffectiveFrom)
  AND (A.EffectiveTo IS NULL OR M.BillingMonth<=A.EffectiveTo)
  AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=A.AdmissionId AND (H.ResumeDate IS NULL OR (M.BillingMonth>=DATEFROMPARTS(YEAR(H.HoldDate),MONTH(H.HoldDate),1) AND M.BillingMonth<H.BillingFromMonth)));

GO
CREATE OR ALTER PROCEDURE dbo.RunAutomaticMonthlyBilling
 @UserId int,@Preview bit=1,@IncludeMissed bit=0,@ReviewMissed bit=0,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @IncludeMissed=1 OR @ReviewMissed=1 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 DECLARE @Month date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),@Start date,@Reviewed date,@Lock int,@Locked int;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Monthly billing is already running. Try again shortly.',1;
  SELECT @Start=StartMonth,@Reviewed=RecoveryReviewedThrough FROM dbo.BillingAutomationState WITH(UPDLOCK,HOLDLOCK) WHERE StateId=1;
  IF @Start IS NULL THROW 54747,'Automatic billing activation is missing. Contact the administrator.',1;
  -- Locks protect the preview token, selected rates and eligibility through posting.
  SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE Status=N'Active';
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE Status=N'Active';
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE CancelledAt IS NULL AND Frequency=N'Monthly';
  SELECT @Locked=COUNT(*) FROM dbo.AcademicYears WITH(HOLDLOCK);
  ;WITH Months AS(SELECT @Start BillingMonth WHERE @Start<=@Month UNION ALL
    SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE BillingMonth<@Month)
  SELECT M.BillingMonth,C.* INTO #Candidates FROM Months M
  CROSS APPLY dbo.fn_MonthlyBillingCandidates(M.BillingMonth,0) C
  WHERE C.BlockReason IS NULL AND C.BillingChargeId IS NULL AND C.PreviouslyCancelled=0
  OPTION(MAXRECURSION 0);
  DECLARE @Raw nvarchar(max)=CONCAT(CONVERT(varchar(10),@Month,120),CONVERT(varchar(10),@Reviewed,120),
   (SELECT * FROM #Candidates ORDER BY BillingMonth,EnrollmentId,FeeTypeId FOR JSON PATH,INCLUDE_NULL_VALUES));
  DECLARE @Snapshot varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
  IF @Preview=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot)
   THROW 54748,'Billing changed since review. Review the current amounts again; nothing was posted.',1;
  SELECT C.AdmissionId INTO #CreditReady FROM dbo.StudentCreditEntries C
   JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
   WHERE S.Status=N'Active' AND S.LegacySetupPending=0
    AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL) AND EXISTS(
     SELECT 1 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
     WHERE B.AdmissionId=C.AdmissionId AND B.Status<>N'Cancelled' AND B.BillingMonth=@Month
      AND B.ChargeDate<=CONVERT(date,GETDATE()) AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
      AND E.Status=N'Active' AND E.BillingReady=1 AND E.BillingStartDate IS NOT NULL AND E.PlacementConfirmedAt IS NOT NULL)
   GROUP BY C.AdmissionId HAVING SUM(C.Amount)>0;
  DECLARE @Created int=0;
  IF @Preview=0
  BEGIN
   DECLARE @RunMonth date,@Year int,@Selection dbo.PostingSelectionType;
   DECLARE @Result TABLE(BillingMonth date,RowsRequested int,ChargesCreated int,RowsNotPosted int);
   DECLARE Cycles CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT BillingMonth,AcademicYearId FROM #Candidates
    WHERE BillingMonth=@Month OR @IncludeMissed=1 ORDER BY BillingMonth,AcademicYearId;
   OPEN Cycles; FETCH NEXT FROM Cycles INTO @RunMonth,@Year;
   WHILE @@FETCH_STATUS=0
   BEGIN
    DELETE FROM @Selection;
    INSERT @Selection SELECT EnrollmentId,FeeTypeId,BillingMonth FROM #Candidates WHERE BillingMonth=@RunMonth AND AcademicYearId=@Year;
    INSERT @Result EXEC dbo.PostSelectedMonthlyFees @UserId,@RunMonth,@Selection,N'Automatic monthly billing',@Year,@AllowRepost=0;
    FETCH NEXT FROM Cycles INTO @RunMonth,@Year;
   END;
   CLOSE Cycles; DEALLOCATE Cycles;
   SELECT @Created=COALESCE(SUM(ChargesCreated),0) FROM @Result;
   IF EXISTS(SELECT 1 FROM @Result WHERE RowsNotPosted<>0) THROW 54749,'Eligibility changed during billing. Nothing was posted; retry the review.',1;
   DECLARE @CreditAccount int;
   DECLARE AvailableCredits CURSOR LOCAL FAST_FORWARD FOR SELECT AdmissionId FROM #CreditReady ORDER BY AdmissionId;
   OPEN AvailableCredits;FETCH NEXT FROM AvailableCredits INTO @CreditAccount;
   WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.ApplyStudentCredit_Internal @CreditAccount,@UserId;FETCH NEXT FROM AvailableCredits INTO @CreditAccount;END;
   CLOSE AvailableCredits;DEALLOCATE AvailableCredits;
   IF @ReviewMissed=1 UPDATE dbo.BillingAutomationState SET RecoveryReviewedThrough=DATEADD(month,-1,@Month),UpdatedBy=@UserId,UpdatedAt=SYSDATETIME() WHERE StateId=1;
  END;
  SELECT @Snapshot Snapshot,@Month BillingMonth,@Created ChargesCreated,(SELECT COUNT(*) FROM #CreditReady) PendingCreditApplications,
   (SELECT COUNT(*) FROM #Candidates WHERE BillingMonth=@Month) CurrentCharges,
   (SELECT COUNT(*) FROM #Candidates WHERE BillingMonth<@Month AND (@Reviewed IS NULL OR BillingMonth>@Reviewed)) MissedCharges,
   (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM #Candidates WHERE BillingMonth=@Month) CurrentAmount,
   (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM #Candidates WHERE BillingMonth<@Month) MissedAmount;
  SELECT BillingMonth,COUNT(DISTINCT AdmissionId) Students,COUNT(*) Charges,SUM(Amount-DiscountAmount) Amount
   FROM #Candidates GROUP BY BillingMonth ORDER BY BillingMonth;
  -- One exception per student; keep all rows available to the administrative workspace.
  SELECT S.AdmissionId,S.StudentName,CONVERT(bit,S.LegacySetupPending) IsLegacyReference,CONVERT(nvarchar(200),CASE
    WHEN S.LegacySetupPending=1 THEN N'Legacy reference â€” activate only if billing is required'
    WHEN E.EnrollmentId IS NULL THEN N'Add an active academic placement'
    WHEN E.BillingReady=0 THEN N'Complete billing setup or confirm return'
    WHEN E.PlacementConfirmedAt IS NULL THEN N'Confirm academic placement'
    WHEN E.BillingStartDate IS NULL THEN N'Set approved billing start date'
    WHEN NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=E.EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly') THEN N'Review monthly fee assignments'
    ELSE N'Review billing dates' END) Reason
   FROM dbo.Students S OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active' ORDER BY E.EnrollmentId DESC) E
   WHERE S.Status=N'Active' AND (S.LegacySetupPending=1 OR E.EnrollmentId IS NULL OR E.BillingReady=0
    OR E.PlacementConfirmedAt IS NULL OR E.BillingStartDate IS NULL
    OR E.BillingStartDate<DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1)
    OR NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=E.EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly'))
   ORDER BY S.AdmissionId;
  COMMIT;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>29',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>29',N'FROM dbo.DatabaseVersion)<>30');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'30',SYSDATETIME(),SUSER_SNAME(),N'Reviewed temporary fee-posting holds, selective restoration and resume decisions');
GO
