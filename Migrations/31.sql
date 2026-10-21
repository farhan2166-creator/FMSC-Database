-- A posting hold must return through the reviewed Lift Hold / Resume workflow.
-- No existing student, placement, bill, payment or hold record is changed.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 31 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>30
 THROW 51000,'Migration 31 requires schema 30.',1;
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
      AND NOT EXISTS (SELECT 1 FROM dbo.StudentPostingHolds H
                      WHERE H.AdmissionId=E.AdmissionId AND H.ResumeDate IS NULL)
    ORDER BY S.StudentName, S.RegistrationNumber;
END;
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
    BEGIN TRY
    BEGIN TRANSACTION;
    -- Serialize with reviewed hold/resume before taking placement locks.
    DECLARE @PostingLock int;
    EXEC @PostingLock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',
        @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
    IF @PostingLock<0 THROW 53016,'Fee posting is busy. Please try again.',1;
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
    -- BillingReady=0 also describes a hold; it is not permission to confirm return.
    -- Check before changing fee assignments, even if no opening charge is payable.
    IF EXISTS (SELECT 1 FROM dbo.StudentPostingHolds WITH(UPDLOCK,HOLDLOCK)
               WHERE AdmissionId=@AdmissionId AND ResumeDate IS NULL)
        THROW 53017,'This student is on hold. In Admission, choose Actions > Lift Hold / Resume to review the return and fees.',1;
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
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END

GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>30',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>30',N'FROM dbo.DatabaseVersion)<>31');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'31',SYSDATETIME(),SUSER_SNAME(),N'Keep held students out of promoted return confirmation and protect reviewed resume');
GO
