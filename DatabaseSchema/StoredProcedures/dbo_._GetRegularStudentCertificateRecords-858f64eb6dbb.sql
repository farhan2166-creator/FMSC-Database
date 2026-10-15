-- Module body only. See companion XML for signatures and relationships.

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
