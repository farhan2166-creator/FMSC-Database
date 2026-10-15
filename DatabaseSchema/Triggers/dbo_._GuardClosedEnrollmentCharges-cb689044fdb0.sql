-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 -- Unpaid charge cancellation is allowed through the audited reversal workflow; closed bills cannot be rewritten.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status NOT IN(N'Active',N'Pending') AND (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId
    OR I.StudentFeeAssignmentId<>D.StudentFeeAssignmentId OR I.AdmissionId<>D.AdmissionId OR I.FeeTypeId<>D.FeeTypeId
    OR I.GrossAmount<>D.GrossAmount OR I.DiscountAmount<>D.DiscountAmount
    OR ISNULL(I.BillingMonth,'19000101')<>ISNULL(D.BillingMonth,'19000101')
    OR I.ChargeDate<>D.ChargeDate OR I.DueDate<>D.DueDate OR I.ChargeKind<>D.ChargeKind
    OR (I.Status<>D.Status AND D.Status=N'Cancelled')))
  THROW 57102,'This enrollment is closed. Its charges cannot be posted, changed or reversed here. Outstanding fees can still be collected.',1;
 IF EXISTS(SELECT 1 FROM deleted D LEFT JOIN inserted I ON I.BillingChargeId=D.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE I.BillingChargeId IS NULL AND E.Status NOT IN(N'Active',N'Pending'))
  THROW 57102,'Closed enrollment charges must remain in history.',1;
 -- New bills must fit the enrollment and academic year, including backdated closures.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=I.EnrollmentId
    JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE D.BillingChargeId IS NULL AND I.Status<>N'Cancelled' AND ((E.PlacementConfirmedAt IS NULL AND E.Status<>N'Pending') OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=E.AdmissionId AND S.LegacySetupPending=1) OR 
      COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)
      OR COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
      OR (E.Status=N'Pending' AND COALESCE(I.BillingMonth,I.ChargeDate)<>DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)) OR (Y.EndDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1))
      OR (E.LeavingDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1))))
    THROW 59633,'The charge period is outside the enrollment or academic year. Correct placement or billing dates in Admission before posting.',1;
 -- Protect payments even if an alternative reversal entry point bypasses the form.
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   WHERE I.Status=N'Cancelled' AND D.Status<>N'Cancelled' AND EXISTS(
    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.BillingChargeId=I.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0))
  THROW 57103,'A paid or partially paid charge cannot be cancelled. Use the payment correction or refund workflow.',1;
END;
