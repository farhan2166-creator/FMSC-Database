
/* =====================================================================
   5. dbo.GetBankAccountOpeningCertificatePrintData

      CertificateNumber follows the same "BA-000123" derived-from-
      identity convention as the other certificates ("RL-"/"RS-"/"CC-"/
      "SC-"/"NOC-"/"PC-").

      InstitutionLine is just SchoolProfile.SchoolName (no campus split -
      staff aren't tied to a single home campus, same as
      RegularLecturerCertificate), composed here so the RDLC itself
      stays a plain First(Fields!InstitutionLine...) - no string-
      building in RDLC expressions.
   ===================================================================== */

CREATE PROCEDURE dbo.GetBankAccountOpeningCertificatePrintData
    @UserId INT,
    @BankAccountOpeningCertificateId INT
AS
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

