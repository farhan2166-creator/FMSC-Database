SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   4. dbo.GetBankAccountOpeningCertificateRecords - grid of letters for
      a staff member (page always loads this scoped to the selected
      staff member, same convention as
      GetRegularLecturerCertificateRecords).
   ===================================================================== */

CREATE   PROCEDURE dbo.GetBankAccountOpeningCertificateRecords
    @UserId INT,
    @StaffId INT = 0
AS
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

GO
