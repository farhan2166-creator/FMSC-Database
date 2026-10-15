-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        NOC.NoObjectionCertificateId,
        NOC.AdmissionId,

        NOC.StudentName,
        NOC.FatherName,
        NOC.CertificateDate,

        NOC.CreatedAt,
        NOC.UpdatedAt

    FROM dbo.NoObjectionCertificates AS NOC

    WHERE
        @AdmissionId = 0
        OR NOC.AdmissionId = @AdmissionId

    ORDER BY
        NOC.NoObjectionCertificateId DESC;
END;
