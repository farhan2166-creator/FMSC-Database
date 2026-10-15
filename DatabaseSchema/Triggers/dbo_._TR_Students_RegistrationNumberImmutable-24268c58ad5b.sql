-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    IF UPDATE(RegistrationNumber)
       AND EXISTS
       (
           SELECT 1
           FROM inserted I
           INNER JOIN deleted D ON D.AdmissionId=I.AdmissionId
           WHERE ISNULL(I.RegistrationNumber,N'')<>ISNULL(D.RegistrationNumber,N'') AND NOT(D.Status=N'Pending' AND D.RegistrationNumber IS NULL AND I.Status=N'Active')
       )
    BEGIN
        THROW 59101,'Registration Number is a permanent reference and cannot be changed after admission.',1;
    END;
END;
