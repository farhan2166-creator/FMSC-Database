-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @RegularLecturerCertificateId <= 0
        THROW 58720, 'A valid certificate is required.', 1;

    SELECT
        RLC.RegularLecturerCertificateId,
        RLC.StaffId,

        RLC.CertificateNumber AS CertificateNumber,

        RLC.StaffName,
        RLC.FatherName,
        RLC.Gender,
        RLC.RoleTitle,
        RLC.SubjectTaught,
        RLC.CertificateDate,
        RLC.CreatedAt AS DateOfIssue,

        SP.SchoolName AS InstitutionLine

    FROM dbo.RegularLecturerCertificates AS RLC

    CROSS JOIN dbo.SchoolProfile AS SP

    WHERE RLC.RegularLecturerCertificateId = @RegularLecturerCertificateId
      AND SP.SchoolProfileId = 1;
END;
