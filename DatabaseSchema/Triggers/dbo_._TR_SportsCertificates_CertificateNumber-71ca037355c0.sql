-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    DECLARE @Number NVARCHAR(24);
    DECLARE @RecordId INT, @IssueDate DATE;

    DECLARE pending CURSOR LOCAL FAST_FORWARD FOR
        SELECT SportsCertificateId, CertificateDate
        FROM inserted
        WHERE CertificateNumber IS NULL;

    OPEN pending;
    FETCH NEXT FROM pending INTO @RecordId, @IssueDate;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.GetNextCertificateNumber N'SPT', @IssueDate, @Number OUTPUT;

        UPDATE dbo.SportsCertificates
        SET CertificateNumber = @Number
        WHERE SportsCertificateId = @RecordId;

        FETCH NEXT FROM pending INTO @RecordId, @IssueDate;
    END;

    CLOSE pending;
    DEALLOCATE pending;
END;
