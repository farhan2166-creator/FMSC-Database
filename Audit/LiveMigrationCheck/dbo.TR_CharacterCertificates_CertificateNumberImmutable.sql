
CREATE TRIGGER dbo.TR_CharacterCertificates_CertificateNumberImmutable
ON dbo.CharacterCertificates AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(CertificateNumber)
       AND EXISTS
       (
           SELECT 1
           FROM inserted AS I
           INNER JOIN deleted AS D ON D.CertificateRecordId = I.CertificateRecordId
           WHERE D.CertificateNumber IS NOT NULL
             AND (I.CertificateNumber IS NULL OR I.CertificateNumber <> D.CertificateNumber)
       )
        THROW 59210, 'A certificate number is issued once and cannot be changed.', 1;
END;

