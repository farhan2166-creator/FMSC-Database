CREATE   PROCEDURE dbo.GetOutstandingAndAgingReport
 @UserId int,@View nvarchar(20),@CampusId int=0,@AcademicYearId int=0,
 @ClassId int=0,@SectionId int=0,@AsOfDate date=NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';
    SET @AsOfDate = COALESCE(@AsOfDate, CONVERT(date, SYSDATETIME()));

    SELECT BC.BillingChargeId, BC.AdmissionId, S.RegistrationNumber, COALESCE(NULLIF(S.AdmissionNumber,''),NULLIF(S.QuickReference,''),CONCAT(N'ID-',S.AdmissionId)) AdmissionNumber, S.StudentName, S.FatherName, S.Gender,
           CAMP.CampusName, CAMP.DisplayOrder CampusSort,C.DisplayOrder ClassSort,SEC.DisplayOrder SectionSort,FT.DisplayOrder FeeSort,S.AdmissionNumberBase,S.AdmissionNumberSuffix, CONCAT(C.ClassName,CASE WHEN NULLIF(LTRIM(RTRIM(SEC.SectionName)),N'') IS NULL THEN N'' ELSE CONCAT(N' â€“ ',SEC.SectionName) END) ClassName, SEC.SectionName, FT.FeeTypeName,
           BC.BillingMonth, BC.ChargeDate, BC.DueDate, BC.GrossAmount, BC.DiscountAmount,
           BC.NetAmount, BC.NetAmount - B.Balance PaidAmount, B.Balance Outstanding,
           CASE WHEN BC.DueDate IS NULL THEN N'Due Date Missing'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) < 0 THEN N'Not Yet Due'
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
        SELECT AdmissionNumber [Adm. / QR No.], StudentName [Student], FatherName [__StudentFatherName], Gender [__StudentGender], CampusName [Campus], ClassName [Class / Section], FeeTypeName [Fee Type],
               BillingMonth [Charge Month], ChargeDate [Charge Date], DueDate [Due Date], GrossAmount [Original Amount],
               DiscountAmount [Discount], NetAmount [Net Charge], PaidAmount [Settled], Outstanding,
               CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END [Overdue Amount],
               CASE WHEN DueDate = @AsOfDate THEN Outstanding ELSE 0 END [Due Today],
               CASE WHEN DueDate > @AsOfDate THEN Outstanding ELSE 0 END [Not Yet Due],
               CASE WHEN DueDate IS NULL THEN Outstanding ELSE 0 END [Due Date Missing], AgingBucket [Aging]
        FROM #Charges ORDER BY CampusSort,ClassSort,SectionSort,COALESCE(AdmissionNumberBase,TRY_CONVERT(bigint,REPLACE(AdmissionNumber,N'QR-',N''))),AdmissionNumberSuffix,AdmissionNumber,StudentName,BillingMonth,FeeSort,BillingChargeId;
        SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
               SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
        RETURN;
    END;
    IF @View = N'Aging'
    BEGIN
        SELECT CampusName [Campus], AgingBucket [Aging Bucket], COUNT(DISTINCT AdmissionId) [Students], COUNT(*) [Charges], SUM(Outstanding) [Outstanding]
        FROM #Charges GROUP BY CampusName,AgingBucket
        ORDER BY MIN(CampusSort),CampusName,CASE AgingBucket WHEN N'Not Yet Due' THEN 0 WHEN N'Due Today' THEN 1 WHEN N'1-30 days' THEN 2 WHEN N'31-60 days' THEN 3 WHEN N'61-90 days' THEN 4 ELSE 5 END;
        SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
               SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
        RETURN;
    END;
    IF @View = N'Families'
    BEGIN
        ;WITH Members AS
        (
            SELECT AdmissionId, MAX(StudentName) StudentName,MAX(AdmissionNumber) AdmissionNumber,CASE WHEN COUNT(DISTINCT CampusName)>1 THEN N'Multiple' ELSE MAX(CampusName) END CampusName,MIN(CampusSort) CampusSort,SUM(Outstanding) Outstanding,
            SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) OverdueAmount,
            SUM(CASE WHEN DueDate = @AsOfDate THEN Outstanding ELSE 0 END) DueToday,
            SUM(CASE WHEN DueDate > @AsOfDate THEN Outstanding ELSE 0 END) NotYetDue,
            SUM(CASE WHEN DueDate IS NULL THEN Outstanding ELSE 0 END) DueDateMissing, MIN(DueDate) OldestDueDate
            FROM #Charges GROUP BY AdmissionId
        )
        SELECT COALESCE(F.FamilyCode,CONCAT(N'Student: ',MAX(M.AdmissionNumber))) [Family],CASE WHEN COUNT(DISTINCT M.CampusName)>1 THEN N'Multiple' ELSE MAX(M.CampusName) END [Campus],COUNT(*) [Students], STRING_AGG(M.StudentName, N', ') [Members],
               SUM(M.Outstanding) [Outstanding], SUM(M.OverdueAmount) [Overdue Amount],
               SUM(M.DueToday) [Due Today], SUM(M.NotYetDue) [Not Yet Due], SUM(M.DueDateMissing) [Due Date Missing], MIN(M.OldestDueDate) [Oldest Due Date]
        FROM Members M LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId = M.AdmissionId
        LEFT JOIN dbo.Families F ON F.FamilyId = FM.FamilyId
        GROUP BY F.FamilyId, F.FamilyCode,CASE WHEN F.FamilyId IS NULL THEN M.AdmissionId END ORDER BY MIN(M.CampusSort),TRY_CONVERT(bigint,REPLACE(F.FamilyCode,N'FAM-',N'')),F.FamilyCode;
        SELECT COUNT(DISTINCT C.AdmissionId) [Students Owing], SUM(C.Outstanding) [Total Outstanding],
               SUM(CASE WHEN C.DueDate < @AsOfDate THEN C.Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, C.DueDate, @AsOfDate) > 90 THEN C.Outstanding ELSE 0 END) [90+ Days]
        FROM #Charges C;
        RETURN;
    END;
    SELECT AdmissionNumber [Adm. / QR No.], StudentName [Student], FatherName [__StudentFatherName], Gender [__StudentGender], CampusName [Campus], ClassName [Class / Section],
           SUM(NetAmount) [Open Charges (Net)], SUM(PaidAmount) [Settled on Open Charges], SUM(Outstanding) [Outstanding],
           SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) [Overdue Amount],
           SUM(CASE WHEN DueDate = @AsOfDate THEN Outstanding ELSE 0 END) [Due Today],
           SUM(CASE WHEN DueDate > @AsOfDate THEN Outstanding ELSE 0 END) [Not Yet Due],
           SUM(CASE WHEN DueDate IS NULL THEN Outstanding ELSE 0 END) [Due Date Missing],
           MIN(DueDate) [Oldest Due Date],
           CASE WHEN COUNT(DueDate)=0 THEN N'Due Date Missing'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 90 THEN N'90+ days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 60 THEN N'61-90 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 30 THEN N'31-60 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 0 THEN N'1-30 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) = 0 THEN N'Due Today' ELSE N'Not Yet Due' END [Aging]
    FROM #Charges GROUP BY AdmissionId, RegistrationNumber, AdmissionNumber, StudentName, FatherName, Gender, CampusName, ClassName
    ORDER BY MIN(CampusSort),MIN(ClassSort),MIN(SectionSort),MIN(COALESCE(AdmissionNumberBase,TRY_CONVERT(bigint,REPLACE(AdmissionNumber,N'QR-',N'')))),MIN(AdmissionNumberSuffix),AdmissionNumber,StudentName;
    SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
           SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
           SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
END;
