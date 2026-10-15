-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.EnrollmentId=I.EnrollmentId JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId
  WHERE D.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
    AND (I.AcademicYearId<>D.AcademicYearId OR I.PlacementConfirmedAt IS NULL))
  THROW 59640,'Academic year is confirmed. Use Promotion for a move to another academic year.',1;
END;
