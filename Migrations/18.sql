-- Student Console corrections may skip audit entries.
-- Apply before deploying the updated application. Default auditing elsewhere is preserved.
-- Permissions, validation, transactions, correction/status histories and fee rules are unchanged.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.CorrectEnrollmentStatus
@UserId INT, @EnrollmentId INT=NULL, @AdmissionId INT=NULL, @NewStatus NVARCHAR (30)=NULL, @NewEnrollmentDate DATE=NULL, @NewLeavingDate DATE=NULL, @ClearLeavingDate BIT=0, @NewClassSectionId INT=NULL, @AcademicYearId INT=NULL, @Reason NVARCHAR (500), @WriteAudit bit=1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
            THROW 57101, 'A reason is required for a manual correction.', 1;
        IF @ClearLeavingDate = 1
           AND @NewLeavingDate IS NOT NULL
            THROW 57102, 'Leaving date cannot both be cleared and set at the same time.', 1;
        BEGIN TRANSACTION;
        IF @EnrollmentId IS NULL
           OR @EnrollmentId = 0
            BEGIN
                IF @AdmissionId IS NULL
                   OR @AdmissionId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57103, 'A valid student is required to create an enrollment.', 1;
                    END
                DECLARE @PriorStudentStatus AS NVARCHAR (30);
                DECLARE @PriorStudentWithdrawalDate AS DATE;
                SELECT @PriorStudentStatus = Status,
                       @PriorStudentWithdrawalDate = WithdrawalDate
                FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
                WHERE  AdmissionId = @AdmissionId;
                IF @PriorStudentStatus IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57104, 'The selected student was not found.', 1;
                    END
                IF @AcademicYearId IS NULL
                   OR @AcademicYearId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57105, 'A valid academic year is required.', 1;
                    END
                IF @NewClassSectionId IS NULL
                   OR @NewClassSectionId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57106, 'A valid class section is required.', 1;
                    END
                IF @NewEnrollmentDate IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57107, 'Enrollment date is required.', 1;
                    END
                SET @NewStatus = ISNULL(NULLIF (@NewStatus, N''), N'Active');
                IF @NewStatus NOT IN (N'Active', N'Promoted', N'Left')
                    BEGIN
                        ROLLBACK;
                        THROW 57108, 'Status must be Active, Promoted or Left.', 1;
                    END
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.CampusClassSections
                               WHERE  ClassSectionId = @NewClassSectionId)
                    BEGIN
                        ROLLBACK;
                        THROW 57109, 'The selected class section is invalid.', 1;
                    END
                DECLARE @NewEffectiveLeavingDate AS DATE = CASE WHEN @ClearLeavingDate = 1 THEN NULL ELSE @NewLeavingDate END;
                IF @NewEffectiveLeavingDate IS NOT NULL
                   AND @NewEffectiveLeavingDate < @NewEnrollmentDate
                    BEGIN
                        ROLLBACK;
                        THROW 57110, 'Leaving date cannot be before the enrollment date.', 1;
                    END
                IF @NewStatus = N'Left'
                   AND @NewEffectiveLeavingDate IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57117, 'A leaving date is required when creating a Left enrollment record.', 1;
                    END
                IF @NewStatus = N'Promoted'
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                                   WHERE  AdmissionId = @AdmissionId
                                          AND Status = N'Active')
                    BEGIN
                        ROLLBACK;
                        THROW 57118, 'Marking a new enrollment Promoted requires an existing Active enrollment for this student. Create/select the destination enrollment first, or use the Promotion function.', 1;
                    END
                IF @NewStatus = N'Active'
                   AND EXISTS (SELECT 1
                               FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                               WHERE  AdmissionId = @AdmissionId
                                      AND Status = N'Active')
                    BEGIN
                        ROLLBACK;
                        THROW 57216, 'An Active enrollment already exists for this student. Select and correct the existing enrollment instead.', 1;
                    END
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, Remarks)
                VALUES                 (@AdmissionId, @AcademicYearId, @NewClassSectionId, @NewEnrollmentDate, @NewEffectiveLeavingDate, @NewStatus, @Reason);
                SET @EnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
                VALUES                       (@EnrollmentId, @AdmissionId, N'CORRECTION_CREATE', @NewEnrollmentDate, NULL, @NewClassSectionId, NULL, @NewStatus, @Reason, @UserId);
                DECLARE @StudentStatusSynced AS BIT = 0;
                IF @NewStatus = N'Active'
                   AND @PriorStudentStatus <> N'Active'
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = N'Active',
                               WithdrawalDate = NULL,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @PriorStudentStatus, N'Active', N'Synchronized by enrollment correction (create).', @NewEnrollmentDate, @Reason, @UserId);
                        SET @StudentStatusSynced = 1;
                    END
