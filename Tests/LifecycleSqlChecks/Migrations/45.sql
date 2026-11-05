-- Return the transport flag required by the same-year transfer fee review.
-- Existing fee definitions, assignments, charges and payments are unchanged.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 45 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>44
    THROW 51000,'Migration 45 requires schema 44.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetFeeTypes
    @UserId INT
AS
BEGIN
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    SET NOCOUNT ON;
    SELECT FeeTypeId,
           FeeTypeCode,
           FeeTypeName,
           Description,
           DisplayOrder,
           IsSystemDefined,
           ISNULL(DefaultFrequency, N'Monthly') AS DefaultFrequency,
           IsTransportLinked
    FROM dbo.FeeTypes
    ORDER BY DisplayOrder, FeeTypeName;
END
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'45',SYSDATETIME(),SUSER_SNAME(),N'Expose the fee transport flag required by class/campus transfer review');
GO
