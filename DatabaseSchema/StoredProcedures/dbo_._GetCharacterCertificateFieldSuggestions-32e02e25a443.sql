-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    IF @Field = N'Remarks'
        SELECT DISTINCT TOP (500) Remarks AS Value
        FROM dbo.CharacterCertificates
        WHERE Remarks IS NOT NULL
          AND LTRIM(RTRIM(Remarks)) <> N''
        ORDER BY Remarks;

    ELSE
        THROW 51600, 'Unknown suggestion field.', 1;
END;
