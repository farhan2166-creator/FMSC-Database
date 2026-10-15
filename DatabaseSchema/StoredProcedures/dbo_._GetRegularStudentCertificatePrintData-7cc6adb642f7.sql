-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @RegularStudentCertificateId <= 0
        THROW 58520, 'A valid regular student certificate is required.', 1;

    SELECT
        RSC.RegularStudentCertificateId,
        RSC.AdmissionId,

        RSC.CertificateNumber AS CertificateNumber,

        RSC.StudentName,
        RSC.FatherName,
        RSC.DateOfBirth,
        RSC.ClassName,
        RSC.CertificateDate,
        RSC.CreatedAt AS DateOfIssue,

        LTRIM(RTRIM(CONCAT(
            SP.SchoolName,
            N' ',
            C.CampusName,
            N' ',
            COALESCE(NULLIF(LTRIM(RTRIM(C.City)), N''), SP.City)
        ))) AS InstitutionLine

    FROM dbo.RegularStudentCertificates AS RSC

    INNER JOIN dbo.Students AS S
        ON S.AdmissionId = RSC.AdmissionId

    INNER JOIN dbo.Campuses AS C
        ON C.CampusId = S.AdmissionCampusId

    CROSS JOIN dbo.SchoolProfile AS SP

    WHERE RSC.RegularStudentCertificateId = @RegularStudentCertificateId
      AND SP.SchoolProfileId = 1;
END;
