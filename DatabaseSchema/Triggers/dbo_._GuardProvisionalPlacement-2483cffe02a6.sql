-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId
  WHERE I.Status=N'Active' AND S.Status IN(N'Pending',N'Cancelled'))
  THROW 59752,'Complete Admission before activating this registration. Cancelled records cannot have an active placement.',1;
END;
