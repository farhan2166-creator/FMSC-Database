-- Reuse one School Profile cutoff policy for admission, leaving and transfer.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER FUNCTION dbo.GetMonthlyBillingStartDate(@EventDate date)
RETURNS date
AS
BEGIN
 DECLARE @Cutoff int=(SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId);
 RETURN CASE WHEN @Cutoff BETWEEN 1 AND 28 AND DAY(@EventDate)>=@Cutoff
  THEN DATEADD(month,1,DATEFROMPARTS(YEAR(@EventDate),MONTH(@EventDate),1)) ELSE @EventDate END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.AdmitStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @QuickRegistration bit=0, @FamilyStudentId int=NULL, @FeeConcessionsJson nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    IF @FeeConcessionsJson IS NOT NULL AND ISJSON(@FeeConcessionsJson)<>1
      THROW 51024,'Review the selected fee concessions.',1;
    DECLARE @Concessions TABLE(FeeTypeId int NOT NULL PRIMARY KEY,ConcessionLabel nvarchar(20) NOT NULL);
    IF EXISTS(SELECT 1 FROM OPENJSON(@FeeConcessionsJson) WITH(FeeTypeId int,ConcessionLabel nvarchar(20))
      WHERE FeeTypeId IS NULL OR ConcessionLabel IS NULL OR ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived'))
      THROW 51024,'Choose a valid fee and concession.',1;
    INSERT @Concessions SELECT FeeTypeId,ConcessionLabel FROM OPENJSON(@FeeConcessionsJson)
      WITH(FeeTypeId int,ConcessionLabel nvarchar(20));
    IF EXISTS(SELECT 1 FROM @Concessions) EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
    DECLARE @ReviewedDiscounts dbo.AdmissionFeeDiscountType;
    INSERT @ReviewedDiscounts SELECT * FROM @AdmissionDiscounts;
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts)
        EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts
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
    SET @BillingStartDate = dbo.GetMonthlyBillingStartDate(@AdmissionDate);
    BEGIN TRANSACTION;
    IF @QuickRegistration=0 EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT; ELSE SET @AdmissionNumber=NULL;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    IF @QuickRegistration=0 EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF @QuickRegistration=1 UPDATE dbo.Students SET QuickReference=CONCAT('QR-',@AdmissionId) WHERE AdmissionId=@AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, BillingReady, PlacementConfirmedAt)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @AdmissionDate, @BillingStartDate, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, @UsesTransport, N'Created by admission',1,CASE WHEN @QuickRegistration=0 THEN SYSDATETIME() END);
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
    IF EXISTS(SELECT 1 FROM @Concessions C LEFT JOIN dbo.StudentFeeAssignments A
      ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId WHERE A.StudentFeeAssignmentId IS NULL OR A.Frequency<>N'Monthly')
      THROW 51024,'Concessions must refer to an assigned monthly fee.',1;
    UPDATE D SET DiscountAmount=A.Amount FROM @ReviewedDiscounts D JOIN @Concessions C ON C.FeeTypeId=D.FeeTypeId
      JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId
      WHERE C.ConcessionLabel IN(N'Free',N'Waived');
    INSERT @ReviewedDiscounts SELECT C.FeeTypeId,A.Amount FROM @Concessions C
      JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=C.FeeTypeId
      WHERE C.ConcessionLabel IN(N'Free',N'Waived') AND NOT EXISTS(SELECT 1 FROM @ReviewedDiscounts D WHERE D.FeeTypeId=C.FeeTypeId);
    IF EXISTS (SELECT 1
               FROM   @ReviewedDiscounts AS D
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
           @ReviewedDiscounts AS D
           ON D.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId AND SFA.Frequency<>N'Monthly';
    -- Monthly admission discounts begin next month and stop with this academic year.
    DECLARE @DiscountFrom date=DATEADD(month,1,DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1));
    IF EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId AND @DiscountFrom<=EndDate)
    BEGIN
      SELECT A.*,D.DiscountAmount ChosenDiscount INTO #AdmissionDiscountVersions
       FROM dbo.StudentFeeAssignments A JOIN @ReviewedDiscounts D ON D.FeeTypeId=A.FeeTypeId
       WHERE A.EnrollmentId=@EnrollmentId AND A.Frequency=N'Monthly' AND D.DiscountAmount>0;
      UPDATE A SET EffectiveTo=DATEADD(month,-1,@DiscountFrom)
       FROM dbo.StudentFeeAssignments A JOIN #AdmissionDiscountVersions V ON V.StudentFeeAssignmentId=A.StudentFeeAssignmentId;
      INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
       SELECT V.EnrollmentId,V.AdmissionId,V.FeeTypeId,V.Amount,V.ChosenDiscount,V.Frequency,V.Remarks,@DiscountFrom,NULL,C.ConcessionLabel
       FROM #AdmissionDiscountVersions V LEFT JOIN @Concessions C ON C.FeeTypeId=V.FeeTypeId;
    END;
    IF @FamilyStudentId IS NOT NULL EXEC dbo.AddStudentSibling @AdmissionId,@FamilyStudentId,N'Sibling',@UserId;
    EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Initial placement created', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Admission', @AdmissionDate, N'Initial student status', @UserId);

COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber, (SELECT QuickReference FROM dbo.Students WHERE AdmissionId=@AdmissionId) QuickReference;
END

GO
CREATE OR ALTER PROCEDURE dbo.BulkPromoteStudents
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
    IF EXISTS(SELECT 1 FROM #Selected X JOIN dbo.Students S ON S.AdmissionId=X.AdmissionId WHERE S.LegacySetupPending=1) THROW 59751,'Finish legacy verification and Go Live before promoting these students.',1;
    IF EXISTS(SELECT 1 FROM #Selected S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId WHERE E.PlacementConfirmedAt IS NULL)
        THROW 59646,'Confirm the source placement in Admission before promotion.',1;
    IF EXISTS(SELECT 1 FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK)
        JOIN #Selected S ON S.EnrollmentId=B.EnrollmentId
        WHERE B.Status<>N'Cancelled' AND COALESCE(B.BillingMonth,B.ChargeDate)>DATEFROMPARTS(YEAR(DATEADD(day,-1,@EffectiveDate)),MONTH(DATEADD(day,-1,@EffectiveDate)),1))
        THROW 59632,'Charges exist after the promotion effective date. Review unpaid charges in Fee Collection or paid charges in Fee Corrections, then retry. No students were promoted.',1;
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
    INSERT dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, BillingReady, Remarks, PlacementConfirmedAt)
    SELECT S.AdmissionId,
           @DestinationAcademicYearId,
           S.NewClassSectionId,
           @EffectiveDate,
           dbo.GetMonthlyBillingStartDate(@EffectiveDate),
           N'Active',
           0,
           0,
           CASE WHEN S.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END, SYSDATETIME()
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

COMMIT TRANSACTION;
    SELECT @PromotionBatchId AS PromotionBatchId,
           @ProcessedCount AS ProcessedCount,
           @PromotedCount AS PromotedCount,
           @HeldBackCount AS HeldBackCount;
END

GO
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
    DECLARE @BillingStartDate AS DATE = dbo.GetMonthlyBillingStartDate(@ReturnDate);
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
CREATE OR ALTER PROCEDURE dbo.PostEnrollmentOpeningCharges @EnrollmentId int AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @BillingStartDate AS DATE, @ChargeDate AS DATE, @Status AS NVARCHAR (30), @BillingReady AS BIT;
    SELECT @BillingStartDate = E.BillingStartDate,
           @ChargeDate = COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate),
           @Status = E.Status,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @BillingStartDate IS NULL
        THROW 59401, 'Select an enrollment with an approved billing start date.', 1;
    IF @Status NOT IN(N'Active',N'Pending')
       OR @BillingReady <> 1
        THROW 59402, 'Confirm admission before posting opening charges.', 1;
    DECLARE @OpeningMonth AS DATE = DATEFROMPARTS(YEAR(@BillingStartDate), MONTH(@BillingStartDate), 1);
    DECLARE @OneOffMonth date=DATEFROMPARTS(YEAR(@ChargeDate),MONTH(@ChargeDate),1);
    IF @@TRANCOUNT=0 THROW 59403,'Opening charges require the admission transaction.',1;
    IF @@TRANCOUNT > 0
        BEGIN
            DECLARE @LockResult AS INT;
            DECLARE @LockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @LockResult = sys.sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @LockResult < 0
                THROW 59403, 'Charges for this enrollment are already being posted.', 1;
        END
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OneOffMonth,
           @ChargeDate,
           @ChargeDate,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Annual'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Yearly'
           AND (SFA.EffectiveFrom IS NULL
                OR @OneOffMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OneOffMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.ChargeKind = N'Annual'
                                  );
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @OpeningMonth,
           @OpeningMonth,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Monthly'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly' AND SFA.Amount>SFA.DiscountAmount
           AND EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
             WHERE E.EnrollmentId=@EnrollmentId AND @OpeningMonth>=DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
               AND @OpeningMonth<=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1))
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @OpeningMonth
                                  AND BC.ChargeKind = N'Monthly'
                                  );

    -- Explicitly selected As Needed fees belong on a pending QR opening bill.
    -- Active-student additional posting keeps its established separate workflow.
    IF @Status=N'Pending'
        INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
            BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
        SELECT A.StudentFeeAssignmentId,A.EnrollmentId,A.AdmissionId,A.FeeTypeId,
            @OneOffMonth,@ChargeDate,@ChargeDate,A.Amount,A.DiscountAmount,N'Manual'
        FROM dbo.StudentFeeAssignments A
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'As Needed'
            AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=@OneOffMonth)
            AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=@OneOffMonth)
            AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@EnrollmentId
                AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N'Manual' AND B.Status<>N'Cancelled');
    DECLARE @CreditStudent int=(SELECT AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId);
    IF @@TRANCOUNT>0 EXEC dbo.ApplyStudentCredit_Internal @CreditStudent;
