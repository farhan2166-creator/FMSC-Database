SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.GetAcademicYearUsageCount
    @UserId INT,
    @AcademicYearId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Calendar.Manage';

    SELECT COUNT(*)
    FROM dbo.Enrollments
    WHERE AcademicYearId = @AcademicYearId;
END;

GO
