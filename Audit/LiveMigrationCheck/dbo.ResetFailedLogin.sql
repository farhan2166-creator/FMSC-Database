
/* ---------------------------------------------------------------------------
   STEP 4 - dbo.ResetFailedLogin
   --------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.ResetFailedLogin
(
    @TargetUserId INT
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* FIX 54 - FMSC sign-in throttle. See the note on @TargetUserId in
       dbo.RecordFailedLogin. The WHERE clause keeps a successful sign-in from
       writing to the row at all in the normal case where nothing is recorded. */

    UPDATE dbo.Users
    SET
        FailedLoginCount = 0,
        LockedUntil = NULL
    WHERE UserId = @TargetUserId
      AND (FailedLoginCount <> 0 OR LockedUntil IS NOT NULL);
END;

