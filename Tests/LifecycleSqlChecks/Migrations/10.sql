-- Missing bills: retain real historical gaps, exclude periods outside placement academic years.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.GetMissingChargeCandidates
    @UserId int,
    @AcademicYearId int,
    @CampusId int = 0,
    @AsOfDate date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Reports.View';
    SET @AsOfDate = COALESCE(@AsOfDate, CONVERT(date, SYSDATETIME()));

    SELECT E.EnrollmentId, E.AdmissionId, S.RegistrationNumber,
           AY.YearName, S.StudentName, CAMP.CampusName, C.ClassName, SEC.SectionName,
           FT.FeeTypeId, FT.FeeTypeName,
           X.BillingMonth AS ExpectedBillingMonth,
           SFA.Amount AS ExpectedAmount, SFA.StudentFeeAssignmentId
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.AcademicYears AY ON AY.AcademicYearId = E.AcademicYearId
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
      AND S.LegacySetupPending = 0
      AND E.PlacementConfirmedAt IS NOT NULL
      AND X.BillingMonth >= DATEFROMPARTS(YEAR(AY.StartDate), MONTH(AY.StartDate), 1)
      AND (AY.EndDate IS NULL OR X.BillingMonth <= DATEFROMPARTS(YEAR(AY.EndDate), MONTH(AY.EndDate), 1))
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


GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'10')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'10',SYSDATETIME(),SUSER_SNAME(),N'Missing-bill report respects academic-year scope and activation.');
GO