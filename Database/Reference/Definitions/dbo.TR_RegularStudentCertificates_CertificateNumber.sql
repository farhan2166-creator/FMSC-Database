SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   TRIGGER dbo.TR_RegularStudentCertificates_CertificateNumber
ON dbo.RegularStudentCertificates
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Number NVARCHAR(24);
    DECLARE @RecordId INT, @IssueDate DATE;

    DECLARE pending CURSOR LOCAL FAST_FORWARD FOR
        SELECT RegularStudentCertificateId, CertificateDate
        FROM inserted
        WHERE CertificateNumber IS NULL;

    OPEN pending;
    FETCH NEXT FROM pending INTO @RecordId, @IssueDate;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.GetNextCertificateNumber N'REG', @IssueDate, @Number OUTPUT;

        UPDATE dbo.RegularStudentCertificates
        SET CertificateNumber = @Number
        WHERE RegularStudentCertificateId = @RecordId;

        FETCH NEXT FROM pending INTO @RecordId, @IssueDate;
    END;

    CLOSE pending;
    DEALLOCATE pending;
END;

GO
