-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Calendar.Manage';

    SELECT COUNT(*)
    FROM dbo.Enrollments
    WHERE AcademicYearId = @AcademicYearId;
END;
