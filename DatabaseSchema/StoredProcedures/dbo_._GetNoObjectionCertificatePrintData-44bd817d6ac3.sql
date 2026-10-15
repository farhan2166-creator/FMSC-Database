-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @NoObjectionCertificateId <= 0
        THROW 58620, 'A valid NOC is required.', 1;

    SELECT
        NOC.NoObjectionCertificateId,
        NOC.AdmissionId,

        NOC.CertificateNumber AS CertificateNumber,

        NOC.StudentName,
        NOC.FatherName,
        NOC.CertificateDate,
        NOC.CreatedAt AS DateOfIssue

    FROM dbo.NoObjectionCertificates AS NOC
    WHERE NOC.NoObjectionCertificateId = @NoObjectionCertificateId;
END;
