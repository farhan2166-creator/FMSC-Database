CREATE OR ALTER PROCEDURE dbo.GetAcademicYears @UserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequireAuthenticatedUser @UserId;
 SELECT Y.*,CONVERT(bit,CASE WHEN
  EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.AcademicYearId=Y.AcademicYearId) OR
  EXISTS(SELECT 1 FROM dbo.PromotionBatches P WHERE P.SourceAcademicYearId=Y.AcademicYearId OR P.DestinationAcademicYearId=Y.AcademicYearId)
  THEN 1 ELSE 0 END) AS DatesLocked
 FROM dbo.AcademicYears Y ORDER BY Y.StartDate DESC,Y.AcademicYearId DESC;
END;
