-- Module body only. See companion XML for signatures and relationships.

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
