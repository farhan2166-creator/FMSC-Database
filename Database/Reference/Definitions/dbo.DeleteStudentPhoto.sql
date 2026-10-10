SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.DeleteStudentPhoto
    @UserId INT,
    @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    DELETE FROM dbo.StudentPhotos WHERE AdmissionId = @AdmissionId;
END;

GO