END
GO
CREATE OR ALTER TRIGGER dbo.GuardClosedEnrollmentCharges ON dbo.BillingCharges AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- Unpaid charge cancellation is allowed through the audited reversal workflow; closed bills cannot be rewritten.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status NOT IN(N'Active',N'Pending') AND (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId
    OR I.StudentFeeAssignmentId<>D.StudentFeeAssignmentId OR I.AdmissionId<>D.AdmissionId OR I.FeeTypeId<>D.FeeTypeId
    OR I.GrossAmount<>D.GrossAmount OR I.DiscountAmount<>D.DiscountAmount
    OR ISNULL(I.BillingMonth,'19000101')<>ISNULL(D.BillingMonth,'19000101')
    OR I.ChargeDate<>D.ChargeDate OR I.DueDate<>D.DueDate OR I.ChargeKind<>D.ChargeKind
    OR (I.Status<>D.Status AND D.Status=N'Cancelled')))
  THROW 57102,'This enrollment is closed. Its charges cannot be posted, changed or reversed here. Outstanding fees can still be collected.',1;
 IF EXISTS(SELECT 1 FROM deleted D LEFT JOIN inserted I ON I.BillingChargeId=D.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE I.BillingChargeId IS NULL AND E.Status NOT IN(N'Active',N'Pending'))
  THROW 57102,'Closed enrollment charges must remain in history.',1;
 -- New bills must fit the enrollment and academic year, including backdated closures.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=I.EnrollmentId
    JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE D.BillingChargeId IS NULL AND I.Status<>N'Cancelled' AND ((E.PlacementConfirmedAt IS NULL AND E.Status<>N'Pending') OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=E.AdmissionId AND S.LegacySetupPending=1) OR
      (I.ChargeKind=N'Monthly' AND (E.BillingStartDate IS NULL OR COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1)))
      OR COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
      OR (E.Status=N'Pending' AND I.ChargeKind=N'Monthly' AND COALESCE(I.BillingMonth,I.ChargeDate)<>DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)) OR (Y.EndDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1))
      OR (E.LeavingDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1))))
    THROW 59633,'The charge period is outside the enrollment or academic year. Correct placement or billing dates in Admission before posting.',1;
 -- Protect payments even if an alternative reversal entry point bypasses the form.
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   WHERE I.Status=N'Cancelled' AND D.Status<>N'Cancelled' AND EXISTS(
    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.BillingChargeId=I.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0))
  THROW 57103,'A paid or partially paid charge cannot be cancelled. Use the payment correction or refund workflow.',1;
END;