IF @WriteAudit=1
BEGIN
                DECLARE @CreateOperationId AS UNIQUEIDENTIFIER = NEWID();
                INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                VALUES                     (@CreateOperationId, @UserId, N'ENROLLMENT_CORRECTION_CREATE', CONVERT (NVARCHAR (200), @AdmissionId));
                EXECUTE dbo.WriteAuditLog @OperationId = @CreateOperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @EnrollmentId, @Action = N'CORRECTION_CREATE', @OldValues = NULL, @NewValues = @Reason;
                IF @StudentStatusSynced = 1
                    BEGIN
                        DECLARE @CreateSyncValues AS NVARCHAR (MAX) = (SELECT @AdmissionId AS AdmissionId,
                                                                              @PriorStudentStatus AS OldStatus,
                                                                              N'Active' AS NewStatus,
                                                                              @EnrollmentId AS EnrollmentId
                                                                       FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        EXECUTE dbo.WriteAuditLog @OperationId = @CreateOperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AdmissionId, @Action = N'STATUS_SYNC', @OldValues = NULL, @NewValues = @CreateSyncValues;
                    END
                DELETE dbo.AuditOperations WHERE OperationId=@CreateOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@CreateOperationId);
END;
COMMIT TRANSACTION;
                SELECT @EnrollmentId AS EnrollmentId,
                       @AdmissionId AS AdmissionId,
                       @NewStatus AS Status,
                       CAST (1 AS BIT) AS Changed;
                RETURN;
            END
        DECLARE @OldAdmissionId AS INT, @OldStatus AS NVARCHAR (30), @OldEnrollmentDate AS DATE, @OldLeavingDate AS DATE, @OldClassSectionId AS INT;
        SELECT @OldAdmissionId = AdmissionId,
               @OldStatus = Status,
               @OldEnrollmentDate = EnrollmentDate,
               @OldLeavingDate = LeavingDate,
               @OldClassSectionId = ClassSectionId
        FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
        WHERE  EnrollmentId = @EnrollmentId;
        IF @OldAdmissionId IS NULL
            BEGIN
                ROLLBACK;
                THROW 57111, 'The selected enrollment was not found.', 1;
            END
        DECLARE @EditPriorStudentStatus AS NVARCHAR (30);
        DECLARE @EditPriorStudentWithdrawalDate AS DATE;
        SELECT @EditPriorStudentStatus = Status,
               @EditPriorStudentWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @OldAdmissionId;
        IF @EditPriorStudentStatus IS NULL
            BEGIN
                ROLLBACK;
                THROW 57119, 'The student record for this enrollment was not found.', 1;
            END
        DECLARE @FinalStatus AS NVARCHAR (30) = ISNULL(NULLIF (@NewStatus, N''), @OldStatus);
        IF @FinalStatus NOT IN (N'Active', N'Promoted', N'Left')
            BEGIN
                ROLLBACK;
                THROW 57112, 'Status must be Active, Promoted or Left.', 1;
            END
        DECLARE @FinalEnrollmentDate AS DATE = ISNULL(@NewEnrollmentDate, @OldEnrollmentDate);
        DECLARE @FinalLeavingDate AS DATE = CASE WHEN @ClearLeavingDate = 1 THEN NULL WHEN @NewLeavingDate IS NOT NULL THEN @NewLeavingDate ELSE @OldLeavingDate END;
        DECLARE @FinalClassSectionId AS INT = ISNULL(@NewClassSectionId, @OldClassSectionId);
        IF @FinalLeavingDate IS NOT NULL
           AND @FinalLeavingDate < @FinalEnrollmentDate
            BEGIN
                ROLLBACK;
                THROW 57113, 'Leaving date cannot be before the enrollment date.', 1;
            END
        IF @FinalClassSectionId <> @OldClassSectionId
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.CampusClassSections
                           WHERE  ClassSectionId = @FinalClassSectionId)
            BEGIN
                ROLLBACK;
                THROW 57114, 'The selected class section is invalid.', 1;
            END
        IF @FinalStatus = @OldStatus
           AND @FinalEnrollmentDate = @OldEnrollmentDate
           AND ISNULL(@FinalLeavingDate, '19000101') = ISNULL(@OldLeavingDate, '19000101')
           AND @FinalClassSectionId = @OldClassSectionId
            BEGIN
                DELETE dbo.AuditOperations WHERE OperationId=@CreateOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@CreateOperationId);
