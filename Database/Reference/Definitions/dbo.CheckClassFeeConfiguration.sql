SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- CheckClassFeeConfiguration
CREATE   PROCEDURE dbo.CheckClassFeeConfiguration
@UserId INT, @CampusId INT, @ClassId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    SELECT   FT.FeeTypeCode,
             FT.FeeTypeName
    FROM     dbo.FeeTypes AS FT
    WHERE    FT.IsMandatory = 1
             AND NOT EXISTS (SELECT 1
                             FROM   dbo.FeeConfigurations AS FC
                             WHERE  FC.CampusId = @CampusId
                                    AND FC.ClassId = @ClassId
                                    AND FC.FeeTypeId = FT.FeeTypeId)
    ORDER BY FT.FeeTypeCode;
END



GO
