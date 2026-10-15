-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51710, 'The selected student was not found.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts
               WHERE  DiscountAmount < 0)
        THROW 51711, 'A fee discount cannot be negative. Nothing was changed.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts AS D
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.StudentFeeAssignments AS SFA
                                         LEFT OUTER JOIN
                                         dbo.Enrollments AS E
                                         ON E.EnrollmentId = SFA.EnrollmentId
                                  WHERE  SFA.StudentFeeAssignmentId = D.StudentFeeAssignmentId
                                         AND (SFA.AdmissionId = @AdmissionId
                                              OR E.AdmissionId = @AdmissionId)))
        THROW 51712, 'A fee discount in this save does not belong to the student being saved. Nothing was changed.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Pending') EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer'; ELSE EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        DECLARE @StoredStatus AS NVARCHAR (30), @StoredAdmissionDate AS DATE, @StoredWithdrawalDate AS DATE;
        SELECT @StoredStatus = Status,
               @StoredAdmissionDate = AdmissionDate,
               @StoredWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @AdmissionId;
        IF @StoredStatus IS NULL
            THROW 51710, 'The selected student was not found.', 1;
        IF @CreateFirstEnrollment = 1
            BEGIN
                EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
                IF EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @AdmissionId)
                    THROW 51720, 'This student already has an enrollment. Reload the record and use enrollment corrections.', 1;
                IF @PlacementCampusId IS NULL
                   OR @PlacementAcademicYearId IS NULL
                   OR @PlacementClassSectionId IS NULL
                    THROW 51721, 'Confirm the campus, academic year, class and section.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.Campuses
                               WHERE  CampusId = @PlacementCampusId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.AcademicYears
                                  WHERE  AcademicYearId = @PlacementAcademicYearId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.CampusClassSections
                                  WHERE  ClassSectionId = @PlacementClassSectionId
                                         AND CampusId = @PlacementCampusId)
                    THROW 51721, 'The verified placement is not a valid campus, year and class section.', 1;
                IF @PlacementEnrollmentDate IS NULL
                   OR @PlacementEnrollmentDate < @StoredAdmissionDate
                   OR @PlacementEnrollmentDate > CONVERT (DATE, SYSDATETIME())
                    THROW 51722, 'Enter the confirmed enrollment date, on or after admission and not in the future.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.AcademicYears
                               WHERE  AcademicYearId = @PlacementAcademicYearId
                                      AND @PlacementEnrollmentDate BETWEEN StartDate AND EndDate)
                    THROW 51722, 'The enrollment date must fall within the selected academic year.', 1;
                IF @PlacementStatus IS NULL
                   OR @PlacementStatus NOT IN (N'Active', N'Left')
                    THROW 51723, 'Choose Active or Left for the verified enrollment.', 1;
                IF @StoredStatus = N'Left'
                   AND @PlacementStatus <> N'Left'
                    THROW 51723, 'An already-left student needs a historical Left enrollment. Use Re-Admit to return them.', 1;
                IF @PlacementStatus = N'Active'
                   AND (@PlacementLeavingDate IS NOT NULL
                        OR @StoredWithdrawalDate IS NOT NULL)
                    THROW 51724, 'An active enrollment cannot have a withdrawal date.', 1;
                IF @StoredWithdrawalDate IS NOT NULL
                   AND @PlacementLeavingDate IS NOT NULL
                   AND @StoredWithdrawalDate <> @PlacementLeavingDate
                    THROW 51724, 'The confirmed withdrawal date differs from the stored date. Correct the stored date through the status workflow first.', 1;
                SET @PlacementLeavingDate = COALESCE (@PlacementLeavingDate, @StoredWithdrawalDate);
                IF @PlacementLeavingDate IS NOT NULL
                   AND (@PlacementLeavingDate < @PlacementEnrollmentDate
                        OR @PlacementLeavingDate > CONVERT (DATE, SYSDATETIME()))
                    THROW 51724, 'Withdrawal date must be on or after enrollment and not in the future.', 1;
                IF EXISTS (SELECT 1
                           FROM   @Discounts)
                    THROW 51725, 'First placement cannot include discounts from another enrollment.', 1;
            END
        IF @AdmissionNumber IS NOT NULL AND @CompleteRegistration=0
            EXECUTE dbo.SetStudentAdmissionNumber @UserId, @AdmissionId, @AdmissionNumber;
        EXECUTE dbo.SaveStudentDetails @UserId = @UserId, @AdmissionId = @AdmissionId, @StudentName = @StudentName, @FatherName = @FatherName, @DateOfBirth = @DateOfBirth, @Gender = @Gender, @BFormNumber = @BFormNumber, @Religion = @Religion, @BloodGroup = @BloodGroup, @ContactNumber = @ContactNumber, @AlternateContactNumber = @AlternateContactNumber, @Address = @Address, @PreviousSchool = @PreviousSchool, @PlaceOfBirth = @PlaceOfBirth, @FatherOccupation = @FatherOccupation, @Disability = @Disability, @UsesTransport = @UsesTransport, @BiseEnrolmentNumber = @BiseEnrolmentNumber, @CasteTribe = @CasteTribe;
        IF EXISTS (SELECT 1
                   FROM   @Discounts)
            BEGIN
                DECLARE @StudentFeeAssignmentId AS INT, @DiscountAmount AS DECIMAL (12, 2);
                DECLARE DiscountRows CURSOR LOCAL FAST_FORWARD
                    FOR SELECT   StudentFeeAssignmentId,
                                 DiscountAmount
                        FROM     @Discounts
                        ORDER BY StudentFeeAssignmentId;
                OPEN DiscountRows;
                FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                WHILE @@FETCH_STATUS = 0
                    BEGIN
                        EXECUTE dbo.SaveStudentFeeDiscount @UserId = @UserId, @StudentFeeAssignmentId = @StudentFeeAssignmentId, @DiscountAmount = @DiscountAmount;
                        FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                    END
                CLOSE DiscountRows;
                DEALLOCATE DiscountRows;
            END
        IF @CreateFirstEnrollment = 1
            BEGIN
                DECLARE @NewEnrollmentId AS INT, @PlacementOperationId AS UNIQUEIDENTIFIER = NEWID();
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, UsesTransport, Remarks, BillingStartDate, BillingReady)
                VALUES                 (@AdmissionId, @PlacementAcademicYearId, @PlacementClassSectionId, @PlacementEnrollmentDate, @PlacementLeavingDate, @PlacementStatus, @PlacementUsesTransport, N'First placement verified from physical school record on Admission form; no fees generated.', NULL, 0);
                SET @NewEnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, NewClassSectionId, NewStatus, Remarks, UserId)
                VALUES                       (@NewEnrollmentId, @AdmissionId, N'VERIFIED_FIRST_PLACEMENT', CONVERT (DATE, SYSDATETIME()), @PlacementClassSectionId, @PlacementStatus, N'Confirmed from physical school record.', @UserId);
                IF @StoredStatus <> @PlacementStatus
                   OR ISNULL(@StoredWithdrawalDate, '00010101') <> ISNULL(@PlacementLeavingDate, '00010101')
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = @PlacementStatus,
                               WithdrawalDate = @PlacementLeavingDate,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @StoredStatus, @PlacementStatus, N'Physical record verification', COALESCE (@PlacementLeavingDate, @PlacementEnrollmentDate), N'First verified enrollment recorded on Admission form.', @UserId);
                    END
                INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                VALUES                     (@PlacementOperationId, @UserId, N'VERIFIED_FIRST_PLACEMENT', CONVERT (NVARCHAR (200), @AdmissionId));
                DECLARE @PlacementNewValues AS NVARCHAR (MAX), @PlacementRecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @NewEnrollmentId);
                SET @PlacementNewValues = (SELECT @AdmissionId AS AdmissionId,
                                                  @NewEnrollmentId AS EnrollmentId,
                                                  @PlacementAcademicYearId AS AcademicYearId,
                                                  @PlacementClassSectionId AS ClassSectionId,
                                                  @PlacementEnrollmentDate AS EnrollmentDate,
                                                  @PlacementStatus AS Status,
                                                  @PlacementLeavingDate AS LeavingDate,
                                                  CAST (0 AS BIT) AS BillingReady
                                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                EXECUTE dbo.WriteAuditLog @OperationId = @PlacementOperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @PlacementRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @PlacementNewValues;
                IF @StoredStatus <> @PlacementStatus
                   OR ISNULL(@StoredWithdrawalDate, '00010101') <> ISNULL(@PlacementLeavingDate, '00010101')
                    BEGIN
                        DECLARE @StudentOld AS NVARCHAR (MAX), @StudentNew AS NVARCHAR (MAX), @StudentRecord AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @AdmissionId);
                        SET @StudentOld = (SELECT @StoredStatus AS Status,
                                                  @StoredWithdrawalDate AS WithdrawalDate
                                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        SET @StudentNew = (SELECT @PlacementStatus AS Status,
                                                  @PlacementLeavingDate AS WithdrawalDate
                                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        EXECUTE dbo.WriteAuditLog @OperationId = @PlacementOperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @StudentRecord, @Action = N'UPDATE', @OldValues = @StudentOld, @NewValues = @StudentNew;
                    END
            END
        DELETE dbo.AuditOperations WHERE OperationId=@PlacementOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@PlacementOperationId);
IF @CompleteRegistration=1 EXEC dbo.CompleteQuickAdmission @UserId,@AdmissionId,@AdmissionNumber;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
    SELECT @AdmissionId AS AdmissionId;
END
