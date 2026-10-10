SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   TRIGGER dbo.TR_CharacterCertificates_CertificateNumber
ON dbo.CharacterCertificates
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Number NVARCHAR(24);
    DECLARE @RecordId INT, @IssueDate DATE;

    DECLARE pending CURSOR LOCAL FAST_FORWARD FOR
        SELECT CertificateRecordId, CertificateDate
        FROM inserted
        WHERE CertificateNumber IS NULL;

    OPEN pending;
    FETCH NEXT FROM pending INTO @RecordId, @IssueDate;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.GetNextCertificateNumber N'CC', @IssueDate, @Number OUTPUT;

        UPDATE dbo.CharacterCertificates
        SET CertificateNumber = @Number
        WHERE CertificateRecordId = @RecordId;

        FETCH NEXT FROM pending INTO @RecordId, @IssueDate;
    END;

    CLOSE pending;
    DEALLOCATE pending;
END;

GO
