-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAuthenticatedUser @UserId;
    IF @Field = N'FatherOccupation'
        SELECT DISTINCT TOP (500) FatherOccupation AS Value FROM dbo.Students
        WHERE NULLIF(LTRIM(RTRIM(FatherOccupation)), N'') IS NOT NULL ORDER BY FatherOccupation;
    ELSE IF @Field = N'PreviousSchool'
        SELECT DISTINCT PreviousSchool AS Value FROM dbo.Students
        WHERE NULLIF(LTRIM(RTRIM(PreviousSchool)), N'') IS NOT NULL ORDER BY PreviousSchool;
    ELSE IF @Field = N'Address'
        SELECT DISTINCT Address AS Value FROM dbo.Students
        WHERE NULLIF(LTRIM(RTRIM(Address)), N'') IS NOT NULL ORDER BY Address;
    ELSE IF @Field = N'PlaceOfBirth'
        SELECT DISTINCT TOP (500) PlaceOfBirth AS Value FROM dbo.Students
        WHERE NULLIF(LTRIM(RTRIM(PlaceOfBirth)), N'') IS NOT NULL ORDER BY PlaceOfBirth;
    ELSE IF @Field = N'CasteTribe'
        SELECT DISTINCT TOP (500) CasteTribe AS Value FROM dbo.Students
        WHERE NULLIF(LTRIM(RTRIM(CasteTribe)), N'') IS NOT NULL ORDER BY CasteTribe;
    ELSE THROW 51500, 'Unknown suggestion field.', 1;
END;
