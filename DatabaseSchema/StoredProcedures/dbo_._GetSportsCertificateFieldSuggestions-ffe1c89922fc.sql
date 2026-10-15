-- Module body only. See companion XML for signatures and relationships.

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
