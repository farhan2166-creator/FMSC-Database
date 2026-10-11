
-- GetFeeConfigurations
CREATE   PROCEDURE dbo.GetFeeConfigurations
@UserId INT, @CampusId INT, @ClassId INT=0, @AllClasses BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    IF @CampusId <= 0
        THROW 56320, 'Please select a campus first.', 1;
    IF @ClassId <= 0
       AND @AllClasses = 0
        THROW 56322, 'Please select a class first.', 1;
    SELECT   FC.FeeConfigurationId,
             FC.CampusId,
             FC.ClassId,
             FC.FeeTypeId,
             FT.FeeTypeCode,
             FT.FeeTypeName,
             FC.Amount,
             FC.Frequency,
             FC.Remarks,
             FT.IsMandatory,
             FT.IsTransportLinked
    FROM     dbo.FeeConfigurations AS FC
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = FC.FeeTypeId
    WHERE    FC.CampusId = @CampusId
             AND (@AllClasses = 1
                  OR FC.ClassId = @ClassId)
    ORDER BY FC.ClassId, FT.DisplayOrder, FT.FeeTypeName;
END



