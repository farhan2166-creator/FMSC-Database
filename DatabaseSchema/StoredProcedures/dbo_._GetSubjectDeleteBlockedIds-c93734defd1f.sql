-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    SELECT S.SubjectId AS Id
    FROM dbo.Subjects S
    WHERE EXISTS (SELECT 1 FROM dbo.ProvisionalCertificateSubjects X WHERE X.SubjectId = S.SubjectId)
;
END;
