-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @CertificateRecordId <= 0
        THROW 58800, 'A valid date of birth certificate is required.', 1;

    SELECT
        DBC.CertificateRecordId,
        DBC.AdmissionId,

        DBC.CertificateNumber AS CertificateNumber,

        DBC.StudentName,
        DBC.FatherName,
        DBC.Gender,
        DBC.DateOfBirth,

        CONVERT(NVARCHAR(10), DBC.DateOfBirth, 105) AS DateOfBirthInFigures,

        CONCAT(
            RIGHT(N'0' + CONVERT(NVARCHAR(2), DAY(DBC.DateOfBirth)), 2),
            CASE
                WHEN DAY(DBC.DateOfBirth) IN (1, 21, 31) THEN N'st'
                WHEN DAY(DBC.DateOfBirth) IN (2, 22)     THEN N'nd'
                WHEN DAY(DBC.DateOfBirth) IN (3, 23)     THEN N'rd'
                ELSE N'th'
            END,
            N', ',
            DATENAME(MONTH, DBC.DateOfBirth),
            N', ',
            CONVERT(NVARCHAR(4), YEAR(DBC.DateOfBirth))
        ) AS DateOfBirthInWords,

        DBC.CertificateDate,
        DBC.CreatedAt AS DateOfIssue,

        SP.SchoolName

    FROM dbo.DateOfBirthCertificates AS DBC

    LEFT JOIN dbo.SchoolProfile AS SP
        ON 1 = 1

    WHERE DBC.CertificateRecordId = @CertificateRecordId;
END;
