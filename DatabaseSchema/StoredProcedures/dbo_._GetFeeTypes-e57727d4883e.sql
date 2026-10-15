-- Module body only. See companion XML for signatures and relationships.

BEGIN
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    SET NOCOUNT ON;
    SELECT   FeeTypeId,
             FeeTypeCode,
             FeeTypeName,
             Description,
             DisplayOrder,
             IsSystemDefined,
             ISNULL(DefaultFrequency, N'Monthly') AS DefaultFrequency
    FROM     dbo.FeeTypes
    ORDER BY DisplayOrder, FeeTypeName;
END
