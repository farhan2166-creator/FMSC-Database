-- Shared monthly billing and application-opening orchestration.
-- Deployment must use the installer's transaction/backup wrapper. Never run on live data during development.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.BillingAutomationState',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.BillingAutomationState(
  StateId int NOT NULL CONSTRAINT PK_BillingAutomationState PRIMARY KEY CONSTRAINT CK_BillingAutomationState_Singleton CHECK(StateId=1),
  StartMonth date NOT NULL CONSTRAINT CK_BillingAutomationState_Month CHECK(DAY(StartMonth)=1),
  RecoveryReviewedThrough date NULL,
  UpdatedBy int NULL REFERENCES dbo.Users(UserId),
  UpdatedAt datetime2(0) NOT NULL DEFAULT SYSDATETIME()
 );
 -- Introducing automation does not authorize recovery of earlier historical gaps.
 INSERT dbo.BillingAutomationState(StateId,StartMonth)
 VALUES(1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1));
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
CREATE OR ALTER PROCEDURE dbo.PostSelectedMonthlyFees
 @UserId int,@BillingMonth date,@Selection dbo.PostingSelectionType READONLY,
 @Reason nvarchar(500)=NULL,@AcademicYearId int,@AllowRepost bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @BillingMonth IS NULL THROW 54720,'Choose a billing month.',1;
 IF NOT EXISTS(SELECT 1 FROM @Selection) THROW 54721,'No charges selected.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
   THROW 54743,'Select a valid academic year.',1;
 DECLARE @Month date=DATEFROMPARTS(YEAR(@BillingMonth),MONTH(@BillingMonth),1),@Lock int,@Count int;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Monthly billing is already running. Try again shortly.',1;
  SELECT @Count=COUNT(*) FROM dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK)
   JOIN @Selection S ON S.EnrollmentId=E.EnrollmentId;
  SELECT @Count=COUNT(*) FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
   JOIN @Selection S ON S.EnrollmentId=A.EnrollmentId AND S.FeeTypeId=A.FeeTypeId;
  SELECT @Count=COUNT(*) FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK)
   JOIN @Selection S ON S.EnrollmentId=B.EnrollmentId AND S.FeeTypeId=B.FeeTypeId WHERE B.BillingMonth=@Month;
  IF EXISTS(SELECT 1 FROM @Selection S LEFT JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
    WHERE E.EnrollmentId IS NULL OR E.AcademicYearId<>@AcademicYearId
      OR DATEFROMPARTS(YEAR(S.BillingMonth),MONTH(S.BillingMonth),1)<>@Month)
    THROW 54744,'The selection changed or belongs to another month/year. Refresh the review.',1;
  IF @AllowRepost=1 AND EXISTS(SELECT 1 FROM @Selection S
    JOIN dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C ON C.EnrollmentId=S.EnrollmentId AND C.FeeTypeId=S.FeeTypeId
    WHERE C.PreviouslyCancelled=1 AND C.BillingChargeId IS NULL)
    AND NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
    THROW 54746,'Enter a reason before charging a previously reversed fee again.',1;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
    BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,Remarks,ChargeKind)
  SELECT C.StudentFeeAssignmentId,C.EnrollmentId,C.AdmissionId,C.FeeTypeId,@Month,@Month,@Month,
    C.Amount,C.DiscountAmount,COALESCE(NULLIF(LTRIM(RTRIM(@Reason)),N''),N'Monthly fee posting'),N'Monthly'
  FROM dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C
  JOIN @Selection S ON S.EnrollmentId=C.EnrollmentId AND S.FeeTypeId=C.FeeTypeId
  WHERE C.BlockReason IS NULL AND C.BillingChargeId IS NULL AND (@AllowRepost=1 OR C.PreviouslyCancelled=0);
  DECLARE @Created int=@@ROWCOUNT,@Requested int=(SELECT COUNT(*) FROM @Selection);
  COMMIT;
  SELECT @Month BillingMonth,@Requested RowsRequested,@Created ChargesCreated,@Requested-@Created RowsNotPosted;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.PreviewMonthlyFees
 @UserId int,@BillingMonth date,@CampusId int=0,@ClassId int=0,@SectionId int=0,
 @FeeTypeIds dbo.FeeTypeIdList READONLY,@ExcludeAlreadyPosted bit=1,@AcademicYearId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @BillingMonth IS NULL THROW 54710,'Choose a billing month.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
   THROW 54743,'Select a valid academic year.',1;
 DECLARE @Month date=DATEFROMPARTS(YEAR(@BillingMonth),MONTH(@BillingMonth),1);
 SELECT C.AdmissionId,C.AcademicYearId,S.RegistrationNumber,S.StudentName,CAM.CampusName,
  CL.ClassName,SEC.SectionName,F.FeeTypeId,F.FeeTypeName,C.StudentFeeAssignmentId,C.EnrollmentId,
  @Month BillingMonth,@Month ChargeDate,@Month DueDate,C.Amount GrossAmount,C.DiscountAmount,
  C.Amount-C.DiscountAmount NetAmount,CONVERT(bit,CASE WHEN C.BillingChargeId IS NULL THEN 0 ELSE 1 END) AlreadyPosted,
  CASE WHEN C.BillingChargeId IS NOT NULL THEN N'Already posted for this month'
    WHEN C.PreviouslyCancelled=1 THEN N'Previously reversed — selecting this creates a new charge' ELSE N'Eligible' END EligibilityNote,
  F.DisplayOrder
 INTO #Eligible
 FROM dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C
 JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
 JOIN dbo.CampusClassSections P ON P.ClassSectionId=C.ClassSectionId
 JOIN dbo.Campuses CAM ON CAM.CampusId=P.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=P.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=P.SectionId
 JOIN dbo.FeeTypes F ON F.FeeTypeId=C.FeeTypeId
 WHERE C.BlockReason IS NULL AND (@CampusId=0 OR P.CampusId=@CampusId)
  AND (@ClassId=0 OR P.ClassId=@ClassId) AND (@SectionId=0 OR P.SectionId=@SectionId)
  AND (NOT EXISTS(SELECT 1 FROM @FeeTypeIds) OR C.FeeTypeId IN(SELECT FeeTypeId FROM @FeeTypeIds));
 SELECT * FROM #Eligible WHERE @ExcludeAlreadyPosted=0 OR AlreadyPosted=0
 ORDER BY CampusName,ClassName,SectionName,StudentName,DisplayOrder,FeeTypeName;
 SELECT @Month BillingMonth,COUNT(DISTINCT CASE WHEN AlreadyPosted=0 THEN AdmissionId END) EligibleStudents,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN 1 ELSE 0 END),0) ChargesToPost,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN GrossAmount ELSE 0 END),0) TotalGross,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN DiscountAmount ELSE 0 END),0) TotalDiscount,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN NetAmount ELSE 0 END),0) TotalNet,
  COUNT(DISTINCT CASE WHEN AlreadyPosted=1 THEN AdmissionId END) AlreadyPostedStudents,
  COALESCE(SUM(CASE WHEN AlreadyPosted=1 THEN 1 ELSE 0 END),0) AlreadyPostedCharges FROM #Eligible;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetPostableMonths
 @UserId int,@AcademicYearId int,@CampusId int=0,@ClassId int=0,@SectionId int=0,
 @FeeTypeIds dbo.FeeTypeIdList READONLY
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 DECLARE @First date,@Last date;
 SELECT @First=DATEFROMPARTS(YEAR(StartDate),MONTH(StartDate),1),
  @Last=DATEFROMPARTS(YEAR(EndDate),MONTH(EndDate),1) FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId;
 IF @First IS NULL OR @Last IS NULL OR @Last<@First OR DATEDIFF(month,@First,@Last)>23
  THROW 54741,'Choose an academic year with a valid date range of at most 24 months.',1;
 ;WITH Months AS(SELECT @First BillingMonth UNION ALL SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE BillingMonth<@Last)
 SELECT M.BillingMonth,COUNT(*) ChargesToPost,COUNT(DISTINCT C.AdmissionId) EligibleStudents,SUM(C.Amount-C.DiscountAmount) TotalNet
 FROM Months M CROSS APPLY dbo.fn_MonthlyBillingCandidates(M.BillingMonth,@AcademicYearId) C
 JOIN dbo.CampusClassSections P ON P.ClassSectionId=C.ClassSectionId
 WHERE C.BlockReason IS NULL AND C.BillingChargeId IS NULL
  AND (@CampusId=0 OR P.CampusId=@CampusId) AND (@ClassId=0 OR P.ClassId=@ClassId) AND (@SectionId=0 OR P.SectionId=@SectionId)
  AND (NOT EXISTS(SELECT 1 FROM @FeeTypeIds) OR C.FeeTypeId IN(SELECT FeeTypeId FROM @FeeTypeIds))
 GROUP BY M.BillingMonth ORDER BY M.BillingMonth OPTION(MAXRECURSION 24);
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetBillingCycleStatus
 @UserId int,@AcademicYearId int,@CampusId int=0,@AsOfDate date=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Reports.View';
 SET @AsOfDate=COALESCE(@AsOfDate,CONVERT(date,GETDATE()));
 DECLARE @Month date=DATEFROMPARTS(YEAR(@AsOfDate),MONTH(@AsOfDate),1);
 ;WITH PerEnrollment AS(
  SELECT C.EnrollmentId,COUNT(*) Expected,COUNT(C.BillingChargeId) Posted
  FROM dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C
  JOIN dbo.CampusClassSections P ON P.ClassSectionId=C.ClassSectionId
  WHERE C.BlockReason IS NULL AND (@CampusId=0 OR P.CampusId=@CampusId) GROUP BY C.EnrollmentId)
 SELECT @AcademicYearId AcademicYearId,@CampusId CampusId,@Month BillingMonth,COUNT(*) TotalActiveEnrollments,
  COALESCE(SUM(CASE WHEN Posted=Expected THEN 1 ELSE 0 END),0) BilledThisMonth,
  COALESCE(SUM(CASE WHEN Posted<Expected THEN 1 ELSE 0 END),0) RemainingToBill,
  COALESCE(SUM(Expected-Posted),0) MissingCharges FROM PerEnrollment;
END;
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
   IF @ReviewMissed=1 UPDATE dbo.BillingAutomationState SET RecoveryReviewedThrough=DATEADD(month,-1,@Month),UpdatedBy=@UserId,UpdatedAt=SYSDATETIME() WHERE StateId=1;
  END;
  SELECT @Snapshot Snapshot,@Month BillingMonth,@Created ChargesCreated,
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
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'14')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'14',SYSDATETIME(),SUSER_SNAME(),N'Shared monthly eligibility and safe application-opening billing; manual recovery retained.');
GO
