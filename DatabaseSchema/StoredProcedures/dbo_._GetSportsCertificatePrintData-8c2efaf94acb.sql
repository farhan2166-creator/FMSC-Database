-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @SportsCertificateId <= 0
        THROW 58420, 'A valid sports certificate is required.', 1;

    SELECT
        SC.SportsCertificateId,
        SC.AdmissionId,

        SC.CertificateNumber AS CertificateNumber,

        SC.StudentName,
        SC.FatherName,
        SC.SportName,
        SC.Performance,
        SC.CertificateDate,
        SC.CreatedAt AS DateOfIssue

    FROM dbo.SportsCertificates AS SC
    WHERE SC.SportsCertificateId = @SportsCertificateId;
END;
