SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetStudentFamily
CREATE   PROCEDURE dbo.GetStudentFamily
@UserId INT, @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    SELECT   FM.FamilyMemberId,
             FM.FamilyId,
             FM.AdmissionId,
             FM.RelationshipType,
             S.StudentName,
             S.AdmissionNumber,
             S.FatherName,
             S.Gender,
             S.RegistrationNumber,
             S.Status,
             C.ClassName,
             COALESCE (C.DisplayOrder, -1) AS ClassDisplayOrder,
             SEC.SectionName,
             E.EnrollmentId AS EnrollmentId
    FROM     dbo.FamilyMembers AS FM
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = FM.AdmissionId OUTER APPLY (SELECT   TOP (1) E1.*
                                                            FROM     dbo.Enrollments AS E1
                                                            WHERE    E1.AdmissionId = S.AdmissionId
                                                            ORDER BY CASE WHEN E1.Status = N'Active' THEN 0 ELSE 1 END, E1.EnrollmentDate DESC, E1.EnrollmentId DESC) AS E
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
    WHERE    FM.FamilyId = (SELECT FamilyId
                            FROM   dbo.FamilyMembers
                            WHERE  AdmissionId = @AdmissionId)
    ORDER BY COALESCE (C.DisplayOrder, -1) DESC, S.StudentName ASC;
END



GO
