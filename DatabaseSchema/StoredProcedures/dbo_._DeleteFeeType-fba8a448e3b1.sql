-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';

    IF NOT EXISTS (SELECT 1 FROM dbo.FeeTypes WHERE FeeTypeId = @FeeTypeId)
        THROW 59261, 'The selected fee type was not found.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.FeeTypes
        WHERE FeeTypeId = @FeeTypeId
          AND (IsSystemDefined = 1
               OR FeeTypeCode IN (N'TUITION', N'ADMISSION', N'TRANSPORT')))
        THROW 59262, 'This fee type is used by admission and billing and cannot be deleted.', 1;

    IF EXISTS (SELECT 1 FROM dbo.FeeConfigurations WHERE FeeTypeId = @FeeTypeId)
        THROW 59263, 'This fee type has fee configuration and cannot be deleted.', 1;

    IF EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments WHERE FeeTypeId = @FeeTypeId)
        THROW 59264, 'This fee type is assigned to students and cannot be deleted.', 1;

    IF EXISTS (SELECT 1 FROM dbo.BillingCharges WHERE FeeTypeId = @FeeTypeId)
        THROW 59265, 'This fee type has billing charges and cannot be deleted.', 1;

    IF EXISTS (SELECT 1 FROM dbo.ManualFeePostingRequests WHERE FeeTypeId = @FeeTypeId)
        THROW 59266, 'This fee type has manual posting records and cannot be deleted.', 1;

    DELETE FROM dbo.FeeTypes
    WHERE FeeTypeId = @FeeTypeId;
END;
