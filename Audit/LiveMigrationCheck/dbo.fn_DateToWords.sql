
CREATE   FUNCTION dbo.fn_DateToWords
(
    @TheDate DATE
)
RETURNS NVARCHAR(300)
AS
BEGIN
    IF @TheDate IS NULL
        RETURN NULL;

    RETURN
        dbo.fn_NumberToWords(DAY(@TheDate))
        + N' ' + DATENAME(MONTH, @TheDate)
        + N' ' + dbo.fn_NumberToWords(YEAR(@TheDate));
END;

