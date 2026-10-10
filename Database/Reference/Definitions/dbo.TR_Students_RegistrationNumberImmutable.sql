SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ================================================================
   40. DATA-INTEGRITY TRIGGERS FOR CROSS-CONTEXT RULES
   ================================================================ */
CREATE   TRIGGER dbo.TR_Students_RegistrationNumberImmutable
ON dbo.Students
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(RegistrationNumber)
       AND EXISTS
       (
           SELECT 1
           FROM inserted I
           INNER JOIN deleted D ON D.AdmissionId=I.AdmissionId
           WHERE ISNULL(I.RegistrationNumber,N'')<>ISNULL(D.RegistrationNumber,N'')
       )
    BEGIN
        THROW 59101,'Registration Number is a permanent reference and cannot be changed after admission.',1;
    END;
END;

GO
