-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        BAC.BankAccountOpeningCertificateId,
        BAC.StaffId,

        BAC.StaffName,
        BAC.FatherName,
        BAC.Designation,
        BAC.BankName,
        BAC.BranchName,
        BAC.MonthlyIncome,
        BAC.Purpose,
        BAC.CertificateDate,

        BAC.CreatedAt,
        BAC.UpdatedAt

    FROM dbo.BankAccountOpeningCertificates AS BAC

    WHERE
        @StaffId = 0
        OR BAC.StaffId = @StaffId

    ORDER BY
        BAC.BankAccountOpeningCertificateId DESC;
END;
