
-- GetStudentRegister
CREATE   PROCEDURE dbo.GetStudentRegister
@UserId INT, @CampusId INT=0, @AcademicYearId INT=0, @ClassId INT=0, @SectionId INT=0, @Gender NVARCHAR (20)=NULL, @StudentStatus NVARCHAR (30)=NULL, @EnrollmentStatus NVARCHAR (30)=NULL, @Search NVARCHAR (200)=NULL, @AdmittedFrom DATE=NULL, @AdmittedTo DATE=NULL, @IncludeFormerStudents BIT=0, @IncludeBalance BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Reports.View';
    DECLARE @CanViewDetails AS BIT = 0;
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'Students.View';
        SET @CanViewDetails = 1;
    END TRY
    BEGIN CATCH
        SET @CanViewDetails = 0;
    END CATCH
    DECLARE @Today AS DATE = CAST (GETDATE() AS DATE);
    DECLARE @SearchText AS NVARCHAR (200) = NULLIF (LTRIM(RTRIM(@Search)), N'');
    DECLARE @StatusFilter AS NVARCHAR (30) = NULLIF (LTRIM(RTRIM(@StudentStatus)), N'');
    DECLARE @EnrolFilter AS NVARCHAR (30) = NULLIF (LTRIM(RTRIM(@EnrollmentStatus)), N'');
    DECLARE @GenderFilter AS NVARCHAR (20) = NULLIF (LTRIM(RTRIM(@Gender)), N'');
    SELECT   S.AdmissionId,
             S.AdmissionNumber,
             S.RegistrationNumber,
             S.StudentName,
             S.FatherName,
             S.Gender,
             S.ContactNumber,
             S.AdmissionDate,
             AC.CampusName AS AdmissionCampusName,
             S.Status AS StudentStatus,
             YEAR(S.AdmissionDate) AS AdmissionYear,
             S.WithdrawalDate,
             AdmC.ClassName AS AdmissionClassName,
             AdmSEC.SectionName AS AdmissionSectionName,
             LeftC.ClassName AS LeavingClassName,
             LeftSEC.SectionName AS LeavingSectionName,
             CASE WHEN @CanViewDetails = 1 THEN S.DateOfBirth END AS DateOfBirth,
             CASE WHEN @CanViewDetails = 1
                       AND S.DateOfBirth IS NOT NULL THEN DATEDIFF(YEAR, S.DateOfBirth, @Today) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, S.DateOfBirth, @Today), S.DateOfBirth) > @Today THEN 1 ELSE 0 END END AS Age,
             CASE WHEN @CanViewDetails = 1 THEN S.BFormNumber END AS BFormNumber,
             CASE WHEN @CanViewDetails = 1 THEN S.BloodGroup END AS BloodGroup,
             CASE WHEN @CanViewDetails = 1 THEN S.Religion END AS Religion,
             CASE WHEN @CanViewDetails = 1 THEN S.CasteTribe END AS CasteTribe,
             CASE WHEN @CanViewDetails = 1 THEN S.Disability END AS Disability,
             CASE WHEN @CanViewDetails = 1 THEN S.PlaceOfBirth END AS PlaceOfBirth,
             CASE WHEN @CanViewDetails = 1 THEN S.AlternateContactNumber END AS AlternateContactNumber,
             CASE WHEN @CanViewDetails = 1 THEN S.Address END AS Address,
             CASE WHEN @CanViewDetails = 1 THEN S.GuardianName END AS GuardianName,
             CASE WHEN @CanViewDetails = 1 THEN S.GuardianRelation END AS GuardianRelation,
             CASE WHEN @CanViewDetails = 1 THEN S.FatherOccupation END AS FatherOccupation,
             CASE WHEN @CanViewDetails = 1 THEN S.PreviousSchool END AS PreviousSchool,
             CASE WHEN @CanViewDetails = 1 THEN S.BiseEnrolmentNumber END AS BiseEnrolmentNumber,
             E.EnrollmentId,
             E.AcademicYearId,
             AY.YearName,
             COALESCE (CCS.CampusId, S.AdmissionCampusId) AS CampusId,
             COALESCE (CC.CampusName, AC.CampusName) AS CampusName,
             C.ClassId,
             C.ClassName,
             C.DisplayOrder AS ClassOrder,
             SEC.SectionId,
             SEC.SectionName,
             AP.ProgramType,
             E.EnrollmentDate,
             E.Status AS EnrollmentStatus,
             E.LeavingDate,
             CASE WHEN E.EnrollmentId IS NULL THEN NULL WHEN E.UsesTransport = 1 THEN N'Yes' ELSE N'No' END AS Transport,
             E.BillingStartDate,
             CASE WHEN @CanViewDetails = 1 THEN FAM.FamilyCode END AS FamilyRef,
             CASE WHEN @CanViewDetails = 1 THEN FAM.FamilyMemberCount END AS FamilyMemberCount,
             CASE WHEN @IncludeBalance = 1 THEN ISNULL((SELECT SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId))
                                                        FROM   dbo.BillingCharges AS BC
                                                        WHERE  BC.AdmissionId = S.AdmissionId
                                                               AND BC.Status <> N'Cancelled'), 0) END AS OutstandingBalance
    FROM     dbo.Students AS S
             INNER JOIN
             dbo.Campuses AS AC
             ON AC.CampusId = S.AdmissionCampusId OUTER APPLY (SELECT   TOP (1) E1.*
                                                               FROM     dbo.Enrollments AS E1
                                                               WHERE    E1.AdmissionId = S.AdmissionId
                                                                        AND (@AcademicYearId = 0
                                                                             OR E1.AcademicYearId = @AcademicYearId)
                                                                        AND (@EnrolFilter IS NULL
                                                                             OR E1.Status = @EnrolFilter)
                                                               ORDER BY CASE WHEN E1.Status = N'Active' THEN 0 ELSE 1 END, E1.EnrollmentDate DESC, E1.EnrollmentId DESC) AS E
             LEFT OUTER JOIN
             dbo.AcademicYears AS AY
             ON AY.AcademicYearId = E.AcademicYearId
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Campuses AS CC
             ON CC.CampusId = CCS.CampusId
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             LEFT OUTER JOIN
             dbo.AcademicPrograms AS AP
             ON AP.AcademicProgramId = C.AcademicProgramId OUTER APPLY (SELECT   TOP (1) E2.ClassSectionId
                                                                        FROM     dbo.Enrollments AS E2
                                                                        WHERE    E2.AdmissionId = S.AdmissionId
                                                                        ORDER BY E2.EnrollmentDate ASC, E2.EnrollmentId ASC) AS AdmE
             LEFT OUTER JOIN
             dbo.CampusClassSections AS AdmCCS
             ON AdmCCS.ClassSectionId = AdmE.ClassSectionId
             LEFT OUTER JOIN
             dbo.Classes AS AdmC
             ON AdmC.ClassId = AdmCCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS AdmSEC
             ON AdmSEC.SectionId = AdmCCS.SectionId OUTER APPLY (SELECT   TOP (1) E3.ClassSectionId
                                                                 FROM     dbo.Enrollments AS E3
                                                                 WHERE    E3.AdmissionId = S.AdmissionId
                                                                          AND E3.LeavingDate IS NOT NULL
                                                                 ORDER BY E3.LeavingDate DESC, E3.EnrollmentId DESC) AS LeftE
             LEFT OUTER JOIN
             dbo.CampusClassSections AS LeftCCS
             ON LeftCCS.ClassSectionId = LeftE.ClassSectionId
             LEFT OUTER JOIN
             dbo.Classes AS LeftC
             ON LeftC.ClassId = LeftCCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS LeftSEC
             ON LeftSEC.SectionId = LeftCCS.SectionId OUTER APPLY (SELECT   TOP (1) F.FamilyCode,
                                                                                    (SELECT COUNT(*)
                                                                                     FROM   dbo.FamilyMembers AS FM2
                                                                                     WHERE  FM2.FamilyId = FM.FamilyId) AS FamilyMemberCount
                                                                   FROM     dbo.FamilyMembers AS FM
                                                                            INNER JOIN
                                                                            dbo.Families AS F
                                                                            ON F.FamilyId = FM.FamilyId
                                                                   WHERE    FM.AdmissionId = S.AdmissionId
                                                                   ORDER BY FM.IsPrimaryStudent DESC, FM.FamilyMemberId) AS FAM
    WHERE    (@CampusId = 0
              OR COALESCE (CCS.CampusId, S.AdmissionCampusId) = @CampusId)
             AND (@AcademicYearId = 0
                  OR E.EnrollmentId IS NOT NULL)
             AND (@EnrolFilter IS NULL
                  OR E.EnrollmentId IS NOT NULL)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@GenderFilter IS NULL
                  OR UPPER(LEFT(LTRIM(S.Gender), 1)) = UPPER(LEFT(@GenderFilter, 1)))
             AND ((@StatusFilter IS NULL
                   AND (@IncludeFormerStudents = 1
                        OR S.Status = N'Active'))
                  OR (@StatusFilter IS NOT NULL
                      AND S.Status = @StatusFilter))
             AND (@AdmittedFrom IS NULL
                  OR S.AdmissionDate >= @AdmittedFrom)
             AND (@AdmittedTo IS NULL
                  OR S.AdmissionDate <= @AdmittedTo)
             AND (@SearchText IS NULL
                  OR S.StudentName LIKE N'%' + @SearchText + N'%'
                  OR S.FatherName LIKE N'%' + @SearchText + N'%'
                  OR S.RegistrationNumber LIKE N'%' + @SearchText + N'%'
                  OR S.AdmissionNumber = @SearchText)
    ORDER BY S.AdmissionNumberBase, S.AdmissionNumberSuffix, S.AdmissionId
    OPTION (RECOMPILE);
END



