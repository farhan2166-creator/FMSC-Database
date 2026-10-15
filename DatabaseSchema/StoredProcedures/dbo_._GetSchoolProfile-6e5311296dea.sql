-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequireAuthenticatedUser @UserId;

    SELECT
        SchoolProfileId,
        SchoolName,
        AddressLine,
        City,
        ContactNumber,
        Email,
        LogoImage,
        LogoFileName,
        LogoContentType,
        FeeProrationCutoffDay,
        CreatedAt,
        CreatedByUserId,
        UpdatedAt,
        UpdatedByUserId
    FROM dbo.SchoolProfile
    WHERE SchoolProfileId = 1;
END;
