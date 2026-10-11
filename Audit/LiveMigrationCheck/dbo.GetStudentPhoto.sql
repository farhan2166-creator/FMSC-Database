/* ================================================================
   STUDENT PHOTOS � new stored procedures
   Run this once against FMSC_DB (the same database the app connects
   to at C:\ProgramData\FMSC_Database\FMSC_DB.mdf) before using the
   new "Photo" tab under Students ? Family & Documents.

   The dbo.StudentPhotos table, its primary key (AdmissionId, 1 row
   per student) and its foreign key to dbo.Students already exist in
   the schema � this script only adds the three procedures the app
   needs to read, save and remove a photo. It follows the same
   conventions as the rest of FMSC_DB: RequirePermission for access
   control, THROW for validation errors, SET NOCOUNT ON.
   ================================================================ */

CREATE PROCEDURE dbo.GetStudentPhoto
    @UserId INT,
    @AdmissionId INT
AS
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

