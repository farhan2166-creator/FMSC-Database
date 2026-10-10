SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- SaveFeeConfiguration
CREATE   PROCEDURE dbo.SaveFeeConfiguration
@UserId INT, @CampusId INT, @ClassId INT, @FeeTypeId INT, @Amount DECIMAL (12, 2), @Frequency NVARCHAR (30), @FeeConfigurationId INT, @Remarks NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @CampusId)
        THROW 56310, 'Please select an campus.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Classes
                   WHERE  ClassId = @ClassId)
        THROW 56312, 'Please select an class.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeTypes
                   WHERE  FeeTypeId = @FeeTypeId)
        THROW 56313, 'Please select an fee type.', 1;
    IF @Amount < 0
        THROW 56314, 'Fee amount cannot be negative.', 1;
    DECLARE @ResultId AS INT;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @AuditAction AS NVARCHAR (20) = NULL;
    DECLARE @OldValues AS NVARCHAR (MAX) = NULL;
    DECLARE @NewValues AS NVARCHAR (MAX);
    DECLARE @NewRemarks AS NVARCHAR (500) = NULLIF (LTRIM(RTRIM(@Remarks)), N'');
    BEGIN TRANSACTION;
    IF @FeeConfigurationId = 0
        BEGIN
            INSERT  dbo.FeeConfigurations (CampusId, ClassId, FeeTypeId, Amount, Frequency, Remarks)
            VALUES                       (@CampusId, @ClassId, @FeeTypeId, @Amount, @Frequency, @NewRemarks);
            SET @ResultId = CONVERT (INT, SCOPE_IDENTITY());
            SET @AuditAction = N'INSERT';
        END
    ELSE
        BEGIN
            DECLARE @OldCampusId AS INT, @OldClassId AS INT, @OldFeeTypeId AS INT, @OldAmount AS DECIMAL (12, 2), @OldFrequency AS NVARCHAR (30), @OldRemarks AS NVARCHAR (500);
            SELECT @OldCampusId = CampusId,
                   @OldClassId = ClassId,
                   @OldFeeTypeId = FeeTypeId,
                   @OldAmount = Amount,
                   @OldFrequency = Frequency,
                   @OldRemarks = Remarks
            FROM   dbo.FeeConfigurations WITH (UPDLOCK, HOLDLOCK)
            WHERE  FeeConfigurationId = @FeeConfigurationId;
            IF @@ROWCOUNT = 0
                THROW 56315, 'The selected fee configuration was not found.', 1;
            SET @ResultId = @FeeConfigurationId;
            IF @OldCampusId = @CampusId
               AND @OldClassId = @ClassId
               AND @OldFeeTypeId = @FeeTypeId
               AND @OldAmount = @Amount
               AND ISNULL(@OldFrequency, N'') = ISNULL(@Frequency, N'')
               AND ISNULL(@OldRemarks, N'') = ISNULL(@NewRemarks, N'')
                BEGIN
                    UPDATE dbo.FeeConfigurations
                    SET    UpdatedAt = SYSDATETIME()
                    WHERE  FeeConfigurationId = @FeeConfigurationId;
                END
            ELSE
                BEGIN
                    SET @OldValues = (SELECT @FeeConfigurationId AS FeeConfigurationId,
                                             @OldCampusId AS CampusId,
                                             @OldClassId AS ClassId,
                                             @OldFeeTypeId AS FeeTypeId,
                                             @OldAmount AS Amount,
                                             @OldFrequency AS Frequency,
                                             @OldRemarks AS Remarks
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                    UPDATE dbo.FeeConfigurations
                    SET    CampusId  = @CampusId,
                           ClassId   = @ClassId,
                           FeeTypeId = @FeeTypeId,
                           Amount    = @Amount,
                           Frequency = @Frequency,
                           Remarks   = @NewRemarks,
                           UpdatedAt = SYSDATETIME()
                    WHERE  FeeConfigurationId = @FeeConfigurationId;
                    SET @AuditAction = N'UPDATE';
                END
        END
    IF @AuditAction IS NOT NULL
        BEGIN
            SET @NewValues = (SELECT @ResultId AS FeeConfigurationId,
                                     @CampusId AS CampusId,
                                     @ClassId AS ClassId,
                                     @FeeTypeId AS FeeTypeId,
                                     @Amount AS Amount,
                                     @Frequency AS Frequency,
                                     @NewRemarks AS Remarks
                              FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
            VALUES                     (@OperationId, @UserId, N'FEE_CONFIGURATION', CONVERT (NVARCHAR (200), @ResultId));
            DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @ResultId);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'FeeConfigurations', @RecordId = @RecordId, @Action = @AuditAction, @OldValues = @OldValues, @NewValues = @NewValues;
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @ResultId AS FeeConfigurationId;
END



GO