COMMIT TRANSACTION;
                SELECT @EnrollmentId AS EnrollmentId,
                       @OldAdmissionId AS AdmissionId,
                       @OldStatus AS Status,
                       CAST (0 AS BIT) AS Changed;
                RETURN;
            END
        IF @FinalEnrollmentDate <> @OldEnrollmentDate
           AND (EXISTS (SELECT 1
                        FROM   dbo.BillingCharges
                        WHERE  EnrollmentId = @EnrollmentId)
                OR EXISTS (SELECT 1
                           FROM   dbo.FeePayments
                           WHERE  EnrollmentId = @EnrollmentId)
                OR EXISTS (SELECT 1
                           FROM   dbo.EnrollmentHistory
                           WHERE  EnrollmentId = @EnrollmentId))
            BEGIN
                ROLLBACK;
                THROW 57116, 'This enrollment already has billing, payment or status history, so its enrollment (start) date cannot be changed by an ordinary correction. Correct the dependent records first, or create a new enrollment with the correct start date.', 1;
            END
        IF @FinalStatus = N'Left'
           AND @FinalLeavingDate IS NULL
            BEGIN
                ROLLBACK;
                THROW 57117, 'A leaving date is required when setting an enrollment to Left.', 1;
            END
        IF @FinalStatus = N'Promoted'
           AND @OldStatus <> N'Promoted'
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @OldAdmissionId
                                  AND Status = N'Active'
                                  AND EnrollmentId <> @EnrollmentId)
            BEGIN
                ROLLBACK;
                THROW 57118, 'Marking this enrollment Promoted requires an existing Active enrollment for this student. Create/select the destination enrollment first, or use the Promotion function.', 1;
            END
        IF @FinalLeavingDate IS NOT NULL
           AND (@OldLeavingDate IS NULL
                OR @FinalLeavingDate <> @OldLeavingDate)
            BEGIN
                DECLARE @MaxBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                                   FROM   dbo.BillingCharges
                                                   WHERE  EnrollmentId = @EnrollmentId
                                                          AND Status <> N'Cancelled');
                IF @MaxBilledMonth IS NOT NULL
                   AND DATEFROMPARTS(YEAR(@FinalLeavingDate), MONTH(@FinalLeavingDate), 1) < @MaxBilledMonth
                    BEGIN
                        ROLLBACK;
                        THROW 57120, 'This enrollment already has billing charges for a later month than the requested leaving date. Cancel/correct those charges first, or choose a leaving date in or after the last billed month.', 1;
                    END
            END
        IF @FinalStatus = N'Active'
           AND EXISTS (SELECT 1
                       FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                       WHERE  AdmissionId = @OldAdmissionId
                              AND Status = N'Active'
                              AND EnrollmentId <> @EnrollmentId)
            BEGIN
                ROLLBACK;
                THROW 57216, 'An Active enrollment already exists for this student. Select and correct the existing enrollment instead.', 1;
            END
        UPDATE dbo.Enrollments
        SET    Status         = @FinalStatus,
               EnrollmentDate = @FinalEnrollmentDate,
               LeavingDate    = @FinalLeavingDate,
               ClassSectionId = @FinalClassSectionId,
               UpdatedAt      = SYSDATETIME(),
               Remarks        = COALESCE (NULLIF (@Reason, N''), Remarks)
        WHERE  EnrollmentId = @EnrollmentId;
        IF @@ROWCOUNT <> 1
            BEGIN
                ROLLBACK;
                THROW 57115, 'The enrollment could not be updated.', 1;
            END
        INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
        VALUES                       (@EnrollmentId, @OldAdmissionId, N'CORRECTION', CONVERT (DATE, SYSDATETIME()), @OldClassSectionId, @FinalClassSectionId, @OldStatus, @FinalStatus, @Reason, @UserId);
        DECLARE @EditStudentStatusSynced AS BIT = 0;
        DECLARE @NewStudentStatus AS NVARCHAR (30) = @EditPriorStudentStatus;
        DECLARE @NewStudentWithdrawalDate AS DATE = @EditPriorStudentWithdrawalDate;
        IF @FinalStatus = N'Active'
           AND @EditPriorStudentStatus <> N'Active'
            BEGIN
                SET @NewStudentStatus = N'Active';
                SET @NewStudentWithdrawalDate = NULL;
                SET @EditStudentStatusSynced = 1;
            END
        ELSE
            IF @OldStatus = N'Active'
               AND @FinalStatus = N'Left'
                BEGIN
                    SET @NewStudentStatus = N'Left';
                    SET @NewStudentWithdrawalDate = @FinalLeavingDate;
                    SET @EditStudentStatusSynced = 1;
                END
        IF @EditStudentStatusSynced = 1
            BEGIN
                UPDATE dbo.Students
                SET    Status         = @NewStudentStatus,
                       WithdrawalDate = @NewStudentWithdrawalDate,
                       UpdatedAt      = SYSDATETIME()
                WHERE  AdmissionId = @OldAdmissionId;
                INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                VALUES                          (@OldAdmissionId, @EditPriorStudentStatus, @NewStudentStatus, N'Synchronized by enrollment correction.', ISNULL(@NewStudentWithdrawalDate, CONVERT (DATE, SYSDATETIME())), @Reason, @UserId);
            END
