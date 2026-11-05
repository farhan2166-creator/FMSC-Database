-- Admission owns pending registrations. Reopening retains the cancelled episode,
-- identity, family links and every financial record; no fees are restored here.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 64 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>63
 THROW 51000,'Migration 64 requires schema 63.',1;
GO
CREATE TABLE dbo.RegistrationReopenRequests(
 RequestId uniqueidentifier NOT NULL PRIMARY KEY,UserId int NOT NULL REFERENCES dbo.Users(UserId),
 AdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),EnrollmentId int NOT NULL REFERENCES dbo.Enrollments(EnrollmentId),
 PayloadHash binary(32) NOT NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME());
GO
CREATE OR ALTER PROCEDURE dbo.ReopenPendingRegistration
 @UserId int,@AdmissionId int,@AcademicYearId int,@ClassSectionId int,@JoiningDate date,
 @Reason nvarchar(500),@RequestId uniqueidentifier,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.View';
 EXEC dbo.RequirePermission @UserId,N'Students.Admit';
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.View';
 IF @RequestId IS NULL OR @RequestId='00000000-0000-0000-0000-000000000000' OR @JoiningDate IS NULL OR @JoiningDate>CONVERT(date,GETDATE())
  OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59840,'Choose a joining date that is not in the future and enter a reopening reason.',1;
 DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONCAT(@UserId,N'|',@AdmissionId,N'|',@AcademicYearId,N'|',@ClassSectionId,N'|',CONVERT(char(10),@JoiningDate,126),N'|',@Reason));
 DECLARE @Own bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 IF @Own=1 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Number varchar(40),@Reference nvarchar(50),@Withdrawal date,@Campus int,@Start date,@End date,@Snapshot varchar(64),@Enrollment int;
 SELECT @State=Status,@Number=AdmissionNumber,@Reference=QuickReference,@Withdrawal=WithdrawalDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.RegistrationReopenRequests WITH(UPDLOCK,HOLDLOCK) WHERE RequestId=@RequestId)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.RegistrationReopenRequests WHERE RequestId=@RequestId AND UserId=@UserId AND AdmissionId=@AdmissionId AND PayloadHash=@Hash)
   THROW 59841,'This reopening request belongs to different details. Reload the student.',1;
  SELECT @Enrollment=EnrollmentId FROM dbo.RegistrationReopenRequests WHERE RequestId=@RequestId;
  IF @Own=1 COMMIT;
  SELECT @Enrollment EnrollmentId,CONVERT(bit,1) Saved,CONVERT(varchar(64),NULL) Snapshot,CONVERT(decimal(18,2),NULL) Outstanding;
  RETURN;
 END;
 IF @State IS NULL OR @State<>N'Cancelled' OR @Number IS NOT NULL OR @Reference IS NULL
  THROW 59842,'Only a cancelled pending registration without an official admission number can be reopened. Reload the student; admitted former students use readmission.',1;
 IF EXISTS(SELECT 1 FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending'))
  THROW 59843,'This student already has an open placement. Reload Admission.',1;
 SELECT @Campus=CampusId FROM dbo.CampusClassSections WITH(HOLDLOCK) WHERE ClassSectionId=@ClassSectionId;
 SELECT @Start=StartDate,@End=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@AcademicYearId;
 IF @Campus IS NULL OR @Start IS NULL OR @JoiningDate NOT BETWEEN @Start AND @End OR @JoiningDate<ISNULL(@Withdrawal,'00010101')
  THROW 59844,'Choose a valid placement and a joining date within its academic year, on or after the recorded cancellation date.',1;
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@Hash,
  (SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentFeeAssignments WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.BillingCharges WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePayments WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePaymentAllocations WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.CampusClassSections WHERE ClassSectionId=@ClassSectionId FOR JSON PATH,INCLUDE_NULL_VALUES))),2);
 DECLARE @Outstanding decimal(18,2)=(SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled');
 IF @Preview=1
 BEGIN
  IF @Own=1 COMMIT;
  SELECT CONVERT(int,NULL) EnrollmentId,CONVERT(bit,0) Saved,@Snapshot Snapshot,@Outstanding Outstanding;
  RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59845,'The registration, placement or financial records changed. Review reopening again.',1;
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,Status,UsesTransport,BillingReady,PlacementConfirmedAt,Remarks)
 VALUES(@AdmissionId,@AcademicYearId,@ClassSectionId,@JoiningDate,N'Pending',0,1,SYSDATETIME(),N'Reopened registration: '+@Reason);
 SET @Enrollment=CONVERT(int,SCOPE_IDENTITY());
 UPDATE dbo.Students SET Status=N'Pending',AdmissionDate=@JoiningDate,AdmissionCampusId=@Campus,WithdrawalDate=NULL,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
 VALUES(@Enrollment,@AdmissionId,N'ADMISSION',@JoiningDate,@ClassSectionId,N'Cancelled',N'Pending',N'Reopened registration: '+@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy)
 VALUES(@AdmissionId,N'Cancelled',N'Pending',@Reason,@JoiningDate,@UserId);
 INSERT dbo.RegistrationReopenRequests(RequestId,UserId,AdmissionId,EnrollmentId,PayloadHash) VALUES(@RequestId,@UserId,@AdmissionId,@Enrollment,@Hash);
 IF @Own=1 COMMIT;
 SELECT @Enrollment EnrollmentId,CONVERT(bit,1) Saved,@Snapshot Snapshot,@Outstanding Outstanding;
 END TRY
 BEGIN CATCH
  IF @Own=1 AND @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CloseStudentAdmission @UserId int,@AdmissionId int,@LeavingDate date,@Reason nvarchar(500),
 @Cancel bit=0,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@ReverseFutureUnpaid bit=0,@PaidReductionAction nvarchar(10)=N'Credit',@MonthlyFeePolicy nvarchar(20)=N'SchoolCutoff',@ReverseFromMonth date=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59710,'Enter the reason for leaving or cancellation.',1;
 IF @LeavingDate IS NULL OR @LeavingDate>CONVERT(date,GETDATE()) THROW 59711,'Choose a leaving date that is not in the future.',1;
 IF @MonthlyFeePolicy IS NULL OR @MonthlyFeePolicy NOT IN(N'SchoolCutoff',N'KeepIssued',N'FromMonth') THROW 59721,'Choose how existing monthly fees should be handled.',1;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Enrollment int,@Start date,@Snapshot varchar(64),@Raw nvarchar(max),@Lock int;
 SELECT @State=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Enrollment=EnrollmentId,@Start=EnrollmentDate FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending');
 IF @Enrollment IS NULL OR @State NOT IN(N'Active',N'Pending') THROW 59712,'This record has no open placement to close.',1;
 IF @Cancel=1 AND (@State<>N'Pending' OR EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND AdmissionNumber IS NOT NULL))
  THROW 59720,'An officially admitted student cannot be cancelled. Use Mark as Left for departure, or correct details and fees in their respective workspaces.',1;
 IF @State=N'Pending' AND @Cancel=0 THROW 59713,'Use Cancel Registration for a pending registration.',1;
 DECLARE @PlacementYearEnd date=(SELECT Y.EndDate FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@Enrollment);
 IF @PlacementYearEnd IS NULL OR @LeavingDate>@PlacementYearEnd
  THROW 59643,'Leaving date must be within the recorded academic year. Review historical departure dates or prepare the current placement first.',1;
 IF @LeavingDate<@Start THROW 59714,'Leaving date cannot be before the placement start.',1;
 SELECT @Lock=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment) THROW 59715,'This placement already has a leaving certificate. Review its recorded departure in Admission.',1;
 IF @Cancel=1 AND EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.AdmissionId=@AdmissionId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)
  THROW 59716,'Payments exist. Review refunds or reversals in Fee Corrections before cancellation; history must be retained. For an admitted student who attended, use Mark as Left.',1;
 IF @Cancel=1 AND (EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE AdmissionId=@AdmissionId) OR
   EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@Enrollment AND Status<>N'Cancelled'))
  THROW 59717,'This admission has academic or certificate history. Use Mark as Left rather than cancellation.',1;
 DECLARE @MonthlyBoundary date=dbo.GetMonthlyBillingStartDate(@LeavingDate);
 SET @MonthlyBoundary=DATEFROMPARTS(YEAR(@MonthlyBoundary),MONTH(@MonthlyBoundary),1);
 IF @MonthlyFeePolicy=N'KeepIssued' SET @MonthlyBoundary='99991201';
 IF @MonthlyFeePolicy=N'FromMonth'
 BEGIN
  IF @ReverseFromMonth IS NULL OR DAY(@ReverseFromMonth)<>1 OR @ReverseFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
   OR @ReverseFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1))
   THROW 59721,'Choose a first month to waive, from the placement start through the month after leaving.',1;
  SET @MonthlyBoundary=@ReverseFromMonth;
 END;
 DECLARE @FutureCount int,@FutureAmount decimal(12,2),@FuturePaid int,@Blocked int;
 SELECT B.BillingChargeId,B.BillingMonth,X.NetAmount,X.Balance INTO #ClosureBills
 FROM dbo.BillingCharges B CROSS APPLY(SELECT SUM(Q.NetAmount) NetAmount,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance
  FROM dbo.BillingCharges Q WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
 WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled' AND B.ChargeKind=N'Monthly'
  AND B.CorrectionOfBillingChargeId IS NULL AND B.BillingMonth>=@MonthlyBoundary AND X.NetAmount>0;
 SELECT @FutureCount=COUNT(*),@FutureAmount=COALESCE(SUM(Balance),0),
  @FuturePaid=COALESCE(SUM(CASE WHEN NetAmount>Balance THEN 1 ELSE 0 END),0) FROM #ClosureBills;
 IF @Cancel=0 AND @FutureCount>0 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @Blocked=CASE WHEN @Cancel=0 AND (@PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund')) THEN 1 ELSE 0 END;
 DECLARE @PaidReturnAmount decimal(12,2)=(SELECT COALESCE(SUM(NetAmount-Balance),0) FROM #ClosureBills);
 SET @Raw=CONCAT(@UserId,CONVERT(varchar(10),@LeavingDate,120),@Reason,CONVERT(char(1),@Cancel),@MonthlyFeePolicy,CONVERT(varchar(10),@MonthlyBoundary,120),@PaidReductionAction,(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@State CurrentStatus,@Cancel CancelAdmission,@Blocked BlockedCount,@FutureCount FutureCount,@FutureAmount FutureAmount,@FuturePaid FuturePaid,COUNT(*) ChargeCount,@PaidReturnAmount PaidReturnAmount,@MonthlyFeePolicy MonthlyFeePolicy,@MonthlyBoundary ReverseFromMonth,
   COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled';
  COMMIT;RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59719,'The record changed. Review the closure again.',1;
 IF @Blocked>0 THROW 59718,'Review charges after the leaving month. Choose reversal for unpaid future charges, or resolve paid charges in Fee Corrections first.',1;
 IF @Cancel=1
 BEGIN
  EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
  DECLARE @Charge bigint; DECLARE @Reverse TABLE(BillingChargeId bigint,AdmissionId int,FeeTypeName nvarchar(100),BillingMonth date,AmountReversed decimal(12,2),Reason nvarchar(500),Status nvarchar(20));
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled'
   AND (@Cancel=1 OR COALESCE(BillingMonth,ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1));
  OPEN C;FETCH NEXT FROM C INTO @Charge;
  WHILE @@FETCH_STATUS=0 BEGIN INSERT @Reverse EXEC dbo.ReverseBillingCharge @UserId,@Charge,@Reason;FETCH NEXT FROM C INTO @Charge;END;
  CLOSE C;DEALLOCATE C;
 END;
 IF @Cancel=0 AND @FutureCount>0
 BEGIN
  DECLARE @MonthlyCharge bigint,@CorrectionRequest uniqueidentifier;
  DECLARE @MonthlyResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
  DECLARE MonthlyCharges CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM #ClosureBills ORDER BY BillingChargeId;
  OPEN MonthlyCharges;FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  WHILE @@FETCH_STATUS=0 BEGIN
   SET @CorrectionRequest=NEWID();
   INSERT @MonthlyResult EXEC dbo.AdjustBillingChargeRate @UserId,@MonthlyCharge,0,@PaidReductionAction,@Reason,@CorrectionRequest;
   FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  END;
  CLOSE MonthlyCharges;DEALLOCATE MonthlyCharges;
 END;
 DECLARE @NewStatus nvarchar(30)=CASE WHEN @Cancel=1 THEN N'Cancelled' ELSE N'Left' END;
 UPDATE dbo.Enrollments SET Status=@NewStatus,LeavingDate=@LeavingDate,BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=@NewStatus,WithdrawalDate=@LeavingDate,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId) VALUES(@Enrollment,@AdmissionId,N'STATUS_CHANGE',@LeavingDate,@State,@NewStatus,@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,@State,@NewStatus,@Reason,@LeavingDate,@UserId);

 COMMIT;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'64',SYSDATETIME(),SUSER_SNAME(),N'Admission registration workflow and recoverable cancelled-registration reopening');
GO
