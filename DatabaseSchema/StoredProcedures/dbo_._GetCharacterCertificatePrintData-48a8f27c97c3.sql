-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @CertificateRecordId <= 0
        THROW 58300, 'A valid character certificate is required.', 1;

    SELECT
        CC.CertificateRecordId,
        CC.AdmissionId,

        CC.CertificateNumber AS CertificateNumber,

        CC.StudentName,
        CC.FatherName,
        CC.Gender,
        CC.LastExamYear,
        CC.RollNumber,
        CC.Remarks,
        CC.CertificateDate,
        CC.CreatedAt AS DateOfIssue

    FROM dbo.CharacterCertificates AS CC
    WHERE CC.CertificateRecordId = @CertificateRecordId;
END;
