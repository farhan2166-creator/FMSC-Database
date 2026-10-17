-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE FUNCTION dbo.fn_ExpectedRecurringChargeMonths(@EnrollmentId int,@AsOfDate date)
RETURNS TABLE AS RETURN
 WITH Numbers AS(SELECT 0 N UNION ALL SELECT N+1 FROM Numbers WHERE N<23),
 EnrollmentWindow AS(
  SELECT E.EnrollmentId,DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1) StartMonth,
   DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1) YearStart,
   DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1) YearEnd,
   DATEFROMPARTS(YEAR(@AsOfDate),MONTH(@AsOfDate),1) AsOfMonth,
   CASE WHEN E.LeavingDate IS NULL THEN NULL ELSE
    DATEADD(month,-1,DATEFROMPARTS(YEAR(dbo.GetMonthlyBillingStartDate(E.LeavingDate)),MONTH(dbo.GetMonthlyBillingStartDate(E.LeavingDate)),1)) END LastMonth
  FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE E.EnrollmentId=@EnrollmentId AND E.BillingStartDate IS NOT NULL AND E.BillingReady=1
   AND E.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
   AND E.Status IN(N'Active',N'Promoted',N'Completed',N'Left'))
 SELECT A.StudentFeeAssignmentId,A.FeeTypeId,M.BillingMonth FROM EnrollmentWindow W CROSS JOIN Numbers N
 CROSS APPLY(SELECT DATEADD(month,N.N,W.YearStart) BillingMonth) M
 JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=W.EnrollmentId
 WHERE M.BillingMonth>=W.StartMonth AND M.BillingMonth<=W.YearEnd AND M.BillingMonth<=W.AsOfMonth
  AND (W.LastMonth IS NULL OR M.BillingMonth<=W.LastMonth)
  AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
  AND (A.EffectiveFrom IS NULL OR M.BillingMonth>=A.EffectiveFrom)
  AND (A.EffectiveTo IS NULL OR M.BillingMonth<=A.EffectiveTo);
