
CREATE PROCEDURE dbo.GetStudentsWithFamily
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Students.View';

    SELECT DISTINCT
        FM.AdmissionId
    FROM dbo.FamilyMembers FM;
END;

