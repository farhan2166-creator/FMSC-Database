-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    DELETE FROM dbo.StudentPhotos WHERE AdmissionId = @AdmissionId;
END;