GO
CREATE OR ALTER PROCEDURE dbo.CloseStudentAdmission @UserId int,@AdmissionId int,@LeavingDate date,@Reason nvarchar(500),
 @Cancel bit=0,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@ReverseFutureUnpaid bit=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59710,'Enter the reason for leaving or cancellation.',1;
 IF @LeavingDate IS NULL OR @LeavingDate>CONVERT(date,GETDATE()) THROW 59711,'Choose a leaving date that is not in the future.',1;
 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Enrollment int,@Start date,@Old nvarchar(max),@New nvarchar(max),@Snapshot varchar(64),@Raw nvarchar(max),@Lock int;
 SELECT @State=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Enrollment=EnrollmentId,@Start=EnrollmentDate FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending');
 IF @Enrollment IS NULL OR @State NOT IN(N'Active',N'Pending') THROW 59712,'This record has no open placement to close.',1;
 IF @Cancel=1 AND (@State<>N'Pending' OR EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND AdmissionNumber IS NOT NULL))
  THROW 59720,'An officially admitted student cannot be cancelled. Use Mark as Left for departure, or correct details and fees in their respective workspaces.',1;
 IF @State=N'Pending' AND @Cancel=0 THROW 59713,'Use Cancel Registration for a pending registration.',1;
 IF @LeavingDate<@Start THROW 59714,'Leaving date cannot be before the placement start.',1;
 SELECT @Lock=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment) THROW 59715,'This placement already has a leaving certificate. Review its recorded departure in Admission.',1;
 IF @Cancel=1 AND EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.AdmissionId=@AdmissionId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)
  THROW 59716,'Payments exist. Review refunds or reversals in Fee Corrections before cancellation; history must be retained. For an admitted student who attended, use Mark as Left.',1;
 IF @Cancel=1 AND (EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE AdmissionId=@AdmissionId) OR
   EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@Enrollment))
  THROW 59717,'This admission has academic or certificate history. Use Mark as Left rather than cancellation.',1;
 DECLARE @MonthlyBoundary date=dbo.GetMonthlyBillingStartDate(@LeavingDate);
 SET @MonthlyBoundary=DATEFROMPARTS(YEAR(@MonthlyBoundary),MONTH(@MonthlyBoundary),1);
 DECLARE @FutureCount int,@FutureAmount decimal(12,2),@FuturePaid int,@Blocked int;
 SELECT @FutureCount=COUNT(*),@FutureAmount=COALESCE(SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)),0)
 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled'
  AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL AND B.BillingMonth>=@MonthlyBoundary;
 SELECT @FuturePaid=COUNT(*) FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled'
  AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL AND B.BillingMonth>=@MonthlyBoundary AND
  EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
   WHERE A.BillingChargeId=B.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0);
 SET @Blocked=CASE WHEN @Cancel=0 AND @PaidReductionAction NOT IN(N'Credit',N'Refund') THEN 1 ELSE 0 END;
 DECLARE @PaidReturnAmount decimal(12,2)=(SELECT COALESCE(SUM(B.NetAmount-dbo.GetFeeChargeBalance(B.BillingChargeId)),0)
   FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled'
    AND (B.BillingChargeId IN(SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Monthly' AND BillingMonth>=@MonthlyBoundary)
      OR B.CorrectionOfBillingChargeId IN(SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Monthly' AND BillingMonth>=@MonthlyBoundary)));
 SET @Raw=CONCAT(CONVERT(varchar(10),@LeavingDate,120),@Reason,CONVERT(char(1),@Cancel),CONVERT(char(1),@ReverseFutureUnpaid),@PaidReductionAction,(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@State CurrentStatus,@Cancel CancelAdmission,@Blocked BlockedCount,@FutureCount FutureCount,@FutureAmount FutureAmount,@FuturePaid FuturePaid,COUNT(*) ChargeCount,@PaidReturnAmount PaidReturnAmount,
   COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled';
  COMMIT;RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59719,'The record changed. Review the closure again.',1;
 IF @Blocked>0 THROW 59718,'Review charges after the leaving month. Choose reversal for unpaid future charges, or resolve paid charges in Fee Corrections first.',1;
 SELECT @Old=(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF @Cancel=1
 BEGIN
  EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
  DECLARE @Charge bigint; DECLARE @Reverse TABLE(BillingChargeId bigint,AdmissionId int,FeeTypeName nvarchar(100),BillingMonth date,AmountReversed decimal(12,2),Reason nvarchar(500),Status nvarchar(20));
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled'
   AND (@Cancel=1 OR COALESCE(BillingMonth,ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1));
  OPEN C;FETCH NEXT FROM C INTO @Charge;
  WHILE @@FETCH_STATUS=0 BEGIN INSERT @Reverse EXEC dbo.ReverseBillingCharge @UserId,@Charge,@Reason;FETCH NEXT FROM C INTO @Charge;END;
  CLOSE C;DEALLOCATE C;
 END;
 IF @Cancel=0 AND @FutureCount>0
 BEGIN
  DECLARE @MonthlyCharge bigint,@CorrectionRequest uniqueidentifier;
  DECLARE @MonthlyResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
  DECLARE MonthlyCharges CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges
   WHERE EnrollmentId=@Enrollment AND ChargeKind=N'Monthly' AND CorrectionOfBillingChargeId IS NULL
     AND Status<>N'Cancelled' AND BillingMonth>=@MonthlyBoundary ORDER BY BillingChargeId;
  OPEN MonthlyCharges;FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  WHILE @@FETCH_STATUS=0 BEGIN
   SET @CorrectionRequest=NEWID();
   INSERT @MonthlyResult EXEC dbo.AdjustBillingChargeRate @UserId,@MonthlyCharge,0,@PaidReductionAction,@Reason,@CorrectionRequest;
   FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  END;
  CLOSE MonthlyCharges;DEALLOCATE MonthlyCharges;
 END;
 DECLARE @NewStatus nvarchar(30)=CASE WHEN @Cancel=1 THEN N'Cancelled' ELSE N'Left' END;
 UPDATE dbo.Enrollments SET Status=@NewStatus,LeavingDate=@LeavingDate,BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=@NewStatus,WithdrawalDate=@LeavingDate,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId) VALUES(@Enrollment,@AdmissionId,N'STATUS_CHANGE',@LeavingDate,@State,@NewStatus,@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,@State,@NewStatus,@Reason,@LeavingDate,@UserId);
 DECLARE @Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@Enrollment);

 SELECT @New=(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);

 COMMIT;
END;

GO
CREATE OR ALTER PROCEDURE dbo.SyncTransferStudentFees_Internal
@UserId INT, @EnrollmentId INT, @FeePolicy NVARCHAR (20), @Reason NVARCHAR (500), @EffectiveDate DATE=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    IF @FeePolicy NOT IN (N'NEXT_MONTH', N'CURRENT_MONTH', N'CUSTOM_MONTH',N'SCHOOL_CUTOFF')
        THROW 59300, 'Invalid fee synchronization policy.', 1;
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 59301, 'A reason is required to synchronize fees after a transfer.', 1;
    DECLARE @AdmissionId AS INT, @CampusId AS INT, @ClassId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 59302, 'The selected enrollment does not exist or is not active.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeConfigurations WITH (HOLDLOCK)
                   WHERE  CampusId = @CampusId
                          AND ClassId = @ClassId)
        THROW 59306, 'No destination fees are configured. Keep existing fees or complete Fee Setup first.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments AS SFA
                      INNER JOIN
                      dbo.FeeTypes AS FT
                      ON FT.FeeTypeId = SFA.FeeTypeId
               WHERE  SFA.EnrollmentId = @EnrollmentId
                      AND SFA.EffectiveTo IS NULL
                      AND SFA.CancelledAt IS NULL
                      AND FT.IsMandatory = 1
                      AND NOT EXISTS (SELECT 1
                                      FROM   dbo.FeeConfigurations AS FC WITH (HOLDLOCK)
                                      WHERE  FC.CampusId = @CampusId
                                             AND FC.ClassId = @ClassId
                                             AND FC.FeeTypeId = SFA.FeeTypeId))
        THROW 59307, 'Required destination fees are missing. Keep existing fees or complete Fee Setup first.', 1;
    DECLARE @Today AS DATE = COALESCE(@EffectiveDate,CAST(GETDATE() AS DATE));
    DECLARE @CurrentMonthStart AS DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @TargetMonth AS DATE = CASE @FeePolicy WHEN N'CURRENT_MONTH' THEN @CurrentMonthStart WHEN N'CUSTOM_MONTH' THEN DATEFROMPARTS(YEAR(@FeeEffectiveMonth),MONTH(@FeeEffectiveMonth),1) ELSE DATEADD(MONTH, 1, @CurrentMonthStart) END;

    DECLARE @CutoffDate date=dbo.GetMonthlyBillingStartDate(@Today);
    DECLARE @CutoffMonth date=DATEFROMPARTS(YEAR(@CutoffDate),MONTH(@CutoffDate),1);
    IF @FeePolicy=N'SCHOOL_CUTOFF' SET @TargetMonth=@CutoffMonth;
    IF @TargetMonth<@CutoffMonth THROW 59327,'The selected month is before the School Profile cutoff permits the new class rate.',1;
    IF @TargetMonth IS NULL OR @TargetMonth<@CurrentMonthStart
        THROW 59327,'Choose an effective month on or after the transfer month.',1;
    DECLARE @InitialLockedRows int;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    IF OBJECT_ID('tempdb..#FeeSyncPlan') IS NOT NULL
        DROP TABLE #FeeSyncPlan;
    CREATE TABLE #FeeSyncPlan (
        FeeTypeId             INT             NOT NULL,
        CurrentAssignmentId   INT             NULL,
        CurrentDiscountAmount DECIMAL (12, 2) NULL,
        CurrentRemarks        NVARCHAR (500)  NULL,
        DestAmount            DECIMAL (12, 2) NULL,
        DestFrequency         NVARCHAR (20)   NULL,
        PlanAction            NVARCHAR (20)   NOT NULL
    );
    WITH CurrentOpen
    AS   (SELECT StudentFeeAssignmentId,
                 FeeTypeId,
                 Amount,
                 Frequency,
                 DiscountAmount,
                 Remarks
          FROM   dbo.StudentFeeAssignments
          WHERE  EnrollmentId = @EnrollmentId
                 AND EffectiveTo IS NULL
                 AND CancelledAt IS NULL),
         DestConfig
    AS   (SELECT FC.FeeConfigurationId,
                 FC.FeeTypeId,
                 FC.Amount,
                 FC.Frequency
          FROM   dbo.FeeConfigurations AS FC
                 INNER JOIN
                 dbo.FeeTypes AS FT
                 ON FT.FeeTypeId = FC.FeeTypeId
          WHERE  FC.CampusId = @CampusId
                 AND FC.ClassId = @ClassId
                 AND FC.Frequency IN (N'Monthly', N'Yearly')
                 -- A transfer must not undo an explicit cancellation.
                 AND NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments CX
                                 WHERE CX.EnrollmentId=@EnrollmentId AND CX.FeeTypeId=FC.FeeTypeId
                                   AND CX.EffectiveTo IS NULL AND CX.CancelledAt IS NOT NULL)
                 AND (FT.IsMandatory = 1
                      OR (FT.IsTransportLinked = 1
                          AND EXISTS (SELECT 1
                                      FROM   CurrentOpen AS CO
                                      WHERE  CO.FeeTypeId = FC.FeeTypeId))))
    INSERT #FeeSyncPlan (FeeTypeId, CurrentAssignmentId, CurrentDiscountAmount, CurrentRemarks, DestAmount, DestFrequency, PlanAction)
    SELECT COALESCE (CO.FeeTypeId, DC.FeeTypeId),
           CO.StudentFeeAssignmentId,
           CO.DiscountAmount,
           CO.Remarks,
           DC.Amount,
           DC.Frequency,
           CASE WHEN CO.StudentFeeAssignmentId IS NOT NULL
                     AND DC.FeeConfigurationId IS NOT NULL
                     AND (CO.Amount <> DC.Amount
                          OR CO.Frequency <> DC.Frequency
                          OR (@ReplaceScheduledFees=1 AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments S
                              WHERE S.EnrollmentId=@EnrollmentId AND S.FeeTypeId=CO.FeeTypeId AND S.CancelledAt IS NULL
                              AND S.StudentFeeAssignmentId<>CO.StudentFeeAssignmentId AND ISNULL(S.EffectiveTo,'99991231')>=@TargetMonth))) THEN N'CHANGE' WHEN CO.StudentFeeAssignmentId IS NULL
                                                                               AND DC.FeeConfigurationId IS NOT NULL THEN N'ADD' WHEN CO.StudentFeeAssignmentId IS NOT NULL
                                                                                                                                      AND DC.FeeConfigurationId IS NULL THEN N'DISCONTINUE' ELSE N'NONE' END
    FROM   CurrentOpen AS CO
           FULL OUTER JOIN
           DestConfig AS DC
           ON DC.FeeTypeId = CO.FeeTypeId;

    -- TRANSFER_TIMELINE_GUARD: validate all versions and bills under the caller transaction.
    IF @@TRANCOUNT=0 THROW 59321,'Transfer fee changes require a transaction.',1;
    DECLARE @LockedRows int;
    SELECT @LockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    SELECT @LockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments
        WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL
          AND EffectiveFrom>EffectiveTo)
        THROW 59322,'An existing fee period ends before it starts. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A JOIN dbo.StudentFeeAssignments B
        ON A.EnrollmentId=B.EnrollmentId AND A.FeeTypeId=B.FeeTypeId
        AND A.StudentFeeAssignmentId<B.StudentFeeAssignmentId
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND B.CancelledAt IS NULL
        AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(B.EffectiveTo,'99991231')
        AND ISNULL(B.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231'))
        THROW 59323,'Existing fee periods overlap. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    DECLARE @CorrectionBill bigint,@DestinationAmount decimal(12,2),@Request uniqueidentifier;
    DECLARE @CorrectionResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
    DECLARE CorrectBills CURSOR LOCAL FAST_FORWARD FOR
      SELECT B.BillingChargeId,CASE WHEN P.PlanAction=N'DISCONTINUE' THEN 0 ELSE P.DestAmount END
      FROM #FeeSyncPlan P JOIN dbo.BillingCharges B ON B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=P.FeeTypeId
      WHERE P.PlanAction<>N'NONE' AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL
        AND B.Status<>N'Cancelled' AND B.BillingMonth>=@TargetMonth ORDER BY B.BillingChargeId;
    OPEN CorrectBills;FETCH NEXT FROM CorrectBills INTO @CorrectionBill,@DestinationAmount;
    WHILE @@FETCH_STATUS=0 BEGIN
      SET @Request=NEWID();
      INSERT @CorrectionResult EXEC dbo.AdjustBillingChargeRate @UserId,@CorrectionBill,@DestinationAmount,@PaidReductionAction,@Reason,@Request;
      FETCH NEXT FROM CorrectBills INTO @CorrectionBill,@DestinationAmount;
    END;
    CLOSE CorrectBills;DEALLOCATE CorrectBills;
    DECLARE @LastIssued date=(SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B JOIN #FeeSyncPlan P ON P.FeeTypeId=B.FeeTypeId
      WHERE B.EnrollmentId=@EnrollmentId AND P.PlanAction<>N'NONE' AND B.ChargeKind=N'Monthly' AND B.Status<>N'Cancelled');
    IF @LastIssued>=@TargetMonth SET @TargetMonth=DATEADD(month,1,@LastIssued);
    -- An unbilled version already starting on the permitted month needs a rate edit, not schedule replacement.
    UPDATE A SET Amount=P.DestAmount,DiscountAmount=P.CurrentDiscountAmount,UpdatedAt=SYSDATETIME()
     FROM dbo.StudentFeeAssignments A JOIN #FeeSyncPlan P ON P.CurrentAssignmentId=A.StudentFeeAssignmentId
     WHERE P.PlanAction=N'CHANGE' AND A.EffectiveFrom=@TargetMonth
       AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId);
    UPDATE P SET PlanAction=N'NONE' FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=P.CurrentAssignmentId
     WHERE P.PlanAction=N'CHANGE' AND A.EffectiveFrom=@TargetMonth AND A.Amount=P.DestAmount
       AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId);
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A
        ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=P.FeeTypeId
        WHERE @ReplaceScheduledFees=0 AND P.PlanAction<>N'NONE' AND A.CancelledAt IS NULL
        AND ((A.StudentFeeAssignmentId=P.CurrentAssignmentId AND A.EffectiveFrom>=@TargetMonth)
          OR (A.StudentFeeAssignmentId<>ISNULL(P.CurrentAssignmentId,-1)
            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth)))
        THROW 59325,'A changed fee has a scheduled rate or waiver. Keep current fees, then explicitly review that schedule in Fee Changes.',1;
    UPDATE P SET CurrentDiscountAmount=CASE WHEN A.ConcessionLabel IS NOT NULL AND A.DiscountAmount=A.Amount THEN P.DestAmount
      WHEN P.CurrentDiscountAmount>P.DestAmount THEN P.DestAmount ELSE P.CurrentDiscountAmount END
    FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=P.CurrentAssignmentId WHERE P.DestAmount IS NOT NULL;
    IF NOT EXISTS (SELECT 1
                   FROM   #FeeSyncPlan
                   WHERE  PlanAction <> N'NONE')
        RETURN;
    DECLARE @PlanFeeTypeId AS INT, @PlanAssignmentId AS INT, @PlanDiscount AS DECIMAL (12, 2), @PlanRemarks AS NVARCHAR (500), @PlanDestAmount AS DECIMAL (12, 2), @PlanDestFrequency AS NVARCHAR (20), @PlanAction AS NVARCHAR (20), @PlanFeeTypeName AS NVARCHAR (100);
    DECLARE @PlanConcession nvarchar(20);
    DECLARE @LastBilledMonth AS DATE, @NewAssignmentId AS INT, @DiscontinueEffectiveTo AS DATE;
    DECLARE plan_cursor CURSOR LOCAL FAST_FORWARD
        FOR SELECT P.FeeTypeId,
                   P.CurrentAssignmentId,
                   P.CurrentDiscountAmount,
                   P.CurrentRemarks,
                   P.DestAmount,
                   P.DestFrequency,
                   P.PlanAction,
                   FT.FeeTypeName
            FROM   #FeeSyncPlan AS P
                   INNER JOIN
                   dbo.FeeTypes AS FT
                   ON FT.FeeTypeId = P.FeeTypeId
            WHERE  P.PlanAction <> N'NONE';
    OPEN plan_cursor;
    FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
    WHILE @@FETCH_STATUS = 0
        BEGIN

                    IF @ReplaceScheduledFees=1 AND @PlanAction IN(N'CHANGE',N'ADD',N'DISCONTINUE')
                    BEGIN
                        UPDATE A SET EffectiveTo=CASE WHEN A.EffectiveFrom IS NULL OR A.EffectiveFrom<@TargetMonth
                                THEN DATEADD(MONTH,-1,@TargetMonth) ELSE COALESCE(A.EffectiveTo,A.EffectiveFrom) END,
                            CancelledAt=CASE WHEN A.EffectiveFrom>=@TargetMonth THEN SYSDATETIME() ELSE A.CancelledAt END,
                            UpdatedAt=SYSDATETIME()
                        FROM dbo.StudentFeeAssignments A
                        WHERE A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=@PlanFeeTypeId AND A.CancelledAt IS NULL
                            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth;
                    END;
            SET @PlanConcession=(SELECT ConcessionLabel FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@PlanAssignmentId);
            IF @PlanAction = N'CHANGE'
                BEGIN
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @PlanAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @TargetMonth <= @LastBilledMonth
                        BEGIN
                            CLOSE plan_cursor;
                            DEALLOCATE plan_cursor;
                            THROW 59304, 'The destination fees could not be applied from the selected month because that month has already been billed for one or more fees. Choose "next month" instead, or keep the existing fees, then adjust manually in Fee Setup if needed.', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @TargetMonth),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @PlanAssignmentId
                           AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);
                    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo, ConcessionLabel)
                    VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, @PlanDiscount, @PlanDestFrequency, @PlanRemarks, @TargetMonth, NULL, @PlanConcession);
                    SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());

                END
            ELSE
                IF @PlanAction = N'ADD'
                    BEGIN
                        INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                        VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, 0, @PlanDestFrequency, N'Added via transfer fee sync', @TargetMonth, NULL);
                        SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());

                    END
                ELSE
                    IF @PlanAction = N'DISCONTINUE'
                        BEGIN
                            SET @DiscontinueEffectiveTo = DATEADD(MONTH, -1, @TargetMonth);
                            SELECT @LastBilledMonth = MAX(BillingMonth)
                            FROM   dbo.BillingCharges
                            WHERE  StudentFeeAssignmentId = @PlanAssignmentId;
                            IF @LastBilledMonth IS NOT NULL
                               AND @DiscontinueEffectiveTo < @LastBilledMonth
                                BEGIN
                                    CLOSE plan_cursor;
                                    DEALLOCATE plan_cursor;
                                    THROW 59305, 'One or more fees that no longer apply at the destination could not be closed because a later month has already been billed for them. Please review those fees manually in Fee Setup.', 1;
                                END
                            UPDATE dbo.StudentFeeAssignments
                            SET    EffectiveTo = @DiscontinueEffectiveTo,
                                   UpdatedAt   = SYSDATETIME()
                            WHERE  StudentFeeAssignmentId = @PlanAssignmentId
                                   AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);

                        END
            FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
        END
    CLOSE plan_cursor;
    DEALLOCATE plan_cursor;

