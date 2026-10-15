-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* FIX 54 - FMSC sign-in throttle.
       No RequirePermission: this runs before anyone is signed in, exactly like
       dbo.GetUserForLogin. The parameter is @TargetUserId and NOT @UserId
       because DatabaseHelper rewrites any @UserId parameter to the signed-in
       user, who does not exist yet at this point. */

    IF @MaxFailedAttempts IS NULL OR @MaxFailedAttempts < 1
        SET @MaxFailedAttempts = 5;

    IF @LockoutMinutes IS NULL OR @LockoutMinutes < 1
        SET @LockoutMinutes = 5;

    BEGIN TRANSACTION;

    DECLARE @NewCount INT;

    SELECT @NewCount = FailedLoginCount + 1
    FROM dbo.Users
        WITH (UPDLOCK, HOLDLOCK)
    WHERE UserId = @TargetUserId;

    IF @NewCount IS NULL
    BEGIN
        /* The account disappeared between the sign-in lookup and here. Nothing
           to record; not an error worth failing the sign-in over. */
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    IF @NewCount >= @MaxFailedAttempts
        UPDATE dbo.Users
        SET
            FailedLoginCount = 0,
            LockedUntil = DATEADD(MINUTE, @LockoutMinutes, SYSDATETIME())
        WHERE UserId = @TargetUserId;
    ELSE
        UPDATE dbo.Users
        SET FailedLoginCount = @NewCount
        WHERE UserId = @TargetUserId;

    COMMIT TRANSACTION;
END;