IF @WriteAudit=1
BEGIN
        DECLARE @EditOperationId AS UNIQUEIDENTIFIER = NEWID();
        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
        VALUES                     (@EditOperationId, @UserId, N'ENROLLMENT_CORRECTION', CONVERT (NVARCHAR (200), @OldAdmissionId));
        DECLARE @EditValues AS NVARCHAR (MAX) = (SELECT @EnrollmentId AS EnrollmentId,
                                                        @OldStatus AS OldStatus,
                                                        @FinalStatus AS NewStatus,
                                                        @OldEnrollmentDate AS OldEnrollmentDate,
                                                        @FinalEnrollmentDate AS NewEnrollmentDate,
                                                        @OldLeavingDate AS OldLeavingDate,
                                                        @FinalLeavingDate AS NewLeavingDate,
                                                        @OldClassSectionId AS OldClassSectionId,
                                                        @FinalClassSectionId AS NewClassSectionId,
                                                        @Reason AS Reason
                                                 FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
        EXECUTE dbo.WriteAuditLog @OperationId = @EditOperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @EnrollmentId, @Action = N'CORRECTION', @OldValues = NULL, @NewValues = @EditValues;
        IF @EditStudentStatusSynced = 1
            BEGIN
                DECLARE @EditSyncValues AS NVARCHAR (MAX) = (SELECT @OldAdmissionId AS AdmissionId,
                                                                    @EditPriorStudentStatus AS OldStatus,
                                                                    @NewStudentStatus AS NewStatus,
                                                                    @EditPriorStudentWithdrawalDate AS OldWithdrawalDate,
                                                                    @NewStudentWithdrawalDate AS NewWithdrawalDate,
                                                                    @EnrollmentId AS EnrollmentId
                                                             FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                EXECUTE dbo.WriteAuditLog @OperationId = @EditOperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @OldAdmissionId, @Action = N'STATUS_SYNC', @OldValues = NULL, @NewValues = @EditSyncValues;
            END
        DELETE dbo.AuditOperations WHERE OperationId=@CreateOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@CreateOperationId);
DELETE dbo.AuditOperations WHERE OperationId=@EditOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@EditOperationId);
END;
COMMIT TRANSACTION;
        SELECT @EnrollmentId AS EnrollmentId,
               @OldAdmissionId AS AdmissionId,
               @FinalStatus AS Status,
               CAST (1 AS BIT) AS Changed;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;
        THROW;
    END CATCH
END



GO

CREATE OR ALTER PROCEDURE [dbo].[CorrectStudentStatus]
@UserId INT, @AdmissionId INT, @NewStatus NVARCHAR (30), @NewWithdrawalDate DATE=NULL, @ClearWithdrawalDate BIT=0, @Reason NVARCHAR (500), @WriteAudit bit=1
AS
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
IF @WriteAudit=1
BEGIN
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
END;
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @NewStatus AS Status,
           @FinalWithdrawalDate AS WithdrawalDate,
           CAST (1 AS BIT) AS Changed;
END



GO

