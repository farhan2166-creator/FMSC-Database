-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.PostEnrollmentOpeningCharges @EnrollmentId int AS

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
    IF @@TRANCOUNT>0 EXEC dbo.ApplyStudentCredit_Internal @CreditStudent;
END