END

GO
CREATE OR ALTER PROCEDURE dbo.TransferEnrollmentClass
@UserId INT, @EnrollmentId INT, @NewClassSectionId INT, @TransferDate DATE, @Remarks NVARCHAR (500)=NULL, @FeePolicy NVARCHAR (20)=NULL, @FeeSyncReason NVARCHAR (500)=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @EnrollmentId <= 0
        THROW 52301, 'A valid enrollment is required.', 1;
    IF @NewClassSectionId <= 0
        THROW 52302, 'A valid destination class/section is required.', 1;
    IF @TransferDate IS NULL
        THROW 52303, 'Transfer date is required.', 1;
    IF @TransferDate > CONVERT (DATE, SYSDATETIME())
        THROW 52304, 'Transfer date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @EnrollmentStartDate AS DATE;
    DECLARE @AdmissionId AS INT, @OldClassSectionId AS INT, @AcademicYearId AS INT, @OldCampusId AS INT, @NewCampusId AS INT, @OldClassId AS INT, @NewClassId AS INT, @OldStatus AS NVARCHAR (30);
    SELECT @EnrollmentStartDate=E.EnrollmentDate,
           @AdmissionId = E.AdmissionId,
           @OldClassSectionId = E.ClassSectionId,
           @AcademicYearId = E.AcademicYearId,
           @OldStatus = E.Status,
           @OldCampusId = CCS.CampusId,
           @OldClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 52305, 'The selected enrollment is not active or does not exist.', 1;
    IF @TransferDate < @EnrollmentStartDate
        THROW 59308,'Transfer date cannot be before the enrollment date.',1;
    SELECT @NewCampusId = CCS.CampusId,
           @NewClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @NewClassSectionId;
    IF @NewCampusId IS NULL
        THROW 52306, 'The destination class/section is invalid.', 1;
    IF @OldCampusId <> @NewCampusId
        THROW 52307, 'Transfer Class cannot move a student to another campus. Use the appropriate enrollment workflow.', 1;
    IF @OldClassSectionId = @NewClassSectionId
        THROW 52308, 'The destination class/section is the same as the current placement.', 1;
    UPDATE dbo.Enrollments
    SET    ClassSectionId = @NewClassSectionId,
           UpdatedAt      = SYSDATETIME(),
           Remarks        = COALESCE (NULLIF (@Remarks, N''), Remarks)
    WHERE  EnrollmentId = @EnrollmentId
           AND Status = N'Active';
    IF @@ROWCOUNT <> 1
        THROW 52309, 'The active enrollment could not be transferred.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'TRANSFER_CLASS', @TransferDate, @OldClassSectionId, @NewClassSectionId, @OldStatus, N'Active', @Remarks, @UserId);

    IF @FeePolicy IS NOT NULL
        BEGIN
            EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
            DECLARE @FeeLockResult AS INT;
            DECLARE @FeeLockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @FeeLockResult = sys.sp_getapplock @Resource = @FeeLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @FeeLockResult < 0
                THROW 52310, 'This enrollment is currently being billed or updated elsewhere. Please try again.', 1;
            EXECUTE dbo.SyncTransferStudentFees_Internal @UserId = @UserId, @EnrollmentId = @EnrollmentId, @FeePolicy = @FeePolicy, @Reason = @FeeSyncReason, @EffectiveDate = @TransferDate, @FeeEffectiveMonth=@FeeEffectiveMonth, @ReplaceScheduledFees=@ReplaceScheduledFees,@PaidReductionAction=@PaidReductionAction;
        END

COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @NewClassSectionId AS NewClassSectionId;
END

GO
CREATE OR ALTER PROCEDURE dbo.TransferEnrollmentCampus
@UserId INT, @EnrollmentId INT, @NewClassSectionId INT, @TransferDate DATE, @Remarks NVARCHAR (500), @FeePolicy NVARCHAR (20)=NULL, @FeeSyncReason NVARCHAR (500)=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @EnrollmentId <= 0
        THROW 52501, 'A valid enrollment is required.', 1;
    IF @NewClassSectionId <= 0
        THROW 52502, 'A valid destination campus, class and section are required.', 1;
    IF @TransferDate IS NULL
        THROW 52503, 'Transfer date is required.', 1;
    IF @TransferDate > CONVERT (DATE, SYSDATETIME())
        THROW 52504, 'Transfer date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @EnrollmentStartDate AS DATE;
    DECLARE @AdmissionId AS INT, @OldClassSectionId AS INT, @AcademicYearId AS INT, @OldCampusId AS INT, @NewCampusId AS INT, @OldStatus AS NVARCHAR (30);
    SELECT @EnrollmentStartDate=E.EnrollmentDate,
           @AdmissionId = E.AdmissionId,
           @OldClassSectionId = E.ClassSectionId,
           @AcademicYearId = E.AcademicYearId,
           @OldStatus = E.Status,
           @OldCampusId = CCS.CampusId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 52505, 'The selected enrollment is not active or does not exist.', 1;
    IF @TransferDate < @EnrollmentStartDate
        THROW 59308,'Transfer date cannot be before the enrollment date.',1;
    SELECT @NewCampusId = CCS.CampusId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @NewClassSectionId;
    IF @NewCampusId IS NULL
        THROW 52506, 'The destination class/section does not exist or is inactive.', 1;
    IF @OldCampusId = @NewCampusId
        THROW 52507, 'The destination campus is the same as the student''s current campus. Use Transfer Class or Transfer Section instead.', 1;
    IF @OldClassSectionId = @NewClassSectionId
        THROW 52508, 'The destination class/section is the same as the current placement.', 1;
    UPDATE dbo.Enrollments
    SET    ClassSectionId = @NewClassSectionId,
           UpdatedAt      = SYSDATETIME(),
           Remarks        = COALESCE (NULLIF (@Remarks, N''), Remarks)
    WHERE  EnrollmentId = @EnrollmentId
           AND Status = N'Active';
    IF @@ROWCOUNT <> 1
        THROW 52509, 'The active enrollment could not be transferred.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'TRANSFER_CAMPUS', @TransferDate, @OldClassSectionId, @NewClassSectionId, @OldStatus, N'Active', @Remarks, @UserId);

    IF @FeePolicy IS NOT NULL
        BEGIN
            EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
            DECLARE @FeeLockResult AS INT;
            DECLARE @FeeLockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @FeeLockResult = sys.sp_getapplock @Resource = @FeeLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @FeeLockResult < 0
                THROW 52510, 'This enrollment is currently being billed or updated elsewhere. Please try again.', 1;
            EXECUTE dbo.SyncTransferStudentFees_Internal @UserId = @UserId, @EnrollmentId = @EnrollmentId, @FeePolicy = @FeePolicy, @Reason = @FeeSyncReason, @EffectiveDate = @TransferDate, @FeeEffectiveMonth=@FeeEffectiveMonth, @ReplaceScheduledFees=@ReplaceScheduledFees,@PaidReductionAction=@PaidReductionAction;
        END

COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @NewClassSectionId AS NewClassSectionId;
END

