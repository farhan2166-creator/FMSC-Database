SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. dbo.fn_NumberToWords / dbo.fn_DateToWords

      Small self-contained number-to-words helper (0-9999, which is all
      that's ever needed here: a day-of-month 1-31, or a year like 2002)
      so the RDLC can just print a plain field instead of needing any
      logic of its own. No number/date-to-words utility existed
      anywhere else in this codebase, so this is new and could be
      reused later (e.g. "amount in words" on a fee receipt) if wanted.

      Produces the same style as the sample certificate: "Two Thousand
      Two", not "Two Thousand and Two".
   ===================================================================== */

CREATE   FUNCTION dbo.fn_NumberToWords
(
    @Number INT
)
RETURNS NVARCHAR(200)
AS
BEGIN
    IF @Number IS NULL OR @Number < 0 OR @Number > 9999
        RETURN NULL;

    IF @Number = 0
        RETURN N'Zero';

    DECLARE @Ones TABLE (N INT PRIMARY KEY, W NVARCHAR(20));
    INSERT INTO @Ones (N, W) VALUES
        (1,N'One'),(2,N'Two'),(3,N'Three'),(4,N'Four'),(5,N'Five'),
        (6,N'Six'),(7,N'Seven'),(8,N'Eight'),(9,N'Nine'),(10,N'Ten'),
        (11,N'Eleven'),(12,N'Twelve'),(13,N'Thirteen'),(14,N'Fourteen'),
        (15,N'Fifteen'),(16,N'Sixteen'),(17,N'Seventeen'),(18,N'Eighteen'),
        (19,N'Nineteen');

    DECLARE @Tens TABLE (N INT PRIMARY KEY, W NVARCHAR(20));
    INSERT INTO @Tens (N, W) VALUES
        (2,N'Twenty'),(3,N'Thirty'),(4,N'Forty'),(5,N'Fifty'),
        (6,N'Sixty'),(7,N'Seventy'),(8,N'Eighty'),(9,N'Ninety');

    DECLARE @Thousands INT = @Number / 1000;
    DECLARE @Hundreds INT = (@Number % 1000) / 100;
    DECLARE @Remainder INT = @Number % 100;
    DECLARE @Result NVARCHAR(200) = N'';

    IF @Thousands > 0
        SET @Result = @Result + (SELECT W FROM @Ones WHERE N = @Thousands) + N' Thousand ';

    IF @Hundreds > 0
        SET @Result = @Result + (SELECT W FROM @Ones WHERE N = @Hundreds) + N' Hundred ';

    IF @Remainder > 0
    BEGIN
        IF @Remainder < 20
            SET @Result = @Result + (SELECT W FROM @Ones WHERE N = @Remainder);
        ELSE
        BEGIN
            SET @Result = @Result + (SELECT W FROM @Tens WHERE N = @Remainder / 10);

            IF (@Remainder % 10) > 0
                SET @Result = @Result + N' ' + (SELECT W FROM @Ones WHERE N = @Remainder % 10);
        END
    END

    RETURN LTRIM(RTRIM(@Result));
END;

GO
