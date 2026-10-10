SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------------
   5. Allocation on insert - one trigger per table.

      A trigger rather than an edit to each Create procedure, for three reasons:
      it runs inside the inserting statement's transaction (which is the
      precondition the allocator documents), it covers every insert path
      including any future one, and it means the nine large Create procedures
      are not rewritten at all. This project already uses a trigger for exactly
      this kind of cross-cutting rule (TR_Students_RegistrationNumberImmutable).

      The prefix comes from the row: the leave tables carry CertificateType, so
      School/College become SLC/CLC in one trigger; the two staff tables both
      use EMP, per the operator's decision.
   --------------------------------------------------------------------------- */

/* Leave certificates - SLC for School, CLC for College */
CREATE   TRIGGER dbo.TR_LeaveCertificates_CertificateNumber
ON dbo.LeaveCertificates
AFTER INSERT
AS
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

GO
