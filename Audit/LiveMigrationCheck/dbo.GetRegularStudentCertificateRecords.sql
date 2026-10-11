
/* =====================================================================
   4. dbo.GetRegularStudentCertificateRecords - grid of certificates for
      a student (page always loads this scoped to the selected student,
      same convention as GetSportsCertificateRecords).
   ===================================================================== */

CREATE   PROCEDURE dbo.GetRegularStudentCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        RSC.RegularStudentCertificateId,
        RSC.AdmissionId,

        RSC.StudentName,
        RSC.FatherName,
        RSC.DateOfBirth,
        RSC.ClassName,
        RSC.CertificateDate,

        RSC.CreatedAt,
        RSC.UpdatedAt

    FROM dbo.RegularStudentCertificates AS RSC

    WHERE
        @AdmissionId = 0
        OR RSC.AdmissionId = @AdmissionId

    ORDER BY
        RSC.RegularStudentCertificateId DESC;
END;

