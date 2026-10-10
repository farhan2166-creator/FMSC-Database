SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- RequireAuthenticatedUser
CREATE   PROCEDURE dbo.RequireAuthenticatedUser
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Users
                   WHERE  UserId = @UserId)
        THROW 50003, 'The specified user is not authenticated.', 1;
END



GO
