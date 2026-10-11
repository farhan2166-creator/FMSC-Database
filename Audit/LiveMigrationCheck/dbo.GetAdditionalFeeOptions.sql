
-- GetAdditionalFeeOptions
CREATE   PROCEDURE dbo.GetAdditionalFeeOptions
@UserId INT, @EnrollmentId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    SELECT   FT.FeeTypeId,
             FT.FeeTypeName,
             FT.FeeTypeCode,
             COALESCE (FC.Amount, 0) AS DefaultAmount,
             FT.FeeTypeName AS Display
    FROM     dbo.Enrollments AS E
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId CROSS JOIN dbo.FeeTypes AS FT OUTER APPLY (SELECT   TOP (1) FC.Amount
                                                                                                 FROM     dbo.FeeConfigurations AS FC
                                                                                                 WHERE    FC.CampusId = CCS.CampusId
                                                                                                          AND FC.ClassId = CCS.ClassId
                                                                                                          AND FC.FeeTypeId = FT.FeeTypeId
                                                                                                 ORDER BY FC.FeeConfigurationId DESC) AS FC
    WHERE    E.EnrollmentId = @EnrollmentId
             AND E.Status = N'Active'
             AND E.BillingReady = 1
    ORDER BY FT.DisplayOrder, FT.FeeTypeName;
END



