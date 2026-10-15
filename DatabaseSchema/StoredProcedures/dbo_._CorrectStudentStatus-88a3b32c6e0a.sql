-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @AdmissionId <= 0
        THROW 57001, 'A valid student is required.', 1;
    IF @NewStatus NOT IN (N'Active', N'Left')
        THROW 57002, 'Status must be Active or Left.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 57003, 'A reason is required for a manual correction.', 1;
    IF @ClearWithdrawalDate = 1
       AND @NewWithdrawalDate IS NOT NULL
        THROW 57004, 'Withdrawal date cannot both be cleared and set at the same time.', 1;
    BEGIN TRANSACTION;
    DECLARE @OldStatus AS NVARCHAR (30), @OldWithdrawalDate AS DATE;
    SELECT @OldStatus = Status,
           @OldWithdrawalDate = WithdrawalDate
    FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @AdmissionId;
    IF @OldStatus IS NULL
        BEGIN
            ROLLBACK;
            THROW 57005, 'The selected student was not found.', 1;
        END
    DECLARE @FinalWithdrawalDate AS DATE = CASE WHEN @ClearWithdrawalDate = 1 THEN NULL WHEN @NewWithdrawalDate IS NOT NULL THEN @NewWithdrawalDate ELSE @OldWithdrawalDate END;
    IF @FinalWithdrawalDate > CONVERT (DATE, SYSDATETIME())
        BEGIN
            ROLLBACK;
            THROW 57006, 'Withdrawal date cannot be in the future.', 1;
        END
    IF @OldStatus = @NewStatus
       AND ISNULL(@OldWithdrawalDate, '19000101') = ISNULL(@FinalWithdrawalDate, '19000101')
        BEGIN
            COMMIT TRANSACTION;
            SELECT @AdmissionId AS AdmissionId,
                   @OldStatus AS Status,
                   @OldWithdrawalDate AS WithdrawalDate,
                   CAST (0 AS BIT) AS Changed;
            RETURN;
        END
    IF @NewStatus = N'Active'
       AND @OldStatus = N'Left'
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @AdmissionId
                                  AND Status = N'Active')
                BEGIN
                    ROLLBACK;
                    THROW 57008, 'This student has no active enrollment. Use Re-Admit Student to bring them back into a class with a proper enrollment date, rather than correcting status directly.', 1;
                END
        END
    UPDATE dbo.Students
    SET    Status         = @NewStatus,
           WithdrawalDate = @FinalWithdrawalDate,
           UpdatedAt      = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    IF @@ROWCOUNT <> 1
        BEGIN
            ROLLBACK;
            THROW 57007, 'The student status could not be updated.', 1;
        END
    DECLARE @ClosedEnrollmentId AS INT;
    DECLARE @ClosedClassSectionId AS INT;
    DECLARE @EnrollmentLeavingDate AS DATE = ISNULL(@FinalWithdrawalDate, CONVERT (DATE, SYSDATETIME()));
    IF @NewStatus = N'Left'
        BEGIN
            SELECT   TOP (1) @ClosedEnrollmentId = E.EnrollmentId,
                             @ClosedClassSectionId = E.ClassSectionId
            FROM     dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
            WHERE    E.AdmissionId = @AdmissionId
                     AND E.Status = N'Active'
            ORDER BY E.EnrollmentId DESC;
            IF @ClosedEnrollmentId IS NOT NULL
                BEGIN
                    DECLARE @MaxBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                                       FROM   dbo.BillingCharges
                                                       WHERE  EnrollmentId = @ClosedEnrollmentId
                                                              AND Status <> N'Cancelled');
                    IF @MaxBilledMonth IS NOT NULL
                       AND DATEFROMPARTS(YEAR(@EnrollmentLeavingDate), MONTH(@EnrollmentLeavingDate), 1) < @MaxBilledMonth
                        BEGIN
                            ROLLBACK;
                            THROW 57009, 'This student already has billing charges for a later month than the requested withdrawal date. Cancel/correct those charges first, or choose a withdrawal date in or after the last billed month.', 1;
                        END
                    UPDATE dbo.Enrollments
                    SET    Status      = N'Left',
                           LeavingDate = @EnrollmentLeavingDate,
                           Remarks     = @Reason,
                           UpdatedAt   = SYSDATETIME()
                    WHERE  EnrollmentId = @ClosedEnrollmentId
                           AND Status = N'Active';
                    IF @@ROWCOUNT = 1
                        BEGIN
                            INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
                            VALUES                       (@ClosedEnrollmentId, @AdmissionId, N'LEAVE', @EnrollmentLeavingDate, @ClosedClassSectionId, NULL, N'Active', N'Left', N'Closed automatically by Student Status Console correction. ' + @Reason, @UserId);
                        END
                END
        END
    IF @OldStatus <> @NewStatus
        BEGIN
            INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
            VALUES                          (@AdmissionId, @OldStatus, @NewStatus, N'Console correction', ISNULL(@FinalWithdrawalDate, CONVERT (DATE, SYSDATETIME())), @Reason, @UserId);
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'STUDENT_STATUS_CORRECTION', CONVERT (NVARCHAR (200), @AdmissionId));
    DECLARE @CorrectionValues AS NVARCHAR (MAX) = (SELECT @AdmissionId AS AdmissionId,
                                                          @OldStatus AS OldStatus,
                                                          @NewStatus AS NewStatus,
                                                          @OldWithdrawalDate AS OldWithdrawalDate,
                                                          @FinalWithdrawalDate AS NewWithdrawalDate,
                                                          @ClosedEnrollmentId AS ClosedEnrollmentId,
                                                          @Reason AS Reason
                                                   FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AdmissionId, @Action = N'STATUS_CORRECTION', @OldValues = NULL, @NewValues = @CorrectionValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @NewStatus AS Status,
           @FinalWithdrawalDate AS WithdrawalDate,
           CAST (1 AS BIT) AS Changed;
END
