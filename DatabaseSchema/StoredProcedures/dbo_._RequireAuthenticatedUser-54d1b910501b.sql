-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @UserId)
        THROW 50003, 'The specified user is not authenticated.', 1;
END
