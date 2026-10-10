SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE FUNCTION dbo.NormalizeAdmissionNumber(@Number VARCHAR(40))
RETURNS VARCHAR(40)
WITH SCHEMABINDING
AS
BEGIN
    SET @Number = LOWER(LTRIM(RTRIM(@Number)));
    IF @Number IS NULL OR @Number = '' RETURN NULL;
    DECLARE @Split INT = PATINDEX('%[^0-9]%', @Number COLLATE Latin1_General_100_BIN2);
    IF @Split = 0 SET @Split = LEN(@Number) + 1;
    IF @Split = 1 RETURN NULL;
    DECLARE @Base BIGINT = TRY_CONVERT(BIGINT, LEFT(@Number, @Split - 1));
    DECLARE @Suffix VARCHAR(40) = SUBSTRING(@Number, @Split, 40);
    IF @Base IS NULL OR @Base < 1 OR @Base >= 9223372036854775807 RETURN NULL;
    IF @Suffix COLLATE Latin1_General_100_BIN2 LIKE '%[^a-z]%' RETURN NULL;
    RETURN CONVERT(VARCHAR(20), @Base) + @Suffix;
END;

GO
