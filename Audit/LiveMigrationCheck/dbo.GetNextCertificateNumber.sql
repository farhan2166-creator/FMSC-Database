
/* ---------------------------------------------------------------------------
   2. The allocator.

      Pads to a MINIMUM of four digits and never truncates, so year 10,000 of a
      series widens the field instead of colliding.
   --------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.GetNextCertificateNumber
(
    @Prefix            NVARCHAR(8),
    @IssueDate         DATE,
    @CertificateNumber NVARCHAR(24) OUTPUT
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Prefix = UPPER(LTRIM(RTRIM(ISNULL(@Prefix, N''))));

    IF @Prefix = N''
        THROW 59200, 'A certificate prefix is required.', 1;

    IF @IssueDate IS NULL
        THROW 59201, 'The date the certificate was issued is required.', 1;

    DECLARE @Year INT = YEAR(@IssueDate);

    IF @Year < 1900 OR @Year > 2999
        THROW 59202, 'The certificate issue date is outside the supported range.', 1;

    DECLARE @LastNumber INT;

    /* The lock is held until the caller commits - that is what stops two
       concurrent certificates taking the same number. */
    SELECT
        @LastNumber = LastNumber
    FROM dbo.CertificateNumberCounters WITH (UPDLOCK, HOLDLOCK)
    WHERE Prefix = @Prefix
      AND CounterYear = @Year;

    IF @LastNumber IS NULL
    BEGIN
        SET @LastNumber = 1;

        INSERT dbo.CertificateNumberCounters
        (
            Prefix,
            CounterYear,
            LastNumber
        )
        VALUES
        (
            @Prefix,
            @Year,
            @LastNumber
        );
    END
    ELSE
    BEGIN
        SET @LastNumber += 1;

        UPDATE dbo.CertificateNumberCounters
        SET
            LastNumber = @LastNumber
        WHERE Prefix = @Prefix
          AND CounterYear = @Year;
    END;

    DECLARE @Digits NVARCHAR(20) = CONVERT(NVARCHAR(20), @LastNumber);

    /* At least four digits, wider if the series has outgrown four. */
    DECLARE @Padded NVARCHAR(24) =
        RIGHT(
            N'0000' + @Digits,
            CASE WHEN LEN(@Digits) > 4 THEN LEN(@Digits) ELSE 4 END);

    SET @CertificateNumber =
        @Prefix + N'-' +
        CONVERT(NVARCHAR(4), @Year) + N'-' +
        @Padded;
END;

