-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.PostSelectedMonthlyFees
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
  DECLARE @CreditStudent int;
  DECLARE CreditStudents CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT E.AdmissionId FROM @Selection S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId ORDER BY E.AdmissionId;
  OPEN CreditStudents; FETCH NEXT FROM CreditStudents INTO @CreditStudent;
  WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.ApplyStudentCredit_Internal @CreditStudent,@UserId; FETCH NEXT FROM CreditStudents INTO @CreditStudent; END;
  CLOSE CreditStudents; DEALLOCATE CreditStudents;
  COMMIT;
  SELECT @Month BillingMonth,@Requested RowsRequested,@Created ChargesCreated,@Requested-@Created RowsNotPosted;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
