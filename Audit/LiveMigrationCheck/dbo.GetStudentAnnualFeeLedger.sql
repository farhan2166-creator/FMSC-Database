
-- GetStudentAnnualFeeLedger
CREATE PROCEDURE dbo.GetStudentAnnualFeeLedger
@UserId INT, @AdmissionId INT, @AcademicYearId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Statement.View';
    IF @AdmissionId <= 0
        THROW 59501, 'A valid student is required.', 1;
    IF @AcademicYearId <= 0
        THROW 59502, 'A valid academic year is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 59503, 'The selected student was not found.', 1;
    DECLARE @YearStart AS DATE, @YearEnd AS DATE;
    SELECT @YearStart = StartDate, @YearEnd = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @YearStart IS NULL
        THROW 59504, 'The selected academic year was not found.', 1;
    -- Business rule (V20 F-06): use the saved academic-year dates, inclusive.
    -- April 1 through March 31 normally covers twelve months; do not manufacture an end date.
    -- Preserve twelve columns for standard callers; expose extra months for longer saved years.
    IF @YearEnd IS NULL OR @YearEnd < @YearStart
        THROW 59505, 'The academic year has invalid saved dates.', 1;
    DECLARE @MonthCount int = DATEDIFF(month, @YearStart, @YearEnd) + 1;
    DECLARE @ColumnCount int = CASE WHEN @MonthCount < 12 THEN 12 ELSE @MonthCount END;
    DECLARE @Labels nvarchar(max) = N'', @Months nvarchar(max) = N'', @i int = 0;
    WHILE @i < @ColumnCount
    BEGIN
        SET @Labels += N'CASE WHEN ' + CONVERT(nvarchar(10),@i) + N' < @MonthCount THEN FORMAT(DATEADD(month,' + CONVERT(nvarchar(10),@i) + N',@YearStart),''MMM-yy'') ELSE NULL END AS M' + CONVERT(nvarchar(10),@i+1) + N'Label,';
        SET @Months += N'SUM(CASE WHEN Src.MonthIndex=' + CONVERT(nvarchar(10),@i) + N' THEN Src.NetAmount ELSE 0 END) AS M' + CONVERT(nvarchar(10),@i+1) + N'Due,SUM(CASE WHEN Src.MonthIndex=' + CONVERT(nvarchar(10),@i) + N' THEN Src.PaidAmount ELSE 0 END) AS M' + CONVERT(nvarchar(10),@i+1) + N'Paid,';
        SET @i += 1;
    END;
    DECLARE @StudentName AS NVARCHAR (200), @FatherName AS NVARCHAR (200), @RegistrationNumber AS NVARCHAR (50), @CampusName AS NVARCHAR (200), @ClassName AS NVARCHAR (100), @SectionName AS NVARCHAR (100), @YearName AS NVARCHAR (50), @SchoolName AS NVARCHAR (200);
    SELECT   TOP (1) @StudentName = S.StudentName,
                     @FatherName = S.FatherName,
                     @RegistrationNumber = S.RegistrationNumber,
                     @CampusName = CAMP.CampusName,
                     @ClassName = C.ClassName,
                     @SectionName = SEC.SectionName
    FROM     dbo.Students AS S
             LEFT OUTER JOIN
             dbo.Enrollments AS E
             ON E.AdmissionId = S.AdmissionId
                AND E.AcademicYearId = @AcademicYearId
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = CCS.CampusId
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
    WHERE    S.AdmissionId = @AdmissionId
    ORDER BY E.EnrollmentDate DESC;
    SELECT @YearName = YearName
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    SELECT TOP (1) @SchoolName = SchoolName
    FROM   dbo.SchoolProfile;
    DECLARE @Query nvarchar(max) = N'WITH     ChargePaid
    AS       (SELECT   FPA.BillingChargeId,
                       SUM(FPA.Amount) AS PaidAmount
              FROM     dbo.FeePaymentAllocations AS FPA
                       INNER JOIN
                       dbo.FeePayments AS FP
                       ON FP.FeePaymentId = FPA.FeePaymentId
              WHERE    FP.Status <> N''Reversed''
              GROUP BY FPA.BillingChargeId),
             Charges
    AS       (SELECT FT.FeeTypeId,
                     FT.FeeTypeName,
                     FT.DisplayOrder,
                     DATEDIFF(month, @YearStart, BC.BillingMonth) AS MonthIndex,
                     BC.NetAmount,
                     ISNULL(CP.PaidAmount, 0) AS PaidAmount
              FROM   dbo.BillingCharges AS BC
                     INNER JOIN
                     dbo.FeeTypes AS FT
                     ON FT.FeeTypeId = BC.FeeTypeId
                     LEFT OUTER JOIN
                     ChargePaid AS CP
                     ON CP.BillingChargeId = BC.BillingChargeId
              WHERE  BC.AdmissionId = @AdmissionId
                     AND BC.Status <> N''Cancelled''
                     AND BC.BillingMonth >= @YearStart
                     AND BC.BillingMonth <= @YearEnd)
    SELECT   @StudentName AS StudentName,
             @FatherName AS FatherName,
             @RegistrationNumber AS RegistrationNumber,
             @CampusName AS CampusName,
             @ClassName AS ClassName,
             @SectionName AS SectionName,
             @YearName AS AcademicYearName,
             @SchoolName AS SchoolName,
             ' + @Labels + N'Src.FeeTypeName,
             Src.DisplayOrder,
             ' + @Months + N'SUM(Src.NetAmount) AS TotalDue,
             SUM(Src.PaidAmount) AS TotalPaid,
             SUM(Src.NetAmount) - SUM(Src.PaidAmount) AS TotalBalance
    FROM     Charges AS Src
    GROUP BY Src.FeeTypeName, Src.DisplayOrder
    UNION ALL
    SELECT   @StudentName,
             @FatherName,
             @RegistrationNumber,
             @CampusName,
             @ClassName,
             @SectionName,
             @YearName,
             @SchoolName,
             ' + @Labels + N'N''TOTAL'',
             999999,
             ' + @Months + N'SUM(NetAmount),
             SUM(PaidAmount),
             SUM(NetAmount) - SUM(PaidAmount)
    FROM     Charges AS Src
    ORDER BY DisplayOrder;';
    EXEC sys.sp_executesql @Query,N'@YearStart date,@YearEnd date,@MonthCount int,@StudentName nvarchar(200),@FatherName nvarchar(200),@RegistrationNumber nvarchar(50),@CampusName nvarchar(200),@ClassName nvarchar(100),@SectionName nvarchar(100),@YearName nvarchar(50),@SchoolName nvarchar(200),@AdmissionId int',@YearStart,@YearEnd,@MonthCount,@StudentName,@FatherName,@RegistrationNumber,@CampusName,@ClassName,@SectionName,@YearName,@SchoolName,@AdmissionId;
END;

