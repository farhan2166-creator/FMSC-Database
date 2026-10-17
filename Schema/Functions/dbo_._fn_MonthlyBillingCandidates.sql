-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE FUNCTION dbo.fn_MonthlyBillingCandidates(@Month date,@AcademicYearId int)
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
