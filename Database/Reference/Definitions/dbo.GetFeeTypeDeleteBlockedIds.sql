SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.GetFeeTypeDeleteBlockedIds
    @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';

    SELECT F.FeeTypeId AS Id
    FROM dbo.FeeTypes F
    WHERE F.IsSystemDefined = 1
       OR F.FeeTypeCode IN (N'TUITION', N'ADMISSION', N'TRANSPORT')
       OR EXISTS (SELECT 1 FROM dbo.BillingCharges X WHERE X.FeeTypeId = F.FeeTypeId)
       OR EXISTS (SELECT 1 FROM dbo.FeeConfigurations X WHERE X.FeeTypeId = F.FeeTypeId)
       OR EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments X WHERE X.FeeTypeId = F.FeeTypeId)
       OR EXISTS (SELECT 1 FROM dbo.ManualFeePostingRequests X WHERE X.FeeTypeId = F.FeeTypeId);
END;
GO
