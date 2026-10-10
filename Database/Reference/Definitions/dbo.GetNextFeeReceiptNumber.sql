SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------------
   GetNextFeeReceiptNumber - only the final number build changes.
   --------------------------------------------------------------------------- */
CREATE   PROCEDURE dbo.GetNextFeeReceiptNumber
(
    @ReceiptDate DATE,
    @ReceiptNumber NVARCHAR(40) OUTPUT
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ReceiptDate IS NULL
        SET @ReceiptDate = CAST(GETDATE() AS DATE);

    DECLARE @Year INT = YEAR(@ReceiptDate);
    DECLARE @LastNumber INT;

    SELECT
        @LastNumber = LastNumber
    FROM dbo.FeeReceiptNumberCounters WITH (UPDLOCK, HOLDLOCK)
    WHERE CounterYear = @Year;

    IF @LastNumber IS NULL
    BEGIN
        SET @LastNumber = 1;

        INSERT dbo.FeeReceiptNumberCounters
        (
            CounterYear,
            LastNumber
        )
        VALUES
        (
            @Year,
            @LastNumber
        );
    END
    ELSE
    BEGIN
        SET @LastNumber += 1;

        UPDATE dbo.FeeReceiptNumberCounters
        SET
            LastNumber = @LastNumber
        WHERE CounterYear = @Year;
    END;

    /* FIX 079 - pad to a minimum of four digits, widen past 9999 instead of
       truncating. RIGHT(..., 4) alone produced '0000' at 10000 and '2345' at
       12345, both of which duplicate a number already issued that year. */
    DECLARE @Digits NVARCHAR(20) = CONVERT(NVARCHAR(20), @LastNumber);

    SET @ReceiptNumber =
        N'FMSC-' +
        CONVERT(NVARCHAR(4), @Year) + N'-' +
        RIGHT(
            N'0000' + @Digits,
            CASE WHEN LEN(@Digits) > 4 THEN LEN(@Digits) ELSE 4 END);
END;

GO
