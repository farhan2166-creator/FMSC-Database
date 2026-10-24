-- New admissions keep admission and placement together, but monthly billing starts now.
-- Admissions in completed academic years create history only, with no financial setup.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 43 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>42
 THROW 51000,'Migration 43 requires schema 42.',1;
GO
CREATE OR ALTER PROCEDURE dbo.AdmitStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @QuickRegistration bit=0, @FamilyStudentId int=NULL, @FeeConcessionsJson nvarchar(max)=NULL,@MonthlyDiscountFrom date=NULL
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
    IF @YearEndDate>=CONVERT(date,GETDATE()) AND @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            THROW 51019, @MandatoryFeeErrorMsg, 1;
        END
    DECLARE @Historical bit=CASE WHEN @YearEndDate<CONVERT(date,GETDATE()) THEN 1 ELSE 0 END;
    DECLARE @BillingStartDate AS DATE;
    SET @BillingStartDate = CASE WHEN @Historical=0 THEN dbo.GetMonthlyBillingStartDate(CONVERT(date,GETDATE())) END;
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
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @AdmissionDate, @BillingStartDate, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, @UsesTransport, N'Created by admission',CASE WHEN @Historical=1 THEN 0 ELSE 1 END,CASE WHEN @QuickRegistration=0 THEN SYSDATETIME() END);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    -- Past academic years record history only; no fee assignments or opening charges.
    IF @Historical=0
    BEGIN
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
    -- The school chooses the first concession month; legacy callers retain next-month defaults.
    DECLARE @DiscountFrom date=COALESCE(@MonthlyDiscountFrom,DATEADD(month,1,DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1)));
    IF DAY(@DiscountFrom)<>1 OR @DiscountFrom<DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1) OR @MonthlyDiscountFrom>@YearEndDate
      THROW 51024,'Choose a first concession month within the admission academic year.',1;
    IF @DiscountFrom=DATEFROMPARTS(YEAR(@AdmissionDate),MONTH(@AdmissionDate),1)
    BEGIN
      UPDATE A SET DiscountAmount=D.DiscountAmount,ConcessionLabel=C.ConcessionLabel
       FROM dbo.StudentFeeAssignments A JOIN @ReviewedDiscounts D ON D.FeeTypeId=A.FeeTypeId
       LEFT JOIN @Concessions C ON C.FeeTypeId=A.FeeTypeId
       WHERE A.EnrollmentId=@EnrollmentId AND A.Frequency=N'Monthly';
    END
    ELSE IF EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId AND @DiscountFrom<=EndDate)
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
    END;
    IF @FamilyStudentId IS NOT NULL EXEC dbo.AddStudentSibling @AdmissionId,@FamilyStudentId,N'Sibling',@UserId;
    IF @Historical=0 EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
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
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'43',SYSDATETIME(),SUSER_SNAME(),N'New admission monthly billing uses current cutoff; completed academic years create history without fees');
GO