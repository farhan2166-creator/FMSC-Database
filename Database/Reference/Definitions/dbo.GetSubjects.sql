SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER OFF;
GO
CREATE   PROCEDURE dbo.GetSubjects @UserId int
AS
BEGIN
    EXEC dbo.RequireAuthenticatedUser @UserId;
    SET NOCOUNT ON;
    SELECT SubjectId,SubjectCode,SubjectName,Description,DisplayOrder
    FROM dbo.Subjects ORDER BY DisplayOrder ASC,SubjectId ASC;
END;

GO
