-- Module body only. See companion XML for signatures and relationships.

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
