-- Additional-fee eligibility must match the selected charge date.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
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
      AND DATEFROMPARTS(YEAR(@ChargeDate),MONTH(@ChargeDate),1) >= DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)
      AND EXISTS(SELECT 1 FROM dbo.AcademicYears AY WHERE AY.AcademicYearId=E.AcademicYearId AND @ChargeDate BETWEEN AY.StartDate AND AY.EndDate)
    ORDER BY C.CampusName, CL.DisplayOrder, SEC.DisplayOrder,
             S.AdmissionNumberBase, S.AdmissionNumberSuffix, S.StudentName;
END;

GO

-- PostAdditionalFee
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
 WHEN DATEFROMPARTS(YEAR(@ChargeDate),MONTH(@ChargeDate),1)<DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)
 THEN N'billing starts on '+CONVERT(nvarchar(11),COALESCE(E.BillingStartDate,E.EnrollmentDate),106)+N'. Choose a charge in or after that billing month, or reload eligible students.' WHEN @ChargeDate < COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate) THEN N'the charge date is before their enrolment start (' + CONVERT (NVARCHAR (11), COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate), 106) + N').' ELSE N'their active enrolment is in academic year ' + AY.YearName + N', which ended on ' + CONVERT (NVARCHAR (11), AY.EndDate, 106) + N'. Promote or re-admit the student into the current academic year first.' END
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
             OR DATEFROMPARTS(YEAR(@ChargeDate),MONTH(@ChargeDate),1)<DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)
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
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ADDITIONAL_FEE_POSTING', CONVERT (NVARCHAR (36), @RequestId));
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
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'11')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'11',SYSDATETIME(),SUSER_SNAME(),N'Date-aware additional-fee candidates and explicit billing-start validation.');
GO