-- Module body only. See companion XML for signatures and relationships.

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