GO
CREATE OR ALTER PROCEDURE dbo.RunAutomaticMonthlyBilling
 @UserId int,@Preview bit=1,@IncludeMissed bit=0,@ReviewMissed bit=0,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @IncludeMissed=1 OR @ReviewMissed=1 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 DECLARE @Month date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),@Start date,@Reviewed date,@Lock int,@Locked int;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Monthly billing is already running. Try again shortly.',1;
  SELECT @Start=StartMonth,@Reviewed=RecoveryReviewedThrough FROM dbo.BillingAutomationState WITH(UPDLOCK,HOLDLOCK) WHERE StateId=1;
  IF @Start IS NULL THROW 54747,'Automatic billing activation is missing. Contact the administrator.',1;
  -- Locks protect the preview token, selected rates and eligibility through posting.
  SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE Status=N'Active';
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE Status=N'Active';
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE CancelledAt IS NULL AND Frequency=N'Monthly';
  SELECT @Locked=COUNT(*) FROM dbo.AcademicYears WITH(HOLDLOCK);
  ;WITH Months AS(SELECT @Start BillingMonth WHERE @Start<=@Month UNION ALL
    SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE BillingMonth<@Month)
  SELECT M.BillingMonth,C.* INTO #Candidates FROM Months M
  CROSS APPLY dbo.fn_MonthlyBillingCandidates(M.BillingMonth,0) C
  WHERE C.BlockReason IS NULL AND C.BillingChargeId IS NULL AND C.PreviouslyCancelled=0
  OPTION(MAXRECURSION 0);
  DECLARE @Raw nvarchar(max)=CONCAT(CONVERT(varchar(10),@Month,120),CONVERT(varchar(10),@Reviewed,120),
   (SELECT * FROM #Candidates ORDER BY BillingMonth,EnrollmentId,FeeTypeId FOR JSON PATH,INCLUDE_NULL_VALUES));
  DECLARE @Snapshot varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
  IF @Preview=0 AND (@ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot)
   THROW 54748,'Billing changed since review. Review the current amounts again; nothing was posted.',1;
  SELECT C.AdmissionId INTO #CreditReady FROM dbo.StudentCreditEntries C
   JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
   WHERE S.Status=N'Active' AND S.LegacySetupPending=0 AND EXISTS(
     SELECT 1 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
     WHERE B.AdmissionId=C.AdmissionId AND B.Status<>N'Cancelled' AND B.BillingMonth=@Month
      AND B.ChargeDate<=CONVERT(date,GETDATE()) AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
      AND E.Status=N'Active' AND E.BillingReady=1 AND E.BillingStartDate IS NOT NULL AND E.PlacementConfirmedAt IS NOT NULL)
   GROUP BY C.AdmissionId HAVING SUM(C.Amount)>0;
  DECLARE @Created int=0;
  IF @Preview=0
  BEGIN
   DECLARE @RunMonth date,@Year int,@Selection dbo.PostingSelectionType;
   DECLARE @Result TABLE(BillingMonth date,RowsRequested int,ChargesCreated int,RowsNotPosted int);
   DECLARE Cycles CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT BillingMonth,AcademicYearId FROM #Candidates
    WHERE BillingMonth=@Month OR @IncludeMissed=1 ORDER BY BillingMonth,AcademicYearId;
   OPEN Cycles; FETCH NEXT FROM Cycles INTO @RunMonth,@Year;
   WHILE @@FETCH_STATUS=0
   BEGIN
    DELETE FROM @Selection;
    INSERT @Selection SELECT EnrollmentId,FeeTypeId,BillingMonth FROM #Candidates WHERE BillingMonth=@RunMonth AND AcademicYearId=@Year;
    INSERT @Result EXEC dbo.PostSelectedMonthlyFees @UserId,@RunMonth,@Selection,N'Automatic monthly billing',@Year,@AllowRepost=0;
    FETCH NEXT FROM Cycles INTO @RunMonth,@Year;
   END;
   CLOSE Cycles; DEALLOCATE Cycles;
   SELECT @Created=COALESCE(SUM(ChargesCreated),0) FROM @Result;
   IF EXISTS(SELECT 1 FROM @Result WHERE RowsNotPosted<>0) THROW 54749,'Eligibility changed during billing. Nothing was posted; retry the review.',1;
   DECLARE @CreditAccount int;
   DECLARE AvailableCredits CURSOR LOCAL FAST_FORWARD FOR SELECT AdmissionId FROM #CreditReady ORDER BY AdmissionId;
   OPEN AvailableCredits;FETCH NEXT FROM AvailableCredits INTO @CreditAccount;
   WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.ApplyStudentCredit_Internal @CreditAccount,@UserId;FETCH NEXT FROM AvailableCredits INTO @CreditAccount;END;
   CLOSE AvailableCredits;DEALLOCATE AvailableCredits;
   IF @ReviewMissed=1 UPDATE dbo.BillingAutomationState SET RecoveryReviewedThrough=DATEADD(month,-1,@Month),UpdatedBy=@UserId,UpdatedAt=SYSDATETIME() WHERE StateId=1;
  END;
  SELECT @Snapshot Snapshot,@Month BillingMonth,@Created ChargesCreated,(SELECT COUNT(*) FROM #CreditReady) PendingCreditApplications,
   (SELECT COUNT(*) FROM #Candidates WHERE BillingMonth=@Month) CurrentCharges,
   (SELECT COUNT(*) FROM #Candidates WHERE BillingMonth<@Month AND (@Reviewed IS NULL OR BillingMonth>@Reviewed)) MissedCharges,
   (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM #Candidates WHERE BillingMonth=@Month) CurrentAmount,
   (SELECT COALESCE(SUM(Amount-DiscountAmount),0) FROM #Candidates WHERE BillingMonth<@Month) MissedAmount;
  SELECT BillingMonth,COUNT(DISTINCT AdmissionId) Students,COUNT(*) Charges,SUM(Amount-DiscountAmount) Amount
   FROM #Candidates GROUP BY BillingMonth ORDER BY BillingMonth;
  -- One exception per student; keep all rows available to the administrative workspace.
  SELECT S.AdmissionId,S.StudentName,CONVERT(bit,S.LegacySetupPending) IsLegacyReference,CONVERT(nvarchar(200),CASE
    WHEN S.LegacySetupPending=1 THEN N'Legacy reference — activate only if billing is required'
    WHEN E.EnrollmentId IS NULL THEN N'Add an active academic placement'
    WHEN E.BillingReady=0 THEN N'Complete billing setup or confirm return'
    WHEN E.PlacementConfirmedAt IS NULL THEN N'Confirm academic placement'
    WHEN E.BillingStartDate IS NULL THEN N'Set approved billing start date'
    WHEN NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=E.EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly') THEN N'Review monthly fee assignments'
    ELSE N'Review billing dates' END) Reason
   FROM dbo.Students S OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active' ORDER BY E.EnrollmentId DESC) E
   WHERE S.Status=N'Active' AND (S.LegacySetupPending=1 OR E.EnrollmentId IS NULL OR E.BillingReady=0
    OR E.PlacementConfirmedAt IS NULL OR E.BillingStartDate IS NULL
    OR E.BillingStartDate<DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1)
    OR NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=E.EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly'))
   ORDER BY S.AdmissionId;
  COMMIT;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER FUNCTION dbo.fn_ExpectedRecurringChargeMonths(@EnrollmentId int,@AsOfDate date)
RETURNS TABLE AS RETURN
 WITH Numbers AS(SELECT 0 N UNION ALL SELECT N+1 FROM Numbers WHERE N<23),
 EnrollmentWindow AS(
  SELECT E.EnrollmentId,DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1) StartMonth,
   DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1) YearStart,
   DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1) YearEnd,
   DATEFROMPARTS(YEAR(@AsOfDate),MONTH(@AsOfDate),1) AsOfMonth,
   CASE WHEN E.LeavingDate IS NULL THEN NULL ELSE
    DATEADD(month,-1,DATEFROMPARTS(YEAR(dbo.GetMonthlyBillingStartDate(E.LeavingDate)),MONTH(dbo.GetMonthlyBillingStartDate(E.LeavingDate)),1)) END LastMonth
  FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE E.EnrollmentId=@EnrollmentId AND E.BillingStartDate IS NOT NULL AND E.BillingReady=1
   AND E.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
   AND E.Status IN(N'Active',N'Promoted',N'Completed',N'Left'))
 SELECT A.StudentFeeAssignmentId,A.FeeTypeId,M.BillingMonth FROM EnrollmentWindow W CROSS JOIN Numbers N
 CROSS APPLY(SELECT DATEADD(month,N.N,W.YearStart) BillingMonth) M
 JOIN dbo.StudentFeeAssignments A ON A.EnrollmentId=W.EnrollmentId
 WHERE M.BillingMonth>=W.StartMonth AND M.BillingMonth<=W.YearEnd AND M.BillingMonth<=W.AsOfMonth
  AND (W.LastMonth IS NULL OR M.BillingMonth<=W.LastMonth)
  AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
  AND (A.EffectiveFrom IS NULL OR M.BillingMonth>=A.EffectiveFrom)
  AND (A.EffectiveTo IS NULL OR M.BillingMonth<=A.EffectiveTo);

GO
CREATE OR ALTER PROCEDURE dbo.PostAdditionalFee
@UserId INT, @RequestId UNIQUEIDENTIFIER, @FeeTypeId INT, @ChargeDate DATE, @Amount DECIMAL (12, 2), @DiscountAmount DECIMAL (12, 2), @Reason NVARCHAR (500), @Selection dbo.ManualChargeSelectionType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @RequestId IS NULL
       OR NOT EXISTS (SELECT 1
                      FROM   @Selection)
        THROW 53020, 'Select at least one student for this charge.', 1;
    IF @ChargeDate IS NULL
       OR @ChargeDate > CONVERT (DATE, SYSDATETIME())
        THROW 53021, 'Enter a valid charge date that is not in the future.', 1;
    IF @Amount IS NULL
       OR @Amount <= 0
       OR @DiscountAmount < 0
       OR @DiscountAmount > @Amount
        THROW 53022, 'Enter a positive amount and a valid discount.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 53023, 'Describe what is being charged.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeTypes
                   WHERE  FeeTypeId = @FeeTypeId)
        THROW 53024, 'The selected fee type is unavailable.', 1;
    BEGIN TRANSACTION;
    DECLARE @LockResult AS INT;
    DECLARE @LockResource AS NVARCHAR (255) = N'FMSC.AdditionalFee.' + CONVERT (NVARCHAR (36), @RequestId);
    EXECUTE @LockResult = sys.sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
    IF @LockResult < 0
        THROW 53019, 'This fee posting is already being processed. Try again.', 1;
    DECLARE @BadReason AS NVARCHAR (400);
    DECLARE @BadName AS NVARCHAR (200);
    DECLARE @BadAdmissionId AS INT;
    DECLARE @BadAdmissionNumber varchar(40);
    SELECT   TOP (1) @BadAdmissionId = E.AdmissionId,
                     @BadName = ST.StudentName,
                     @BadAdmissionNumber = ST.AdmissionNumber,
                     @BadReason = CASE WHEN E.EnrollmentId IS NULL THEN N'a selected enrolment no longer exists.' WHEN E.Status <> N'Active' THEN N'their enrolment is ' + E.Status + N', not Active.' WHEN E.BillingReady <> 1 THEN N'their enrolment is not ready for billing yet.'
 WHEN E.PlacementConfirmedAt IS NULL OR ST.LegacySetupPending=1 THEN N'their placement has not completed billing activation.'
 WHEN @ChargeDate < COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate) THEN N'the charge date is before their enrolment start (' + CONVERT (NVARCHAR (11), COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate), 106) + N').' ELSE N'their active enrolment is in academic year ' + AY.YearName + N', which ended on ' + CONVERT (NVARCHAR (11), AY.EndDate, 106) + N'. Promote or re-admit the student into the current academic year first.' END
    FROM     @Selection AS S
             LEFT OUTER JOIN
             dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
             ON E.EnrollmentId = S.EnrollmentId
             LEFT OUTER JOIN
             dbo.AcademicYears AS AY
             ON AY.AcademicYearId = E.AcademicYearId
             LEFT OUTER JOIN
             dbo.Students AS ST
             ON ST.AdmissionId = E.AdmissionId
    WHERE    E.EnrollmentId IS NULL
             OR E.Status <> N'Active'
             OR E.BillingReady <> 1
             OR E.PlacementConfirmedAt IS NULL OR ST.LegacySetupPending=1
             OR @ChargeDate < COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate)
             OR @ChargeDate > AY.EndDate
    ORDER BY S.EnrollmentId;
    IF @BadReason IS NOT NULL
        BEGIN
            DECLARE @Msg AS NVARCHAR (2048) = N'Cannot post this charge for ' + COALESCE (@BadName + N' (admission ' + @BadAdmissionNumber + N')', N'one of the selected students') + N': ' + @BadReason;
            THROW 53025, @Msg, 1;
        END
    IF EXISTS (SELECT 1
               FROM   dbo.BillingCharges
               WHERE  ManualRequestId = @RequestId)
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.ManualFeePostingRequests AS R
                           WHERE  R.RequestId = @RequestId
                                  AND R.FeeTypeId = @FeeTypeId
                                  AND R.ChargeDate = @ChargeDate
                                  AND R.Amount = @Amount
                                  AND R.DiscountAmount = @DiscountAmount
                                  AND R.Reason = @Reason)
                THROW 53026, 'This posting request was already used with different fee details.', 1;
            IF EXISTS (SELECT 1
                       FROM   @Selection AS S
                       WHERE  NOT EXISTS (SELECT 1
                                          FROM   dbo.BillingCharges AS BC
                                          WHERE  BC.ManualRequestId = @RequestId
                                                 AND BC.EnrollmentId = S.EnrollmentId
                                                 AND BC.FeeTypeId = @FeeTypeId))
               OR EXISTS (SELECT 1
                          FROM   dbo.BillingCharges AS BC
                          WHERE  BC.ManualRequestId = @RequestId
                                 AND NOT EXISTS (SELECT 1
                                                 FROM   @Selection AS S
                                                 WHERE  S.EnrollmentId = BC.EnrollmentId))
                THROW 53026, 'This posting request was already used for different students.', 1;
            COMMIT TRANSACTION;
            SELECT BillingChargeId,
                   EnrollmentId,
                   AdmissionId,
                   GrossAmount,
                   DiscountAmount,
                   ChargeDate
            FROM   dbo.BillingCharges
            WHERE  ManualRequestId = @RequestId;
            RETURN;
        END
    INSERT  dbo.ManualFeePostingRequests (RequestId, FeeTypeId, ChargeDate, Amount, DiscountAmount, Reason)
    VALUES                              (@RequestId, @FeeTypeId, @ChargeDate, @Amount, @DiscountAmount, @Reason);
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT E.EnrollmentId,
           E.AdmissionId,
           @FeeTypeId,
           @Amount,
           0,
           N'As Needed',
           N'Created for additional charges'
    FROM   @Selection AS S
           INNER JOIN
           dbo.Enrollments AS E
           ON E.EnrollmentId = S.EnrollmentId
    WHERE  NOT EXISTS (SELECT 1
                       FROM   dbo.StudentFeeAssignments AS SFA WITH (UPDLOCK, HOLDLOCK)
                       WHERE  SFA.EnrollmentId = E.EnrollmentId
                              AND SFA.FeeTypeId = @FeeTypeId
                              AND SFA.EffectiveTo IS NULL);
    DECLARE @BillingMonth AS DATE = DATEFROMPARTS(YEAR(@ChargeDate), MONTH(@ChargeDate), 1);
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, Remarks, ChargeKind, ManualRequestId)
    SELECT SFA.StudentFeeAssignmentId,
           E.EnrollmentId,
           E.AdmissionId,
           @FeeTypeId,
           @BillingMonth,
           @ChargeDate,
           @ChargeDate,
           @Amount,
           @DiscountAmount,
           @Reason,
           N'Manual',
           @RequestId
    FROM   @Selection AS S
           INNER JOIN
           dbo.Enrollments AS E
           ON E.EnrollmentId = S.EnrollmentId
           INNER JOIN
           dbo.StudentFeeAssignments AS SFA
           ON SFA.EnrollmentId = E.EnrollmentId
              AND SFA.FeeTypeId = @FeeTypeId
              AND SFA.EffectiveTo IS NULL;
    IF @@ROWCOUNT <> (SELECT COUNT(*)
                      FROM   @Selection)
        THROW 53027, 'Not every selected student received the charge.', 1;

    DECLARE @CreditStudent int;
    DECLARE CreditStudents CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT E.AdmissionId FROM @Selection S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId ORDER BY E.AdmissionId;
    OPEN CreditStudents;FETCH NEXT FROM CreditStudents INTO @CreditStudent;
    WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.ApplyStudentCredit_Internal @CreditStudent,@UserId;FETCH NEXT FROM CreditStudents INTO @CreditStudent;END;
    CLOSE CreditStudents;DEALLOCATE CreditStudents;
    COMMIT TRANSACTION;
    SELECT BillingChargeId,
           EnrollmentId,
           AdmissionId,
           GrossAmount,
           DiscountAmount,
           ChargeDate
    FROM   dbo.BillingCharges
    WHERE  ManualRequestId = @RequestId;
