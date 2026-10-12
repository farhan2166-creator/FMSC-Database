-- v1 baseline: existing recorded placements stay confirmed. Future placements
-- remain drafts until the operator explicitly confirms them in Admission.
IF COL_LENGTH(N'dbo.Enrollments',N'PlacementConfirmedAt') IS NULL
BEGIN
 ALTER TABLE dbo.Enrollments ADD PlacementConfirmedAt datetime2(7) NULL;
 EXEC(N'UPDATE dbo.Enrollments SET PlacementConfirmedAt=COALESCE(ReturnConfirmedAt,CreatedAt);');
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardConfirmedAcademicYear ON dbo.Enrollments AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.EnrollmentId=I.EnrollmentId
  WHERE D.PlacementConfirmedAt IS NOT NULL AND (I.AcademicYearId<>D.AcademicYearId OR I.PlacementConfirmedAt IS NULL))
  THROW 59640,'Academic year is confirmed. Use Promotion for an actual move to another academic year.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ConfirmStudentPlacement
 @UserId int,@EnrollmentId int,@ExpectedSnapshot varchar(64)=NULL,@Preview bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer';
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @AdmissionId int,@YearId int,@Placement int,@Start date,@BillingStart date,@End date,@Status nvarchar(30),@Confirmed datetime2(7),@Locked int;
  SELECT @AdmissionId=AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @YearId=AcademicYearId,@Placement=ClassSectionId,@Start=EnrollmentDate,@BillingStart=COALESCE(BillingStartDate,EnrollmentDate),@End=LeavingDate,
   @Status=Status,@Confirmed=PlacementConfirmedAt FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
  IF @AdmissionId IS NULL THROW 59641,'The enrollment no longer exists.',1;
  IF @Confirmed IS NOT NULL THROW 59642,'This placement is already confirmed. Reload Admission.',1;
  DECLARE @YearStart date,@YearEnd date;
  SELECT @YearStart=StartDate,@YearEnd=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@YearId;
  IF @Start<@YearStart OR (@YearEnd IS NOT NULL AND @Start>@YearEnd)
   THROW 59643,'Correct the enrollment start or academic year before confirming placement.',1;
  IF @BillingStart<@Start OR (@YearEnd IS NOT NULL AND @BillingStart>@YearEnd) OR (@End IS NOT NULL AND @BillingStart>@End)
   THROW 59644,'Correct billing start before confirming placement.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  DECLARE @Snapshot varchar(64),@Json nvarchar(max)=CONCAT(
   (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@YearId FOR JSON PATH,INCLUDE_NULL_VALUES));
  SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Json),2);
  IF @Preview=1
  BEGIN
   SELECT @Snapshot Snapshot,@Status Status,@BillingStart BillingStart,@Start EnrollmentStart,
    (SELECT CONCAT(Y.YearName,N' · ',C.CampusName,N' · ',CL.ClassName,N' · ',S.SectionName)
     FROM dbo.CampusClassSections CS JOIN dbo.Campuses C ON C.CampusId=CS.CampusId
     JOIN dbo.Classes CL ON CL.ClassId=CS.ClassId JOIN dbo.Sections S ON S.SectionId=CS.SectionId
     CROSS JOIN dbo.AcademicYears Y WHERE CS.ClassSectionId=@Placement AND Y.AcademicYearId=@YearId) PlacementName,
    (SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL) FeeCount,
    (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL) FeeTotal;
   COMMIT; RETURN;
  END;
  IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59645,'Placement or fees changed. Review confirmation again.',1;
  DECLARE @Operation uniqueidentifier=NEWID(),@Record nvarchar(200)=CONVERT(nvarchar(200),@EnrollmentId),@Old nvarchar(max),@New nvarchar(max);
  SET @Old=(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
  UPDATE dbo.Enrollments SET PlacementConfirmedAt=SYSDATETIME(),BillingReady=CASE WHEN @Status=N'Active' THEN 1 ELSE BillingReady END,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@EnrollmentId;
  -- Closed historical records are confirmed as history, without new billing.
  IF @Status=N'Active' EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
  SET @New=(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
  INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'CONFIRM_PLACEMENT',@Record);
  EXEC dbo.WriteAuditLog @Operation,@UserId,N'Enrollments',@Record,N'UPDATE',@Old,@New;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO

-- AdmitStudent
CREATE OR ALTER PROCEDURE dbo.AdmitStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts)
        EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
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
    IF @AdmissionDate < @YearStartDate
       OR @AdmissionDate > @YearEndDate
        THROW 51022, 'The admission date must fall within the selected academic year''s date range.', 1;
    DECLARE @MissingMandatoryFees AS NVARCHAR (1000);
    SELECT @MissingMandatoryFees = STRING_AGG(FT.FeeTypeName, N', ')
    FROM   dbo.FeeTypes AS FT
    WHERE  FT.IsMandatory = 1
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                           WHERE  FC.CampusId = @AdmissionCampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.FeeTypeId = FT.FeeTypeId);
    IF @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            THROW 51019, @MandatoryFeeErrorMsg, 1;
        END
    DECLARE @CutoffDay AS INT;
    SELECT TOP (1) @CutoffDay = FeeProrationCutoffDay
    FROM   dbo.SchoolProfile;
    DECLARE @BillingStartDate AS DATE;
    SET @BillingStartDate = CASE WHEN @CutoffDay BETWEEN 1 AND 28
                                      AND DAY(@AdmissionDate) >= @CutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(@AdmissionDate), MONTH(@AdmissionDate), 1)) ELSE @AdmissionDate END;
    BEGIN TRANSACTION;
    EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, N'Active', NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, BillingReady)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @AdmissionDate, @BillingStartDate, N'Active', @UsesTransport, N'Created by admission',0);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT @EnrollmentId,
           @AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           N'Created by admission'
    FROM   dbo.FeeConfigurations AS FC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FC.CampusId = @AdmissionCampusId
           AND FC.ClassId = @ClassId
           AND (FT.IsMandatory = 1
                OR (FT.IsTransportLinked = 1
                    AND @UsesTransport = 1)
                OR FC.FeeTypeId IN (SELECT FeeTypeId
                                    FROM   @SelectedFeeTypeIds));
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts AS D
                      LEFT OUTER JOIN
                      dbo.StudentFeeAssignments AS SFA
                      ON SFA.EnrollmentId = @EnrollmentId
                         AND SFA.FeeTypeId = D.FeeTypeId
               WHERE  SFA.StudentFeeAssignmentId IS NULL
                      OR D.DiscountAmount > SFA.Amount)
        THROW 51024, 'A fee discount is invalid or exceeds the assigned fee.', 1;
    UPDATE SFA
    SET    DiscountAmount = D.DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           @AdmissionDiscounts AS D
           ON D.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId;
    -- Opening charges are posted only by explicit Confirm Placement.
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, N'Active', N'Initial enrollment created by admission', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, N'Active', N'Admission', @AdmissionDate, N'Initial student status', @UserId);
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
                                  @BillingStartDate AS BillingStartDate,
                                  @AdmissionCampusId AS AdmissionCampusId,
                                  @AcademicYearId AS AcademicYearId,
                                  @ClassSectionId AS ClassSectionId,
                                  N'Active' AS Status
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber;
END



