SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   TRIGGER dbo.GuardClosedEnrollmentCharges ON dbo.BillingCharges AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- Payment status changes remain allowed; cancelling or rewriting a closed bill does not.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status<>N'Active' AND (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId
    OR I.StudentFeeAssignmentId<>D.StudentFeeAssignmentId OR I.AdmissionId<>D.AdmissionId OR I.FeeTypeId<>D.FeeTypeId
    OR I.GrossAmount<>D.GrossAmount OR I.DiscountAmount<>D.DiscountAmount
    OR ISNULL(I.BillingMonth,'19000101')<>ISNULL(D.BillingMonth,'19000101')
    OR I.ChargeDate<>D.ChargeDate OR I.DueDate<>D.DueDate OR I.ChargeKind<>D.ChargeKind
    OR (I.Status<>D.Status AND (I.Status=N'Cancelled' OR D.Status=N'Cancelled'))))
  THROW 57102,'This enrollment is closed. Its charges cannot be posted, changed or reversed here. Outstanding fees can still be collected.',1;
 IF EXISTS(SELECT 1 FROM deleted D LEFT JOIN inserted I ON I.BillingChargeId=D.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE I.BillingChargeId IS NULL AND E.Status<>N'Active')
  THROW 57102,'Closed enrollment charges must remain in history.',1;
 -- Protect payments even if an alternative reversal entry point bypasses the form.
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   WHERE I.Status=N'Cancelled' AND D.Status<>N'Cancelled' AND EXISTS(
    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.BillingChargeId=I.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0))
  THROW 57103,'A paid or partially paid charge cannot be cancelled. Use the payment correction or refund workflow.',1;
END;

GO
