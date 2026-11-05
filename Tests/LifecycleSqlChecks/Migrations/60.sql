-- Admission/fee flow fixes: authorization, retained cancellation reason, actor attribution, verified refusal.
-- Existing business records are retained. Run under an outer migration transaction.
SET XACT_ABORT ON;SET ANSI_NULLS ON;SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 60 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>59
 THROW 51000,'Migration 60 requires schema 59.',1;
ALTER TABLE dbo.BillingCharges ADD CancellationReason nvarchar(500) NULL,CancelledAt datetime2(7) NULL,CancelledByUserId int NULL;
ALTER TABLE dbo.BillingCharges ADD CONSTRAINT FK_BillingCharges_CancelledBy FOREIGN KEY(CancelledByUserId) REFERENCES dbo.Users(UserId);
GO

-- dbo.ConfirmStudentPlacement
-- dbo.ConfirmStudentPlacement
CREATE OR ALTER PROCEDURE dbo.ConfirmStudentPlacement
 @UserId int,@EnrollmentId int,@ExpectedSnapshot varchar(64)=NULL,@Preview bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer';
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @AdmissionId int,@YearId int,@Placement int,@Start date,@BillingStart date,@End date,@Status nvarchar(30),@Confirmed datetime2(7),@Locked int;
  SELECT @AdmissionId=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @YearId=AcademicYearId,@Placement=ClassSectionId,@Start=EnrollmentDate,@BillingStart=COALESCE(BillingStartDate,EnrollmentDate),@End=LeavingDate,
   @Status=Status,@Confirmed=PlacementConfirmedAt FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  IF @AdmissionId IS NULL THROW 59641,'The enrollment no longer exists.',1;
  IF @Confirmed IS NOT NULL THROW 59642,'This placement is already confirmed. Reload Admission.',1;
  DECLARE @YearStart date,@YearEnd date;
  SELECT @YearStart=StartDate,@YearEnd=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@YearId;
  IF @Start<@YearStart OR (@YearEnd IS NOT NULL AND @Start>@YearEnd)
   THROW 59643,'Correct the enrollment start or academic year before confirming placement.',1;
  IF @BillingStart<@Start OR (@YearEnd IS NOT NULL AND @BillingStart>@YearEnd) OR (@End IS NOT NULL AND @BillingStart>@End)
   THROW 59644,'Correct billing start before confirming placement.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  DECLARE @Snapshot varchar(64),@Json nvarchar(max)=CONCAT(
   (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@YearId FOR JSON PATH,INCLUDE_NULL_VALUES));
  SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Json),2);
  IF @Preview=1
  BEGIN
   SELECT @Snapshot Snapshot,@Status Status,@BillingStart BillingStart,@Start EnrollmentStart,
    (SELECT CONCAT(Y.YearName,N' · ',C.CampusName,N' · ',CL.ClassName,N' · ',S.SectionName)
     FROM dbo.CampusClassSections CS JOIN dbo.Campuses C ON C.CampusId=CS.CampusId
     JOIN dbo.Classes CL ON CL.ClassId=CS.ClassId JOIN dbo.Sections S ON S.SectionId=CS.SectionId
     CROSS JOIN dbo.AcademicYears Y WHERE CS.ClassSectionId=@Placement AND Y.AcademicYearId=@YearId) PlacementName,
    (SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL) FeeCount,
    (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL) FeeTotal;
   COMMIT; RETURN;
  END;
  IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59645,'Placement or fees changed. Review confirmation again.',1;
  UPDATE dbo.Enrollments SET PlacementConfirmedAt=SYSDATETIME(),BillingReady=CASE WHEN @Status=N'Active' THEN 1 ELSE BillingReady END,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@EnrollmentId;
  -- Closed historical records are confirmed as history, without new billing.
  IF @Status=N'Active' EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId,@UserId=@UserId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO

-- dbo.PostEnrollmentOpeningCharges
CREATE OR ALTER PROCEDURE dbo.PostEnrollmentOpeningCharges @EnrollmentId int,@UserId int=NULL AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @BillingStartDate AS DATE, @ChargeDate AS DATE, @Status AS NVARCHAR (30), @BillingReady AS BIT;
    SELECT @BillingStartDate = E.BillingStartDate,
           @ChargeDate = COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate),
           @Status = E.Status,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @BillingStartDate IS NULL
        THROW 59401, 'Select an enrollment with an approved billing start date.', 1;
    IF @Status NOT IN(N'Active',N'Pending')
       OR @BillingReady <> 1
        THROW 59402, 'Confirm admission before posting opening charges.', 1;
    DECLARE @OpeningMonth AS DATE = DATEFROMPARTS(YEAR(@BillingStartDate), MONTH(@BillingStartDate), 1);
    DECLARE @OneOffMonth date=DATEFROMPARTS(YEAR(@ChargeDate),MONTH(@ChargeDate),1);
    IF @@TRANCOUNT=0 THROW 59403,'Opening charges require the admission transaction.',1;
    IF @@TRANCOUNT > 0
        BEGIN
            DECLARE @LockResult AS INT;
            DECLARE @LockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @LockResult = sys.sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @LockResult < 0
                THROW 59403, 'Charges for this enrollment are already being posted.', 1;
        END
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OneOffMonth,
           @ChargeDate,
           @ChargeDate,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Annual'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Yearly'
           AND (SFA.EffectiveFrom IS NULL
                OR @OneOffMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OneOffMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.ChargeKind = N'Annual'
                                  );
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @OpeningMonth,
           @OpeningMonth,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Monthly'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly' AND SFA.Amount>SFA.DiscountAmount
           AND EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
             WHERE E.EnrollmentId=@EnrollmentId AND @OpeningMonth>=DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
               AND @OpeningMonth<=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1))
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @OpeningMonth
                                  AND BC.ChargeKind = N'Monthly'
                                  );

    -- Explicitly selected As Needed fees belong on a pending QR opening bill.
    -- Active-student additional posting keeps its established separate workflow.
    IF @Status=N'Pending'
        INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
            BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
        SELECT A.StudentFeeAssignmentId,A.EnrollmentId,A.AdmissionId,A.FeeTypeId,
            @OneOffMonth,@ChargeDate,@ChargeDate,A.Amount,A.DiscountAmount,N'Manual'
        FROM dbo.StudentFeeAssignments A
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'As Needed'
            AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=@OneOffMonth)
            AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=@OneOffMonth)
            AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@EnrollmentId
                AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N'Manual' AND B.Status<>N'Cancelled');
    DECLARE @CreditStudent int=(SELECT AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId);
    IF @@TRANCOUNT>0 EXEC dbo.ApplyStudentCredit_Internal @CreditStudent,@UserId;
END
GO

-- dbo.ApplyStudentCredit_Internal
CREATE OR ALTER PROCEDURE dbo.ApplyStudentCredit_Internal @AdmissionId int,@UserId int=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 54900,'Credit application requires a transaction.',1;
 DECLARE @Credit decimal(12,2),@Lock int,@Resource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@AdmissionId),@Actor int;
 EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 54901,'The student credit account is busy. Try again.',1;
 SELECT @Credit=COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @Credit<=0 RETURN;
 IF @UserId IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.Users WHERE UserId=@UserId)
  THROW 54902,'Credit application requires the authenticated operator.',1;
 SET @Actor=@UserId;
 DECLARE @Charge bigint,@Enrollment int,@Balance decimal(12,2),@Use decimal(12,2),@Payment int,
  @Receipt nvarchar(40),@Request uniqueidentifier;
 -- Apply only to obligations whose charge date has arrived. Never consume credit against a future opening bill early.
 DECLARE Charges CURSOR LOCAL FAST_FORWARD FOR
  SELECT B.BillingChargeId,B.EnrollmentId,dbo.GetFeeChargeBalance(B.BillingChargeId)
  FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled' AND B.ChargeDate<=CONVERT(date,GETDATE())
   AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
  ORDER BY CASE WHEN F.FeeTypeCode=N'TUITION' THEN 0 ELSE 1 END,COALESCE(B.BillingMonth,B.ChargeDate),B.BillingChargeId;
 OPEN Charges; FETCH NEXT FROM Charges INTO @Charge,@Enrollment,@Balance;
 WHILE @@FETCH_STATUS=0 AND @Credit>0
 BEGIN
  SET @Use=CASE WHEN @Credit>@Balance THEN @Balance ELSE @Credit END;
  SET @Request=NEWID();SET @Receipt=CONCAT(N'CR-',CONVERT(nvarchar(36),@Request));
  INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,CreatedByUserId,IsCreditApplication)
   VALUES(@AdmissionId,@Enrollment,CONVERT(date,GETDATE()),@Receipt,@Use,N'Account Credit',N'Existing credit applied; no new money received',N'Completed',@Actor,1);
  SET @Payment=CONVERT(int,SCOPE_IDENTITY());
  INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount,Remarks)
   VALUES(@Payment,@AdmissionId,@Charge,@Use,N'Existing student credit');
  INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,FeePaymentId,RequestId,Reason,CreatedByUserId)
   VALUES(@AdmissionId,-@Use,N'Application',@Payment,@Request,N'Existing credit applied to fees',@Actor);
  UPDATE dbo.BillingCharges SET Status=CASE WHEN dbo.GetFeeChargeBalance(@Charge)=0 THEN N'Paid' ELSE N'PartiallyPaid' END WHERE BillingChargeId=@Charge;
  SET @Credit=@Credit-@Use;
  FETCH NEXT FROM Charges INTO @Charge,@Enrollment,@Balance;
 END;
 CLOSE Charges; DEALLOCATE Charges;