CREATE OR ALTER PROCEDURE dbo.RepairLifecycleDates
 @UserId int,@AdmissionId int,@ExpectedSnapshot nvarchar(max),@Changes nvarchar(max),@Reason nvarchar(500),
 @AllowLegacyExceptions bit=0,@KeepFinancialHistory bit=0,
 @CertificateChanges nvarchar(max)=N'[]',@UpdateWithdrawal bit=0,@NewWithdrawalDate date=NULL, @WriteAudit bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 57200,'A reason is required for this repair.',1;
 IF ISJSON(@Changes)<>1 THROW 57201,'Reload the date repair before saving.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Locked int,@Actual nvarchar(max),@AdmissionDate date,@Status nvarchar(30),@Withdrawal date;
  SELECT @Status=Status,@AdmissionDate=AdmissionDate,@Withdrawal=WithdrawalDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @Status IS NULL THROW 57201,'The student no longer exists.',1;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Locked=COUNT(*) FROM dbo.LeaveCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@Actual OUTPUT;
  IF @ExpectedSnapshot IS NULL OR @Actual<>@ExpectedSnapshot THROW 57202,'Records changed while this repair was open. Reload and review again.',1;
  DECLARE @Dates TABLE(EnrollmentId int PRIMARY KEY,EnrollmentDate date NOT NULL,LeavingDate date NULL,BillingStartDate date NULL);
  INSERT @Dates SELECT EnrollmentId,EnrollmentDate,LeavingDate,BillingStartDate FROM OPENJSON(@Changes)
   WITH(EnrollmentId int,EnrollmentDate date,LeavingDate date,BillingStartDate date);
  IF EXISTS(SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId EXCEPT SELECT EnrollmentId FROM @Dates)
    OR EXISTS(SELECT EnrollmentId FROM @Dates EXCEPT SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId)
   THROW 57203,'Review every enrollment belonging to this student together.',1;
  IF EXISTS(SELECT 1 FROM @Dates WHERE LeavingDate<EnrollmentDate)
   THROW 57204,'A leaving date cannot precede its enrollment start. Correct the start date as well if it is wrong.',1;
  IF EXISTS(SELECT 1 FROM @Dates WHERE EnrollmentDate>CONVERT(date,SYSDATETIME()) OR LeavingDate>CONVERT(date,SYSDATETIME()))
   THROW 57205,'Historical enrollment and leaving dates cannot be in the future.',1;
  IF EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId
     WHERE E.Status=N'Active' AND D.LeavingDate IS NOT NULL)
   THROW 57206,'An active enrollment cannot have a leaving date. Correct its status separately if necessary.',1;
  IF EXISTS(SELECT 1 FROM @Dates D JOIN dbo.LeaveCertificates L ON L.EnrollmentId=D.EnrollmentId WHERE D.LeavingDate IS NULL)
   THROW 57207,'An enrollment with a leave certificate needs a leaving date.',1;
  IF @AllowLegacyExceptions=0 AND (EXISTS(SELECT 1 FROM @Dates D WHERE D.EnrollmentDate<@AdmissionDate)
    OR EXISTS(SELECT 1 FROM @Dates A JOIN @Dates B ON A.EnrollmentId<B.EnrollmentId
      WHERE A.EnrollmentDate<=ISNULL(B.LeavingDate,'99991231') AND B.EnrollmentDate<=ISNULL(A.LeavingDate,'99991231'))
    OR EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
      WHERE D.EnrollmentDate<Y.StartDate OR D.EnrollmentDate>Y.EndDate OR D.LeavingDate>Y.EndDate))
   THROW 57208,'Dates overlap, precede admission, or fall outside an academic year. Correct them or explicitly acknowledge a legacy exception.',1;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @KeepFinancialHistory=0 AND EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId)
   THROW 57209,'Explicitly acknowledge that existing bills and payments retain their original dates and amounts.',1;
  DECLARE @Before nvarchar(max)=@Actual,@Changed int;
  UPDATE E SET EnrollmentDate=D.EnrollmentDate,LeavingDate=D.LeavingDate,BillingStartDate=D.BillingStartDate,UpdatedAt=SYSDATETIME()
   FROM dbo.Enrollments E JOIN @Dates D ON D.EnrollmentId=E.EnrollmentId
   WHERE E.EnrollmentDate<>D.EnrollmentDate OR ISNULL(E.LeavingDate,'19000101')<>ISNULL(D.LeavingDate,'19000101')
     OR ISNULL(E.BillingStartDate,'19000101')<>ISNULL(D.BillingStartDate,'19000101');
  SET @Changed=@@ROWCOUNT;
  DECLARE @SelectedCertificates TABLE(LeaveRecordId int PRIMARY KEY,LeaveDate date NOT NULL);
  IF ISJSON(@CertificateChanges)<>1 THROW 57210,'Reload and review the certificate corrections.',1;
  INSERT @SelectedCertificates SELECT LeaveRecordId,LeaveDate FROM OPENJSON(@CertificateChanges)
   WITH(LeaveRecordId int,LeaveDate date);
  IF EXISTS(SELECT 1 FROM @SelectedCertificates C LEFT JOIN dbo.LeaveCertificates L ON L.LeaveRecordId=C.LeaveRecordId
      WHERE L.LeaveRecordId IS NULL OR L.AdmissionId<>@AdmissionId)
   THROW 57211,'A selected certificate does not belong to this student.',1;
  IF EXISTS(SELECT 1 FROM @SelectedCertificates WHERE LeaveDate>CONVERT(date,SYSDATETIME()))
   THROW 57212,'A historical certificate leave date cannot be in the future.',1;
  IF @AllowLegacyExceptions=0 AND EXISTS(SELECT 1 FROM @SelectedCertificates C JOIN dbo.LeaveCertificates L ON L.LeaveRecordId=C.LeaveRecordId
      JOIN @Dates D ON D.EnrollmentId=L.EnrollmentId WHERE D.LeavingDate IS NULL OR C.LeaveDate<>D.LeavingDate)
   THROW 57213,'A selected certificate date differs from its enrollment leave date. Correct both, or explicitly acknowledge a legacy exception.',1;
  UPDATE L SET LeaveDate=C.LeaveDate FROM dbo.LeaveCertificates L JOIN @SelectedCertificates C ON C.LeaveRecordId=L.LeaveRecordId WHERE L.LeaveDate<>C.LeaveDate;
  DECLARE @Certificates int=@@ROWCOUNT,@FinalWithdrawal date=@Withdrawal;
  IF @UpdateWithdrawal=1
  BEGIN
   IF @NewWithdrawalDate>CONVERT(date,SYSDATETIME()) THROW 57214,'A withdrawal date cannot be in the future.',1;
   IF @Status=N'Active' AND @NewWithdrawalDate IS NOT NULL THROW 57215,'An active student cannot have a withdrawal date. Correct the student status separately.',1;
   IF @AllowLegacyExceptions=0 AND @NewWithdrawalDate IS NOT NULL AND (@NewWithdrawalDate<@AdmissionDate OR
      NOT EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE E.Status=N'Left' AND D.LeavingDate=@NewWithdrawalDate))
    THROW 57217,'The withdrawal date does not match a departure. Review the enrollment dates, or explicitly acknowledge a legacy exception.',1;
   SET @FinalWithdrawal=@NewWithdrawalDate;
   UPDATE dbo.Students SET WithdrawalDate=@FinalWithdrawal,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId
     AND ISNULL(WithdrawalDate,'19000101')<>ISNULL(@FinalWithdrawal,'19000101');
   IF @@ROWCOUNT>0
    INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,Remarks,CreatedBy)
     VALUES(@AdmissionId,@Status,@Status,N'Withdrawal date repaired',ISNULL(@FinalWithdrawal,CONVERT(date,SYSDATETIME())),@Reason,@UserId);
  END;
  INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   SELECT E.EnrollmentId,@AdmissionId,N'CORRECTION',CONVERT(date,SYSDATETIME()),E.ClassSectionId,E.ClassSectionId,E.Status,E.Status,
    LEFT(N'Lifecycle dates reviewed: '+@Reason,500),@UserId FROM dbo.Enrollments E JOIN @Dates D ON D.EnrollmentId=E.EnrollmentId
    WHERE EXISTS(SELECT 1 FROM OPENJSON(@Before,'$.Enrollments') WITH(EnrollmentId int,EnrollmentDate date,LeavingDate date,BillingStartDate date) O
      WHERE O.EnrollmentId=E.EnrollmentId AND (O.EnrollmentDate<>D.EnrollmentDate OR ISNULL(O.LeavingDate,'19000101')<>ISNULL(D.LeavingDate,'19000101')
        OR ISNULL(O.BillingStartDate,'19000101')<>ISNULL(D.BillingStartDate,'19000101')));
