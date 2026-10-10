-- Replace an unbilled future rate in place; preserve every existing bill.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @sql nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.ChangeStudentFeeAssignmentAmount_Internal'));
    IF @sql IS NULL THROW 53200, 'Fee amount procedure is missing.', 1;
    IF CHARINDEX(N'UNBILLED_RATE_REPLACEMENT', @sql) = 0
    BEGIN
        DECLARE @marker nvarchar(200) = N'    IF @ExistingEffectiveFrom IS NOT NULL';
        DECLARE @position int = CHARINDEX(@marker, @sql);
        IF @position = 0 THROW 53200, 'Fee amount procedure does not match the expected structure.', 1;
        DECLARE @branch nvarchar(max) = N'
    -- UNBILLED_RATE_REPLACEMENT: no extra version or obsolete scheduled action.
    IF @ExistingEffectiveFrom IS NOT NULL AND @NormalizedEffectiveFrom <= @ExistingEffectiveFrom
       AND NOT EXISTS (SELECT 1 FROM dbo.BillingCharges WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId)
    BEGIN
        BEGIN TRANSACTION;
        -- Serialize against posting or another rate edit before checking the timeline.
        IF NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments WITH (UPDLOCK, HOLDLOCK)
                       WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId AND EffectiveTo IS NULL
                         AND CancelledAt IS NULL AND Amount = @OldAmount AND EffectiveFrom = @ExistingEffectiveFrom)
            THROW 53146, ''The fee has changed. Refresh the fees and try again.'', 1;
        IF EXISTS (SELECT 1 FROM dbo.BillingCharges WITH (UPDLOCK, HOLDLOCK)
                   WHERE EnrollmentId = @EnrollmentId AND FeeTypeId = @FeeTypeId
                     AND (BillingMonth >= @NormalizedEffectiveFrom OR StudentFeeAssignmentId = @StudentFeeAssignmentId))
            THROW 53148, ''Choose a month after the last month already billed for this fee.'', 1;
        IF EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments
                   WHERE EnrollmentId = @EnrollmentId AND FeeTypeId = @FeeTypeId
                     AND StudentFeeAssignmentId <> @StudentFeeAssignmentId AND CancelledAt IS NULL
                     AND (EffectiveTo IS NULL OR EffectiveTo >= @NormalizedEffectiveFrom))
            THROW 53145, ''The selected month overlaps another fee period. Choose a later month.'', 1;
        IF @NewAmount < @DiscountAmount
            THROW 53149, ''The new fee cannot be lower than its existing discount.'', 1;
        UPDATE dbo.StudentFeeAssignments
        SET Amount = @NewAmount, EffectiveFrom = @NormalizedEffectiveFrom, UpdatedAt = SYSDATETIME()
        WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId;
        DECLARE @ReplacementOperation uniqueidentifier = NEWID();
        INSERT dbo.AuditOperations(OperationId, UserId, OperationType, CorrelationNote)
        VALUES(@ReplacementOperation, @UserId, N''FEE_ASSIGNMENT_AMOUNT_CHANGE'', N''Unbilled rate replaced in place'');
        DECLARE @ReplacementOld nvarchar(max) = (SELECT @OldAmount Amount, @ExistingEffectiveFrom EffectiveFrom FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        DECLARE @ReplacementNew nvarchar(max) = (SELECT @NewAmount Amount, @NormalizedEffectiveFrom EffectiveFrom, @Reason Reason FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC dbo.WriteAuditLog @OperationId = @ReplacementOperation, @UserId = @UserId,
             @TableName = N''StudentFeeAssignments'', @RecordId = @StudentFeeAssignmentId,
             @Action = N''UPDATE'', @OldValues = @ReplacementOld, @NewValues = @ReplacementNew;
        DELETE dbo.AuditOperations WHERE OperationId = @ReplacementOperation
          AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId = @ReplacementOperation);
        COMMIT TRANSACTION;
        SELECT StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount,
               Frequency, EffectiveFrom, EffectiveTo
        FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId;
        RETURN;
    END;
';
        SET @sql = STUFF(@sql, @position, 0, @branch);
    END;
    SET @sql = REPLACE(@sql, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
    SET @sql = REPLACE(@sql, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
    EXEC sys.sp_executesql @sql;
    SET @sql = OBJECT_DEFINITION(OBJECT_ID(N'dbo.GetStudentsAffectedByFeeConfiguration'));
    IF @sql IS NULL THROW 53200, 'Fee preview procedure is missing.', 1;
    DECLARE @start int = CHARINDEX(N'SELECT MAX(BC.BillingMonth)', @sql);
    DECLARE @finish int = CHARINDEX(N') AS LastBilledMonth', @sql, @start);
    IF @start = 0 OR @finish = 0 THROW 53200, 'Fee preview procedure does not match the expected structure.', 1;
    SET @sql = STUFF(@sql, @start, @finish - @start,
        N'SELECT MAX(BC.BillingMonth) FROM dbo.BillingCharges BC
          WHERE BC.EnrollmentId = X.EnrollmentId AND BC.FeeTypeId = @FeeTypeId
        ');
    SET @sql = REPLACE(@sql, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
    SET @sql = REPLACE(@sql, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
    EXEC sys.sp_executesql @sql;
    IF NOT EXISTS (SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber = N'4')
        INSERT dbo.DatabaseVersion(VersionNumber, AppliedOn, AppliedBy, Notes)
        VALUES(N'4', SYSDATETIME(), SUSER_SNAME(), N'Replace unbilled scheduled fee rates without duplicate versions.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
