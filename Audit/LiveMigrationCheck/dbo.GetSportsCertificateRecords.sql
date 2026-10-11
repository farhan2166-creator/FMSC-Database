
/* =====================================================================
   4. dbo.GetSportsCertificateRecords - grid of certificates for a
      student (or, with @AdmissionId = 0, an empty result - the page
      always loads this scoped to the selected student, same
      convention as GetCharacterCertificateRecords).
   ===================================================================== */

CREATE   PROCEDURE dbo.GetSportsCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        SC.SportsCertificateId,
        SC.AdmissionId,

        SC.StudentName,
        SC.FatherName,
        SC.SportName,
        SC.Performance,
        SC.CertificateDate,

        SC.CreatedAt,
        SC.UpdatedAt

    FROM dbo.SportsCertificates AS SC

    WHERE
        @AdmissionId = 0
        OR SC.AdmissionId = @AdmissionId

    ORDER BY
        SC.SportsCertificateId DESC;
END;

