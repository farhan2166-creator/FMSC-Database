-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Students.View';

    SELECT DISTINCT
        FM.AdmissionId
    FROM dbo.FamilyMembers FM;
END;