END;
GO

-- dbo.AddStudentFeeAssignment
-- dbo.AddStudentFeeAssignment
CREATE OR ALTER PROCEDURE dbo.AddStudentFeeAssignment @UserId int,@EnrollmentId int,@FeeTypeId int AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId)
        THROW 53110, 'The selected enrollment does not exist.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId
                          AND (BillingReady = 1 OR PlacementConfirmedAt IS NULL))
        THROW 53114, 'Confirm return admission before adding fees.', 1;
    DECLARE @CampusId AS INT, @ClassId AS INT, @AdmissionId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId;
    DECLARE @FmscCutoffDay AS INT;
    SELECT TOP (1) @FmscCutoffDay = FeeProrationCutoffDay
    FROM   dbo.SchoolProfile;
    DECLARE @FmscOpeningMonth AS DATE = CASE WHEN @FmscCutoffDay BETWEEN 1 AND 28
                                                  AND DAY(GETDATE()) >= @FmscCutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) ELSE DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1) END;
    DECLARE @BillingYearStart date,@BillingYearEnd date,@BillingEnrollmentStart date;
    SELECT @BillingYearStart=Y.StartDate,@BillingYearEnd=Y.EndDate,@BillingEnrollmentStart=E.EnrollmentDate
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId AND E.Status IN(N'Active',N'Pending');
    IF @BillingYearStart IS NULL THROW 59630,'Correct or select an active enrollment in Admission before adding fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NULL OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1)))
        SELECT @FmscOpeningMonth=DATEFROMPARTS(YEAR(COALESCE(BillingStartDate,EnrollmentDate)),MONTH(COALESCE(BillingStartDate,EnrollmentDate)),1)
        FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
    IF @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingYearStart),MONTH(@BillingYearStart),1)
       OR (@BillingYearEnd IS NOT NULL AND @FmscOpeningMonth>DATEFROMPARTS(YEAR(@BillingYearEnd),MONTH(@BillingYearEnd),1))
       OR @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingEnrollmentStart),MONTH(@BillingEnrollmentStart),1)
       THROW 59631,'The opening fee month is outside this enrollment. Correct its placement in Admission; use reviewed historical posting for an old year.',1;
    BEGIN TRANSACTION;
    -- FMSC_ACKNOWLEDGED_ADMISSION_FEE_GUARD
    IF EXISTS(
        SELECT 1 FROM dbo.Enrollments E
        JOIN dbo.StudentAdmissionFeeAcknowledgements A WITH(UPDLOCK,HOLDLOCK)
            ON A.AdmissionId=E.AdmissionId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=@FeeTypeId
        WHERE E.EnrollmentId=@EnrollmentId AND F.FeeTypeCode=N'ADMISSION')
        THROW 59971, 'This student''s admission fee is recorded as already paid before the system. It cannot be charged again.', 1;

    DECLARE @StudentFeeAssignmentId AS INT;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments
               WHERE  EnrollmentId = @EnrollmentId
                      AND FeeTypeId = @FeeTypeId
                      AND EffectiveTo IS NULL)
        BEGIN
            DECLARE @ExistingAssignmentId AS INT;
            SELECT @ExistingAssignmentId = StudentFeeAssignmentId
            FROM   dbo.StudentFeeAssignments
            WHERE  EnrollmentId = @EnrollmentId
                   AND FeeTypeId = @FeeTypeId
                   AND EffectiveTo IS NULL;
            DECLARE @HasBillingHistory AS BIT = CASE WHEN EXISTS (SELECT 1
                                                                  FROM   dbo.BillingCharges
                                                                  WHERE  StudentFeeAssignmentId = @ExistingAssignmentId) THEN 1 ELSE 0 END;
            -- A pending registration may restore an explicitly removed, unpaid
            -- opening fee in the same month. Preserve cancelled charge history.
            IF @HasBillingHistory = 0 OR (
                EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
                    WHERE E.EnrollmentId=@EnrollmentId AND E.Status=N'Pending' AND S.Status=N'Pending')
                AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@ExistingAssignmentId AND CancelledAt IS NOT NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B JOIN dbo.FeePaymentAllocations A ON A.BillingChargeId=B.BillingChargeId
                    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
                    WHERE B.StudentFeeAssignmentId=@ExistingAssignmentId AND P.Status=N'Completed' AND A.Amount>0))
                BEGIN
                    UPDATE SFA
                    SET    SFA.CancelledAt    = NULL,
                           SFA.Amount         = FC.Amount,
                           SFA.Frequency      = FC.Frequency,
                           SFA.DiscountAmount = 0,
                           SFA.UpdatedAt      = SYSDATETIME()
                    FROM   dbo.StudentFeeAssignments AS SFA
                           INNER JOIN
                           dbo.FeeConfigurations AS FC
                           ON FC.FeeTypeId = SFA.FeeTypeId
                              AND FC.CampusId = @CampusId
                              AND FC.ClassId = @ClassId
                    WHERE  SFA.StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, 'This fee is not configured for the student''s campus/class.', 1;
                        END
                    SET @StudentFeeAssignmentId = @ExistingAssignmentId;
                END
            ELSE
                BEGIN
                    DECLARE @ReactivateEffectiveFrom AS DATE = @FmscOpeningMonth;
                    DECLARE @LastBilledMonth AS DATE;
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @ReactivateEffectiveFrom <= @LastBilledMonth
                        BEGIN
                            ROLLBACK;
                            THROW 53113, 'This fee cannot be reactivated starting this month because a later month has already been billed under the previous version. Please review Fee Setup.', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @ReactivateEffectiveFrom),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId
                           AND EffectiveTo IS NULL;
                    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                    SELECT @EnrollmentId,
                           @AdmissionId,
                           FC.FeeTypeId,
                           FC.Amount,
                           0,
                           FC.Frequency,
                           N'Reactivated - new version starting current month',
                           @ReactivateEffectiveFrom,
                           NULL
                    FROM   dbo.FeeConfigurations AS FC
                    WHERE  FC.FeeTypeId = @FeeTypeId
                           AND FC.CampusId = @CampusId
                           AND FC.ClassId = @ClassId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, 'This fee is not configured for the student''s campus/class.', 1;
                        END
                    SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
                END
        END
    ELSE
        BEGIN
            DECLARE @EffectiveFromForNewRow AS DATE = @FmscOpeningMonth;
            INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
            SELECT @EnrollmentId,
                   @AdmissionId,
                   FC.FeeTypeId,
                   FC.Amount,
                   0,
                   FC.Frequency,
                   N'Added from Student Admission',
                   @EffectiveFromForNewRow,
                   NULL
            FROM   dbo.FeeConfigurations AS FC
            WHERE  FC.FeeTypeId = @FeeTypeId
                   AND FC.CampusId = @CampusId
                   AND FC.ClassId = @ClassId;
            IF @@ROWCOUNT = 0
                BEGIN
                    ROLLBACK;
                    THROW 53112, 'This fee is not configured for the student''s campus/class.', 1;
                END
            SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
        END
    DECLARE @FmscFrequency AS NVARCHAR (30), @FmscAmount AS DECIMAL (18, 2), @FmscDiscount AS DECIMAL (18, 2), @FmscFeeTypeId AS INT, @FmscEnrollmentId AS INT, @FmscAdmissionId AS INT, @FmscStartMonth AS DATE, @FmscPostMonth AS DATE;
    SELECT @FmscFrequency = Frequency,
           @FmscAmount = Amount,
           @FmscDiscount = DiscountAmount,
           @FmscFeeTypeId = FeeTypeId,
           @FmscEnrollmentId = EnrollmentId,
           @FmscAdmissionId = AdmissionId,
           @FmscStartMonth = DATEFROMPARTS(YEAR(ISNULL(EffectiveFrom, @FmscOpeningMonth)), MONTH(ISNULL(EffectiveFrom, @FmscOpeningMonth)), 1)
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    SET @FmscPostMonth = CASE WHEN @FmscStartMonth > @FmscOpeningMonth THEN @FmscStartMonth ELSE @FmscOpeningMonth END;
    IF @FmscFrequency IN (N'Monthly', N'Yearly') AND EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NOT NULL OR Status=N'Pending') AND NOT EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1))
        BEGIN
            INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
            SELECT @StudentFeeAssignmentId,
                   @FmscEnrollmentId,
                   @FmscAdmissionId,
                   @FmscFeeTypeId,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscAmount,
                   @FmscDiscount,
                   CASE WHEN @FmscFrequency = N'Yearly' THEN N'Annual' ELSE N'Monthly' END
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.BillingCharges AS BC
                               WHERE  BC.EnrollmentId = @FmscEnrollmentId
                                      AND BC.FeeTypeId = @FmscFeeTypeId
                                      AND BC.Status <> N'Cancelled'
                                      AND ((@FmscFrequency = N'Yearly'
                                            AND BC.ChargeKind = N'Annual')
                                           OR (@FmscFrequency = N'Monthly'
                                               AND BC.ChargeKind = N'Monthly'
                                               AND BC.BillingMonth = @FmscPostMonth)));
        END
    IF EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
        WHERE E.EnrollmentId=@EnrollmentId AND E.Status=N'Pending' AND S.Status=N'Pending')
        EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId,@UserId=@UserId;
