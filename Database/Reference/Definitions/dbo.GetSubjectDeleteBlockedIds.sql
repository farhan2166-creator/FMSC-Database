SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* Staff assignment feature intentionally retired 2026-10-03; retain all other guards. */

CREATE PROCEDURE dbo.GetSubjectDeleteBlockedIds
    @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    SELECT S.SubjectId AS Id
    FROM dbo.Subjects S
    WHERE EXISTS (SELECT 1 FROM dbo.ProvisionalCertificateSubjects X WHERE X.SubjectId = S.SubjectId)
;
END;

GO
