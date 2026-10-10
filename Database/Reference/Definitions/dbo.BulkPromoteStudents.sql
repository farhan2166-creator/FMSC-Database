SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- BulkPromoteStudents
CREATE   PROCEDURE dbo.BulkPromoteStudents
@UserId INT, @SourceAcademicYearId INT, @DestinationAcademicYearId INT, @SourceCampusId INT, @SourceClassId INT, @SourceSectionId INT, @DestinationClassSectionId INT, @EffectiveDate DATE, @Remarks NVARCHAR (500), @Decisions dbo.BulkPromotionDecisionType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @SourceAcademicYearId <= 0
        THROW 52501, 'A valid source academic year is required.', 1;
    IF @DestinationAcademicYearId <= 0
        THROW 52502, 'A valid destination academic year is required.', 1;
    IF @DestinationAcademicYearId = @SourceAcademicYearId
        THROW 52503, 'Bulk Promotion must move students into a different academic year. Use Transfer Class for same-year movement.', 1;
    IF @SourceCampusId <= 0
       OR @SourceClassId <= 0
        THROW 52504, 'A valid source campus and class are required.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
       AND @DestinationClassSectionId <= 0
        THROW 52506, 'A valid destination class/section is required for promoted students.', 1;
    IF @EffectiveDate IS NULL
        THROW 52508, 'The effective date is required.', 1;
    IF @EffectiveDate > CONVERT (DATE, SYSDATETIME())
        THROW 52509, 'The effective date cannot be in the future.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   @Decisions)
        THROW 52510, 'Select at least one student to process.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision NOT IN (N'PROMOTE', N'HOLD'))
        THROW 52511, 'Bulk promotion contains an invalid decision.', 1;
    DECLARE @SourceStartDate AS DATE, @DestinationStartDate AS DATE, @DestinationEndDate AS DATE, @DestinationCampusId AS INT, @DestinationClassId AS INT;
    SELECT @SourceStartDate = StartDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @SourceAcademicYearId;
    SELECT @DestinationStartDate = StartDate, @DestinationEndDate=EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @DestinationAcademicYearId;
    IF @SourceStartDate IS NULL
        THROW 52512, 'The source academic year is invalid.', 1;
    IF @DestinationStartDate IS NULL
        THROW 52513, 'The destination academic year is invalid.', 1;
    IF @DestinationStartDate <= @SourceStartDate
        THROW 52514, 'The destination academic year must be later than the source academic year.', 1;
    IF @EffectiveDate<@DestinationStartDate OR (@DestinationEndDate IS NOT NULL AND @EffectiveDate>@DestinationEndDate)
        THROW 52520,'The effective date must fall within the destination academic year.',1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            SELECT @DestinationCampusId = CCS.CampusId,
                   @DestinationClassId = CCS.ClassId
            FROM   dbo.CampusClassSections AS CCS
            WHERE  CCS.ClassSectionId = @DestinationClassSectionId;
            IF @DestinationCampusId IS NULL
                THROW 52516, 'The destination class/section is invalid.', 1;
            IF @DestinationCampusId <> @SourceCampusId
                THROW 52517, 'Bulk Promotion cannot move students between campuses.', 1;
        END
    ELSE
        BEGIN
            SET @DestinationClassSectionId = NULL;
        END
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            DECLARE @MissingMandatoryFeesDest AS NVARCHAR (1000);
            SELECT @MissingMandatoryFeesDest = STRING_AGG(FT.FeeTypeName, N', ')
            FROM   dbo.FeeTypes AS FT
            WHERE  FT.IsMandatory = 1
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.FeeConfigurations AS FC
                                   WHERE  FC.CampusId = @DestinationCampusId
                                          AND FC.ClassId = @DestinationClassId
                                          AND FC.FeeTypeId = FT.FeeTypeId);
            IF @MissingMandatoryFeesDest IS NOT NULL
                BEGIN
                    DECLARE @MandatoryFeeErrorMsgDest AS NVARCHAR (1100) = N'Required fee configuration is missing for the destination class: ' + @MissingMandatoryFeesDest + N'.';
                    THROW 52519, @MandatoryFeeErrorMsgDest, 1;
                END
        END
    BEGIN TRANSACTION;
    SELECT E.EnrollmentId,
           E.AdmissionId,
           E.ClassSectionId,
           E.UsesTransport,
           D.Decision,
           C.ClassId AS CurrentClassId,
           CCS.CampusId AS CurrentCampusId
    INTO   #Selected
    FROM   @Decisions AS D
           INNER JOIN
           dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           ON E.AdmissionId = D.AdmissionId
              AND E.Status = N'Active'
              AND E.AcademicYearId = @SourceAcademicYearId
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
           INNER JOIN
           dbo.Classes AS C
           ON C.ClassId = CCS.ClassId
    WHERE  CCS.CampusId = @SourceCampusId
           AND CCS.ClassId = @SourceClassId
           AND (@SourceSectionId = 0
                OR CCS.SectionId = @SourceSectionId);
    IF EXISTS (SELECT 1
               FROM   @Decisions AS D
                      LEFT OUTER JOIN
                      #Selected AS S
                      ON S.AdmissionId = D.AdmissionId
               WHERE  S.AdmissionId IS NULL)
        THROW 52521, 'One or more selected students are no longer active in the selected source class. Reload the candidates and try again.', 1;
    ALTER TABLE #Selected
        ADD NewClassSectionId INT NULL;
    UPDATE #Selected
    SET    NewClassSectionId = CASE WHEN Decision = N'PROMOTE' THEN @DestinationClassSectionId ELSE ClassSectionId END;
    IF EXISTS (SELECT 1
               FROM   #Selected AS S
                      INNER JOIN
                      dbo.CampusClassSections AS CCS
                      ON CCS.ClassSectionId = S.NewClassSectionId
               WHERE  CCS.CampusId <> @SourceCampusId)
        THROW 52522, 'One or more destination class/sections are invalid for the source campus.', 1;
    DECLARE @BatchLockResult AS INT;
    DECLARE @BatchLockResource AS NVARCHAR (255) = N'FMSC.BulkPromotion.' + CONVERT (NVARCHAR (20), @SourceAcademicYearId) + N'.' + CONVERT (NVARCHAR (20), @SourceClassId) + N'.' + CONVERT (NVARCHAR (20), @SourceCampusId);
    EXECUTE @BatchLockResult = sys.sp_getapplock @Resource = @BatchLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
    IF @BatchLockResult < 0
        THROW 52523, 'Another bulk promotion is currently processing this class. Please try again after it completes.', 1;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'BULK_PROMOTION', CONVERT (NVARCHAR (200), @SourceAcademicYearId));
    INSERT  dbo.PromotionBatches (SourceAcademicYearId, DestinationAcademicYearId, SourceCampusId, SourceClassId, SourceSectionId, DestinationClassSectionId, EffectiveDate, Remarks, CreatedByUserId)
    VALUES                      (@SourceAcademicYearId, @DestinationAcademicYearId, @SourceCampusId, @SourceClassId, NULLIF (@SourceSectionId, 0), @DestinationClassSectionId, @EffectiveDate, @Remarks, @UserId);
    DECLARE @PromotionBatchId AS INT = SCOPE_IDENTITY();
    IF EXISTS (SELECT 1
               FROM   (SELECT DISTINCT CCS.CampusId,
                                       CCS.ClassId
                       FROM   #Selected AS S
                              INNER JOIN
                              dbo.CampusClassSections AS CCS
                              ON CCS.ClassSectionId = S.NewClassSectionId) AS D
               WHERE  EXISTS (SELECT 1
                              FROM   dbo.FeeTypes AS FT
                              WHERE  FT.IsMandatory = 1
                                     AND NOT EXISTS (SELECT 1
                                                     FROM   dbo.FeeConfigurations AS FC
                                                     WHERE  FC.CampusId = D.CampusId
                                                            AND FC.ClassId = D.ClassId
                                                            AND FC.FeeTypeId = FT.FeeTypeId)))
        THROW 52524, 'Required fee configuration is missing for one or more destination classes.', 1;
    UPDATE E
    SET    Status      = N'Promoted',
           LeavingDate = @EffectiveDate,
           UpdatedAt   = SYSDATETIME(),
           Remarks     = COALESCE (NULLIF (@Remarks, N''), E.Remarks)
    FROM   dbo.Enrollments AS E
           INNER JOIN
           #Selected AS S
           ON S.EnrollmentId = E.EnrollmentId
    WHERE  E.Status = N'Active';
    IF @@ROWCOUNT <> (SELECT COUNT(*)
                      FROM   #Selected)
        THROW 52525, 'The source enrollments could not all be closed as historical enrollments.', 1;
    DECLARE @CutoffDay AS INT;
    SELECT TOP (1) @CutoffDay = SP.FeeProrationCutoffDay
    FROM   dbo.SchoolProfile AS SP;
    INSERT dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, BillingReady, Remarks)
    SELECT S.AdmissionId,
           @DestinationAcademicYearId,
           S.NewClassSectionId,
           @EffectiveDate,
           CASE WHEN @CutoffDay BETWEEN 1 AND 28
                     AND DAY(@EffectiveDate) >= @CutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(@EffectiveDate), MONTH(@EffectiveDate), 1)) ELSE @EffectiveDate END,
           N'Active',
           0,
           0,
           CASE WHEN S.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END
    FROM   #Selected AS S;
    DECLARE @NewEnrollments TABLE (
        AdmissionId       INT           NOT NULL,
        OldEnrollmentId   INT           NOT NULL,
        NewEnrollmentId   INT           NOT NULL,
        Decision          NVARCHAR (20) NOT NULL,
        NewClassSectionId INT           NOT NULL,
        UsesTransport     BIT           NOT NULL);
    INSERT INTO @NewEnrollments (AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, NewClassSectionId, UsesTransport)
    SELECT S.AdmissionId,
           S.EnrollmentId,
           E.EnrollmentId,
           S.Decision,
           S.NewClassSectionId,
           0
    FROM   #Selected AS S
           INNER JOIN
           dbo.Enrollments AS E
           ON E.AdmissionId = S.AdmissionId
              AND E.AcademicYearId = @DestinationAcademicYearId
              AND E.Status = N'Active';
    IF (SELECT COUNT(*)
        FROM   @NewEnrollments) <> (SELECT COUNT(*)
                                    FROM   #Selected)
        THROW 52526, 'The new academic-year enrollments could not all be created.', 1;
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT N.NewEnrollmentId,
           N.AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = N.NewClassSectionId
           INNER JOIN
           dbo.FeeConfigurations AS FC
           ON FC.CampusId = CCS.CampusId
              AND FC.ClassId = CCS.ClassId
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FT.IsMandatory = 1
           AND FT.IsTransportLinked = 0;
    INSERT dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    SELECT N.OldEnrollmentId,
           N.AdmissionId,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'BULK_PROMOTE' ELSE N'BULK_HOLD' END,
           @EffectiveDate,
           S.ClassSectionId,
           N.NewClassSectionId,
           N'Active',
           N'Promoted',
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Bulk promoted to EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) ELSE N'Bulk held back/repeated class in new academic year; EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) END,
           @UserId
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    INSERT dbo.PromotionBatchStudents (PromotionBatchId, AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, Remarks)
    SELECT @PromotionBatchId,
           N.AdmissionId,
           N.OldEnrollmentId,
           N.NewEnrollmentId,
           N.Decision,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Promoted to destination class.' ELSE N'Held back/repeated the current class in the destination academic year.' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    DECLARE @ProcessedCount AS INT = (SELECT COUNT(*)
                                      FROM   @NewEnrollments), @PromotedCount AS INT = (SELECT COUNT(*)
                                                                                        FROM   @NewEnrollments
                                                                                        WHERE  Decision = N'PROMOTE'), @HeldBackCount AS INT = (SELECT COUNT(*)
                                                                                                                                                FROM   @NewEnrollments
                                                                                                                                                WHERE  Decision = N'HOLD');
    UPDATE dbo.PromotionBatches
    SET    ProcessedCount = @ProcessedCount,
           PromotedCount  = @PromotedCount,
           HeldBackCount  = @HeldBackCount
    WHERE  PromotionBatchId = @PromotionBatchId;
    DECLARE @AuditValues AS NVARCHAR (MAX);
    SELECT @AuditValues = (SELECT @PromotionBatchId AS PromotionBatchId,
                                  @SourceAcademicYearId AS SourceAcademicYearId,
                                  @DestinationAcademicYearId AS DestinationAcademicYearId,
                                  @SourceCampusId AS SourceCampusId,
                                  @SourceClassId AS SourceClassId,
                                  @SourceSectionId AS SourceSectionId,
                                  @DestinationClassSectionId AS DestinationClassSectionId,
                                  @EffectiveDate AS EffectiveDate,
                                  @ProcessedCount AS ProcessedCount,
                                  @PromotedCount AS PromotedCount,
                                  @HeldBackCount AS HeldBackCount,
                                  @Remarks AS Remarks
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @PromotionBatchAuditRecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @PromotionBatchId);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'PromotionBatches', @RecordId = @PromotionBatchAuditRecordId, @Action = N'BULK_PROMOTION', @OldValues = NULL, @NewValues = @AuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @PromotionBatchId AS PromotionBatchId,
           @ProcessedCount AS ProcessedCount,
           @PromotedCount AS PromotedCount,
           @HeldBackCount AS HeldBackCount;
END

GO