COMMIT TRANSACTION;
END
GO

-- dbo.FinishLegacyAdmissionSetup
-- dbo.FinishLegacyAdmissionSetup
CREATE OR ALTER PROCEDURE dbo.FinishLegacyAdmissionSetup @UserId int,@GoLiveDate date,@BillingStart date,@PlacementStart date=NULL,
 @Preview bit=1,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF @GoLiveDate IS NULL OR @GoLiveDate>CONVERT(date,GETDATE()) OR @BillingStart IS NULL OR DAY(@BillingStart)<>1
  THROW 59740,'Choose a go-live date up to today and a billing start month.',1;
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WITH(UPDLOCK,HOLDLOCK) WHERE CompletedAt IS NOT NULL)
  THROW 59741,'Legacy setup has already been completed. It cannot be run again.',1;
 DECLARE @Locked int,@Snapshot varchar(64),@Raw nvarchar(max),@Blocked int;
 SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE LegacySetupPending=1;
 SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 CREATE TABLE #Issues(AdmissionId int NOT NULL,Reason nvarchar(500) NOT NULL);
 INSERT #Issues SELECT AdmissionId,N'Open Admission, review and mark the legacy record verified'
 FROM dbo.Students WHERE LegacySetupPending=1 AND Status=N'Active' AND LegacyVerifiedAt IS NULL;
 INSERT #Issues SELECT AdmissionId,N'An active student needs exactly one academic placement; correct placement history in Admission'
 FROM dbo.Students S WHERE LegacySetupPending=1 AND Status=N'Active' AND
  (SELECT COUNT(*) FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active')<>1;
 SELECT E.EnrollmentId,E.AdmissionId,COALESCE(@PlacementStart,E.EnrollmentDate) StartDate,@BillingStart BillingStart INTO #Ready
 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE E.Status=N'Active' AND S.LegacySetupPending=1 AND S.LegacyVerifiedAt IS NOT NULL;
 INSERT #Issues SELECT R.AdmissionId,CONCAT(N'Placement/billing dates must fit ',Y.YearName,N' (',CONVERT(nvarchar(10),Y.StartDate,120),N' to ',CONVERT(nvarchar(10),Y.EndDate,120),N'), admission date and go-live date')
 FROM #Ready R JOIN dbo.Enrollments E ON E.EnrollmentId=R.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  JOIN dbo.Students S ON S.AdmissionId=R.AdmissionId WHERE R.StartDate NOT BETWEEN Y.StartDate AND Y.EndDate OR R.StartDate<S.AdmissionDate OR R.StartDate>@GoLiveDate
   OR @BillingStart<DATEFROMPARTS(YEAR(R.StartDate),MONTH(R.StartDate),1) OR @BillingStart>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1);
 INSERT #Issues SELECT R.AdmissionId,CONCAT(N'Missing mandatory fee: ',F.FeeTypeName,N'. Add/review it in Admission')
 FROM #Ready R CROSS JOIN dbo.FeeTypes F WHERE F.IsMandatory=1 AND NOT EXISTS(
   SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=R.EnrollmentId AND A.FeeTypeId=F.FeeTypeId AND A.CancelledAt IS NULL);
 -- No date overwrite may invalidate an existing charge/payment or overlap historical placement.
 IF @PlacementStart IS NOT NULL INSERT #Issues SELECT R.AdmissionId,N'The replacement placement date conflicts with existing bills or placement history. Keep the recorded date or review the record in Admission'
 FROM #Ready R WHERE EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=R.EnrollmentId
   AND B.Status<>N'Cancelled' AND B.ChargeDate<R.StartDate) OR EXISTS(SELECT 1 FROM dbo.Enrollments H WHERE H.AdmissionId=R.AdmissionId AND H.EnrollmentId<>R.EnrollmentId
   AND (H.LeavingDate IS NULL OR H.LeavingDate>=R.StartDate));
 SELECT @Blocked=COUNT(*) FROM #Issues;
 SET @Raw=CONCAT(CONVERT(varchar(10),@GoLiveDate,120),CONVERT(varchar(10),@BillingStart,120),CONVERT(varchar(10),@PlacementStart,120),
  (SELECT * FROM dbo.Students WHERE LegacySetupPending=1 ORDER BY AdmissionId FOR JSON PATH),
  (SELECT E.* FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY E.EnrollmentId FOR JSON PATH),
  (SELECT A.* FROM dbo.StudentFeeAssignments A JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY A.StudentFeeAssignmentId FOR JSON PATH),
  (SELECT B.* FROM dbo.BillingCharges B JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY B.BillingChargeId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1 BEGIN
  SELECT @Snapshot Snapshot,@Blocked BlockedCount,(SELECT COUNT(*) FROM #Ready R WHERE NOT EXISTS(SELECT 1 FROM #Issues I WHERE I.AdmissionId=R.AdmissionId)) ActiveStudents,
   (SELECT COUNT(*) FROM dbo.Students WHERE LegacySetupPending=1 AND Status=N'Active' AND LegacyVerifiedAt IS NULL) UnverifiedStudents,
   (SELECT STRING_AGG(CONVERT(nvarchar(max),CONCAT(D.AdmissionNumber,N' · ',D.StudentName,N' · ',D.Reason)),CHAR(13)+CHAR(10))
    FROM(SELECT TOP(200) S.AdmissionNumber,S.StudentName,I.Reason FROM #Issues I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId ORDER BY S.AdmissionId,I.Reason) D) ReviewDetails;
  SELECT S.AdmissionNumber,S.StudentName,R.StartDate PlacementStartDate,R.BillingStart FROM #Ready R JOIN dbo.Students S ON S.AdmissionId=R.AdmissionId;
  COMMIT;RETURN;
 END;
 IF @Blocked>0 THROW 59742,'Legacy setup has unresolved records, missing fee assignments or conflicting dates. Review Data Review and Admission first.',1;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59743,'Records changed after review. Preview Go Live again.',1;
 -- Remove the legacy lock only within this atomic activation; opening charges still use the usual deduplication.
 UPDATE dbo.Students SET LegacySetupPending=0 WHERE LegacySetupPending=1;
 UPDATE E SET EnrollmentDate=R.StartDate,BillingStartDate=R.BillingStart,BillingReady=1,PlacementConfirmedAt=COALESCE(E.PlacementConfirmedAt,SYSDATETIME()),UpdatedAt=SYSDATETIME()
 FROM dbo.Enrollments E JOIN #Ready R ON R.EnrollmentId=E.EnrollmentId;
 DECLARE @Enrollment int;
 DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT EnrollmentId FROM #Ready;
 OPEN C;FETCH NEXT FROM C INTO @Enrollment;
 WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.PostEnrollmentOpeningCharges @Enrollment,@UserId=@UserId;FETCH NEXT FROM C INTO @Enrollment;END;
 CLOSE C;DEALLOCATE C;
 UPDATE dbo.LegacyAdmissionSetup SET CompletedAt=SYSDATETIME(),CompletedBy=@UserId,GoLiveDate=@GoLiveDate,BillingStart=@BillingStart WHERE SetupId=1;
 COMMIT;
END;
GO

-- dbo.ConfirmPromotedAdmission
CREATE OR ALTER PROCEDURE dbo.ConfirmPromotedAdmission
@UserId INT, @EnrollmentId INT, @ReturnDate DATE, @UsesTransport BIT, @FeeChoices dbo.ReturnFeeChoiceType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @ReturnDate IS NULL
       OR @ReturnDate > CONVERT (DATE, SYSDATETIME())
        THROW 53010, 'Enter a valid return date that is not in the future.', 1;
    IF EXISTS (SELECT 1
               FROM   @FeeChoices
               WHERE  Amount < 0
                      OR DiscountAmount < 0
                      OR DiscountAmount > Amount)
        THROW 53011, 'Each fee amount and discount must be valid.', 1;
    BEGIN TRY
    BEGIN TRANSACTION;
    -- Serialize with reviewed hold/resume before taking placement locks.
    DECLARE @PostingLock int;
    EXEC @PostingLock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',
        @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
    IF @PostingLock<0 THROW 53016,'Fee posting is busy. Please try again.',1;
    DECLARE @AdmissionId AS INT, @AcademicYearId AS INT, @ClassSectionId AS INT, @EnrollmentDate AS DATE, @BillingReady AS BIT, @YearStart AS DATE, @YearEnd AS DATE, @CampusId AS INT, @ClassId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @AcademicYearId = E.AcademicYearId,
           @ClassSectionId = E.ClassSectionId,
           @EnrollmentDate = E.EnrollmentDate,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active' AND E.PlacementConfirmedAt IS NOT NULL
           AND EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=E.AdmissionId AND S.Status=N'Active' AND S.LegacySetupPending=0);
    IF @AdmissionId IS NULL
       OR @BillingReady <> 0
        THROW 53012, 'This promoted enrollment is not awaiting return confirmation.', 1;
    -- BillingReady=0 also describes a hold; it is not permission to confirm return.
    -- Check before changing fee assignments, even if no opening charge is payable.
    IF EXISTS (SELECT 1 FROM dbo.StudentPostingHolds WITH(UPDLOCK,HOLDLOCK)
               WHERE AdmissionId=@AdmissionId AND ResumeDate IS NULL)
        THROW 53017,'This student is on hold. In Admission, choose Actions > Lift Hold / Resume to review the return and fees.',1;
    SELECT @YearStart = AY.StartDate, @YearEnd = AY.EndDate
    FROM   dbo.AcademicYears AS AY
    WHERE  AY.AcademicYearId = @AcademicYearId;
    IF @YearStart IS NULL OR @YearEnd IS NULL OR CONVERT(date,GETDATE()) NOT BETWEEN @YearStart AND @YearEnd
        THROW 53018, 'This placement is outside the current academic year. Promote the previous-year placement before confirming return.', 1;
    IF @ReturnDate < @EnrollmentDate
       OR @ReturnDate > @YearEnd
        THROW 53013, 'Return date must be within the promoted academic year.', 1;
    SELECT @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments AS SFA
                      INNER JOIN
                      dbo.FeeTypes AS FT
                      ON FT.FeeTypeId = SFA.FeeTypeId
               WHERE  SFA.EnrollmentId = @EnrollmentId
                      AND SFA.CancelledAt IS NULL
                      AND FT.IsMandatory = 1
                      AND NOT EXISTS (SELECT 1
                                      FROM   @FeeChoices AS C
                                      WHERE  C.FeeTypeId = SFA.FeeTypeId))
       OR EXISTS (SELECT 1
                  FROM   @FeeChoices AS C
                  WHERE  NOT EXISTS (SELECT 1
                                     FROM   dbo.StudentFeeAssignments AS SFA
                                            INNER JOIN
                                            dbo.FeeTypes AS FT
                                            ON FT.FeeTypeId = SFA.FeeTypeId
                                     WHERE  SFA.EnrollmentId = @EnrollmentId
                                            AND SFA.CancelledAt IS NULL
                                            AND FT.IsMandatory = 1
                                            AND SFA.FeeTypeId = C.FeeTypeId))
        THROW 53014, 'Review every mandatory class fee before confirming return.', 1;
    UPDATE SFA
    SET    Amount         = C.Amount,
           DiscountAmount = C.DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           @FeeChoices AS C
           ON C.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL;
    IF @UsesTransport = 1
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                                  INNER JOIN
                                  dbo.FeeTypes AS FT
                                  ON FT.FeeTypeId = FC.FeeTypeId
                           WHERE  FC.CampusId = @CampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.Frequency = N'Monthly'
                                  AND FT.IsTransportLinked = 1)
                THROW 53015, 'Transport cannot be selected without a monthly class fee.', 1;
            INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
            SELECT @EnrollmentId,
                   @AdmissionId,
                   FC.FeeTypeId,
                   FC.Amount,
                   0,
                   FC.Frequency,
                   N'Chosen at return admission'
            FROM   dbo.FeeConfigurations AS FC
                   INNER JOIN
                   dbo.FeeTypes AS FT
                   ON FT.FeeTypeId = FC.FeeTypeId
            WHERE  FC.CampusId = @CampusId
                   AND FC.ClassId = @ClassId
                   AND FT.IsTransportLinked = 1
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.StudentFeeAssignments AS SFA
                                   WHERE  SFA.EnrollmentId = @EnrollmentId
                                          AND SFA.FeeTypeId = FC.FeeTypeId
                                          AND SFA.EffectiveTo IS NULL);
        END
    DECLARE @BillingStartDate AS DATE = dbo.GetMonthlyBillingStartDate(@ReturnDate);
    UPDATE dbo.Enrollments
    SET    BillingReady      = 1, PlacementConfirmedAt=COALESCE(PlacementConfirmedAt,SYSDATETIME()),
           ReturnConfirmedAt = CONVERT (DATETIME2 (0), @ReturnDate),
           BillingStartDate  = @BillingStartDate,
           UsesTransport     = @UsesTransport
    WHERE  EnrollmentId = @EnrollmentId;
    EXECUTE dbo.PostEnrollmentOpeningCharges @EnrollmentId = @EnrollmentId,@UserId=@UserId;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ReturnConfirmed', @ReturnDate, @ClassSectionId, @ClassSectionId, N'Active', N'Active', N'Admission confirmed after promotion', @UserId);
    COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @BillingStartDate AS BillingStartDate;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- dbo.SaveStudentDetailsWithFirstEnrollment
-- dbo.SaveStudentDetailsWithFirstEnrollment
-- SaveStudentDetailsWithFirstEnrollment
CREATE OR ALTER PROCEDURE dbo.SaveStudentDetailsWithFirstEnrollment
@UserId INT, @AdmissionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500)=NULL, @DateOfBirth DATE=NULL, @Gender NVARCHAR (20)=NULL, @BFormNumber NVARCHAR (50)=NULL, @Religion NVARCHAR (50)=NULL, @BloodGroup NVARCHAR (10)=NULL, @ContactNumber NVARCHAR (30)=NULL, @AlternateContactNumber NVARCHAR (30)=NULL, @Address NVARCHAR (500)=NULL, @PreviousSchool NVARCHAR (500)=NULL, @PlaceOfBirth NVARCHAR (500)=NULL, @FatherOccupation NVARCHAR (250)=NULL, @Disability NVARCHAR (3)=N'No', @UsesTransport BIT=NULL, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @Discounts dbo.StudentFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @CreateFirstEnrollment BIT=0, @PlacementCampusId INT=NULL, @PlacementAcademicYearId INT=NULL, @PlacementClassSectionId INT=NULL, @PlacementEnrollmentDate DATE=NULL, @PlacementStatus NVARCHAR (30)=NULL, @PlacementLeavingDate DATE=NULL, @PlacementUsesTransport BIT=0, @CompleteRegistration bit=0
AS
BEGIN
    DECLARE @FmscWriteCommitted bit=0,@FmscCallerTransaction int=@@TRANCOUNT;
    BEGIN TRY

    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51710, 'The selected student was not found.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts
               WHERE  DiscountAmount < 0)
        THROW 51711, 'A fee discount cannot be negative. Nothing was changed.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts AS D
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.StudentFeeAssignments AS SFA
                                         LEFT OUTER JOIN
                                         dbo.Enrollments AS E
                                         ON E.EnrollmentId = SFA.EnrollmentId
                                  WHERE  SFA.StudentFeeAssignmentId = D.StudentFeeAssignmentId
                                         AND (SFA.AdmissionId = @AdmissionId
                                              OR E.AdmissionId = @AdmissionId)))
        THROW 51712, 'A fee discount in this save does not belong to the student being saved. Nothing was changed.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Pending') EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer'; ELSE EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        DECLARE @StoredStatus AS NVARCHAR (30), @StoredAdmissionDate AS DATE, @StoredWithdrawalDate AS DATE;
        SELECT @StoredStatus = Status,
               @StoredAdmissionDate = AdmissionDate,
               @StoredWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @AdmissionId;
        IF @StoredStatus IS NULL
            THROW 51710, 'The selected student was not found.', 1;
        IF @CreateFirstEnrollment = 1
            BEGIN
                EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
                IF EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @AdmissionId)
                    THROW 51720, 'This student already has an enrollment. Reload the record and use enrollment corrections.', 1;
                IF @PlacementCampusId IS NULL
                   OR @PlacementAcademicYearId IS NULL
                   OR @PlacementClassSectionId IS NULL
                    THROW 51721, 'Confirm the campus, academic year, class and section.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.Campuses
                               WHERE  CampusId = @PlacementCampusId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.AcademicYears
                                  WHERE  AcademicYearId = @PlacementAcademicYearId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.CampusClassSections
                                  WHERE  ClassSectionId = @PlacementClassSectionId
                                         AND CampusId = @PlacementCampusId)
                    THROW 51721, 'The verified placement is not a valid campus, year and class section.', 1;
                IF @PlacementEnrollmentDate IS NULL
                   OR @PlacementEnrollmentDate < @StoredAdmissionDate
                   OR @PlacementEnrollmentDate > CONVERT (DATE, SYSDATETIME())
                    THROW 51722, 'Enter the confirmed enrollment date, on or after admission and not in the future.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.AcademicYears
                               WHERE  AcademicYearId = @PlacementAcademicYearId
                                      AND @PlacementEnrollmentDate BETWEEN StartDate AND EndDate)
                    THROW 51722, 'The enrollment date must fall within the selected academic year.', 1;
                IF @PlacementStatus IS NULL
                   OR @PlacementStatus NOT IN (N'Active', N'Left')
                    THROW 51723, 'Choose Active or Left for the verified enrollment.', 1;
                IF @StoredStatus = N'Left'
                   AND @PlacementStatus <> N'Left'
                    THROW 51723, 'An already-left student needs a historical Left enrollment. Use Re-Admit to return them.', 1;
                IF @PlacementStatus = N'Active'
                   AND (@PlacementLeavingDate IS NOT NULL
                        OR @StoredWithdrawalDate IS NOT NULL)
                    THROW 51724, 'An active enrollment cannot have a withdrawal date.', 1;
                IF @StoredWithdrawalDate IS NOT NULL
                   AND @PlacementLeavingDate IS NOT NULL
                   AND @StoredWithdrawalDate <> @PlacementLeavingDate
                    THROW 51724, 'The confirmed withdrawal date differs from the stored date. Correct the stored date through the status workflow first.', 1;
                SET @PlacementLeavingDate = COALESCE (@PlacementLeavingDate, @StoredWithdrawalDate);
                IF @PlacementLeavingDate IS NOT NULL
                   AND (@PlacementLeavingDate < @PlacementEnrollmentDate
                        OR @PlacementLeavingDate > CONVERT (DATE, SYSDATETIME()))
                    THROW 51724, 'Withdrawal date must be on or after enrollment and not in the future.', 1;
                IF EXISTS (SELECT 1
                           FROM   @Discounts)
                    THROW 51725, 'First placement cannot include discounts from another enrollment.', 1;
            END
        IF @AdmissionNumber IS NOT NULL AND @CompleteRegistration=0
            EXECUTE dbo.SetStudentAdmissionNumber @UserId, @AdmissionId, @AdmissionNumber;
        EXECUTE dbo.SaveStudentDetails @UserId = @UserId, @AdmissionId = @AdmissionId, @StudentName = @StudentName, @FatherName = @FatherName, @DateOfBirth = @DateOfBirth, @Gender = @Gender, @BFormNumber = @BFormNumber, @Religion = @Religion, @BloodGroup = @BloodGroup, @ContactNumber = @ContactNumber, @AlternateContactNumber = @AlternateContactNumber, @Address = @Address, @PreviousSchool = @PreviousSchool, @PlaceOfBirth = @PlaceOfBirth, @FatherOccupation = @FatherOccupation, @Disability = @Disability, @UsesTransport = @UsesTransport, @BiseEnrolmentNumber = @BiseEnrolmentNumber, @CasteTribe = @CasteTribe;
        IF EXISTS (SELECT 1
                   FROM   @Discounts)
            BEGIN
                DECLARE @StudentFeeAssignmentId AS INT, @DiscountAmount AS DECIMAL (12, 2);
                DECLARE DiscountRows CURSOR LOCAL FAST_FORWARD
                    FOR SELECT   StudentFeeAssignmentId,
                                 DiscountAmount
                        FROM     @Discounts
                        ORDER BY StudentFeeAssignmentId;
                OPEN DiscountRows;
                FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                WHILE @@FETCH_STATUS = 0
                    BEGIN
                        EXECUTE dbo.SaveStudentFeeDiscount @UserId = @UserId, @StudentFeeAssignmentId = @StudentFeeAssignmentId, @DiscountAmount = @DiscountAmount;
                        FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                    END
                CLOSE DiscountRows;
                DEALLOCATE DiscountRows;
            END
        IF @CreateFirstEnrollment = 1
            BEGIN
                DECLARE @NewEnrollmentId AS INT;
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, UsesTransport, Remarks, BillingStartDate, BillingReady)
                VALUES                 (@AdmissionId, @PlacementAcademicYearId, @PlacementClassSectionId, @PlacementEnrollmentDate, @PlacementLeavingDate, @PlacementStatus, @PlacementUsesTransport, N'First placement verified from physical school record on Admission form; no fees generated.', NULL, 0);
                SET @NewEnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, NewClassSectionId, NewStatus, Remarks, UserId)
                VALUES                       (@NewEnrollmentId, @AdmissionId, N'VERIFIED_FIRST_PLACEMENT', CONVERT (DATE, SYSDATETIME()), @PlacementClassSectionId, @PlacementStatus, N'Confirmed from physical school record.', @UserId);
                IF @StoredStatus <> @PlacementStatus
                   OR ISNULL(@StoredWithdrawalDate, '00010101') <> ISNULL(@PlacementLeavingDate, '00010101')
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = @PlacementStatus,
                               WithdrawalDate = @PlacementLeavingDate,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @StoredStatus, @PlacementStatus, N'Physical record verification', COALESCE (@PlacementLeavingDate, @PlacementEnrollmentDate), N'First verified enrollment recorded on Admission form.', @UserId);
                    END
            END
