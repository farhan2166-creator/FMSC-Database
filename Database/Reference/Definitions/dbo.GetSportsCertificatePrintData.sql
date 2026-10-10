SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   5. dbo.GetSportsCertificatePrintData

      CertificateNumber follows the same "SC-000123" derived-from-
      identity convention as CharacterCertificate's "CC-000123".
   ===================================================================== */

CREATE PROCEDURE dbo.GetSportsCertificatePrintData
    @UserId INT,
    @SportsCertificateId INT
AS
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

GO
