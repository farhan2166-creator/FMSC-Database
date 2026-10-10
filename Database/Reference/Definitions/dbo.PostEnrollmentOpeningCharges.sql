SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- PostEnrollmentOpeningCharges
CREATE PROCEDURE dbo.PostEnrollmentOpeningCharges
@EnrollmentId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @BillingStartDate AS DATE, @ChargeDate AS DATE, @Status AS NVARCHAR (30), @BillingReady AS BIT;
    SELECT @BillingStartDate = ISNULL(E.BillingStartDate, E.EnrollmentDate),
           @ChargeDate = COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate),
           @Status = E.Status,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @BillingStartDate IS NULL
        THROW 59401, 'The enrollment does not exist.', 1;
    IF @Status <> N'Active'
       OR @BillingReady <> 1
        THROW 59402, 'Confirm admission before posting opening charges.', 1;
    DECLARE @OpeningMonth AS DATE = DATEFROMPARTS(YEAR(@BillingStartDate), MONTH(@BillingStartDate), 1);
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
           @OpeningMonth,
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
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
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
           AND SFA.Frequency = N'Monthly'
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
END



GO
