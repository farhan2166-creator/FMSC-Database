-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    DECLARE @Number NVARCHAR(24);
    DECLARE @LeaveRecordId INT, @IssueDate DATE, @Prefix NVARCHAR(8);

    DECLARE pending CURSOR LOCAL FAST_FORWARD FOR
        SELECT I.LeaveRecordId,
               I.LeaveDate,
               CASE WHEN I.CertificateType = N'College' THEN N'CLC' ELSE N'SLC' END
        FROM inserted AS I
        WHERE I.CertificateNumber IS NULL;

    OPEN pending;
    FETCH NEXT FROM pending INTO @LeaveRecordId, @IssueDate, @Prefix;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.GetNextCertificateNumber @Prefix, @IssueDate, @Number OUTPUT;

        UPDATE dbo.LeaveCertificates
        SET CertificateNumber = @Number
        WHERE LeaveRecordId = @LeaveRecordId;

        FETCH NEXT FROM pending INTO @LeaveRecordId, @IssueDate, @Prefix;
    END;

    CLOSE pending;
    DEALLOCATE pending;
END;
