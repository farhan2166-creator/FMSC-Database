-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @BankAccountOpeningCertificateId <= 0
        THROW 58820, 'A valid certificate is required.', 1;

    SELECT
        BAC.BankAccountOpeningCertificateId,
        BAC.StaffId,

        BAC.CertificateNumber AS CertificateNumber,

        BAC.StaffName,
        BAC.FatherName,
        BAC.Gender,
        BAC.CNIC,
        BAC.Designation,
        BAC.BankName,
        BAC.BranchName,
        BAC.MonthlyIncome,
        BAC.Purpose,
        BAC.CertificateDate,
        BAC.CreatedAt AS DateOfIssue,

        SP.SchoolName AS InstitutionLine

    FROM dbo.BankAccountOpeningCertificates AS BAC

    CROSS JOIN dbo.SchoolProfile AS SP

    WHERE BAC.BankAccountOpeningCertificateId = @BankAccountOpeningCertificateId
      AND SP.SchoolProfileId = 1;
END;
