-- Module body only. See companion XML for signatures and relationships.

BEGIN
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    SET NOCOUNT ON;
    SELECT   *
    FROM     dbo.AcademicYears
    ORDER BY StartDate DESC, AcademicYearId DESC;
END
