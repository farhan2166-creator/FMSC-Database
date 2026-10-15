-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.View';

    IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE AdmissionId = @AdmissionId)
        THROW 56660, 'The selected student was not found.', 1;

    SELECT
        AdmissionId,
        PhotoData,
        FileName,
        ContentType,
        UpdatedAt
    FROM dbo.StudentPhotos
    WHERE AdmissionId = @AdmissionId;
END;
