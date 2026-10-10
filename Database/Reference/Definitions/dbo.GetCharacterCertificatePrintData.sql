SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   7. dbo.GetCharacterCertificatePrintData

      CertificateNumber is derived the same way Leave Certificate
      derives its CertificateNumber - a formatted string built from
      the identity column, not a separately stored/tracked sequence.
   ===================================================================== */

CREATE PROCEDURE dbo.GetCharacterCertificatePrintData
    @UserId INT,
    @CertificateRecordId INT
AS
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
GO
