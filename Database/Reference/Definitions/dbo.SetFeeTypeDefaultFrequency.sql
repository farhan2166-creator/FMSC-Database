SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.SetFeeTypeDefaultFrequency
    @UserId           INT,
    @FeeTypeId        INT,
    @DefaultFrequency NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';

    IF @DefaultFrequency NOT IN (N'Monthly', N'Yearly', N'As Needed')
        THROW 53001, 'Default frequency must be Monthly, Yearly, or As Needed.', 1;

    UPDATE dbo.FeeTypes
    SET DefaultFrequency = @DefaultFrequency
    WHERE FeeTypeId = @FeeTypeId;

    IF @@ROWCOUNT = 0
        THROW 53002, 'The fee type was not found.', 1;
END;


GO