IF @CompleteRegistration=1 EXEC dbo.CompleteQuickAdmission @UserId,@AdmissionId,@AdmissionNumber;
        COMMIT TRANSACTION;
    SET @FmscWriteCommitted=1;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
    SELECT @AdmissionId AS AdmissionId;

    END TRY
    BEGIN CATCH
        IF @FmscCallerTransaction=0 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        -- An enclosing C# transaction retains its own rollback/recovery ownership.
        -- State 60 is a verified admission refusal, never a lost commit response.
        IF @FmscCallerTransaction=0 AND @FmscWriteCommitted=0 AND @@TRANCOUNT=0 AND ERROR_NUMBER() BETWEEN 50000 AND 59999
        BEGIN
            DECLARE @FmscError int=ERROR_NUMBER(),@FmscMessage nvarchar(2048)=ERROR_MESSAGE();
            THROW @FmscError,@FmscMessage,60;
        END;
        THROW;
    END CATCH
END
GO

-- dbo.AdmitHistoricalStudent
-- dbo.AdmitHistoricalStudent
-- AdmitHistoricalStudent
CREATE OR ALTER PROCEDURE dbo.AdmitHistoricalStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @VerifiedEnrollmentDate DATE, @VerifiedStatus NVARCHAR (30), @VerifiedWithdrawalDate DATE=NULL
AS
BEGIN
    DECLARE @FmscWriteCommitted bit=0,@FmscCallerTransaction int=@@TRANCOUNT;
    BEGIN TRY

    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF NULLIF (LTRIM(RTRIM(@AdmissionNumber)), '') IS NULL
        THROW 51726, 'Enter the admission number from the physical register.', 1;
    IF @VerifiedStatus IS NULL
       OR @VerifiedStatus NOT IN (N'Active', N'Left')
        THROW 51723, 'Confirm Active or Left for the historical record.', 1;
    IF @VerifiedEnrollmentDate IS NULL
       OR @VerifiedEnrollmentDate < @AdmissionDate
       OR @VerifiedEnrollmentDate > CONVERT (DATE, SYSDATETIME())
        THROW 51722, 'Confirm the enrollment date on or after admission, not in the future.', 1;
    IF @VerifiedWithdrawalDate IS NOT NULL
       AND (@VerifiedStatus <> N'Left'
            OR @VerifiedWithdrawalDate < @VerifiedEnrollmentDate
            OR @VerifiedWithdrawalDate > CONVERT (DATE, SYSDATETIME()))
        THROW 51724, 'Confirm a withdrawal date on or after enrollment for a Left student.', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedFeeTypeIds)
       OR EXISTS (SELECT 1
                  FROM   @AdmissionDiscounts)
        THROW 51725, 'Historical records cannot generate fees or discounts.', 1;
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @VerifiedEnrollmentDate < @YearStartDate
       OR @VerifiedEnrollmentDate > @YearEndDate
        THROW 51022, 'The verified enrollment date must fall within the selected academic year''s date range.', 1;
    DECLARE @BillingStartDate AS DATE = NULL;
    BEGIN TRANSACTION;
    EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, @VerifiedStatus, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WHERE CompletedAt IS NULL) UPDATE dbo.Students SET LegacySetupPending=1 WHERE AdmissionId=@AdmissionId;
    UPDATE dbo.Students
    SET    WithdrawalDate = @VerifiedWithdrawalDate
    WHERE  AdmissionId = @AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, LeavingDate, BillingReady)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @VerifiedEnrollmentDate, @BillingStartDate, @VerifiedStatus, @UsesTransport, N'Historical admission verified from physical school record; no fees generated.', @VerifiedWithdrawalDate, 0);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, @VerifiedStatus, N'Historical first enrollment confirmed from physical record', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, @VerifiedStatus, N'Admission', @AdmissionDate, N'Historical student status confirmed from physical record', @UserId);
