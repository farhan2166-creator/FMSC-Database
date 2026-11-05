-- Clarify outstanding report references, open-charge totals and due-today aging.
-- Read-only report change: no charges, payments, allocations or credit are modified.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 25 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>24
 THROW 51000,'Migration 25 requires schema 24.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetOutstandingAndAgingReport
    @UserId int,
    @View nvarchar(20),
    @CampusId int = 0,
    @AcademicYearId int = 0,
    @ClassId int = 0,
    @SectionId int = 0,
    @AsOfDate date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';
    SET @AsOfDate = COALESCE(@AsOfDate, CONVERT(date, SYSDATETIME()));

    SELECT BC.BillingChargeId, BC.AdmissionId, S.RegistrationNumber, COALESCE(NULLIF(S.AdmissionNumber,''),NULLIF(S.QuickReference,''),CONCAT(N'ID-',S.AdmissionId)) AdmissionNumber, S.StudentName,
           CAMP.CampusName, CONCAT(C.ClassName,CASE WHEN NULLIF(LTRIM(RTRIM(SEC.SectionName)),N'') IS NULL THEN N'' ELSE CONCAT(N' – ',SEC.SectionName) END) ClassName, SEC.SectionName, FT.FeeTypeName,
           BC.BillingMonth, BC.ChargeDate, BC.DueDate, BC.GrossAmount, BC.DiscountAmount,
           BC.NetAmount, BC.NetAmount - B.Balance PaidAmount, B.Balance Outstanding,
           CASE WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) < 0 THEN N'Not Yet Due'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) = 0 THEN N'Due Today'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) <= 30 THEN N'1-30 days'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) <= 60 THEN N'31-60 days'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) <= 90 THEN N'61-90 days'
                ELSE N'90+ days' END AgingBucket
    INTO #Charges
    FROM dbo.BillingCharges BC
    CROSS APPLY (SELECT dbo.GetFeeChargeBalance(BC.BillingChargeId) Balance) B
    JOIN dbo.Students S ON S.AdmissionId = BC.AdmissionId
    JOIN dbo.Enrollments E ON E.EnrollmentId = BC.EnrollmentId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
    WHERE BC.Status <> N'Cancelled' AND B.Balance > 0
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND (@AcademicYearId = 0 OR E.AcademicYearId = @AcademicYearId)
      AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId);

    IF @View = N'Charges'
    BEGIN
        SELECT AdmissionNumber [Adm. / QR No.], StudentName [Student], ClassName [Class / Section], FeeTypeName [Fee Type],
               BillingMonth [Charge Month], ChargeDate [Charge Date], DueDate [Due Date], GrossAmount [Original Amount],
               DiscountAmount [Discount], NetAmount [Net Charge], PaidAmount [Settled], Outstanding, AgingBucket [Aging]
        FROM #Charges ORDER BY StudentName, DueDate, BillingChargeId;
        SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
               SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
        RETURN;
    END;
    IF @View = N'Aging'
    BEGIN
        SELECT AgingBucket [Aging Bucket], COUNT(DISTINCT AdmissionId) [Students], COUNT(*) [Charges], SUM(Outstanding) [Outstanding]
        FROM #Charges GROUP BY AgingBucket
        ORDER BY CASE AgingBucket WHEN N'Not Yet Due' THEN 0 WHEN N'Due Today' THEN 1 WHEN N'1-30 days' THEN 2 WHEN N'31-60 days' THEN 3 WHEN N'61-90 days' THEN 4 ELSE 5 END;
        SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
               SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
        RETURN;
    END;
    IF @View = N'Families'
    BEGIN
        ;WITH Members AS
        (
            SELECT AdmissionId, MAX(StudentName) StudentName, SUM(Outstanding) Outstanding, MIN(DueDate) OldestDueDate
            FROM #Charges GROUP BY AdmissionId
        )
        SELECT F.FamilyCode [Family], COUNT(*) [Students], STRING_AGG(M.StudentName, N', ') [Members],
               SUM(M.Outstanding) [Outstanding], MIN(M.OldestDueDate) [Oldest Due Date]
        FROM Members M JOIN dbo.FamilyMembers FM ON FM.AdmissionId = M.AdmissionId
        JOIN dbo.Families F ON F.FamilyId = FM.FamilyId
        GROUP BY F.FamilyId, F.FamilyCode ORDER BY SUM(M.Outstanding) DESC;
        SELECT COUNT(DISTINCT C.AdmissionId) [Students Owing], SUM(C.Outstanding) [Total Outstanding],
               SUM(CASE WHEN C.DueDate < @AsOfDate THEN C.Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, C.DueDate, @AsOfDate) > 90 THEN C.Outstanding ELSE 0 END) [90+ Days]
        FROM #Charges C JOIN dbo.FamilyMembers FM ON FM.AdmissionId = C.AdmissionId;
        RETURN;
    END;
    SELECT AdmissionNumber [Adm. / QR No.], StudentName [Student], ClassName [Class / Section],
           SUM(NetAmount) [Open Charges (Net)], SUM(PaidAmount) [Settled on Open Charges], SUM(Outstanding) [Outstanding],
           MIN(DueDate) [Oldest Due Date],
           CASE WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 90 THEN N'90+ days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 60 THEN N'61-90 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 30 THEN N'31-60 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 0 THEN N'1-30 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) = 0 THEN N'Due Today' ELSE N'Not Yet Due' END [Aging]
    FROM #Charges GROUP BY AdmissionId, RegistrationNumber, AdmissionNumber, StudentName, ClassName
    ORDER BY SUM(Outstanding) DESC;
    SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
           SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
           SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
END;
GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>24',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>24',N'FROM dbo.DatabaseVersion)<>25');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'25',SYSDATETIME(),SUSER_SNAME(),N'Clarify outstanding report references, open-charge totals and due-today aging');
GO