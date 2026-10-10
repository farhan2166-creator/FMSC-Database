SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- CorrectEnrollmentStatus
CREATE   PROCEDURE dbo.CorrectEnrollmentStatus
@UserId INT, @EnrollmentId INT=NULL, @AdmissionId INT=NULL, @NewStatus NVARCHAR (30)=NULL, @NewEnrollmentDate DATE=NULL, @NewLeavingDate DATE=NULL, @ClearLeavingDate BIT=0, @NewClassSectionId INT=NULL, @AcademicYearId INT=NULL, @Reason NVARCHAR (500)
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
