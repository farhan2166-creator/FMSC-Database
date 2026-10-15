-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF NULLIF (LTRIM(RTRIM(@AdmissionNumber)), '') IS NULL
        THROW 51726, 'Enter the admission number from the physical register.', 1;
    IF @VerifiedStatus IS NULL
       OR @VerifiedStatus NOT IN (N'Active', N'Left')
        THROW 51723, 'Confirm Active or Left for the historical record.', 1;
    IF @VerifiedEnrollmentDate IS NULL
       OR @VerifiedEnrollmentDate < @AdmissionDate
       OR @VerifiedEnrollmentDate > CONVERT (DATE, SYSDATETIME())
        THROW 51722, 'Confirm the enrollment date on or after admission, not in the future.', 1;
    IF @VerifiedWithdrawalDate IS NOT NULL
       AND (@VerifiedStatus <> N'Left'
            OR @VerifiedWithdrawalDate < @VerifiedEnrollmentDate
            OR @VerifiedWithdrawalDate > CONVERT (DATE, SYSDATETIME()))
        THROW 51724, 'Confirm a withdrawal date on or after enrollment for a Left student.', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedFeeTypeIds)
       OR EXISTS (SELECT 1
                  FROM   @AdmissionDiscounts)
        THROW 51725, 'Historical records cannot generate fees or discounts.', 1;
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @VerifiedEnrollmentDate < @YearStartDate
       OR @VerifiedEnrollmentDate > @YearEndDate
        THROW 51022, 'The verified enrollment date must fall within the selected academic year''s date range.', 1;
    DECLARE @BillingStartDate AS DATE = NULL;
    BEGIN TRANSACTION;
    EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, @VerifiedStatus, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WHERE CompletedAt IS NULL) UPDATE dbo.Students SET LegacySetupPending=1 WHERE AdmissionId=@AdmissionId;
    UPDATE dbo.Students
    SET    WithdrawalDate = @VerifiedWithdrawalDate
    WHERE  AdmissionId = @AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, LeavingDate, BillingReady)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @VerifiedEnrollmentDate, @BillingStartDate, @VerifiedStatus, @UsesTransport, N'Historical admission verified from physical school record; no fees generated.', @VerifiedWithdrawalDate, 0);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, @VerifiedStatus, N'Historical first enrollment confirmed from physical record', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, @VerifiedStatus, N'Admission', @AdmissionDate, N'Historical student status confirmed from physical record', @UserId);
    DECLARE @OperationId AS UNIQUEIDENTIFIER;
    SET @OperationId = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ADMISSION', CAST (@AdmissionId AS NVARCHAR (200)));
    DECLARE @AuditRecordId AS NVARCHAR (200);
    SET @AuditRecordId = CAST (@AdmissionId AS NVARCHAR (200));
    DECLARE @AuditValues AS NVARCHAR (MAX);
    SELECT @AuditValues = (SELECT @AdmissionId AS AdmissionId,
                                  @EnrollmentId AS EnrollmentId,
                                  @RegistrationNumber AS RegistrationNumber,
                                  @AdmissionDate AS AdmissionDate,
                                  @VerifiedEnrollmentDate AS EnrollmentDate,
                                  @VerifiedWithdrawalDate AS WithdrawalDate,
                                  @AdmissionNumber AS AdmissionNumber,
                                  @BillingStartDate AS BillingStartDate,
                                  @AdmissionCampusId AS AdmissionCampusId,
                                  @AcademicYearId AS AcademicYearId,
                                  @ClassSectionId AS ClassSectionId,
                                  @VerifiedStatus AS Status
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    SET @AuditRecordId = CONVERT (NVARCHAR (200), @EnrollmentId);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber;
END
