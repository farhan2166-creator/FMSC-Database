

/* -----------------------------------------------------------------------------
   dbo.GetStudentMovementReport - adds AdmissionId and FatherName. Nothing that
   was selected before has changed name, meaning or order (new columns are last).
   ----------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.GetStudentMovementReport
    @UserId   INT,
    @FromDate DATE,
    @ToDate   DATE,
    @CampusId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    SELECT
        EH.ActionDate,
        EH.ActionType,
        S.RegistrationNumber,
        S.StudentName,
        CAMP.CampusName,
        OLD_C.ClassName AS OldClass,
        OLD_SEC.SectionName AS OldSection,
        NEW_C.ClassName AS NewClass,
        NEW_SEC.SectionName AS NewSection,
        EH.OldStatus,
        EH.NewStatus,
        EH.Remarks,
        U.Username AS ChangedBy,
        S.AdmissionId,
        S.FatherName
    FROM dbo.EnrollmentHistory EH
    INNER JOIN dbo.Students S
        ON S.AdmissionId = EH.AdmissionId
    LEFT JOIN dbo.CampusClassSections OLD_CCS
        ON OLD_CCS.ClassSectionId = EH.OldClassSectionId
    LEFT JOIN dbo.CampusClassSections NEW_CCS
        ON NEW_CCS.ClassSectionId = EH.NewClassSectionId
    LEFT JOIN dbo.Campuses CAMP
        ON CAMP.CampusId = COALESCE(
            NEW_CCS.CampusId,
            OLD_CCS.CampusId,
            S.AdmissionCampusId)
    LEFT JOIN dbo.Classes OLD_C
        ON OLD_C.ClassId = OLD_CCS.ClassId
    LEFT JOIN dbo.Sections OLD_SEC
        ON OLD_SEC.SectionId = OLD_CCS.SectionId
    LEFT JOIN dbo.Classes NEW_C
        ON NEW_C.ClassId = NEW_CCS.ClassId
    LEFT JOIN dbo.Sections NEW_SEC
        ON NEW_SEC.SectionId = NEW_CCS.SectionId
    LEFT JOIN dbo.Users U
        ON U.UserId = EH.UserId
    WHERE EH.ActionDate >= @FromDate
      AND EH.ActionDate < DATEADD(DAY, 1, @ToDate)
      AND (@CampusId = 0 OR
           COALESCE(
               NEW_CCS.CampusId,
               OLD_CCS.CampusId,
               S.AdmissionCampusId) = @CampusId)
    ORDER BY
        EH.ActionDate,
        CAMP.CampusName,
        S.StudentName,
        EH.EnrollmentHistoryId;
END;

