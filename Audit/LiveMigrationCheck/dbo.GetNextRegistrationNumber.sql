
/* ---------------------------------------------------------------------------
   GetNextRegistrationNumber - the same one change.

   Note this procedure has no SET XACT_ABORT ON in its original form; that is
   left exactly as it was, because this fix is about the number, not about
   changing error behaviour that other callers may be relying on.
   --------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.GetNextRegistrationNumber
    @RegistrationDate DATE,
    @RegistrationNumber NVARCHAR(40) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @RegistrationDate IS NULL
        THROW 51100, 'Registration date is required.', 1;

    DECLARE @RegistrationYear INT =
        YEAR(@RegistrationDate);

    DECLARE @LastNumber INT;

    SELECT
        @LastNumber = LastNumber
    FROM dbo.RegistrationNumberCounters WITH (UPDLOCK, HOLDLOCK)
    WHERE RegistrationYear = @RegistrationYear;

    IF @LastNumber IS NULL
    BEGIN
        SET @LastNumber = 1;

        INSERT dbo.RegistrationNumberCounters
        (
            RegistrationYear,
            LastNumber
        )
        VALUES
        (
            @RegistrationYear,
            @LastNumber
        );
    END
    ELSE
    BEGIN
        SET @LastNumber += 1;

        UPDATE dbo.RegistrationNumberCounters
        SET LastNumber = @LastNumber
        WHERE RegistrationYear = @RegistrationYear;
    END;

    /* FIX 079 - same repair as the receipt allocator above. */
    DECLARE @Digits NVARCHAR(20) = CONVERT(NVARCHAR(20), @LastNumber);

    SET @RegistrationNumber =
        N'FMSC-' +
        CONVERT(NVARCHAR(4), @RegistrationYear) +
        N'-' +
        RIGHT(
            N'0000' + @Digits,
            CASE WHEN LEN(@Digits) > 4 THEN LEN(@Digits) ELSE 4 END);
END;

