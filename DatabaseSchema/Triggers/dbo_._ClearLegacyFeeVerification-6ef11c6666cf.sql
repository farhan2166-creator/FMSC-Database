-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 UPDATE S SET LegacyVerifiedAt=NULL,LegacyVerifiedBy=NULL FROM dbo.Students S
 WHERE S.LegacySetupPending=1 AND S.AdmissionId IN(SELECT AdmissionId FROM inserted UNION SELECT AdmissionId FROM deleted);
END;
