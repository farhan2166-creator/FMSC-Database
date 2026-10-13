-- Quick Registration: optional phone and safe pending opening-fee restoration.
-- Apply after migration 4, with a verified backup and application sessions closed.
-- No student, payment, role or permission records are rewritten.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Version nvarchar(20)=(SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion WITH(UPDLOCK,HOLDLOCK) ORDER BY VersionId DESC);
    IF @Version=N'5' BEGIN COMMIT TRANSACTION; RETURN; END;
    IF @Version IS NULL OR @Version<>N'4'
        THROW 57520,'Quick Registration usability migration requires database version 4.',1;
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.PostEnrollmentOpeningCharges @EnrollmentId int AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @BillingStartDate AS DATE, @ChargeDate AS DATE, @Status AS NVARCHAR (30), @BillingReady AS BIT;
    SELECT @BillingStartDate = ISNULL(E.BillingStartDate, E.EnrollmentDate),
           @ChargeDate = COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate),
           @Status = E.Status,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @BillingStartDate IS NULL
        THROW 59401, ''The enrollment does not exist.'', 1;
    IF @Status NOT IN(N''Active'',N''Pending'')
       OR @BillingReady <> 1
        THROW 59402, ''Confirm admission before posting opening charges.'', 1;
    DECLARE @OpeningMonth AS DATE = DATEFROMPARTS(YEAR(@BillingStartDate), MONTH(@BillingStartDate), 1);
    IF @@TRANCOUNT > 0
        BEGIN
            DECLARE @LockResult AS INT;
            DECLARE @LockResource AS NVARCHAR (255) = N''FMSC.GenerateEnrollmentCharges.'' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @LockResult = sys.sp_getapplock @Resource = @LockResource, @LockMode = N''Exclusive'', @LockOwner = N''Transaction'', @LockTimeout = 0;
            IF @LockResult < 0
                THROW 59403, ''Charges for this enrollment are already being posted.'', 1;
        END
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @ChargeDate,
           @ChargeDate,
           SFA.Amount,
           SFA.DiscountAmount,
           N''Annual''
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N''Yearly''
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.ChargeKind = N''Annual''
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
           N''Monthly''
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N''Monthly''
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @OpeningMonth
                                  AND BC.ChargeKind = N''Monthly''
                                  );

    -- Explicitly selected As Needed fees belong on a pending QR opening bill.
    -- Active-student additional posting keeps its established separate workflow.
    IF @Status=N''Pending''
        INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
            BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
        SELECT A.StudentFeeAssignmentId,A.EnrollmentId,A.AdmissionId,A.FeeTypeId,
            @OpeningMonth,@ChargeDate,@ChargeDate,A.Amount,A.DiscountAmount,N''Manual''
        FROM dbo.StudentFeeAssignments A
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N''As Needed''
            AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=@OpeningMonth)
            AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=@OpeningMonth)
            AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@EnrollmentId
                AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N''Manual'' AND B.Status<>N''Cancelled'');
END
';
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.CompleteQuickAdmission @UserId int,@AdmissionId int,@AdmissionNumber varchar(40)=NULL AS

BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N''Students.Admit'';
 BEGIN TRAN;
 DECLARE @Date date,@Enrollment int,@Old nvarchar(max),@New nvarchar(max),@Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@AdmissionId);
 SELECT @Date=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N''Pending'' AND QuickReference IS NOT NULL AND AdmissionNumber IS NULL;
 IF @Date IS NULL THROW 59701,''Select a pending registration. It may already have been completed or cancelled.'',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND (NULLIF(LTRIM(RTRIM(StudentName)),N'''') IS NULL OR
  NULLIF(LTRIM(RTRIM(FatherName)),N'''') IS NULL OR Gender IS NULL OR
  NULLIF(LTRIM(RTRIM(Address)),N'''') IS NULL OR NULLIF(LTRIM(RTRIM(PlaceOfBirth)),N'''') IS NULL))
  THROW 59702,''Complete and save the required details in Quick Registration review before confirming admission.'',1;
 SELECT @Enrollment=EnrollmentId FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N''Pending'';
 IF @Enrollment IS NULL THROW 59703,''The pending academic placement could not be found.'',1;
 SELECT @Old=(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.ReserveAdmissionNumber @AdmissionNumber OUTPUT;
 DECLARE @Registration nvarchar(40);
 EXEC dbo.GetNextRegistrationNumber @RegistrationDate=@Date,@RegistrationNumber=@Registration OUTPUT;
 UPDATE dbo.Students SET AdmissionNumber=@AdmissionNumber,RegistrationNumber=@Registration,Status=N''Active'',UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 UPDATE dbo.Enrollments SET Status=N''Active'',PlacementConfirmedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 -- Same placement and fee assignments: opening bills already issued under the registration remain untouched.
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
 SELECT EnrollmentId,AdmissionId,N''ADMISSION'',@Date,ClassSectionId,N''Pending'',N''Active'',N''Quick registration completed'',@UserId FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment;
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,N''Pending'',N''Active'',N''Admission completed'',@Date,@UserId);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N''COMPLETE_ADMISSION'',@Key);
 SELECT @New=(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N''Students'',@Key,N''UPDATE'',@Old,@New;
 COMMIT;
 SELECT @AdmissionNumber AdmissionNumber;
END;';
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.AddStudentFeeAssignment @UserId int,@EnrollmentId int,@FeeTypeId int AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N''Fees.Adjust'';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId)
        THROW 53110, ''The selected enrollment does not exist.'', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId
                          AND (BillingReady = 1 OR PlacementConfirmedAt IS NULL))
        THROW 53114, ''Confirm return admission before adding fees.'', 1;
    DECLARE @CampusId AS INT, @ClassId AS INT, @AdmissionId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId;
    DECLARE @FmscCutoffDay AS INT;
    SELECT TOP (1) @FmscCutoffDay = FeeProrationCutoffDay
    FROM   dbo.SchoolProfile;
    DECLARE @FmscOpeningMonth AS DATE = CASE WHEN @FmscCutoffDay BETWEEN 1 AND 28
                                                  AND DAY(GETDATE()) >= @FmscCutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) ELSE DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1) END;
    DECLARE @BillingYearStart date,@BillingYearEnd date,@BillingEnrollmentStart date;
    SELECT @BillingYearStart=Y.StartDate,@BillingYearEnd=Y.EndDate,@BillingEnrollmentStart=E.EnrollmentDate
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId AND E.Status IN(N''Active'',N''Pending'');
    IF @BillingYearStart IS NULL THROW 59630,''Correct or select an active enrollment in Admission before adding fees.'',1;
    IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NULL OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1)))
        SELECT @FmscOpeningMonth=DATEFROMPARTS(YEAR(COALESCE(BillingStartDate,EnrollmentDate)),MONTH(COALESCE(BillingStartDate,EnrollmentDate)),1)
        FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
    IF @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingYearStart),MONTH(@BillingYearStart),1)
       OR (@BillingYearEnd IS NOT NULL AND @FmscOpeningMonth>DATEFROMPARTS(YEAR(@BillingYearEnd),MONTH(@BillingYearEnd),1))
       OR @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingEnrollmentStart),MONTH(@BillingEnrollmentStart),1)
       THROW 59631,''The opening fee month is outside this enrollment. Correct its placement in Admission; use reviewed historical posting for an old year.'',1;
    BEGIN TRANSACTION;
    DECLARE @StudentFeeAssignmentId AS INT;
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT TOP (1) StudentFeeAssignmentId,
                                                           EnrollmentId,
                                                           AdmissionId,
                                                           FeeTypeId,
                                                           Amount,
                                                           DiscountAmount,
                                                           Frequency,
                                                           EffectiveFrom,
                                                           EffectiveTo,
                                                           CancelledAt
                                            FROM   dbo.StudentFeeAssignments WITH (UPDLOCK, HOLDLOCK)
                                            WHERE  EnrollmentId = @EnrollmentId
                                                   AND FeeTypeId = @FeeTypeId
                                                   AND EffectiveTo IS NULL
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @AuditAction AS NVARCHAR (20);
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments
               WHERE  EnrollmentId = @EnrollmentId
                      AND FeeTypeId = @FeeTypeId
                      AND EffectiveTo IS NULL)
        BEGIN
            DECLARE @ExistingAssignmentId AS INT;
            SELECT @ExistingAssignmentId = StudentFeeAssignmentId
            FROM   dbo.StudentFeeAssignments
            WHERE  EnrollmentId = @EnrollmentId
                   AND FeeTypeId = @FeeTypeId
                   AND EffectiveTo IS NULL;
            DECLARE @HasBillingHistory AS BIT = CASE WHEN EXISTS (SELECT 1
                                                                  FROM   dbo.BillingCharges
                                                                  WHERE  StudentFeeAssignmentId = @ExistingAssignmentId) THEN 1 ELSE 0 END;
            SET @AuditAction = N''UPDATE'';
            -- A pending registration may restore an explicitly removed, unpaid
            -- opening fee in the same month. Preserve cancelled charge history.
            IF @HasBillingHistory = 0 OR (
                EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
                    WHERE E.EnrollmentId=@EnrollmentId AND E.Status=N''Pending'' AND S.Status=N''Pending'')
                AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@ExistingAssignmentId AND CancelledAt IS NOT NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B JOIN dbo.FeePaymentAllocations A ON A.BillingChargeId=B.BillingChargeId
                    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
                    WHERE B.StudentFeeAssignmentId=@ExistingAssignmentId AND P.Status=N''Completed'' AND A.Amount>0))
                BEGIN
                    UPDATE SFA
                    SET    SFA.CancelledAt    = NULL,
                           SFA.Amount         = FC.Amount,
                           SFA.Frequency      = FC.Frequency,
                           SFA.DiscountAmount = 0,
                           SFA.UpdatedAt      = SYSDATETIME()
                    FROM   dbo.StudentFeeAssignments AS SFA
                           INNER JOIN
                           dbo.FeeConfigurations AS FC
                           ON FC.FeeTypeId = SFA.FeeTypeId
                              AND FC.CampusId = @CampusId
                              AND FC.ClassId = @ClassId
                    WHERE  SFA.StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, ''This fee is not configured for the student''''s campus/class.'', 1;
                        END
                    SET @StudentFeeAssignmentId = @ExistingAssignmentId;
                END
            ELSE
                BEGIN
                    DECLARE @ReactivateEffectiveFrom AS DATE = @FmscOpeningMonth;
                    DECLARE @LastBilledMonth AS DATE;
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @ReactivateEffectiveFrom <= @LastBilledMonth
                        BEGIN
                            ROLLBACK;
                            THROW 53113, ''This fee cannot be reactivated starting this month because a later month has already been billed under the previous version. Please review Fee Setup.'', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @ReactivateEffectiveFrom),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId
                           AND EffectiveTo IS NULL;
                    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                    SELECT @EnrollmentId,
                           @AdmissionId,
                           FC.FeeTypeId,
                           FC.Amount,
                           0,
                           FC.Frequency,
                           N''Reactivated - new version starting current month'',
                           @ReactivateEffectiveFrom,
                           NULL
                    FROM   dbo.FeeConfigurations AS FC
                    WHERE  FC.FeeTypeId = @FeeTypeId
                           AND FC.CampusId = @CampusId
                           AND FC.ClassId = @ClassId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, ''This fee is not configured for the student''''s campus/class.'', 1;
                        END
                    SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
                END
        END
    ELSE
        BEGIN
            SET @AuditAction = N''INSERT'';
            DECLARE @EffectiveFromForNewRow AS DATE = @FmscOpeningMonth;
            INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
            SELECT @EnrollmentId,
                   @AdmissionId,
                   FC.FeeTypeId,
                   FC.Amount,
                   0,
                   FC.Frequency,
                   N''Added from Student Admission'',
                   @EffectiveFromForNewRow,
                   NULL
            FROM   dbo.FeeConfigurations AS FC
            WHERE  FC.FeeTypeId = @FeeTypeId
                   AND FC.CampusId = @CampusId
                   AND FC.ClassId = @ClassId;
            IF @@ROWCOUNT = 0
                BEGIN
                    ROLLBACK;
                    THROW 53112, ''This fee is not configured for the student''''s campus/class.'', 1;
                END
            SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N''FEE_ASSIGNMENT_ADD'', CONVERT (NVARCHAR (200), @StudentFeeAssignmentId));
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT StudentFeeAssignmentId,
                                                   EnrollmentId,
                                                   AdmissionId,
                                                   FeeTypeId,
                                                   Amount,
                                                   DiscountAmount,
                                                   Frequency,
                                                   EffectiveFrom,
                                                   EffectiveTo,
                                                   CancelledAt
                                            FROM   dbo.StudentFeeAssignments
                                            WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N''StudentFeeAssignments'', @RecordId = @StudentFeeAssignmentId, @Action = @AuditAction, @OldValues = @OldValues, @NewValues = @NewValues;
    DECLARE @FmscFrequency AS NVARCHAR (30), @FmscAmount AS DECIMAL (18, 2), @FmscDiscount AS DECIMAL (18, 2), @FmscFeeTypeId AS INT, @FmscEnrollmentId AS INT, @FmscAdmissionId AS INT, @FmscStartMonth AS DATE, @FmscPostMonth AS DATE;
    SELECT @FmscFrequency = Frequency,
           @FmscAmount = Amount,
           @FmscDiscount = DiscountAmount,
           @FmscFeeTypeId = FeeTypeId,
           @FmscEnrollmentId = EnrollmentId,
           @FmscAdmissionId = AdmissionId,
           @FmscStartMonth = DATEFROMPARTS(YEAR(ISNULL(EffectiveFrom, @FmscOpeningMonth)), MONTH(ISNULL(EffectiveFrom, @FmscOpeningMonth)), 1)
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    SET @FmscPostMonth = CASE WHEN @FmscStartMonth > @FmscOpeningMonth THEN @FmscStartMonth ELSE @FmscOpeningMonth END;
    IF @FmscFrequency IN (N''Monthly'', N''Yearly'') AND EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NOT NULL OR Status=N''Pending'') AND NOT EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1))
        BEGIN
            INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
            SELECT @StudentFeeAssignmentId,
                   @FmscEnrollmentId,
                   @FmscAdmissionId,
                   @FmscFeeTypeId,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscAmount,
                   @FmscDiscount,
                   CASE WHEN @FmscFrequency = N''Yearly'' THEN N''Annual'' ELSE N''Monthly'' END
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.BillingCharges AS BC
                               WHERE  BC.EnrollmentId = @FmscEnrollmentId
                                      AND BC.FeeTypeId = @FmscFeeTypeId
                                      AND BC.Status <> N''Cancelled''
                                      AND ((@FmscFrequency = N''Yearly''
                                            AND BC.ChargeKind = N''Annual'')
                                           OR (@FmscFrequency = N''Monthly''
                                               AND BC.ChargeKind = N''Monthly''
                                               AND BC.BillingMonth = @FmscPostMonth)));
        END
    IF EXISTS(SELECT 1 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
        WHERE E.EnrollmentId=@EnrollmentId AND E.Status=N''Pending'' AND S.Status=N''Pending'')
        EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
END';
    INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'5',SYSDATETIME(),SUSER_SNAME(),N'Optional registration phone and restoration of unpaid pending opening fees.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