COMMIT TRANSACTION;
    SET @FmscWriteCommitted=1;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber;

    END TRY
    BEGIN CATCH
        IF @FmscCallerTransaction=0 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        -- An enclosing C# transaction retains its own rollback/recovery ownership.
        -- State 60 is a verified admission refusal, never a lost commit response.
        IF @FmscCallerTransaction=0 AND @FmscWriteCommitted=0 AND @@TRANCOUNT=0 AND ERROR_NUMBER() BETWEEN 50000 AND 59999
        BEGIN
            DECLARE @FmscError int=ERROR_NUMBER(),@FmscMessage nvarchar(2048)=ERROR_MESSAGE();
            THROW @FmscError,@FmscMessage,60;
        END;
        THROW;
    END CATCH
END
GO

-- dbo.AdmitStudent
CREATE OR ALTER PROCEDURE dbo.AdmitStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @QuickRegistration bit=0, @FamilyStudentId int=NULL, @FeeConcessionsJson nvarchar(max)=NULL,@MonthlyDiscountFrom date=NULL,@UseSelectedFeeTypes bit=0
AS
BEGIN
    DECLARE @FmscWriteCommitted bit=0,@FmscCallerTransaction int=@@TRANCOUNT;
    BEGIN TRY

    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    IF @FeeConcessionsJson IS NOT NULL AND ISJSON(@FeeConcessionsJson)<>1
      THROW 51024,'Review the selected fee concessions.',1;
    DECLARE @Concessions TABLE(FeeTypeId int NOT NULL PRIMARY KEY,ConcessionLabel nvarchar(20) NOT NULL);
    IF EXISTS(SELECT 1 FROM OPENJSON(@FeeConcessionsJson) WITH(FeeTypeId int,ConcessionLabel nvarchar(20))
      WHERE FeeTypeId IS NULL OR ConcessionLabel IS NULL OR ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived'))
      THROW 51024,'Choose a valid fee and concession.',1;
    INSERT @Concessions SELECT FeeTypeId,ConcessionLabel FROM OPENJSON(@FeeConcessionsJson)
      WITH(FeeTypeId int,ConcessionLabel nvarchar(20));
    IF EXISTS(SELECT 1 FROM @Concessions) EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
    DECLARE @ReviewedDiscounts dbo.AdmissionFeeDiscountType;
    INSERT @ReviewedDiscounts SELECT * FROM @AdmissionDiscounts;
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts)
        EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @AdmissionDate < @YearStartDate
       OR @AdmissionDate > @YearEndDate
        THROW 51022, 'The admission date must fall within the selected academic year''s date range.', 1;
    DECLARE @MissingMandatoryFees AS NVARCHAR (1000);
    SELECT @MissingMandatoryFees = STRING_AGG(FT.FeeTypeName, N', ')
    FROM   dbo.FeeTypes AS FT
    WHERE  FT.IsMandatory = 1
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                           WHERE  FC.CampusId = @AdmissionCampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.FeeTypeId = FT.FeeTypeId);
    IF @UseSelectedFeeTypes=0 AND @YearEndDate>=CONVERT(date,GETDATE()) AND @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            THROW 51019, @MandatoryFeeErrorMsg, 1;
        END
    IF @UseSelectedFeeTypes=1 AND @YearEndDate>=CONVERT(date,GETDATE()) AND EXISTS
      (SELECT 1 FROM @SelectedFeeTypeIds S WHERE NOT EXISTS(SELECT 1 FROM dbo.FeeConfigurations C
       WHERE C.CampusId=@AdmissionCampusId AND C.ClassId=@ClassId AND C.FeeTypeId=S.FeeTypeId))
      THROW 51019,'A selected fee has no configured class rate. Reload the fees and review the selection.',1;
    DECLARE @Historical bit=CASE WHEN @YearEndDate<CONVERT(date,GETDATE()) THEN 1 ELSE 0 END;
    DECLARE @BillingStartDate AS DATE;
    SET @BillingStartDate = CASE WHEN @Historical=0 THEN dbo.GetMonthlyBillingStartDate(CONVERT(date,GETDATE())) END;
    BEGIN TRANSACTION;
    IF @QuickRegistration=0 EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT; ELSE SET @AdmissionNumber=NULL;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    IF @QuickRegistration=0 EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF @QuickRegistration=1 UPDATE dbo.Students SET QuickReference=CONCAT('QR-',@AdmissionId) WHERE AdmissionId=@AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, BillingReady, PlacementConfirmedAt)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @AdmissionDate, @BillingStartDate, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, @UsesTransport, N'Created by admission',CASE WHEN @Historical=1 THEN 0 ELSE 1 END,CASE WHEN @QuickRegistration=0 THEN SYSDATETIME() END);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    -- Past academic years record history only; no fee assignments or opening charges.
    IF @Historical=0
    BEGIN
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT @EnrollmentId,
           @AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           N'Created by admission'
    FROM   dbo.FeeConfigurations AS FC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FC.CampusId = @AdmissionCampusId
           AND FC.ClassId = @ClassId
           AND (FC.FeeTypeId IN (SELECT FeeTypeId FROM @SelectedFeeTypeIds)
                OR (@UseSelectedFeeTypes=0 AND (FT.IsMandatory=1 OR (FT.IsTransportLinked=1 AND @UsesTransport=1))));
    IF EXISTS(SELECT 1 FROM @Concessions C LEFT JOIN dbo.StudentFeeAssignments A
      ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId WHERE A.StudentFeeAssignmentId IS NULL OR A.Frequency<>N'Monthly')
      THROW 51024,'Concessions must refer to an assigned monthly fee.',1;
    UPDATE D SET DiscountAmount=A.Amount FROM @ReviewedDiscounts D JOIN @Concessions C ON C.FeeTypeId=D.FeeTypeId
      JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId
      WHERE C.ConcessionLabel IN(N'Free',N'Waived');
    INSERT @ReviewedDiscounts SELECT C.FeeTypeId,A.Amount FROM @Concessions C
      JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId
      WHERE C.ConcessionLabel IN(N'Free',N'Waived') AND NOT EXISTS(SELECT 1 FROM @ReviewedDiscounts D WHERE D.FeeTypeId=C.FeeTypeId);
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts AS D
                      LEFT OUTER JOIN
                      dbo.StudentFeeAssignments AS SFA
                      ON SFA.EnrollmentId = @EnrollmentId
                         AND SFA.FeeTypeId = D.FeeTypeId
               WHERE  SFA.StudentFeeAssignmentId IS NULL
                      OR D.DiscountAmount > SFA.Amount)
        THROW 51024, 'A fee discount is invalid or exceeds the assigned fee.', 1;
    UPDATE SFA
    SET    DiscountAmount = D.DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           @ReviewedDiscounts AS D
           ON D.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId AND SFA.Frequency<>N'Monthly';
    -- The school chooses the first concession month; legacy callers retain next-month defaults.
    DECLARE @DiscountFrom date=COALESCE(@MonthlyDiscountFrom,DATEADD(month,1,DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1)));
    IF DAY(@DiscountFrom)<>1 OR @DiscountFrom<DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1) OR @MonthlyDiscountFrom>@YearEndDate
      THROW 51024,'Choose a first concession month within the admission academic year.',1;
    IF @DiscountFrom=DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1)
    BEGIN
      UPDATE A SET DiscountAmount=D.DiscountAmount,ConcessionLabel=C.ConcessionLabel
       FROM dbo.StudentFeeAssignments A JOIN @ReviewedDiscounts D ON D.FeeTypeId=A.FeeTypeId
       LEFT JOIN @Concessions C ON C.FeeTypeId=A.FeeTypeId
       WHERE A.EnrollmentId=@EnrollmentId AND A.Frequency=N'Monthly';
    END
    ELSE IF EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId AND @DiscountFrom<=EndDate)
    BEGIN
      SELECT A.*,D.DiscountAmount ChosenDiscount INTO #AdmissionDiscountVersions
       FROM dbo.StudentFeeAssignments A JOIN @ReviewedDiscounts D ON D.FeeTypeId=A.FeeTypeId
       WHERE A.EnrollmentId=@EnrollmentId AND A.Frequency=N'Monthly' AND D.DiscountAmount>0;
      UPDATE A SET EffectiveTo=DATEADD(month,-1,@DiscountFrom)
       FROM dbo.StudentFeeAssignments A JOIN #AdmissionDiscountVersions V ON V.StudentFeeAssignmentId=A.StudentFeeAssignmentId;
      INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
       SELECT V.EnrollmentId,V.AdmissionId,V.FeeTypeId,V.Amount,V.ChosenDiscount,V.Frequency,V.Remarks,@DiscountFrom,NULL,C.ConcessionLabel
       FROM #AdmissionDiscountVersions V LEFT JOIN @Concessions C ON C.FeeTypeId=V.FeeTypeId;
    END;
    END;
    IF @FamilyStudentId IS NOT NULL EXEC dbo.AddStudentSibling @AdmissionId,@FamilyStudentId,N'Sibling',@UserId;
    IF @Historical=0 EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId,@UserId=@UserId;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Initial placement created', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Admission', @AdmissionDate, N'Initial student status', @UserId);

