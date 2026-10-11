
/* =====================================================================
   5. dbo.GetNoObjectionCertificatePrintData

      CertificateNumber follows the same derived-from-identity
      convention as the other certificates ("NOC-000123").
   ===================================================================== */

CREATE PROCEDURE dbo.GetNoObjectionCertificatePrintData
    @UserId INT,
    @NoObjectionCertificateId INT
AS
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

