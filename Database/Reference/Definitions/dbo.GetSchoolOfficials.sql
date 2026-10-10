SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetSchoolOfficials
CREATE   PROCEDURE dbo.GetSchoolOfficials
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    SELECT   SchoolOfficialId,
             Designation,
             PersonName,
             CreatedAt,
             CreatedByUserId,
             UpdatedAt,
             UpdatedByUserId
    FROM     dbo.SchoolOfficials
    ORDER BY Designation, PersonName;
END



GO
