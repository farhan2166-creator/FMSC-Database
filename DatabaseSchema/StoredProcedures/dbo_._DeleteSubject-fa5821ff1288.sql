-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';

    IF NOT EXISTS (SELECT 1 FROM dbo.Subjects WHERE SubjectId = @SubjectId)
        THROW 59001, 'The selected subject was not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.ProvisionalCertificateSubjects WHERE SubjectId = @SubjectId)
        THROW 59006, 'This subject is used by provisional certificates and cannot be deleted. Deactivate it instead.', 1;

    DELETE FROM dbo.Subjects WHERE SubjectId = @SubjectId;
END;