COMMIT TRANSACTION;
    SET @FmscWriteCommitted=1;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber, (SELECT QuickReference FROM dbo.Students WHERE AdmissionId=@AdmissionId) QuickReference;

    END TRY
    BEGIN CATCH
        IF @FmscCallerTransaction=0 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        -- An enclosing C# transaction retains its own rollback/recovery ownership.
        -- State 60 is a verified admission refusal, never a lost commit response.
        IF @FmscCallerTransaction=0 AND @FmscWriteCommitted=0 AND @@TRANCOUNT=0 AND ERROR_NUMBER() BETWEEN 50000 AND 59999
        BEGIN
            DECLARE @FmscError int=ERROR_NUMBER(),@FmscMessage nvarchar(2048)=ERROR_MESSAGE();
            THROW @FmscError,@FmscMessage,60;
        END;
        THROW;
    END CATCH
END
GO

-- dbo.ReAdmitStudent
CREATE OR ALTER PROCEDURE dbo.ReAdmitStudent
@UserId INT, @AdmissionId INT, @CampusId INT, @AcademicYearId INT, @ClassSectionId INT, @EnrollmentDate DATE, @Reason NVARCHAR (500), @UsesTransport BIT, @MonthlyStartMonth date=NULL, @ChargeYearlyFees bit=NULL, @ReviewedRates nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @AdmissionId <= 0
        THROW 56510, 'A valid student is required.', 1;
    IF @CampusId <= 0
        THROW 56511, 'A valid campus is required.', 1;
    IF @AcademicYearId <= 0
        THROW 56512, 'A valid academic year is required.', 1;
    IF @ClassSectionId <= 0
        THROW 56513, 'A valid class section is required.', 1;
    IF @EnrollmentDate IS NULL
        THROW 56514, 'Enrollment date is required.', 1;
    IF @EnrollmentDate > CONVERT (DATE, SYSDATETIME())
        THROW 56515, 'Enrollment date cannot be in the future.', 1;

    IF @MonthlyStartMonth IS NULL OR @ChargeYearlyFees IS NULL
        THROW 57100, 'Choose the monthly start month and yearly-fee decision before readmission.',1;
    BEGIN TRANSACTION;
    DECLARE @OldStudentStatus AS NVARCHAR (30), @OldWithdrawalDate AS DATE, @OldEnrollmentId AS INT = NULL, @ClassId AS INT, @SectionCampusId AS INT, @MaxPriorLeavingDate AS DATE = NULL, @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @OldStudentStatus = S.Status,
           @OldWithdrawalDate = S.WithdrawalDate
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @OldStudentStatus IS NULL
        THROW 56516, 'The student was not found.', 1;
    SELECT TOP (1) @OldEnrollmentId = E.EnrollmentId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
    WHERE  E.AdmissionId = @AdmissionId
           AND E.Status = N'Active';
    IF @OldEnrollmentId IS NOT NULL
        THROW 56517, 'The student already has an active enrollment.', 1;
    IF @OldStudentStatus = N'Active'
        THROW 56524, 'The student is already active.', 1;
    SELECT @MaxPriorLeavingDate = MAX(E2.LeavingDate)
    FROM   dbo.Enrollments AS E2
    WHERE  E2.AdmissionId = @AdmissionId
           AND E2.LeavingDate IS NOT NULL;
    IF @MaxPriorLeavingDate IS NOT NULL
       AND @EnrollmentDate <= @MaxPriorLeavingDate
        BEGIN
            ROLLBACK;
            THROW 56527, 'The re-admission enrollment date must be after the student''s most recent leaving date on file. Correct the prior enrollment''s leaving date first if this re-admission is genuinely meant to start on or before it.', 1;
        END
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @CampusId)
        THROW 56518, 'The selected campus is not active.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 56519, 'The selected academic year is not active.', 1;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @EnrollmentDate < @YearStartDate
       OR @EnrollmentDate > @YearEndDate
        BEGIN
            ROLLBACK;
            THROW 56528, 'The re-admission enrollment date must fall within the selected academic year''s date range.', 1;
        END
    SELECT @SectionCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @SectionCampusId IS NULL
        THROW 56521, 'The selected class section is invalid.', 1;
    IF @SectionCampusId <> @CampusId
        THROW 56522, 'The selected class section does not belong to the selected campus.', 1;
    DECLARE @MissingMandatoryFees AS NVARCHAR (1000);
    SELECT @MissingMandatoryFees = STRING_AGG(FT.FeeTypeName, N', ')
    FROM   dbo.FeeTypes AS FT
    WHERE  FT.IsMandatory = 1
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                           WHERE  FC.CampusId = @CampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.FeeTypeId = FT.FeeTypeId);
    IF @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            ROLLBACK;
            THROW 56525, @MandatoryFeeErrorMsg, 1;
        END

    IF @ReviewedRates IS NULL OR ISJSON(@ReviewedRates)<>1
      THROW 57107,'Review the readmission rates before confirming.',1;
    DECLARE @Rates TABLE(FeeTypeId int,Amount decimal(12,2),Frequency nvarchar(40));
    INSERT @Rates SELECT FeeTypeId,Amount,Frequency FROM OPENJSON(@ReviewedRates)
       WITH(FeeTypeId int,Amount decimal(12,2),Frequency nvarchar(40));
    IF EXISTS(SELECT FC.FeeTypeId,FC.Amount,FC.Frequency FROM dbo.FeeConfigurations FC WITH(UPDLOCK,HOLDLOCK)
       JOIN dbo.FeeTypes FT ON FT.FeeTypeId=FC.FeeTypeId WHERE FC.CampusId=@CampusId AND FC.ClassId=@ClassId
       AND (@UsesTransport=1 OR FT.IsTransportLinked=0) EXCEPT SELECT * FROM @Rates)
       OR EXISTS(SELECT * FROM @Rates EXCEPT SELECT FC.FeeTypeId,FC.Amount,FC.Frequency FROM dbo.FeeConfigurations FC
       JOIN dbo.FeeTypes FT ON FT.FeeTypeId=FC.FeeTypeId WHERE FC.CampusId=@CampusId AND FC.ClassId=@ClassId
       AND (@UsesTransport=1 OR FT.IsTransportLinked=0))
      THROW 57108,'Readmission rates changed. Review them again; nothing was saved.',1;
    DECLARE @BillingStartDate date;
    IF DAY(@MonthlyStartMonth)<>1 OR @MonthlyStartMonth<DATEFROMPARTS(YEAR(@EnrollmentDate),MONTH(@EnrollmentDate),1)
       OR @MonthlyStartMonth>DATEFROMPARTS(YEAR(@YearEndDate),MONTH(@YearEndDate),1)
        THROW 57101,'Choose a monthly start month within the remaining academic year.',1;
    SET @BillingStartDate=@MonthlyStartMonth;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, Remarks, UsesTransport, BillingReady, PlacementConfirmedAt)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @EnrollmentDate, @BillingStartDate, N'Active', @Reason, @UsesTransport, 1, SYSDATETIME());
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT @EnrollmentId,
           @AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           CASE WHEN FC.Frequency=N'Yearly' AND @ChargeYearlyFees=0 THEN FC.Amount ELSE 0 END,
           FC.Frequency,
           CASE WHEN FC.Frequency=N'Yearly' AND @ChargeYearlyFees=0 THEN N'Yearly fee explicitly waived at re-admission' ELSE N'Created by re-admission' END
    FROM   dbo.FeeConfigurations AS FC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FC.CampusId = @CampusId
           AND FC.ClassId = @ClassId
           AND (@UsesTransport = 1
                OR FT.IsTransportLinked = 0);
    EXECUTE dbo.PostEnrollmentOpeningCharges @EnrollmentId = @EnrollmentId,@UserId=@UserId;
    UPDATE dbo.Students
    SET    Status         = N'Active',
           WithdrawalDate = NULL,
           UpdatedAt      = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    IF @@ROWCOUNT <> 1
        THROW 56523, 'The student status could not be updated.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'RE-ADMIT', @EnrollmentDate, NULL, @ClassSectionId, NULL, N'Active', @Reason, @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, @OldStudentStatus, N'Active', N'Re-admission', @EnrollmentDate, @Reason, @UserId);

COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @EnrollmentDate AS EnrollmentDate;
END
GO

-- dbo.ActivateStudentBilling
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
 EXEC dbo.ApplyStudentCredit_Internal @AdmissionId,@UserId;
 COMMIT;
 SELECT @EnrollmentId EnrollmentId;
 END TRY
 BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
 END CATCH;
END;
GO

-- dbo.ReverseBillingCharge
-- dbo.ReverseBillingCharge
-- ReverseBillingCharge
CREATE OR ALTER PROCEDURE dbo.ReverseBillingCharge
@UserId INT, @BillingChargeId BIGINT, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 53201, 'A reason is required to reverse a charge.', 1;
    DECLARE @Status AS NVARCHAR (40);
    DECLARE @FeeTypeName AS NVARCHAR (200);
    DECLARE @NetAmount AS DECIMAL (18, 2);
    DECLARE @BillingMonth AS DATE;
    DECLARE @AdmissionId AS INT;
    SELECT @Status = BC.Status,
           @FeeTypeName = FT.FeeTypeName,
           @NetAmount = BC.NetAmount,
           @BillingMonth = BC.BillingMonth,
           @AdmissionId = BC.AdmissionId
    FROM   dbo.BillingCharges AS BC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = BC.FeeTypeId
    WHERE  BC.BillingChargeId = @BillingChargeId;
    IF @Status IS NULL
        THROW 53200, 'The selected charge does not exist. Reload the list and try again.', 1;
    IF @Status = N'Cancelled'
        THROW 53202, 'This charge has already been reversed.', 1;
    IF @Status <> N'Open'
       OR (SELECT ISNULL(SUM(A.Amount), 0)
           FROM   dbo.FeePaymentAllocations AS A
                  INNER JOIN
                  dbo.FeePayments AS FP
                  ON FP.FeePaymentId = A.FeePaymentId
                     AND FP.Status = N'Completed'
           WHERE  A.BillingChargeId = @BillingChargeId) > 0
        THROW 53203, 'This fee has already been collected. It cannot be reversed as an uncollected charge - use Correction, Refund or Reversal in Student Fees.', 1;
    DECLARE @CleanedReason AS NVARCHAR (500) = LTRIM(RTRIM(@Reason));
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.BillingCharges
        SET    Status  = N'Cancelled',
               CancellationReason = @CleanedReason, CancelledAt=SYSDATETIME(), CancelledByUserId=@UserId
        FROM   dbo.BillingCharges AS BC
        WHERE  BC.BillingChargeId = @BillingChargeId
               AND BC.Status = N'Open';
        IF @@ROWCOUNT = 0
            THROW 53204, 'The charge could not be reversed because it changed while this screen was open. Reload the list and try again.', 1;
COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;
        THROW;
    END CATCH
    SELECT @BillingChargeId AS BillingChargeId,
           @AdmissionId AS AdmissionId,
           @FeeTypeName AS FeeTypeName,
           @BillingMonth AS BillingMonth,
           @NetAmount AS AmountReversed,
           @CleanedReason AS Reason,
           N'Cancelled' AS Status;
END
GO

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'60',SYSDATETIME(),SUSER_SNAME(),N'Admission/fee flow: cancellation permission and reason, explicit credit actor, verified admission rejection');
GO
