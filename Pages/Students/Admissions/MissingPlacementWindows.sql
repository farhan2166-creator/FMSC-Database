-- Shared by the picker and the transactional save guard. A history entry may
-- fill a real gap; it must never become a new admission or bypass an overlap.
DECLARE @MissingToday date=CONVERT(date,GETDATE()),@MissingCutoff date,
 @MissingStatus nvarchar(30),@MissingBirth date,@MissingAdmission date,@MissingFirst date;
SELECT @MissingStatus=Status,@MissingCutoff=WithdrawalDate,@MissingBirth=DateOfBirth,@MissingAdmission=AdmissionDate
FROM dbo.Students WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId;
SET @MissingFirst=CASE WHEN @MissingBirth>@MissingAdmission THEN @MissingBirth ELSE @MissingAdmission END;
IF @MissingStatus IN(N'Left',N'Withdrawn',N'Inactive') AND @MissingCutoff IS NULL
 SELECT @MissingCutoff=MAX(LeavingDate) FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId;
IF @MissingStatus NOT IN(N'Left',N'Withdrawn',N'Inactive') SET @MissingCutoff=@MissingToday;
IF @MissingCutoff>@MissingToday SET @MissingCutoff=@MissingToday;

DECLARE @MissingWindows TABLE(WindowId int IDENTITY(1,1),AcademicYearId int,YearName nvarchar(200),
 GapStart date,GapEnd date,PromotionDate date NULL);
;WITH Years AS
(
 SELECT Y.AcademicYearId,Y.YearName,
  CASE WHEN @MissingFirst>Y.StartDate THEN @MissingFirst ELSE Y.StartDate END FirstDay,
  CASE WHEN Y.EndDate<@MissingCutoff THEN Y.EndDate ELSE @MissingCutoff END LastDay
 FROM dbo.AcademicYears Y WITH(HOLDLOCK)
 WHERE @MissingCutoff IS NOT NULL AND @MissingAdmission IS NOT NULL AND Y.StartDate<=@MissingCutoff
  AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=@AdmissionId
   AND E.AcademicYearId=Y.AcademicYearId AND E.Status=N'Active')
), Candidates AS
(
 SELECT AcademicYearId,YearName,FirstDay,LastDay,FirstDay GapStart FROM Years
 UNION
 SELECT Y.AcademicYearId,Y.YearName,Y.FirstDay,Y.LastDay,
  CASE WHEN E.Status=N'Promoted' THEN E.LeavingDate ELSE DATEADD(day,1,E.LeavingDate) END
 FROM Years Y JOIN dbo.Enrollments E ON E.AdmissionId=@AdmissionId AND E.LeavingDate IS NOT NULL
 WHERE E.LeavingDate>=Y.FirstDay AND E.LeavingDate<Y.LastDay
), Gaps AS
(
 SELECT C.*,N.NextStart,
  CASE WHEN N.NextStart<=C.LastDay THEN DATEADD(day,-1,N.NextStart) ELSE C.LastDay END GapEnd
 FROM Candidates C
 OUTER APPLY(SELECT MIN(E.EnrollmentDate) NextStart FROM dbo.Enrollments E
  WHERE E.AdmissionId=@AdmissionId AND E.EnrollmentDate>C.GapStart) N
 WHERE C.GapStart<=C.LastDay AND NOT EXISTS
  (SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=@AdmissionId AND E.EnrollmentDate<=C.GapStart
   AND (E.LeavingDate IS NULL OR E.LeavingDate>C.GapStart OR E.LeavingDate=C.GapStart AND E.Status<>N'Promoted'))
)
INSERT @MissingWindows(AcademicYearId,YearName,GapStart,GapEnd,PromotionDate)
SELECT AcademicYearId,YearName,GapStart,GapEnd,
 CASE WHEN NextStart<=DATEADD(day,1,LastDay) AND NextStart<=@MissingCutoff
  AND EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AdmissionId=@AdmissionId
   AND E.EnrollmentDate=NextStart AND E.AcademicYearId<>G.AcademicYearId) THEN NextStart END
FROM Gaps G WHERE GapStart<=GapEnd;

-- Follow the class order used by Promotion. Repeating a class is valid history,
-- but a higher class cannot precede a lower one. School precedes College;
-- parallel programs of the same type do not have a relative class order.
DECLARE @MissingPlacements TABLE(WindowId int,ClassSectionId int,CanPromote bit,
 PRIMARY KEY(WindowId,ClassSectionId));
;WITH Catalog AS
(
 SELECT C.ClassId,C.AcademicProgramId,C.DisplayOrder,
  CASE P.ProgramType WHEN N'School' THEN 0 WHEN N'College' THEN 1 END Stage
 FROM dbo.Classes C WITH(HOLDLOCK) JOIN dbo.AcademicPrograms P WITH(HOLDLOCK)
  ON P.AcademicProgramId=C.AcademicProgramId
), History AS
(
 SELECT E.EnrollmentDate,C.* FROM dbo.Enrollments E
 JOIN dbo.CampusClassSections CS WITH(HOLDLOCK) ON CS.ClassSectionId=E.ClassSectionId
 JOIN Catalog C ON C.ClassId=CS.ClassId WHERE E.AdmissionId=@AdmissionId
)
INSERT @MissingPlacements
SELECT W.WindowId,CS.ClassSectionId,CONVERT(bit,CASE WHEN W.PromotionDate IS NOT NULL AND EXISTS
 (SELECT 1 FROM History H WHERE H.EnrollmentDate=W.PromotionDate AND H.AcademicProgramId=C.AcademicProgramId
  AND (H.DisplayOrder>C.DisplayOrder OR H.DisplayOrder=C.DisplayOrder AND H.ClassId>C.ClassId)) THEN 1 ELSE 0 END)
FROM @MissingWindows W CROSS JOIN Catalog C
JOIN dbo.CampusClassSections CS WITH(HOLDLOCK) ON CS.ClassId=C.ClassId AND CS.IsActive=1
WHERE NOT EXISTS
 (SELECT 1 FROM History H WHERE
  (H.EnrollmentDate<W.GapStart AND
   (C.Stage<H.Stage OR C.AcademicProgramId=H.AcademicProgramId AND
    (C.DisplayOrder<H.DisplayOrder OR C.DisplayOrder=H.DisplayOrder AND C.ClassId<H.ClassId)))
  OR (H.EnrollmentDate>W.GapStart AND
   (C.Stage>H.Stage OR C.AcademicProgramId=H.AcademicProgramId AND
    (C.DisplayOrder>H.DisplayOrder OR C.DisplayOrder=H.DisplayOrder AND C.ClassId>H.ClassId))));
DELETE W FROM @MissingWindows W WHERE NOT EXISTS(SELECT 1 FROM @MissingPlacements P WHERE P.WindowId=W.WindowId);