GO

-- ConfirmPromotedAdmission
CREATE OR ALTER PROCEDURE dbo.ConfirmPromotedAdmission
@UserId INT, @EnrollmentId INT, @ReturnDate DATE, @UsesTransport BIT, @FeeChoices dbo.ReturnFeeChoiceType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @ReturnDate IS NULL
       OR @ReturnDate > CONVERT (DATE, SYSDATETIME())
        THROW 53010, 'Enter a valid return date that is not in the future.', 1;
    IF EXISTS (SELECT 1
               FROM   @FeeChoices
               WHERE  Amount < 0
                      OR DiscountAmount < 0
                      OR DiscountAmount > Amount)
        THROW 53011, 'Each fee amount and discount must be valid.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT, @AcademicYearId AS INT, @ClassSectionId AS INT, @EnrollmentDate AS DATE, @BillingReady AS BIT, @YearEnd AS DATE, @CampusId AS INT, @ClassId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @AcademicYearId = E.AcademicYearId,
           @ClassSectionId = E.ClassSectionId,
           @EnrollmentDate = E.EnrollmentDate,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
       OR @BillingReady <> 0
        THROW 53012, 'This promoted enrollment is not awaiting return confirmation.', 1;
    SELECT @YearEnd = AY.EndDate
    FROM   dbo.AcademicYears AS AY
    WHERE  AY.AcademicYearId = @AcademicYearId;
    IF @ReturnDate < @EnrollmentDate
       OR @ReturnDate > @YearEnd
        THROW 53013, 'Return date must be within the promoted academic year.', 1;
    SELECT @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments AS SFA
                      INNER JOIN
                      dbo.FeeTypes AS FT
                      ON FT.FeeTypeId = SFA.FeeTypeId
               WHERE  SFA.EnrollmentId = @EnrollmentId
                      AND SFA.CancelledAt IS NULL
                      AND FT.IsMandatory = 1
                      AND NOT EXISTS (SELECT 1
                                      FROM   @FeeChoices AS C
                                      WHERE  C.FeeTypeId = SFA.FeeTypeId))
       OR EXISTS (SELECT 1
                  FROM   @FeeChoices AS C
                  WHERE  NOT EXISTS (SELECT 1
                                     FROM   dbo.StudentFeeAssignments AS SFA
                                            INNER JOIN
                                            dbo.FeeTypes AS FT
                                            ON FT.FeeTypeId = SFA.FeeTypeId
                                     WHERE  SFA.EnrollmentId = @EnrollmentId
                                            AND SFA.CancelledAt IS NULL
                                            AND FT.IsMandatory = 1
                                            AND SFA.FeeTypeId = C.FeeTypeId))
        THROW 53014, 'Review every mandatory class fee before confirming return.', 1;
    UPDATE SFA
    SET    Amount         = C.Amount,
           DiscountAmount = C.DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           @FeeChoices AS C
           ON C.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL;
    IF @UsesTransport = 1
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                                  INNER JOIN
                                  dbo.FeeTypes AS FT
                                  ON FT.FeeTypeId = FC.FeeTypeId
                           WHERE  FC.CampusId = @CampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.Frequency = N'Monthly'
                                  AND FT.IsTransportLinked = 1)
                THROW 53015, 'Transport cannot be selected without a monthly class fee.', 1;
            INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
            SELECT @EnrollmentId,
                   @AdmissionId,
                   FC.FeeTypeId,
                   FC.Amount,
                   0,
                   FC.Frequency,
                   N'Chosen at return admission'
            FROM   dbo.FeeConfigurations AS FC
                   INNER JOIN
                   dbo.FeeTypes AS FT
                   ON FT.FeeTypeId = FC.FeeTypeId
            WHERE  FC.CampusId = @CampusId
                   AND FC.ClassId = @ClassId
                   AND FT.IsTransportLinked = 1
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.StudentFeeAssignments AS SFA
                                   WHERE  SFA.EnrollmentId = @EnrollmentId
                                          AND SFA.FeeTypeId = FC.FeeTypeId
                                          AND SFA.EffectiveTo IS NULL);
        END
    DECLARE @CutoffDay AS INT = (SELECT TOP (1) FeeProrationCutoffDay
                                 FROM   dbo.SchoolProfile);
    DECLARE @BillingStartDate AS DATE = CASE WHEN @CutoffDay BETWEEN 1 AND 28
                                                  AND DAY(@ReturnDate) >= @CutoffDay THEN DATEADD(month, 1, DATEFROMPARTS(YEAR(@ReturnDate), MONTH(@ReturnDate), 1)) ELSE @ReturnDate END;
    UPDATE dbo.Enrollments
    SET    BillingReady      = 1, PlacementConfirmedAt=COALESCE(PlacementConfirmedAt,SYSDATETIME()),
           ReturnConfirmedAt = CONVERT (DATETIME2 (0), @ReturnDate),
           BillingStartDate  = @BillingStartDate,
           UsesTransport     = @UsesTransport
    WHERE  EnrollmentId = @EnrollmentId;
    EXECUTE dbo.PostEnrollmentOpeningCharges @EnrollmentId = @EnrollmentId;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ReturnConfirmed', @ReturnDate, @ClassSectionId, @ClassSectionId, N'Active', N'Active', N'Admission confirmed after promotion', @UserId);
    COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @BillingStartDate AS BillingStartDate;
