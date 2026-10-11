
CREATE   TRIGGER dbo.TR_BankAccountOpeningCertificates_CertificateNumber
ON dbo.BankAccountOpeningCertificates
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Number NVARCHAR(24);
    DECLARE @RecordId INT, @IssueDate DATE;

    DECLARE pending CURSOR LOCAL FAST_FORWARD FOR
        SELECT BankAccountOpeningCertificateId, CertificateDate
        FROM inserted
        WHERE CertificateNumber IS NULL;

    OPEN pending;
    FETCH NEXT FROM pending INTO @RecordId, @IssueDate;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.GetNextCertificateNumber N'EMP', @IssueDate, @Number OUTPUT;

        UPDATE dbo.BankAccountOpeningCertificates
        SET CertificateNumber = @Number
        WHERE BankAccountOpeningCertificateId = @RecordId;

        FETCH NEXT FROM pending INTO @RecordId, @IssueDate;
    END;

    CLOSE pending;
    DEALLOCATE pending;
END;

