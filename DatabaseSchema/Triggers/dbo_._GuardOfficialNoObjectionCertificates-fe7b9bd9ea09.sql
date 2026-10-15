-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
