
CREATE PROCEDURE dbo.DeleteStudentPhoto
    @UserId INT,
    @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    DELETE FROM dbo.StudentPhotos WHERE AdmissionId = @AdmissionId;
END;

