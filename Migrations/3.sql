-- Legacy setup is for current students. Former students must use readmission.
IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), 0) <> 2
    THROW 51000, 'The readmission safeguard requires schema 2.', 1;
GO

CREATE OR ALTER PROCEDURE dbo.ActivateStudentBilling
 @UserId int,@AdmissionId int,@EnrollmentId int=0,@YearId int,@PlacementId int,
 @StartDate date,@BillingStart date,@StudentName nvarchar(500),@FatherName nvarchar(500),
 @Fees nvarchar(max),@Reason nvarchar(500),@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@AdmissionFeeAlreadyPaid bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
 BEGIN TRAN;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 DECLARE @Pending bit,@AdmissionDate date,@Locked int,@StudentStatus nvarchar(30);
 SELECT @Pending=LegacySetupPending,@AdmissionDate=AdmissionDate,@StudentStatus=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @StudentStatus IS NOT NULL AND @StudentStatus<>N'Active' THROW 59973,'Only active students can complete legacy setup. Use Re-Admit for a returning student.',1;
 IF @Pending IS NULL OR @Pending=0 THROW 59960,'This student is not awaiting legacy setup. Reload Admission; use restricted corrections for activated students.',1;
 IF NULLIF(LTRIM(RTRIM(@StudentName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@FatherName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 59961,'Enter student name, father name and a setup reason.',1;
 SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @EnrollmentId>0 AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND AdmissionId=@AdmissionId AND Status=N'Active' AND PlacementConfirmedAt IS NULL)
  THROW 59962,'Choose an unconfirmed active placement or add a missing placement. History cannot be overwritten here.',1;
 IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@EnrollmentId AND (Status=N'Active' OR LeavingDate IS NULL OR LeavingDate>@StartDate))
  THROW 59963,'Another placement is active or overlaps this start. Review placement history first.',1;
 IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId) OR EXISTS(SELECT 1 FROM dbo.FeePayments WHERE EnrollmentId=@EnrollmentId)
  THROW 59964,'This placement already has financial history. Use restricted corrections.',1;
 DECLARE @YearStart date,@YearEnd date,@Campus int;
 SELECT @YearStart=StartDate,@YearEnd=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@YearId;
 SELECT @Campus=CampusId FROM dbo.CampusClassSections WITH(HOLDLOCK) WHERE ClassSectionId=@PlacementId;
 IF @YearStart IS NULL OR @Campus IS NULL OR @StartDate IS NULL OR @BillingStart IS NULL OR @StartDate<@AdmissionDate OR @StartDate<@YearStart OR @StartDate>@YearEnd OR @StartDate>CONVERT(date,GETDATE()) OR @BillingStart<@StartDate OR @BillingStart>@YearEnd
  THROW 59965,'Placement and billing dates must fit the admission and academic year; placement cannot start in the future.',1;
 -- Placement is class history. Billing date alone determines the fee cutoff.
 DECLARE @MonthlyStart date=dbo.GetMonthlyBillingStartDate(@BillingStart);
 IF ISJSON(@Fees)<>1 THROW 59966,'Review valid fee rows before activation.',1;
 DECLARE @F TABLE(FeeTypeId int PRIMARY KEY,Amount decimal(12,2),DiscountAmount decimal(12,2),Frequency nvarchar(30));
 INSERT @F SELECT FeeTypeId,Amount,DiscountAmount,Frequency FROM OPENJSON(@Fees) WITH(FeeTypeId int,Amount decimal(12,2),DiscountAmount decimal(12,2),Frequency nvarchar(30));
 IF NOT EXISTS(SELECT 1 FROM @F) OR EXISTS(SELECT 1 FROM @F F LEFT JOIN dbo.FeeTypes T WITH(HOLDLOCK) ON T.FeeTypeId=F.FeeTypeId WHERE T.FeeTypeId IS NULL OR F.Amount IS NULL OR F.DiscountAmount IS NULL OR F.Frequency IS NULL OR F.Amount<0 OR F.DiscountAmount<0 OR F.DiscountAmount>F.Amount OR F.Amount<>FLOOR(F.Amount) OR F.DiscountAmount<>FLOOR(F.DiscountAmount) OR F.Frequency NOT IN(N'Monthly',N'Yearly',N'As Needed'))
  THROW 59967,'Select fees with valid frequency, whole-rupee amounts and discounts.',1;
 IF @AdmissionFeeAlreadyPaid IS NULL THROW 59970,'Choose whether admission fees were paid before system billing.',1;
 IF @AdmissionFeeAlreadyPaid=1 AND EXISTS(SELECT 1 FROM @F F JOIN dbo.FeeTypes T ON T.FeeTypeId=F.FeeTypeId WHERE T.FeeTypeCode=N'ADMISSION')
  THROW 59971,'Admission fee is marked already paid; remove it from the opening charges.',1;
 IF EXISTS(SELECT 1 FROM dbo.FeeTypes WITH(HOLDLOCK) WHERE IsMandatory=1 AND NOT (@AdmissionFeeAlreadyPaid=1 AND FeeTypeCode=N'ADMISSION') AND FeeTypeId NOT IN(SELECT FeeTypeId FROM @F))
  THROW 59968,'Include all mandatory fees before activation.',1;
 DECLARE @Raw nvarchar(max)=CONCAT(
  (SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentFeeAssignments WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeeTypes ORDER BY FeeTypeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@YearId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.CampusClassSections WHERE ClassSectionId=@PlacementId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT @EnrollmentId EnrollmentId,@YearId YearId,@PlacementId PlacementId,@StartDate StartDate,@BillingStart BillingStart,@StudentName StudentName,@FatherName FatherName,@Reason Reason,@Fees Fees,@AdmissionFeeAlreadyPaid AdmissionFeeAlreadyPaid FOR JSON PATH,INCLUDE_NULL_VALUES));
 DECLARE @Snapshot varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1 BEGIN
  SELECT @Snapshot Snapshot,
   COALESCE((SELECT SUM(Amount-DiscountAmount) FROM @F WHERE Frequency<>N'Monthly' OR (Amount>DiscountAmount AND @MonthlyStart<=@YearEnd)),0) OpeningTotal,
   (SELECT COUNT(*) FROM @F WHERE Frequency<>N'Monthly' OR (Amount>DiscountAmount AND @MonthlyStart<=@YearEnd)) ChargeCount;
  COMMIT; RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59969,'The record or setup changed. Review again before activation.',1;
 -- Clear the lock for this student only, inside the same transaction as fees and charges.
 UPDATE dbo.Students SET StudentName=LTRIM(RTRIM(@StudentName)),FatherName=LTRIM(RTRIM(@FatherName)),Status=N'Active',WithdrawalDate=NULL,LegacySetupPending=0,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 UPDATE dbo.Students SET LegacyVerifiedAt=SYSDATETIME(),LegacyVerifiedBy=@UserId WHERE AdmissionId=@AdmissionId;
 IF @EnrollmentId=0 BEGIN
  INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,BillingStartDate,Status,BillingReady,UsesTransport,PlacementConfirmedAt,Remarks)
   VALUES(@AdmissionId,@YearId,@PlacementId,@StartDate,@MonthlyStart,N'Active',1,0,SYSDATETIME(),@Reason);
  SET @EnrollmentId=CONVERT(int,SCOPE_IDENTITY());
 END ELSE BEGIN
  UPDATE dbo.Enrollments SET AcademicYearId=@YearId,ClassSectionId=@PlacementId,EnrollmentDate=@StartDate,BillingStartDate=@MonthlyStart,BillingReady=1,PlacementConfirmedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME(),Remarks=@Reason WHERE EnrollmentId=@EnrollmentId;
 END;
 UPDATE dbo.StudentFeeAssignments SET CancelledAt=COALESCE(CancelledAt,SYSDATETIME()),
  EffectiveTo=COALESCE(EffectiveTo,EffectiveFrom,@BillingStart),UpdatedAt=SYSDATETIME()
  WHERE EnrollmentId=@EnrollmentId AND (CancelledAt IS NULL OR EffectiveTo IS NULL);
 DECLARE @Month date=DATEFROMPARTS(YEAR(@MonthlyStart),MONTH(@MonthlyStart),1),
         @OneOffMonth date=DATEFROMPARTS(YEAR(@BillingStart),MONTH(@BillingStart),1);
 INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,EffectiveFrom,Remarks)
  SELECT @EnrollmentId,@AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @OneOffMonth END,@Reason FROM @F;
 IF @AdmissionFeeAlreadyPaid=1
  INSERT dbo.StudentAdmissionFeeAcknowledgements(AdmissionId,EnrollmentId,RecordedBy,Reason)
   VALUES(@AdmissionId,@EnrollmentId,@UserId,@Reason);
 -- Activation posts the reviewed opening fees from billing dates, never the historical class start.
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
  SELECT StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
   CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @OneOffMonth END,
   CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @BillingStart END,
   CASE WHEN Frequency=N'Monthly' THEN @Month ELSE @BillingStart END,
   Amount,DiscountAmount,CASE WHEN Frequency=N'Monthly' THEN N'Monthly' ELSE N'Annual' END
  FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL
   AND (Frequency=N'Yearly' OR (Frequency=N'Monthly' AND Amount>DiscountAmount AND @MonthlyStart<=@YearEnd));
 -- Explicitly selected one-time fees are part of the reviewed opening bill.
 INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
  SELECT StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,@OneOffMonth,@BillingStart,@BillingStart,Amount,DiscountAmount,N'Manual'
  FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL AND Frequency=N'As Needed';
 EXEC dbo.ApplyStudentCredit_Internal @AdmissionId,@UserId;
 COMMIT;
 SELECT @EnrollmentId EnrollmentId;
 END TRY
 BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
 END CATCH;
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

    -- Left includes leaving certificates, recorded departures, and final-class completion.
    -- Withdrawn is accepted as a legacy synonym, not a new departure status.
    IF @OldStudentStatus NOT IN (N'Left',N'Withdrawn',N'Inactive')
       OR NOT EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=@AdmissionId
          AND NULLIF(LTRIM(RTRIM(S.AdmissionNumber)),N'') IS NOT NULL)
        THROW 56529, 'Only officially admitted former students who left or became inactive can be readmitted. Cancelled pending registrations must use Reopen Registration.',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WITH(UPDLOCK,HOLDLOCK)
              WHERE H.AdmissionId=@AdmissionId AND H.ResumeDate IS NULL)
        THROW 56530, 'This student is on hold. Use Lift Hold / Resume rather than readmission.',1;

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
    UPDATE dbo.Students
    SET    Status         = N'Active',
           WithdrawalDate = NULL,
           LegacySetupPending = 0,
           UpdatedAt      = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    IF @@ROWCOUNT <> 1
        THROW 56523, 'The student status could not be updated.', 1;
    EXECUTE dbo.PostEnrollmentOpeningCharges @EnrollmentId = @EnrollmentId,@UserId=@UserId;
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

INSERT dbo.DatabaseVersion(VersionNumber, AppliedOn, AppliedBy, Notes)
VALUES (N'3', SYSDATETIME(), SUSER_SNAME(), N'Require readmission for returning students');
