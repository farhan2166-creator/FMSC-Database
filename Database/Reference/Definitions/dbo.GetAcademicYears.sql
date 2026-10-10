SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetAcademicYears
CREATE   PROCEDURE dbo.GetAcademicYears
@UserId INT
AS
BEGIN
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    SET NOCOUNT ON;
    SELECT   *
    FROM     dbo.AcademicYears
    ORDER BY StartDate DESC, AcademicYearId DESC;
END



GO
