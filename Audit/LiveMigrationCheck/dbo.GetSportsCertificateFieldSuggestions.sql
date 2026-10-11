
/* =====================================================================
   6. dbo.GetSportsCertificateFieldSuggestions

      Both Sport Name and Performance recur across certificates, so
      unlike Character Certificate (Remarks only) both fields get a
      type-ahead suggestion list.
   ===================================================================== */

CREATE   PROCEDURE dbo.GetSportsCertificateFieldSuggestions
    @UserId INT,
    @Field NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    IF @Field = N'SportName'
        SELECT DISTINCT TOP (500) SportName AS Value
        FROM dbo.SportsCertificates
        WHERE SportName IS NOT NULL
          AND LTRIM(RTRIM(SportName)) <> N''
        ORDER BY SportName;

    ELSE IF @Field = N'Performance'
        SELECT DISTINCT TOP (500) Performance AS Value
        FROM dbo.SportsCertificates
        WHERE Performance IS NOT NULL
          AND LTRIM(RTRIM(Performance)) <> N''
        ORDER BY Performance;

    ELSE
        THROW 51601, 'Unknown suggestion field.', 1;
END;

