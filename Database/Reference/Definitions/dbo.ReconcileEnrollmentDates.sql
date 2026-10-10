SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.ReconcileEnrollmentDates
 @UserId int, @EnrollmentId int, @EnrollmentDate date, @BillingStartDate date,
 @Reason nvarchar(500), @AllowLegacyExceptions bit=0,
 @Preview bit=1, @ExpectedSnapshot nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 57400,'Enter a reason for this correction.',1;
 IF @EnrollmentDate IS NULL OR @EnrollmentDate>CONVERT(date,SYSDATETIME())
  THROW 57401,'Enter an enrollment start that is not in the future.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @AdmissionId int,@Ready bit,@Return datetime2,@YearStart date,@YearEnd date,@FirstAdmission date,
   @Status nvarchar(30),@ClassSectionId int,@Locked int,@LockResult int;
  SELECT @AdmissionId=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  IF @AdmissionId IS NULL THROW 57402,'The enrollment no longer exists.',1;
  SELECT @FirstAdmission=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Ready=E.BillingReady,@Return=E.ReturnConfirmedAt,@Status=E.Status,@ClassSectionId=E.ClassSectionId,
   @YearStart=Y.StartDate,@YearEnd=Y.EndDate
   FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@EnrollmentId;
  IF @Status<>N'Active' THROW 57403,'Closed enrollments are view only.',1;
  IF @Ready=1 AND @BillingStartDate IS NULL THROW 57404,'Billing start is required for a confirmed enrollment.',1;
  IF @BillingStartDate<@EnrollmentDate OR @BillingStartDate>@YearEnd
   THROW 57405,'Billing start must be on or after enrollment start and within the academic year.',1;
  IF @AllowLegacyExceptions=0 AND (@EnrollmentDate<@FirstAdmission OR @EnrollmentDate<@YearStart OR @EnrollmentDate>@YearEnd
   OR EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@EnrollmentId
     AND ISNULL(LeavingDate,'99991231')>@EnrollmentDate))
   THROW 57406,'The start precedes admission, overlaps another enrollment, or falls outside its academic year.',1;
  DECLARE @LockResource nvarchar(255)=N'FMSC.GenerateEnrollmentCharges.'+CONVERT(nvarchar(20),@EnrollmentId);
  EXEC @LockResult=sys.sp_getapplock @Resource=@LockResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 57407,'Billing is being changed elsewhere. Reload and try again.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId WHERE B.EnrollmentId=@EnrollmentId;
  DECLARE @Snapshot nvarchar(max)=(SELECT
   JSON_QUERY((SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Enrollments,
   JSON_QUERY((SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Assignments,
   JSON_QUERY((SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES)) Charges,
   JSON_QUERY((SELECT A.* FROM dbo.FeePaymentAllocations A JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
    WHERE B.EnrollmentId=@EnrollmentId ORDER BY A.FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES)) Allocations
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  IF @Preview=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot)
   THROW 57408,'Records changed after the preview. Review the reconciliation again.',1;
  DECLARE @Opening date=DATEFROMPARTS(YEAR(@BillingStartDate),MONTH(@BillingStartDate),1),
   @AsOf date=CASE WHEN CONVERT(date,SYSDATETIME())<@YearEnd THEN CONVERT(date,SYSDATETIME()) ELSE @YearEnd END;
  -- Retain valid months already posted ahead of today within this academic year.
  SELECT @AsOf=CASE WHEN MAX(BillingMonth)>@AsOf THEN MAX(BillingMonth) ELSE @AsOf END
   FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId AND ChargeKind=N'Monthly' AND Status<>N'Cancelled' AND BillingMonth<=@YearEnd;
  DECLARE @CorrectedReturn datetime2=CASE WHEN @Return IS NOT NULL AND @Return<@EnrollmentDate THEN CONVERT(datetime2,@EnrollmentDate) ELSE @Return END;
  DECLARE @Expected TABLE(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,ChargeKind nvarchar(20),
   ChargeDate date,GrossAmount decimal(18,2),DiscountAmount decimal(18,2));
  IF @Ready=1
  BEGIN
   ;WITH Months AS (
    SELECT @Opening BillingMonth WHERE @Opening<=@AsOf
    UNION ALL SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE DATEADD(month,1,BillingMonth)<=@AsOf)
   INSERT @Expected
   SELECT F.StudentFeeAssignmentId,F.FeeTypeId,M.BillingMonth,N'Monthly',M.BillingMonth,F.Amount,F.DiscountAmount
    FROM Months M JOIN dbo.StudentFeeAssignments F ON F.EnrollmentId=@EnrollmentId
    WHERE F.CancelledAt IS NULL AND F.Frequency=N'Monthly'
     AND (F.EffectiveFrom IS NULL OR M.BillingMonth>=F.EffectiveFrom)
     AND (F.EffectiveTo IS NULL OR M.BillingMonth<=F.EffectiveTo) OPTION(MAXRECURSION 1200);
   INSERT @Expected
   SELECT F.StudentFeeAssignmentId,F.FeeTypeId,@Opening,N'Annual',COALESCE(CONVERT(date,@CorrectedReturn),@EnrollmentDate),
    COALESCE(B.GrossAmount,F.Amount),COALESCE(B.DiscountAmount,F.DiscountAmount)
    FROM dbo.StudentFeeAssignments F OUTER APPLY(SELECT TOP(1) GrossAmount,DiscountAmount FROM dbo.BillingCharges
     WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=F.FeeTypeId AND ChargeKind=N'Annual' AND Status<>N'Cancelled' ORDER BY BillingChargeId) B
    WHERE F.EnrollmentId=@EnrollmentId AND F.CancelledAt IS NULL AND F.Frequency=N'Yearly'
     AND (F.EffectiveFrom IS NULL OR @Opening>=F.EffectiveFrom) AND (F.EffectiveTo IS NULL OR @Opening<=F.EffectiveTo);
  END;
  IF EXISTS(SELECT 1 FROM @Expected GROUP BY FeeTypeId,BillingMonth,ChargeKind HAVING COUNT(*)>1)
   THROW 57409,'Fee-assignment periods overlap. Correct the assignments before reconciling.',1;
  DECLARE @Cancel TABLE(BillingChargeId bigint PRIMARY KEY);
  INSERT @Cancel SELECT B.BillingChargeId FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=@EnrollmentId AND B.Status<>N'Cancelled' AND B.ChargeKind IN(N'Monthly',N'Annual')
    AND NOT EXISTS(SELECT 1 FROM @Expected X WHERE X.FeeTypeId=B.FeeTypeId AND X.BillingMonth=B.BillingMonth AND X.ChargeKind=B.ChargeKind
     AND (B.ChargeKind<>N'Annual' OR B.ChargeDate=X.ChargeDate));
  DECLARE @Blocked int=(SELECT COUNT(*) FROM @Cancel C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId
   WHERE B.Status<>N'Open' OR EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations WHERE BillingChargeId=B.BillingChargeId));
  DECLARE @Add TABLE(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,ChargeKind nvarchar(20),ChargeDate date,
   GrossAmount decimal(18,2),DiscountAmount decimal(18,2));
  INSERT @Add SELECT X.* FROM @Expected X WHERE NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=X.FeeTypeId AND B.ChargeKind=X.ChargeKind
    AND B.BillingMonth=X.BillingMonth AND B.Status<>N'Cancelled' AND NOT EXISTS(SELECT 1 FROM @Cancel C WHERE C.BillingChargeId=B.BillingChargeId));
  DECLARE @CancelCount int=(SELECT COUNT(*) FROM @Cancel),@AddCount int=(SELECT COUNT(*) FROM @Add),
   @CancelledAmount decimal(18,2)=COALESCE((SELECT SUM(B.NetAmount) FROM @Cancel C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId),0),
   @AddedAmount decimal(18,2)=COALESCE((SELECT SUM(GrossAmount-DiscountAmount) FROM @Add),0);
  IF @Preview=1
  BEGIN
   COMMIT;
   SELECT @Snapshot Snapshot,@CancelCount CancelCount,@AddCount AddCount,@CancelledAmount CancelledAmount,@AddedAmount AddedAmount,
    @Blocked BlockedCount,@AsOf ThroughDate,@CorrectedReturn ReturnDate;
   RETURN;
  END;
  IF @Blocked>0 THROW 57410,'Affected charges have payment history. Reverse or refund those payments in Student Fees before reconciling; no records were changed.',1;
  UPDATE dbo.Enrollments SET EnrollmentDate=@EnrollmentDate,BillingStartDate=@BillingStartDate,
   ReturnConfirmedAt=@CorrectedReturn,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@EnrollmentId;
  UPDATE B SET Status=N'Cancelled',Remarks=LEFT(COALESCE(B.Remarks+N' | ',N'')+N'Enrollment reconciliation: '+@Reason,1000)
   FROM dbo.BillingCharges B JOIN @Cancel C ON C.BillingChargeId=B.BillingChargeId;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
   SELECT StudentFeeAssignmentId,@EnrollmentId,@AdmissionId,FeeTypeId,BillingMonth,ChargeDate,ChargeDate,GrossAmount,DiscountAmount,ChargeKind,
    LEFT(N'Enrollment reconciliation: '+@Reason,1000) FROM @Add;
  INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   VALUES(@EnrollmentId,@AdmissionId,N'CORRECTION',CONVERT(date,SYSDATETIME()),@ClassSectionId,@ClassSectionId,@Status,@Status,
    LEFT(N'Dates and billing reconciled: '+@Reason,500),@UserId);
  DECLARE @Operation uniqueidentifier=NEWID(),@After nvarchar(max)=(SELECT @EnrollmentId EnrollmentId,@EnrollmentDate EnrollmentDate,
   @BillingStartDate BillingStartDate,@CorrectedReturn ReturnConfirmedAt,@CancelCount CancelledCharges,@AddCount AddedCharges,
   @Reason Reason,@AllowLegacyExceptions LegacyException,
   JSON_QUERY((SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH)) Charges
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'ENROLLMENT_RECONCILIATION',CONVERT(nvarchar(200),@EnrollmentId));
  DECLARE @Record nvarchar(200)=CONVERT(nvarchar(200),@EnrollmentId);
  EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@UserId,@TableName=N'Enrollments',@RecordId=@Record,@Action=N'CORRECTION',@OldValues=@Snapshot,@NewValues=@After;
  COMMIT;
  SELECT @CancelCount CancelledCharges,@AddCount AddedCharges;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;

GO