END



GO
CREATE OR ALTER PROCEDURE dbo.GetStudentEnrollmentsForLifecycle
    @UserId INT,
    @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Matches the permission already required by three of this page's
    -- four operations (TransferEnrollmentClassSection/Class/Campus);
    -- Re-Admit itself is gated separately by dbo.ReAdmitStudent's own
    -- Students.Administer check when it runs.
    EXEC dbo.RequirePermission
        @UserId,
        N'Enrollment.Manage';

    IF @AdmissionId <= 0
        THROW 52001, 'A valid student is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Students
        WHERE AdmissionId = @AdmissionId
    )
        THROW 52002, 'The selected student was not found.', 1;

    -- No fee-balance filtering, unlike dbo.GetStudentEnrollments - every
    -- enrollment the student has ever had is relevant here, so the page
    -- can always show a Left student's last known placement.
    SELECT
        E.EnrollmentId,
        E.AdmissionId,
        E.AcademicYearId,
        AY.YearName,
        E.ClassSectionId,
        CCS.CampusId, CMP.CampusName, C.ClassId, SEC.SectionId,
        C.ClassName,
        SEC.SectionName,
        E.EnrollmentDate,
        E.LeavingDate, E.BillingStartDate, E.PlacementConfirmedAt, E.BillingReady,
        E.Status

    FROM dbo.Enrollments E

    INNER JOIN dbo.AcademicYears AY
        ON AY.AcademicYearId = E.AcademicYearId

    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId

    INNER JOIN dbo.Campuses CMP ON CMP.CampusId = CCS.CampusId
    INNER JOIN dbo.Classes C
        ON C.ClassId = CCS.ClassId

    INNER JOIN dbo.Sections SEC
        ON SEC.SectionId = CCS.SectionId

    WHERE E.AdmissionId = @AdmissionId

    ORDER BY
        CASE
            WHEN E.Status = N'Active' THEN 0
            ELSE 1
        END,

        E.EnrollmentId DESC;
END;


GO
CREATE OR ALTER PROCEDURE dbo.GetPendingReturnAdmissions
    @UserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    SELECT E.EnrollmentId, E.AdmissionId, S.RegistrationNumber,
           S.StudentName, S.FatherName, AY.YearName AS AcademicYear,
           C.ClassName, SEC.SectionName, CAMP.CampusName,
           S.RegistrationNumber + N' - ' + S.StudentName + N' - ' +
           C.ClassName + N' / ' + SEC.SectionName AS Display,
           E.EnrollmentDate
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.AcademicYears AY ON AY.AcademicYearId = E.AcademicYearId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    WHERE E.Status = N'Active' AND E.BillingReady = 0 AND E.PlacementConfirmedAt IS NOT NULL
      AND S.Status = N'Active'
    ORDER BY S.StudentName, S.RegistrationNumber;
END;
GO
