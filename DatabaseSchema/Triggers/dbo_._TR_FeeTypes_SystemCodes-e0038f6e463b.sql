-- Module body only. See companion XML for signatures and relationships.
 BEGIN
           SET NOCOUNT ON;
           IF EXISTS (SELECT 1
                      FROM   inserted AS I
                             INNER JOIN
                             deleted AS D
                             ON D.FeeTypeId = I.FeeTypeId
                      WHERE  D.IsSystemDefined = 1
                             AND (I.FeeTypeCode <> D.FeeTypeCode))
               BEGIN
                   THROW 59102, 'System fee types and their stable business codes cannot be renamed or deactivated.', 1;
               END
           IF NOT EXISTS (SELECT 1
                          FROM   inserted)
              AND EXISTS (SELECT 1
                          FROM   deleted
                          WHERE  IsSystemDefined = 1)
               BEGIN
                   THROW 59103, 'System fee types cannot be deleted.', 1;
               END
       END
