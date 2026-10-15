-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE AdmissionId = @AdmissionId)
        THROW 56661, 'The selected student was not found.', 1;

    IF @PhotoData IS NULL OR DATALENGTH(@PhotoData) = 0
        THROW 56662, 'No photo data was supplied.', 1;

    IF EXISTS (SELECT 1 FROM dbo.StudentPhotos WHERE AdmissionId = @AdmissionId)
    BEGIN
        UPDATE dbo.StudentPhotos
        SET PhotoData = @PhotoData,
            FileName = @FileName,
            ContentType = @ContentType,
            UpdatedAt = SYSDATETIME()
        WHERE AdmissionId = @AdmissionId;
    END
    ELSE
    BEGIN
        INSERT dbo.StudentPhotos (AdmissionId, PhotoData, FileName, ContentType)
        VALUES (@AdmissionId, @PhotoData, @FileName, @ContentType);
    END;

    SELECT @AdmissionId AS AdmissionId;
END;
