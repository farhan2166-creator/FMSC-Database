SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------------
   3. Immutability - an issued number cannot be edited.

      Created after the backfill, so they can afford to be strict: ANY change is
      refused, including clearing the number. Mirrors
      TR_Students_RegistrationNumberImmutable, which does the same for the
      registration number.
   --------------------------------------------------------------------------- */
CREATE TRIGGER dbo.TR_LeaveCertificates_CertificateNumberImmutable
ON dbo.LeaveCertificates AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(CertificateNumber)
       AND EXISTS
       (
           SELECT 1
           FROM inserted AS I
           INNER JOIN deleted AS D ON D.LeaveRecordId = I.LeaveRecordId
           WHERE D.CertificateNumber IS NOT NULL
             AND (I.CertificateNumber IS NULL OR I.CertificateNumber <> D.CertificateNumber)
       )
        THROW 59210, 'A certificate number is issued once and cannot be changed.', 1;
END;

GO
