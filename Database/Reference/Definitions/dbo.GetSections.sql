SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.GetSections @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAuthenticatedUser @UserId;
    SELECT DisplayOrder, SectionId, SectionCode, SectionName, Description
    FROM dbo.Sections
    ORDER BY DisplayOrder, SectionId;
END
GO
