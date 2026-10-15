-- Module body only. See companion XML for signatures and relationships.


BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'System.Setup';

    SELECT COUNT(*)
    FROM dbo.Enrollments E
    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId
    WHERE CCS.SectionId = @SectionId
      AND E.Status = N'Active';
END;
