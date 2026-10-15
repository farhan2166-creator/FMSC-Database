-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.View';

    SELECT
        L.AdmissionId,
        FM.FamilyId,
        (
            SELECT COUNT(*)
            FROM dbo.FamilyMembers FM2
            WHERE FM2.FamilyId = FM.FamilyId
        ) AS FamilyMemberCount
    FROM @AdmissionIds L
    LEFT JOIN dbo.FamilyMembers FM
        ON FM.AdmissionId = L.AdmissionId;
END;
