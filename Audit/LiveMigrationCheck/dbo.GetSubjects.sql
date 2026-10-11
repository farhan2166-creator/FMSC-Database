CREATE   PROCEDURE dbo.GetSubjects @UserId int
AS
BEGIN
    EXEC dbo.RequireAuthenticatedUser @UserId;
    SET NOCOUNT ON;
    SELECT SubjectId,SubjectCode,SubjectName,Description,DisplayOrder
    FROM dbo.Subjects ORDER BY DisplayOrder ASC,SubjectId ASC;
END;

