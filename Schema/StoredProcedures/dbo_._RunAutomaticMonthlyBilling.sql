-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.RunAutomaticMonthlyBilling
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
   WHERE S.Status=N'Active' AND S.LegacySetupPending=0 AND EXISTS(
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
    WHEN S.LegacySetupPending=1 THEN N'Legacy reference — activate only if billing is required'
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
