-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 53151, 'A reason is required to apply a fee configuration change to existing students.', 1;
    IF @EffectiveFrom IS NULL
        THROW 53152, 'An effective-from date is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeConfigurations
                   WHERE  CampusId = @CampusId
                          AND ClassId = @ClassId
                          AND FeeTypeId = @FeeTypeId)
        THROW 53153, 'No fee configuration exists for the selected campus, class and fee type.', 1;
    DECLARE @NormalizedEffectiveFrom AS DATE = DATEFROMPARTS(YEAR(@EffectiveFrom), MONTH(@EffectiveFrom), 1);
    DECLARE @Candidates TABLE (
        RowNum                 INT             IDENTITY (1, 1),
        StudentFeeAssignmentId INT            ,
        NewAmount              DECIMAL (12, 2));
    INSERT INTO @Candidates (StudentFeeAssignmentId, NewAmount)
    SELECT StudentFeeAssignmentId,
           NewAmount
    FROM   dbo.fn_StaleFeeAssignmentsForConfiguration(@CampusId, @ClassId, @FeeTypeId);
    DECLARE @Total AS INT = (SELECT COUNT(*)
                             FROM   @Candidates);
    DECLARE @Succeeded AS INT = 0;
    DECLARE @Failed AS INT = 0;
    DECLARE @i AS INT = 1;
    DECLARE @CurrentAssignmentId AS INT, @CurrentNewAmount AS DECIMAL (12, 2);
    DECLARE @Failures TABLE (
        StudentFeeAssignmentId INT            ,
        ErrorMessage           NVARCHAR (2000));
    WHILE @i <= @Total
        BEGIN
            SELECT @CurrentAssignmentId = StudentFeeAssignmentId,
                   @CurrentNewAmount = NewAmount
            FROM   @Candidates
            WHERE  RowNum = @i;
            BEGIN TRY
                EXECUTE dbo.ChangeStudentFeeAssignmentAmount_Internal @UserId = @UserId, @StudentFeeAssignmentId = @CurrentAssignmentId, @NewAmount = @CurrentNewAmount, @EffectiveFrom = @NormalizedEffectiveFrom, @Reason = @Reason;
                SET @Succeeded = @Succeeded + 1;
            END TRY
            BEGIN CATCH
                SET @Failed = @Failed + 1;
                INSERT  INTO @Failures (StudentFeeAssignmentId, ErrorMessage)
                VALUES                (@CurrentAssignmentId, ERROR_MESSAGE());
            END CATCH
            SET @i = @i + 1;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'FEE_APPLY_CONFIG_TO_EXISTING', N'CampusId=' + CONVERT (NVARCHAR (20), @CampusId) + N' ClassId=' + CONVERT (NVARCHAR (20), @ClassId) + N' FeeTypeId=' + CONVERT (NVARCHAR (20), @FeeTypeId) + N' EffectiveFrom=' + CONVERT (NVARCHAR (20), @NormalizedEffectiveFrom) + N' Total=' + CONVERT (NVARCHAR (20), @Total) + N' Succeeded=' + CONVERT (NVARCHAR (20), @Succeeded) + N' Failed=' + CONVERT (NVARCHAR (20), @Failed) + N' Reason=' + @Reason);
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @CampusId AS CampusId,
                                                   @ClassId AS ClassId,
                                                   @FeeTypeId AS FeeTypeId,
                                                   @NormalizedEffectiveFrom AS EffectiveFrom,
                                                   @Reason AS Reason,
                                                   @Total AS TotalAssignments,
                                                   @Succeeded AS Succeeded,
                                                   @Failed AS Failed
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = 0, @Action = N'UPDATE', @OldValues = NULL, @NewValues = @NewValues;
    SELECT @Total AS TotalAssignments,
           @Succeeded AS Succeeded,
           @Failed AS Failed,
           @NormalizedEffectiveFrom AS EffectiveFrom;
    SELECT StudentFeeAssignmentId,
           ErrorMessage
    FROM   @Failures;
DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
END
