-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Reports.View';
    SET @AsOfDate = COALESCE(@AsOfDate, CONVERT(date, SYSDATETIME()));

    SELECT E.EnrollmentId, E.AdmissionId, S.RegistrationNumber,
           S.StudentName, CAMP.CampusName, C.ClassName, SEC.SectionName,
           FT.FeeTypeId, FT.FeeTypeName,
           X.BillingMonth AS ExpectedBillingMonth,
           SFA.Amount AS ExpectedAmount, SFA.StudentFeeAssignmentId
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    CROSS APPLY dbo.fn_ExpectedRecurringChargeMonths(E.EnrollmentId, @AsOfDate) X
    JOIN dbo.StudentFeeAssignments SFA ON SFA.StudentFeeAssignmentId = X.StudentFeeAssignmentId
    JOIN dbo.FeeTypes FT ON FT.FeeTypeId = X.FeeTypeId
    WHERE (@AcademicYearId = 0 OR E.AcademicYearId = @AcademicYearId)
      AND E.Status IN (N'Active', N'Promoted', N'Left')
      AND E.BillingReady = 1
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.BillingCharges BC
          WHERE BC.EnrollmentId = E.EnrollmentId
            AND BC.FeeTypeId = X.FeeTypeId
            AND BC.BillingMonth = X.BillingMonth
            AND BC.ChargeKind = N'Monthly'
            AND BC.Status <> N'Cancelled'
      )
    ORDER BY CAMP.CampusName, C.ClassName, SEC.SectionName,
             S.StudentName, X.BillingMonth
    OPTION (MAXRECURSION 0);
END;