END

GO
CREATE OR ALTER PROCEDURE dbo.GetAdditionalFeeCandidates
    @UserId int,
    @AcademicYearId int,
    @CampusId int = 0,
    @ClassId int = 0,
    @SectionId int = 0,
    @ChargeDate date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @ChargeDate=COALESCE(@ChargeDate,CONVERT(date,GETDATE()));
    IF @ChargeDate>CONVERT(date,GETDATE()) THROW 53021,'Enter a charge date that is not in the future.',1;
    EXEC dbo.RequirePermission @UserId, N'Fees.Collect';
    IF NOT EXISTS (SELECT 1 FROM dbo.AcademicYears
                   WHERE AcademicYearId = @AcademicYearId AND IsCurrent = 1)
        THROW 53040, 'Additional fees must use the current academic year. Reload the page if it has changed.', 1;

    SELECT E.EnrollmentId, S.AdmissionNumber, S.RegistrationNumber, S.StudentName,
           S.FatherName, C.CampusName, CL.ClassName, SEC.SectionName,
           E.BillingStartDate
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses C ON C.CampusId = CCS.CampusId
    JOIN dbo.Classes CL ON CL.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    WHERE E.AcademicYearId = @AcademicYearId
      AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
      AND E.Status = N'Active' AND E.BillingReady = 1
      AND E.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
      AND @ChargeDate >= COALESCE(CONVERT(date,E.ReturnConfirmedAt),E.EnrollmentDate)
      AND EXISTS(SELECT 1 FROM dbo.AcademicYears AY WHERE AY.AcademicYearId=E.AcademicYearId AND @ChargeDate BETWEEN AY.StartDate AND AY.EndDate)
    ORDER BY C.CampusName, CL.DisplayOrder, SEC.DisplayOrder,
             S.AdmissionNumberBase, S.AdmissionNumberSuffix, S.StudentName;