IF @WriteAudit=1
BEGIN
  DECLARE @Operation uniqueidentifier=NEWID(),@After nvarchar(max),@Audit nvarchar(max);
  EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@After OUTPUT;
  SET @Audit=(SELECT JSON_QUERY(@After) Records,@Reason Reason,@AllowLegacyExceptions LegacyException,
    JSON_QUERY(@CertificateChanges) SelectedCertificateCorrections,@UpdateWithdrawal WithdrawalCorrectionSelected,
    N'Existing charges, payments, original history entries and creation/issuance timestamps retained.' FinancialHistory FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'LIFECYCLE_DATE_REPAIR',CONVERT(nvarchar(200),@AdmissionId));
  EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@UserId,@TableName=N'Enrollments',@RecordId=NULL,@Action=N'CORRECTION',@OldValues=@Before,@NewValues=@Audit;
END;
  COMMIT;
  SELECT @Changed EnrollmentsChanged,@Certificates CertificatesUpdated,@FinalWithdrawal WithdrawalDate;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;

GO

CREATE OR ALTER PROCEDURE dbo.ReconcileEnrollmentDates
 @UserId int, @EnrollmentId int, @EnrollmentDate date, @BillingStartDate date,
 @Reason nvarchar(500), @AllowLegacyExceptions bit=0,
 @Preview bit=1, @ExpectedSnapshot nvarchar(max)=NULL, @WriteAudit bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 57400,'Enter a reason for this correction.',1;
 IF @EnrollmentDate IS NULL OR @EnrollmentDate>CONVERT(date,SYSDATETIME())
  THROW 57401,'Enter an enrollment start that is not in the future.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @AdmissionId int,@Ready bit,@Return datetime2,@YearStart date,@YearEnd date,@FirstAdmission date,
   @Status nvarchar(30),@ClassSectionId int,@Locked int,@LockResult int;
  SELECT @AdmissionId=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  IF @AdmissionId IS NULL THROW 57402,'The enrollment no longer exists.',1;
  SELECT @FirstAdmission=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Ready=E.BillingReady,@Return=E.ReturnConfirmedAt,@Status=E.Status,@ClassSectionId=E.ClassSectionId,
   @YearStart=Y.StartDate,@YearEnd=Y.EndDate
   FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@EnrollmentId;
  IF @Status<>N'Active' THROW 57403,'Closed enrollments are view only.',1;
  IF @Ready=1 AND @BillingStartDate IS NULL THROW 57404,'Billing start is required for a confirmed enrollment.',1;
  IF @BillingStartDate<@EnrollmentDate OR @BillingStartDate>@YearEnd
   THROW 57405,'Billing start must be on or after enrollment start and within the academic year.',1;
  IF @AllowLegacyExceptions=0 AND (@EnrollmentDate<@FirstAdmission OR @EnrollmentDate<@YearStart OR @EnrollmentDate>@YearEnd
   OR EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@EnrollmentId
     AND ISNULL(LeavingDate,'99991231')>@EnrollmentDate))
   THROW 57406,'The start precedes admission, overlaps another enrollment, or falls outside its academic year.',1;
  DECLARE @LockResource nvarchar(255)=N'FMSC.GenerateEnrollmentCharges.'+CONVERT(nvarchar(20),@EnrollmentId);
  EXEC @LockResult=sys.sp_getapplock @Resource=@LockResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 57407,'Billing is being changed elsewhere. Reload and try again.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId WHERE B.EnrollmentId=@EnrollmentId;
  DECLARE @Snapshot nvarchar(max)=(SELECT
   JSON_QUERY((SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Enrollments,
   JSON_QUERY((SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Assignments,
   JSON_QUERY((SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES)) Charges,
   JSON_QUERY((SELECT A.* FROM dbo.FeePaymentAllocations A JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
    WHERE B.EnrollmentId=@EnrollmentId ORDER BY A.FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES)) Allocations
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  IF @Preview=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot)
   THROW 57408,'Records changed after the preview. Review the reconciliation again.',1;
  DECLARE @Opening date=DATEFROMPARTS(YEAR(@BillingStartDate),MONTH(@BillingStartDate),1),
   @AsOf date=CASE WHEN CONVERT(date,SYSDATETIME())<@YearEnd THEN CONVERT(date,SYSDATETIME()) ELSE @YearEnd END;
  -- Retain valid months already posted ahead of today within this academic year.
  SELECT @AsOf=CASE WHEN MAX(BillingMonth)>@AsOf THEN MAX(BillingMonth) ELSE @AsOf END
   FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId AND ChargeKind=N'Monthly' AND Status<>N'Cancelled' AND BillingMonth<=@YearEnd;
  DECLARE @CorrectedReturn datetime2=CASE WHEN @Return IS NOT NULL AND @Return<@EnrollmentDate THEN CONVERT(datetime2,@EnrollmentDate) ELSE @Return END;
  DECLARE @Expected TABLE(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,ChargeKind nvarchar(20),
   ChargeDate date,GrossAmount decimal(18,2),DiscountAmount decimal(18,2));
  IF @Ready=1
  BEGIN
   ;WITH Months AS (
    SELECT @Opening BillingMonth WHERE @Opening<=@AsOf
    UNION ALL SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE DATEADD(month,1,BillingMonth)<=@AsOf)
   INSERT @Expected
   SELECT F.StudentFeeAssignmentId,F.FeeTypeId,M.BillingMonth,N'Monthly',M.BillingMonth,F.Amount,F.DiscountAmount
    FROM Months M JOIN dbo.StudentFeeAssignments F ON F.EnrollmentId=@EnrollmentId
    WHERE F.CancelledAt IS NULL AND F.Frequency=N'Monthly'
     AND (F.EffectiveFrom IS NULL OR M.BillingMonth>=F.EffectiveFrom)
     AND (F.EffectiveTo IS NULL OR M.BillingMonth<=F.EffectiveTo) OPTION(MAXRECURSION 1200);
   INSERT @Expected
   SELECT F.StudentFeeAssignmentId,F.FeeTypeId,@Opening,N'Annual',COALESCE(CONVERT(date,@CorrectedReturn),@EnrollmentDate),
    COALESCE(B.GrossAmount,F.Amount),COALESCE(B.DiscountAmount,F.DiscountAmount)
    FROM dbo.StudentFeeAssignments F OUTER APPLY(SELECT TOP(1) GrossAmount,DiscountAmount FROM dbo.BillingCharges
     WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=F.FeeTypeId AND ChargeKind=N'Annual' AND Status<>N'Cancelled' ORDER BY BillingChargeId) B
    WHERE F.EnrollmentId=@EnrollmentId AND F.CancelledAt IS NULL AND F.Frequency=N'Yearly'
     AND (F.EffectiveFrom IS NULL OR @Opening>=F.EffectiveFrom) AND (F.EffectiveTo IS NULL OR @Opening<=F.EffectiveTo);
  END;
  IF EXISTS(SELECT 1 FROM @Expected GROUP BY FeeTypeId,BillingMonth,ChargeKind HAVING COUNT(*)>1)
   THROW 57409,'Fee-assignment periods overlap. Correct the assignments before reconciling.',1;
  DECLARE @Cancel TABLE(BillingChargeId bigint PRIMARY KEY);
  INSERT @Cancel SELECT B.BillingChargeId FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=@EnrollmentId AND B.Status<>N'Cancelled' AND B.ChargeKind IN(N'Monthly',N'Annual')
    AND NOT EXISTS(SELECT 1 FROM @Expected X WHERE X.FeeTypeId=B.FeeTypeId AND X.BillingMonth=B.BillingMonth AND X.ChargeKind=B.ChargeKind
     AND (B.ChargeKind<>N'Annual' OR B.ChargeDate=X.ChargeDate));
  DECLARE @Blocked int=(SELECT COUNT(*) FROM @Cancel C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId
   WHERE B.Status<>N'Open' OR EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations WHERE BillingChargeId=B.BillingChargeId));
  DECLARE @Add TABLE(StudentFeeAssignmentId int,FeeTypeId int,BillingMonth date,ChargeKind nvarchar(20),ChargeDate date,
   GrossAmount decimal(18,2),DiscountAmount decimal(18,2));
  INSERT @Add SELECT X.* FROM @Expected X WHERE NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=X.FeeTypeId AND B.ChargeKind=X.ChargeKind
    AND B.BillingMonth=X.BillingMonth AND B.Status<>N'Cancelled' AND NOT EXISTS(SELECT 1 FROM @Cancel C WHERE C.BillingChargeId=B.BillingChargeId));
  DECLARE @CancelCount int=(SELECT COUNT(*) FROM @Cancel),@AddCount int=(SELECT COUNT(*) FROM @Add),
   @CancelledAmount decimal(18,2)=COALESCE((SELECT SUM(B.NetAmount) FROM @Cancel C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId),0),
   @AddedAmount decimal(18,2)=COALESCE((SELECT SUM(GrossAmount-DiscountAmount) FROM @Add),0);
  IF @Preview=1
  BEGIN
   COMMIT;
   SELECT @Snapshot Snapshot,@CancelCount CancelCount,@AddCount AddCount,@CancelledAmount CancelledAmount,@AddedAmount AddedAmount,
    @Blocked BlockedCount,@AsOf ThroughDate,@CorrectedReturn ReturnDate;
   RETURN;
  END;
  IF @Blocked>0 THROW 57410,'Affected charges have payment history. Reverse or refund those payments in Student Fees before reconciling; no records were changed.',1;
  UPDATE dbo.Enrollments SET EnrollmentDate=@EnrollmentDate,BillingStartDate=@BillingStartDate,
   ReturnConfirmedAt=@CorrectedReturn,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@EnrollmentId;
  UPDATE B SET Status=N'Cancelled',Remarks=LEFT(COALESCE(B.Remarks+N' | ',N'')+N'Enrollment reconciliation: '+@Reason,1000)
   FROM dbo.BillingCharges B JOIN @Cancel C ON C.BillingChargeId=B.BillingChargeId;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
   SELECT StudentFeeAssignmentId,@EnrollmentId,@AdmissionId,FeeTypeId,BillingMonth,ChargeDate,ChargeDate,GrossAmount,DiscountAmount,ChargeKind,
    LEFT(N'Enrollment reconciliation: '+@Reason,1000) FROM @Add;
  INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   VALUES(@EnrollmentId,@AdmissionId,N'CORRECTION',CONVERT(date,SYSDATETIME()),@ClassSectionId,@ClassSectionId,@Status,@Status,
    LEFT(N'Dates and billing reconciled: '+@Reason,500),@UserId);
IF @WriteAudit=1
BEGIN
  DECLARE @Operation uniqueidentifier=NEWID(),@After nvarchar(max)=(SELECT @EnrollmentId EnrollmentId,@EnrollmentDate EnrollmentDate,
   @BillingStartDate BillingStartDate,@CorrectedReturn ReturnConfirmedAt,@CancelCount CancelledCharges,@AddCount AddedCharges,
   @Reason Reason,@AllowLegacyExceptions LegacyException,
   JSON_QUERY((SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH)) Charges
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'ENROLLMENT_RECONCILIATION',CONVERT(nvarchar(200),@EnrollmentId));
  DECLARE @Record nvarchar(200)=CONVERT(nvarchar(200),@EnrollmentId);
  EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@UserId,@TableName=N'Enrollments',@RecordId=@Record,@Action=N'CORRECTION',@OldValues=@Snapshot,@NewValues=@After;
END;
  COMMIT;
  SELECT @CancelCount CancelledCharges,@AddCount AddedCharges;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;

GO
