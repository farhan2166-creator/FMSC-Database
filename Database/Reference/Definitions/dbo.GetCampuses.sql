SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER OFF;
GO
CREATE   PROCEDURE dbo.GetCampuses
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    SELECT DisplayOrder, CampusId,
             CampusCode,
             CampusName,
             CampusType,
             AddressLine,
             City,
             ContactNumber,
             Email
    FROM     dbo.Campuses
    ORDER BY DisplayOrder, CampusId;
END

GO