END;

GO
CREATE OR ALTER PROCEDURE dbo.ReAdmitStudent
@UserId INT, @AdmissionId INT, @CampusId INT, @AcademicYearId INT, @ClassSectionId INT, @EnrollmentDate DATE, @Reason NVARCHAR (500), @UsesTransport BIT, @MonthlyStartMonth date=NULL, @ChargeYearlyFees bit=NULL, @ReviewedRates nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @AdmissionId <= 0
        THROW 56510, 'A valid student is required.', 1;
    IF @CampusId <= 0
        THROW 56511, 'A valid campus is required.', 1;
    IF @AcademicYearId <= 0
        THROW 56512, 'A valid academic year is required.', 1;
    IF @ClassSectionId <= 0
        THROW 56513, 'A valid class section is required.', 1;
    IF @EnrollmentDate IS NULL
        THROW 56514, 'Enrollment date is required.', 1;
    IF @EnrollmentDate > CONVERT (DATE, SYSDATETIME())
        THROW 56515, 'Enrollment date cannot be in the future.', 1;

    IF @MonthlyStartMonth IS NULL OR @ChargeYearlyFees IS NULL
        THROW 57100, 'Choose the monthly start month and yearly-fee decision before readmission.',1;
    BEGIN TRANSACTION;
    DECLARE @OldStudentStatus AS NVARCHAR (30), @OldWithdrawalDate AS DATE, @OldEnrollmentId AS INT = NULL, @ClassId AS INT, @SectionCampusId AS INT, @MaxPriorLeavingDate AS DATE = NULL, @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @OldStudentStatus = S.Status,
           @OldWithdrawalDate = S.WithdrawalDate
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @OldStudentStatus IS NULL
        THROW 56516, 'The student was not found.', 1;
    SELECT TOP (1) @OldEnrollmentId = E.EnrollmentId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
    WHERE  E.AdmissionId = @AdmissionId
           AND E.Status = N'Active';
    IF @OldEnrollmentId IS NOT NULL
        THROW 56517, 'The student already has an active enrollment.', 1;
    IF @OldStudentStatus = N'Active'
        THROW 56524, 'The student is already active.', 1;
    SELECT @MaxPriorLeavingDate = MAX(E2.LeavingDate)
    FROM   dbo.Enrollments AS E2
    WHERE  E2.AdmissionId = @AdmissionId
           AND E2.LeavingDate IS NOT NULL;
    IF @MaxPriorLeavingDate IS NOT NULL
       AND @EnrollmentDate <= @MaxPriorLeavingDate
        BEGIN
            ROLLBACK;
            THROW 56527, 'The re-admission enrollment date must be after the student''s most recent leaving date on file. Correct the prior enrollment''s leaving date first if this re-admission is genuinely meant to start on or before it.', 1;
        END
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @CampusId)
        THROW 56518, 'The selected campus is not active.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 56519, 'The selected academic year is not active.', 1;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @EnrollmentDate < @YearStartDate
       OR @EnrollmentDate > @YearEndDate
        BEGIN
            ROLLBACK;
            THROW 56528, 'The re-admission enrollment date must fall within the selected academic year''s date range.', 1;
        END
    SELECT @SectionCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @SectionCampusId IS NULL
        THROW 56521, 'The selected class section is invalid.', 1;
    IF @SectionCampusId <> @CampusId
        THROW 56522, 'The selected class section does not belong to the selected campus.', 1;
    DECLARE @MissingMandatoryFees AS NVARCHAR (1000);
    SELECT @MissingMandatoryFees = STRING_AGG(FT.FeeTypeName, N', ')
    FROM   dbo.FeeTypes AS FT
    WHERE  FT.IsMandatory = 1
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                           WHERE  FC.CampusId = @CampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.FeeTypeId = FT.FeeTypeId);
    IF @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            ROLLBACK;
            THROW 56525, @MandatoryFeeErrorMsg, 1;
        END

    IF @ReviewedRates IS NULL OR ISJSON(@ReviewedRates)<>1
      THROW 57107,'Review the readmission rates before confirming.',1;
    DECLARE @Rates TABLE(FeeTypeId int,Amount decimal(12,2),Frequency nvarchar(40));
    INSERT @Rates SELECT FeeTypeId,Amount,Frequency FROM OPENJSON(@ReviewedRates)
       WITH(FeeTypeId int,Amount decimal(12,2),Frequency nvarchar(40));
    IF EXISTS(SELECT FC.FeeTypeId,FC.Amount,FC.Frequency FROM dbo.FeeConfigurations FC WITH(UPDLOCK,HOLDLOCK)
       JOIN dbo.FeeTypes FT ON FT.FeeTypeId=FC.FeeTypeId WHERE FC.CampusId=@CampusId AND FC.ClassId=@ClassId
       AND (@UsesTransport=1 OR FT.IsTransportLinked=0) EXCEPT SELECT * FROM @Rates)
       OR EXISTS(SELECT * FROM @Rates EXCEPT SELECT FC.FeeTypeId,FC.Amount,FC.Frequency FROM dbo.FeeConfigurations FC
       JOIN dbo.FeeTypes FT ON FT.FeeTypeId=FC.FeeTypeId WHERE FC.CampusId=@CampusId AND FC.ClassId=@ClassId
       AND (@UsesTransport=1 OR FT.IsTransportLinked=0))
      THROW 57108,'Readmission rates changed. Review them again; nothing was saved.',1;
    DECLARE @BillingStartDate date;
    IF DAY(@MonthlyStartMonth)<>1 OR @MonthlyStartMonth<DATEFROMPARTS(YEAR(@EnrollmentDate),MONTH(@EnrollmentDate),1)
       OR @MonthlyStartMonth>DATEFROMPARTS(YEAR(@YearEndDate),MONTH(@YearEndDate),1)
        THROW 57101,'Choose a monthly start month within the remaining academic year.',1;
    SET @BillingStartDate=@MonthlyStartMonth;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, Remarks, UsesTransport, BillingReady, PlacementConfirmedAt)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @EnrollmentDate, @BillingStartDate, N'Active', @Reason, @UsesTransport, 1, SYSDATETIME());
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT @EnrollmentId,
           @AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           CASE WHEN FC.Frequency=N'Yearly' AND @ChargeYearlyFees=0 THEN FC.Amount ELSE 0 END,
           FC.Frequency,
           CASE WHEN FC.Frequency=N'Yearly' AND @ChargeYearlyFees=0 THEN N'Yearly fee explicitly waived at re-admission' ELSE N'Created by re-admission' END
    FROM   dbo.FeeConfigurations AS FC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FC.CampusId = @CampusId
           AND FC.ClassId = @ClassId
           AND (@UsesTransport = 1
                OR FT.IsTransportLinked = 0);
    EXECUTE dbo.PostEnrollmentOpeningCharges @EnrollmentId = @EnrollmentId;
    UPDATE dbo.Students
    SET    Status         = N'Active',
           WithdrawalDate = NULL,
           UpdatedAt      = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    IF @@ROWCOUNT <> 1
        THROW 56523, 'The student status could not be updated.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'RE-ADMIT', @EnrollmentDate, NULL, @ClassSectionId, NULL, N'Active', @Reason, @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, @OldStudentStatus, N'Active', N'Re-admission', @EnrollmentDate, @Reason, @UserId);

COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @EnrollmentDate AS EnrollmentDate;
END

GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'19')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'19',SYSDATETIME(),SUSER_SNAME(),N'Shared cutoff policy, immediate admission charges and reviewed transfer/leaving differences.');
GO
