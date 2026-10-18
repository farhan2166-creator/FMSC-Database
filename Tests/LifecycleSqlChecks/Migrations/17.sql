-- Concession labels follow effective-dated fee assignments; they never exempt unrelated assigned fees.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH(N'dbo.StudentFeeAssignments',N'ConcessionLabel') IS NULL
 ALTER TABLE dbo.StudentFeeAssignments ADD ConcessionLabel nvarchar(20) NULL
  CONSTRAINT CK_StudentFeeAssignments_Concession CHECK(ConcessionLabel IN(N'Free',N'Scholarship',N'Waived'));
IF COL_LENGTH(N'dbo.FeePayments',N'ConcessionLinesJson') IS NULL
 ALTER TABLE dbo.FeePayments ADD ConcessionLinesJson nvarchar(max) NULL
  CONSTRAINT CK_FeePayments_ConcessionLines CHECK(ConcessionLinesJson IS NULL OR ISJSON(ConcessionLinesJson)=1);
GO
CREATE OR ALTER PROCEDURE dbo.GetFeeReceiptLines
 @UserId int,@ReceiptNumber nvarchar(40),@ReadPermission nvarchar(100)=N'Fees.View'
AS
BEGIN
 SET NOCOUNT ON;
 IF @ReadPermission NOT IN(N'Fees.View',N'Fees.Statement.View',N'Reports.View') THROW 54911,'Invalid receipt read permission.',1;
 EXEC dbo.RequirePermission @UserId,@ReadPermission;
 ;WITH Lines AS(
  SELECT P.AdmissionId,P.EnrollmentId,F.FeeTypeName,B.BillingMonth,A.Amount,P.ReceiptNumber,P.PaymentDate
  FROM dbo.FeePayments P JOIN dbo.FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
  JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  WHERE P.ReceiptNumber=@ReceiptNumber AND P.RelatedFeePaymentId IS NULL AND A.Amount<>0
  UNION ALL
  SELECT P.AdmissionId,P.EnrollmentId,CASE WHEN C.EntryKind=N'Deposit' THEN N'Credit toward future fees' ELSE N'Unused credit refunded' END,
   NULL,C.Amount,P.ReceiptNumber,P.PaymentDate
  FROM dbo.StudentCreditEntries C JOIN dbo.FeePayments P ON P.FeePaymentId=C.FeePaymentId
  WHERE P.ReceiptNumber=@ReceiptNumber AND C.EntryKind IN(N'Deposit',N'Refund')
  UNION ALL
  SELECT J.AdmissionId,J.EnrollmentId,J.FeeTypeName,J.BillingMonth,CONVERT(decimal(12,2),0),P.ReceiptNumber,P.PaymentDate
  FROM (SELECT TOP(1) ReceiptNumber,PaymentDate,ConcessionLinesJson FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber ORDER BY FeePaymentId) P
  CROSS APPLY OPENJSON(P.ConcessionLinesJson) WITH(AdmissionId int,EnrollmentId int,FeeTypeName nvarchar(150),BillingMonth date) J
 )
 SELECT S.StudentName,S.FatherName,L.AdmissionId,L.EnrollmentId,C.ClassName,SEC.SectionName,CAM.CampusName,
  L.FeeTypeName,L.BillingMonth,L.Amount,L.ReceiptNumber,L.PaymentDate
 FROM Lines L JOIN dbo.Students S ON S.AdmissionId=L.AdmissionId JOIN dbo.Enrollments E ON E.EnrollmentId=L.EnrollmentId
 JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId JOIN dbo.Classes C ON C.ClassId=P.ClassId
 JOIN dbo.Sections SEC ON SEC.SectionId=P.SectionId JOIN dbo.Campuses CAM ON CAM.CampusId=P.CampusId
 ORDER BY S.StudentName,L.EnrollmentId,L.BillingMonth,L.FeeTypeName;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStudentFeeDiscount
 @UserId int,@StudentFeeAssignmentId int,@DiscountAmount decimal(12,2),@EffectiveFrom date=NULL,@ConcessionLabel nvarchar(20)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 IF @ConcessionLabel IS NOT NULL AND @ConcessionLabel NOT IN(N'Free',N'Scholarship',N'Waived') THROW 53103,'Choose a valid concession label.',1;
 SET @DiscountAmount=COALESCE(@DiscountAmount,0);
 IF @DiscountAmount<0 OR @DiscountAmount<>ROUND(@DiscountAmount,0) THROW 53101,'Enter a whole-rupee discount, 0 or more.',1;
 DECLARE @FullConcession bit=CASE WHEN @ConcessionLabel IN(N'Free',N'Waived') OR (@ConcessionLabel=N'Scholarship' AND @DiscountAmount=(SELECT Amount FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@StudentFeeAssignmentId)) THEN 1 ELSE 0 END;
 DECLARE @From date=DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),
  @Enrollment int,@Admission int,@Fee int,@Frequency nvarchar(30),@YearEnd date,@Lock int,@Locked int;
 IF @EffectiveFrom IS NOT NULL AND @EffectiveFrom<>@From THROW 53103,'Assignment discounts start next month. Adjust existing unpaid bills in Fee Collection.',1;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  SELECT @Enrollment=A.EnrollmentId,@Admission=A.AdmissionId,@Fee=A.FeeTypeId,@Frequency=A.Frequency,
   @YearEnd=DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1)
  FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
  JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE A.StudentFeeAssignmentId=@StudentFeeAssignmentId AND A.CancelledAt IS NULL AND E.Status IN(N'Active',N'Pending');
  IF @Enrollment IS NULL THROW 53102,'Select an active or pending student fee assignment.',1;
  IF @From>@YearEnd THROW 53103,'No future month remains in this academic year.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee;
  IF @Frequency<>N'Monthly'
   THROW 53103,'Adjust an existing one-off or annual bill in Fee Collection. Recurring assignment discounts apply to monthly fees.',1;
  IF EXISTS(SELECT 1 FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
    WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND BillingMonth>=@From AND Status<>N'Cancelled')
    THROW 53104,'Future bills already exist. Review their discounts in Fee Collection before changing the assignment.',1;
  SELECT * INTO #Crossing FROM dbo.StudentFeeAssignments
   WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND CancelledAt IS NULL
    AND (EffectiveFrom IS NULL OR EffectiveFrom<@From) AND (EffectiveTo IS NULL OR EffectiveTo>=@From);
  UPDATE A SET EffectiveTo=DATEADD(month,-1,@From),UpdatedAt=SYSDATETIME()
   FROM dbo.StudentFeeAssignments A JOIN #Crossing C ON C.StudentFeeAssignmentId=A.StudentFeeAssignmentId;
  UPDATE dbo.StudentFeeAssignments SET ConcessionLabel=@ConcessionLabel,DiscountAmount=CASE WHEN @FullConcession=1 OR @DiscountAmount>Amount THEN Amount ELSE @DiscountAmount END,UpdatedAt=SYSDATETIME()
   WHERE EnrollmentId=@Enrollment AND FeeTypeId=@Fee AND CancelledAt IS NULL AND EffectiveFrom>=@From AND EffectiveFrom<=@YearEnd;
  INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo,ConcessionLabel)
   SELECT EnrollmentId,AdmissionId,FeeTypeId,Amount,CASE WHEN @FullConcession=1 OR @DiscountAmount>Amount THEN Amount ELSE @DiscountAmount END,
    Frequency,Remarks,@From,EffectiveTo,@ConcessionLabel FROM #Crossing;
  COMMIT;
  SELECT @StudentFeeAssignmentId StudentFeeAssignmentId,@Enrollment EnrollmentId,@Admission AdmissionId,
   @From EffectiveFrom,@YearEnd EffectiveThrough;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ChangeStudentFeeAssignmentAmount_Internal
@UserId INT, @StudentFeeAssignmentId INT, @NewAmount DECIMAL (12, 2), @EffectiveFrom DATE, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 53140, 'A reason is required to change a fee amount.', 1;
    IF @NewAmount < 0
        THROW 53141, 'Amount cannot be negative.', 1;
    IF @EffectiveFrom IS NULL
        THROW 53147, 'An effective-from date is required.', 1;
    DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END,@BillingLock int;
    BEGIN TRY
    BEGIN TRANSACTION;
    EXEC @BillingLock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
    IF @BillingLock<0 THROW 54745,'Billing is running. Try again shortly.',1;
    DECLARE @ConcessionLabel nvarchar(20)=(SELECT ConcessionLabel FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@StudentFeeAssignmentId);
    DECLARE @EnrollmentId AS INT, @AdmissionId AS INT, @FeeTypeId AS INT, @OldAmount AS DECIMAL (12, 2), @DiscountAmount AS DECIMAL (12, 2), @Frequency AS NVARCHAR (20), @Remarks AS NVARCHAR (500), @ExistingEffectiveFrom AS DATE, @ExistingEffectiveTo AS DATE, @CancelledAt AS DATETIME2;
    SELECT @EnrollmentId = EnrollmentId,
           @AdmissionId = AdmissionId,
           @FeeTypeId = FeeTypeId,
           @OldAmount = Amount,
           @DiscountAmount = DiscountAmount,
           @Frequency = Frequency,
           @Remarks = Remarks,
           @ExistingEffectiveFrom = EffectiveFrom,
           @ExistingEffectiveTo = EffectiveTo,
           @CancelledAt = CancelledAt
    FROM   dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    IF @EnrollmentId IS NULL
        THROW 53142, 'The selected student fee assignment does not exist.', 1;
    IF @CancelledAt IS NOT NULL
        THROW 53143, 'This fee assignment has been cancelled.', 1;
    IF @ExistingEffectiveTo IS NOT NULL
        THROW 53144, 'This fee assignment has already been closed/superseded.', 1;
    DECLARE @NormalizedEffectiveFrom AS DATE = DATEFROMPARTS(YEAR(@EffectiveFrom), MONTH(@EffectiveFrom), 1);
    DECLARE @NormalizedOldEffectiveTo AS DATE = DATEADD(MONTH, -1, @NormalizedEffectiveFrom);

    -- UNBILLED_RATE_REPLACEMENT: no extra version or obsolete scheduled action.
    IF @ExistingEffectiveFrom IS NOT NULL AND @NormalizedEffectiveFrom <= @ExistingEffectiveFrom
       AND NOT EXISTS (SELECT 1 FROM dbo.BillingCharges WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId)
    BEGIN

        -- Serialize against posting or another rate edit before checking the timeline.
        IF NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments WITH (UPDLOCK, HOLDLOCK)
                       WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId AND EffectiveTo IS NULL
                         AND CancelledAt IS NULL AND Amount = @OldAmount AND EffectiveFrom = @ExistingEffectiveFrom)
            THROW 53146, 'The fee has changed. Refresh the fees and try again.', 1;
        IF EXISTS (SELECT 1 FROM dbo.BillingCharges WITH (UPDLOCK, HOLDLOCK)
                   WHERE EnrollmentId = @EnrollmentId AND FeeTypeId = @FeeTypeId
                     AND (BillingMonth >= @NormalizedEffectiveFrom OR StudentFeeAssignmentId = @StudentFeeAssignmentId))
            THROW 53148, 'Choose a month after the last month already billed for this fee.', 1;
        IF EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments
                   WHERE EnrollmentId = @EnrollmentId AND FeeTypeId = @FeeTypeId
                     AND StudentFeeAssignmentId <> @StudentFeeAssignmentId AND CancelledAt IS NULL
                     AND (EffectiveTo IS NULL OR EffectiveTo >= @NormalizedEffectiveFrom))
            THROW 53145, 'The selected month overlaps another fee period. Choose a later month.', 1;
        SET @DiscountAmount = CASE WHEN @ConcessionLabel IS NOT NULL AND @DiscountAmount=@OldAmount THEN @NewAmount WHEN @DiscountAmount>@NewAmount THEN @NewAmount ELSE @DiscountAmount END;
        UPDATE dbo.StudentFeeAssignments
        SET Amount = @NewAmount, DiscountAmount = @DiscountAmount, EffectiveFrom = @NormalizedEffectiveFrom, UpdatedAt = SYSDATETIME()
        WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId;
        COMMIT TRANSACTION;
        SELECT StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount,
               Frequency, EffectiveFrom, EffectiveTo
        FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId = @StudentFeeAssignmentId;
        RETURN;
    END;
    IF @ExistingEffectiveFrom IS NOT NULL
       AND @NormalizedEffectiveFrom <= @ExistingEffectiveFrom
        THROW 53145, 'Effective-from month must be after this version''s own starting month.', 1;
    DECLARE @LastBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                        FROM   dbo.BillingCharges
                                        WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId);
    IF @LastBilledMonth IS NOT NULL
       AND @NormalizedEffectiveFrom <= @LastBilledMonth
        THROW 53148, 'Effective-from month cannot be on or before the last month already billed for this fee.', 1;
    SET @DiscountAmount = CASE WHEN @ConcessionLabel IS NOT NULL AND @DiscountAmount=@OldAmount THEN @NewAmount WHEN @DiscountAmount>@NewAmount THEN @NewAmount ELSE @DiscountAmount END;

    UPDATE dbo.StudentFeeAssignments
    SET    EffectiveTo = @NormalizedOldEffectiveTo,
           UpdatedAt   = SYSDATETIME()
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
           AND EffectiveTo IS NULL;
    IF @@ROWCOUNT = 0
        BEGIN
            THROW 53146, 'The fee assignment could not be updated (it may have just been changed by another operation).', 1;
        END
    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo, ConcessionLabel)
    VALUES                           (@EnrollmentId, @AdmissionId, @FeeTypeId, @NewAmount, @DiscountAmount, @Frequency, @Remarks, @NormalizedEffectiveFrom, NULL, @ConcessionLabel);
    DECLARE @NewStudentFeeAssignmentId AS INT = CONVERT (INT, SCOPE_IDENTITY());
COMMIT TRANSACTION;
    SELECT StudentFeeAssignmentId,
           EnrollmentId,
           AdmissionId,
           FeeTypeId,
           Amount,
           DiscountAmount,
           Frequency,
           EffectiveFrom,
           EffectiveTo
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @NewStudentFeeAssignmentId;
    END TRY
    BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END

GO
CREATE OR ALTER PROCEDURE dbo.GetStudentFeeAssignments
@UserId INT, @EnrollmentId INT, @IncludeAvailableFees BIT=0, @IncludeCancelled BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId)
        THROW 53100, 'The selected enrollment does not exist.', 1;
    SELECT   SFA.StudentFeeAssignmentId,
             SFA.EnrollmentId,
             SFA.AdmissionId,
             SFA.FeeTypeId,
             FT.FeeTypeCode,
             FT.FeeTypeName,
             SFA.Amount AS StandardAmount,
             SFA.DiscountAmount,
             SFA.ConcessionLabel,
             SFA.Amount - SFA.DiscountAmount AS NetAmount,
             SFA.Frequency,
             SFA.Remarks,
             SFA.EffectiveFrom,
             SFA.EffectiveTo,
             SFA.CancelledAt,
             FT.IsMandatory,
             FT.IsTransportLinked,
             CAST (CASE WHEN EXISTS (SELECT 1
                                     FROM   dbo.BillingCharges AS BC
                                     WHERE  BC.StudentFeeAssignmentId = SFA.StudentFeeAssignmentId) THEN 1 ELSE 0 END AS BIT) AS HasPayments,
             (SELECT MAX(BC.BillingMonth)
              FROM   dbo.BillingCharges AS BC
              WHERE  BC.StudentFeeAssignmentId = SFA.StudentFeeAssignmentId) AS LastBilledMonth
    FROM     dbo.StudentFeeAssignments AS SFA
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = SFA.FeeTypeId
    WHERE    SFA.EnrollmentId = @EnrollmentId
             AND (@IncludeCancelled=1 OR SFA.CancelledAt IS NULL)
    ORDER BY FT.DisplayOrder, FT.FeeTypeName, SFA.EffectiveFrom;
    IF @IncludeAvailableFees = 1
        BEGIN
            SELECT   FC.FeeConfigurationId,
                     FC.CampusId,
                     FC.ClassId,
                     FC.FeeTypeId,
                     FT.FeeTypeCode,
                     FT.FeeTypeName,
                     FC.Amount,
                     FC.Frequency,
                     FC.Remarks,
                     FT.IsMandatory,
                     FT.IsTransportLinked
            FROM     dbo.Enrollments AS E
                     INNER JOIN
                     dbo.CampusClassSections AS CCS
                     ON CCS.ClassSectionId = E.ClassSectionId
                     INNER JOIN
                     dbo.FeeConfigurations AS FC
                     ON FC.CampusId = CCS.CampusId
                        AND FC.ClassId = CCS.ClassId
                     INNER JOIN
                     dbo.FeeTypes AS FT
                     ON FT.FeeTypeId = FC.FeeTypeId
            WHERE    E.EnrollmentId = @EnrollmentId
            ORDER BY FT.DisplayOrder, FT.FeeTypeName;
        END
END

GO
CREATE OR ALTER PROCEDURE dbo.ProcessFamilyFeeTransaction
    @TransactionType NVARCHAR(20),
    @TransactionDate DATE,
    @PaymentMethod   NVARCHAR(30),
    @ReferenceNumber NVARCHAR(100),
    @Remarks         NVARCHAR(500),
    @Allocations     dbo.FamilyFeeAllocationType READONLY,
    @UserId          INT,
    @ReceivedAmount decimal(12,2)=NULL,@CreditAdmissionId int=NULL,@CreditRequestId uniqueidentifier=NULL,@IncludedStudentsJson nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Fees.Collect';

    SET @TransactionType =
        LTRIM(RTRIM(@TransactionType));

    IF @TransactionType NOT IN
    (
        N'Collection',
        N'Correction',
        N'Refund',
        N'Reversal'
    )
        THROW 59217, 'Invalid fee transaction type.', 1;


    /* ========================================================================
       VALIDATE THE WHOLE FAMILY BEFORE ANYTHING IS WRITTEN

       Every check below covers the entire batch at once. Validating up front is
       not what makes the operation atomic - the transaction is - but it means a
       family with a bad line is refused with a sentence that names the problem
       instead of failing after some children have already been processed.
       ======================================================================== */

    IF NOT EXISTS
    (
        SELECT 1
        FROM @Allocations
    )
        THROW 59218, 'No fee allocations were supplied, so there is nothing to collect.', 1;


    /* A row with no student, no enrollment or no charge cannot be attributed to
       anybody. This catches a malformed batch rather than a data problem. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations
        WHERE AdmissionId <= 0
           OR EnrollmentId <= 0
           OR BillingChargeId <= 0
    )
        THROW 59219, 'A family collection line is missing its student, enrollment or charge. Nothing has been posted.', 1;


    /* Every student. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations A
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Students S
            WHERE S.AdmissionId = A.AdmissionId
        )
    )
        THROW 59220, 'One of the students in this family collection no longer exists. Nothing has been posted.', 1;


    /* Every enrollment, and that it belongs to the student it is filed under.
       A mismatch here would post a charge against the wrong child. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations A
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Enrollments E
            WHERE E.EnrollmentId = A.EnrollmentId
              AND E.AdmissionId = A.AdmissionId
        )
    )
        THROW 59221, 'One of the enrollments in this family collection does not belong to its student. Nothing has been posted.', 1;


    /* Every charge, and that it belongs to that student's enrollment. */
    IF EXISTS
    (
        SELECT 1
        FROM @Allocations A
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.BillingCharges BC
            WHERE BC.BillingChargeId = A.BillingChargeId
              AND BC.AdmissionId = A.AdmissionId
              AND BC.EnrollmentId = A.EnrollmentId
        )
    )
        THROW 59222, 'One of the fee charges in this family collection does not belong to its student. Nothing has been posted.', 1;


    /* ========================================================================
       GROUP THE BATCH BY STUDENT, IN A FIXED ORDER

       ROW_NUMBER rather than IDENTITY so the order is the ORDER BY and not an
       implementation detail: the sequence decides the order in which the engine
       takes its locks, which is what keeps two concurrent family collections
       from deadlocking on each other.
       ======================================================================== */

    DECLARE @Groups TABLE
    (
        Seq          INT NOT NULL PRIMARY KEY,
        AdmissionId  INT NOT NULL,
        EnrollmentId INT NOT NULL
    );

    INSERT INTO @Groups (Seq, AdmissionId, EnrollmentId)
    SELECT
        ROW_NUMBER() OVER (ORDER BY AdmissionId, EnrollmentId),
        AdmissionId,
        EnrollmentId
    FROM
    (
        SELECT DISTINCT
            AdmissionId,
            EnrollmentId
        FROM @Allocations
    ) D;


    /* dbo.ProcessFeeTransaction requires @Amount to equal the sum of that call's
       POSITIVE allocations and rejects a non-positive amount (54011). A student
       whose lines are discounts only would fail inside the loop, after the
       transaction had opened; refusing it here says which situation it is. */
    DECLARE @Receipts TABLE
    (
        AdmissionId   INT           NOT NULL,
        EnrollmentId  INT           NOT NULL,
        FeePaymentId  INT           NULL,
        ReceiptNumber NVARCHAR(40)  NULL,
        Amount        DECIMAL(12,2) NULL,
        Balance       DECIMAL(12,2) NULL
    );

    /* dbo.ProcessFeeTransaction returns exactly these five columns. The names
       and types are declared to match it, because INSERT ... EXEC matches a
       result set to its target by position and data type. */
    DECLARE @OneResult TABLE
    (
        FeePaymentId    INT,
        ReceiptNumber   NVARCHAR(40),
        TransactionType NVARCHAR(20),
        Amount          DECIMAL(12,2),
        Balance         DECIMAL(12,2)
    );

    DECLARE @One dbo.FeePaymentAllocationType;

    DECLARE @Seq INT = 1;
    DECLARE @GroupCount INT =
        (SELECT COUNT(*) FROM @Groups);

    DECLARE @AdmissionId INT;
    DECLARE @EnrollmentId INT;
    DECLARE @StudentAmount DECIMAL(12,2);
    DECLARE @Processed INT = 0;

    /* The engine's own nullable inputs. Assigned to variables rather than
       written as NULL in the EXEC, because EXEC parameter values must be
       constants or variables. */
    DECLARE @NoPaymentId INT = NULL;
    DECLARE @NoChargeId BIGINT = NULL;


    /* ONE transaction for the whole family. See the header: this is the only
       boundary that writes, and XACT_ABORT ON in both procedures means any
       failure inside the engine rolls all of it back. */
    BEGIN TRANSACTION;
    DECLARE @Allocated decimal(12,2)=(SELECT COALESCE(SUM(CASE WHEN Amount>0 THEN Amount ELSE 0 END),0) FROM @Allocations);
    SET @ReceivedAmount=COALESCE(@ReceivedAmount,@Allocated);
    IF @ReceivedAmount<@Allocated OR @ReceivedAmount<>FLOOR(@ReceivedAmount)
      THROW 54908,'Received amount must cover the allocations and use whole rupees.',1;
    DECLARE @Surplus decimal(12,2)=@ReceivedAmount-@Allocated;
    IF @Surplus>0 AND (@CreditAdmissionId IS NULL OR @CreditRequestId IS NULL)
      THROW 54909,'Select the student receiving the surplus credit.',1;


    /* FIX 70b: ONE receipt number for the whole collection, taken here so a
       family collection does not consume one number per child and leave gaps in
       the series. Inside the transaction, so a rollback gives the number back. */
    DECLARE @FamilyReceiptNumber NVARCHAR(40);

    IF @ReceivedAmount>0
    EXEC dbo.GetNextFeeReceiptNumber
        @ReceiptDate = @TransactionDate,
        @ReceiptNumber = @FamilyReceiptNumber OUTPUT;

    WHILE @Seq <= @GroupCount
    BEGIN
        SELECT
            @AdmissionId = AdmissionId,
            @EnrollmentId = EnrollmentId
        FROM @Groups
        WHERE Seq = @Seq;

        /* One child's allocations, in the shape the engine takes. */
        DELETE FROM @One;

        INSERT INTO @One (BillingChargeId, Amount)
        SELECT
            BillingChargeId,
            Amount
        FROM @Allocations
        WHERE AdmissionId = @AdmissionId
          AND EnrollmentId = @EnrollmentId;

        SELECT
            @StudentAmount = ISNULL(SUM(Amount),0)
        FROM @One
        WHERE Amount > 0;

        DELETE FROM @OneResult;

        INSERT INTO @OneResult
        (
            FeePaymentId,
            ReceiptNumber,
            TransactionType,
            Amount,
            Balance
        )
        EXEC dbo.ProcessFeeTransaction
            @AdmissionId = @AdmissionId,
            @EnrollmentId = @EnrollmentId,
            @TransactionType = @TransactionType,
            @Amount = @StudentAmount,
            @TransactionDate = @TransactionDate,
            @PaymentMethod = @PaymentMethod,
            @ReferenceNumber = @ReferenceNumber,
            @Remarks = @Remarks,
            @GroupReceiptNumber = @FamilyReceiptNumber,
            @FeePaymentId = @NoPaymentId,
            @BillingChargeId = @NoChargeId,
            @Allocations = @One,
            @UserId = @UserId;

        INSERT INTO @Receipts
        (
            AdmissionId,
            EnrollmentId,
            FeePaymentId,
            ReceiptNumber,
            Amount,
            Balance
        )
        SELECT
            @AdmissionId,
            @EnrollmentId,
            FeePaymentId,
            ReceiptNumber,
            Amount,
            Balance
        FROM @OneResult;

        SET @Processed = @Processed + 1;
        SET @Seq = @Seq + 1;
    END;



    IF @Surplus>0
    BEGIN
        DECLARE @CreditResult TABLE(FeePaymentId int,ReceiptNumber nvarchar(40),AdmissionId int,AvailableCredit decimal(12,2));
        INSERT @CreditResult EXEC dbo.ProcessStudentCredit @UserId=@UserId,@AdmissionId=@CreditAdmissionId,
          @Action=N'Deposit',@Amount=@Surplus,@RequestId=@CreditRequestId,
          @Reason=N'Excess collection retained as student credit',@PaymentMethod=@PaymentMethod,@ReceiptNumber=@FamilyReceiptNumber,@TransactionDate=@TransactionDate;
        INSERT @Receipts(AdmissionId,EnrollmentId,FeePaymentId,ReceiptNumber,Amount,Balance)
        SELECT C.AdmissionId,P.EnrollmentId,C.FeePaymentId,C.ReceiptNumber,@Surplus,0
          FROM @CreditResult C JOIN dbo.FeePayments P ON P.FeePaymentId=C.FeePaymentId;
    END;
    /* ========================================================================
       PROVE THE FAMILY IS WHOLE BEFORE COMMITTING

       These four run inside the transaction, so a collection that did not
       produce one payment and one receipt per child rolls itself back instead of
       committing something half-formed. They are the difference between "the
       loop finished" and "the family was collected".
       ======================================================================== */

    IF @Processed <> @GroupCount
        THROW 59224, 'Not every student in the family collection produced a payment, so the whole collection has been rolled back.', 1;

    IF (SELECT COUNT(*) FROM @Receipts) <> @GroupCount+CASE WHEN @Surplus>0 THEN 1 ELSE 0 END
        THROW 59225, 'Not every student in the family collection produced a payment row, so the whole collection has been rolled back.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @Receipts
        WHERE Amount>0 AND (ReceiptNumber IS NULL
           OR LTRIM(RTRIM(ReceiptNumber)) = '')
    )
        THROW 59226, 'A student in the family collection received no receipt number, so the whole collection has been rolled back.', 1;

    /* FIX 70b/70: a family Collection is issued ONE receipt number that every
       child's payment shares (ProcessFeeTransaction stamps @GroupReceiptNumber
       and sets IsGroupPayment = 1; UQ_FeePayments_Receipt_Single is filtered to
       IsGroupPayment = 0 for exactly this reason; GetFeeReceiptGroup prints the
       whole family from that one number).

       This check used to demand the opposite - that no two children share a
       number - so every family of two or more children was rolled back with
       59227. What it must prove now is that every child's payment carries the
       family's number and nothing else. */
    IF @TransactionType = N'Collection'
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM @Receipts
            WHERE Amount>0 AND ReceiptNumber <> @FamilyReceiptNumber
        )
            THROW 59227, 'A student in the family collection was not given the family receipt number, so the whole collection has been rolled back.', 1;
    END
    ELSE
    BEGIN
        /* Any other type keeps its own number per payment (ProcessFeeTransaction
           allocates them itself), so there the numbers must all differ. */
        IF EXISTS
        (
            SELECT ReceiptNumber
            FROM @Receipts
            GROUP BY ReceiptNumber
            HAVING COUNT(*) > 1
        )
            THROW 59227, 'Two students in the family collection were given the same receipt number, so the whole collection has been rolled back.', 1;
    END;


    IF EXISTS
    (
        SELECT 1
        FROM @Receipts
        WHERE Amount>0 AND FeePaymentId IS NULL
    )
        THROW 59228, 'A student in the family collection has no payment record, so the whole collection has been rolled back.', 1;



    IF @IncludedStudentsJson IS NOT NULL
    BEGIN
      IF ISJSON(@IncludedStudentsJson)<>1 THROW 54912,'Refresh the selected family members.',1;
      DECLARE @PrimaryStudent int=COALESCE(@CreditAdmissionId,(SELECT MIN(AdmissionId) FROM @Allocations));
      IF EXISTS(SELECT 1 FROM OPENJSON(@IncludedStudentsJson) J WHERE TRY_CONVERT(int,J.[value]) IS NULL OR
        (TRY_CONVERT(int,J.[value])<>@PrimaryStudent AND NOT EXISTS(
          SELECT 1 FROM dbo.FamilyMembers A JOIN dbo.FamilyMembers B ON A.FamilyId=B.FamilyId
          WHERE A.AdmissionId=@PrimaryStudent AND B.AdmissionId=TRY_CONVERT(int,J.[value]))))
        THROW 54912,'Family membership changed. Refresh the selected children.',1;
      DECLARE @Concessions nvarchar(max)=(SELECT A.AdmissionId,A.EnrollmentId,
        CONCAT(F.FeeTypeName,N' — ',A.ConcessionLabel) FeeTypeName,
        DATEFROMPARTS(YEAR(@TransactionDate),MONTH(@TransactionDate),1) BillingMonth
        FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
        JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
        WHERE A.AdmissionId IN(SELECT TRY_CONVERT(int,[value]) FROM OPENJSON(@IncludedStudentsJson))
          AND A.ConcessionLabel IS NOT NULL AND A.CancelledAt IS NULL AND A.Amount-A.DiscountAmount=0
          AND E.Status=N'Active' AND E.BillingReady=1 AND E.BillingStartDate IS NOT NULL AND E.PlacementConfirmedAt IS NOT NULL
          AND EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=A.AdmissionId AND S.LegacySetupPending=0)
          AND @TransactionDate BETWEEN Y.StartDate AND Y.EndDate
          AND DATEFROMPARTS(YEAR(@TransactionDate),MONTH(@TransactionDate),1)>=DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1)
          AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=DATEFROMPARTS(YEAR(@TransactionDate),MONTH(@TransactionDate),1))
          AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=DATEFROMPARTS(YEAR(@TransactionDate),MONTH(@TransactionDate),1))
        FOR JSON PATH);
      UPDATE dbo.FeePayments SET ConcessionLinesJson=@Concessions WHERE ReceiptNumber=@FamilyReceiptNumber;
    END;
    /* The one commit that writes. Everything above is provisional until here. */
    COMMIT TRANSACTION;


    /* ========================================================================
       RETURN RESULT - one row per child, in a fixed order, so the caller can
       report each child's own receipt number.
       ======================================================================== */

    SELECT
        AdmissionId,
        EnrollmentId,
        FeePaymentId,
        ReceiptNumber,
        Amount,
        Balance
    FROM @Receipts
    ORDER BY AdmissionId;
END;

GO
CREATE OR ALTER PROCEDURE dbo.GetBillingAndDiscountsReport
    @UserId int,
    @View nvarchar(30),
    @GroupBy nvarchar(30) = N'Month',
    @FromDate date = NULL,
    @ToDate date = NULL,
    @CampusId int = 0,
    @ClassId int = 0,
    @SectionId int = 0
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    IF @View NOT IN (N'Billing Summary', N'Billing Detail', N'Discount Detail', N'Concession Assignments')
        THROW 59904, 'The billing report view is not supported.', 1;
    IF @View = N'Billing Summary' AND @GroupBy NOT IN
       (N'Day', N'Week', N'Month', N'Class', N'Campus', N'Fee Type')
        THROW 59905, 'The billing report grouping is not supported.', 1;
    IF @FromDate IS NOT NULL AND @ToDate IS NOT NULL AND @FromDate > @ToDate
        THROW 59906, 'The billing report date range is invalid.', 1;

    IF @View=N'Concession Assignments'
    BEGIN
      SELECT S.AdmissionNumber [Admission Number],S.StudentName Student,C.ClassName Class,F.FeeTypeName [Fee Type],
        A.ConcessionLabel Concession,A.Amount [Fee Amount],A.DiscountAmount Discount,A.Amount-A.DiscountAmount Payable,
        COALESCE(A.EffectiveFrom,E.BillingStartDate) [Effective From],COALESCE(A.EffectiveTo,Y.EndDate) [Effective Through],E.Status [Placement Status]
      INTO #ConcessionAssignments
      FROM dbo.StudentFeeAssignments A JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId
      JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
      JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId JOIN dbo.Classes C ON C.ClassId=P.ClassId
      JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
      WHERE A.CancelledAt IS NULL AND A.ConcessionLabel IS NOT NULL
        AND (@FromDate IS NULL OR COALESCE(A.EffectiveTo,Y.EndDate)>=@FromDate)
        AND (@ToDate IS NULL OR COALESCE(A.EffectiveFrom,E.BillingStartDate)<=@ToDate)
        AND (@CampusId=0 OR P.CampusId=@CampusId) AND (@ClassId=0 OR P.ClassId=@ClassId)
        AND (@SectionId=0 OR P.SectionId=@SectionId)
      ;
      SELECT * FROM #ConcessionAssignments ORDER BY Student,[Fee Type],[Effective From];
      SELECT COUNT(*) [Concession Assignments] FROM #ConcessionAssignments;
      RETURN;
    END;
    SELECT BC.BillingChargeId, BC.ChargeDate, BC.BillingMonth, S.RegistrationNumber, S.AdmissionNumber, S.StudentName,
           CAMP.CampusName, C.ClassName, SEC.SectionName, CASE WHEN A.ConcessionLabel IS NULL THEN FT.FeeTypeName ELSE CONCAT(FT.FeeTypeName,N' — ',A.ConcessionLabel) END FeeTypeName,
           BC.GrossAmount, BC.DiscountAmount, BC.NetAmount
    INTO #Billing
    FROM dbo.BillingCharges BC
    JOIN dbo.Students S ON S.AdmissionId = BC.AdmissionId
    JOIN dbo.Enrollments E ON E.EnrollmentId = BC.EnrollmentId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
    LEFT JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=BC.StudentFeeAssignmentId
    WHERE BC.Status <> N'Cancelled' AND (@FromDate IS NULL OR BC.ChargeDate >= @FromDate) AND (@ToDate IS NULL OR BC.ChargeDate <= @ToDate)
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId);

    IF @View = N'Billing Detail'
    BEGIN
        SELECT AdmissionNumber [Admission Number], StudentName [Student], ClassName [Class], FeeTypeName [Fee Type],
               BillingMonth [Billing Period], ChargeDate [Charge Date], GrossAmount [Gross Amount],
               DiscountAmount [Discount], NetAmount [Net Charge]
        FROM #Billing ORDER BY ChargeDate, StudentName, FeeTypeName;
        SELECT COALESCE(SUM(GrossAmount), 0) [Gross Billed], COALESCE(SUM(DiscountAmount), 0) Discounts,
               COALESCE(SUM(NetAmount), 0) [Net Billed] FROM #Billing;
        RETURN;
    END;

    IF @View = N'Discount Detail'
    BEGIN
        SELECT AdmissionNumber [Admission Number], StudentName [Student], ClassName [Class], FeeTypeName [Fee Type],
               BillingMonth [Billing Period], ChargeDate [Charge Date], GrossAmount [Gross Amount],
               DiscountAmount [Applied Discount], NetAmount [Net Charge]
        FROM #Billing WHERE DiscountAmount > 0 ORDER BY ChargeDate, StudentName, FeeTypeName;
        SELECT COALESCE(SUM(GrossAmount), 0) [Gross Billed], COALESCE(SUM(DiscountAmount), 0) Discounts,
               COALESCE(SUM(NetAmount), 0) [Net Billed]
        FROM #Billing WHERE DiscountAmount > 0;
        RETURN;
    END;

    SELECT CASE @GroupBy
               WHEN N'Day' THEN CONVERT(char(10), ChargeDate, 23)
               WHEN N'Week' THEN CONCAT(CONVERT(char(10), DATEADD(day, -(DATEDIFF(day, 0, ChargeDate) % 7), ChargeDate), 23), N' week')
               WHEN N'Month' THEN CONVERT(char(7), ChargeDate, 126)
               WHEN N'Fee Type' THEN FeeTypeName
               WHEN N'Class' THEN ClassName
               WHEN N'Campus' THEN CampusName END [Group],
           COUNT(*) [Charges], COALESCE(SUM(GrossAmount), 0) [Gross Charges],
           COALESCE(SUM(DiscountAmount), 0) [Discounts Applied], COALESCE(SUM(NetAmount), 0) [Net Charges]
    FROM #Billing
    GROUP BY CASE @GroupBy
               WHEN N'Day' THEN CONVERT(char(10), ChargeDate, 23)
               WHEN N'Week' THEN CONCAT(CONVERT(char(10), DATEADD(day, -(DATEDIFF(day, 0, ChargeDate) % 7), ChargeDate), 23), N' week')
               WHEN N'Month' THEN CONVERT(char(7), ChargeDate, 126)
               WHEN N'Fee Type' THEN FeeTypeName
               WHEN N'Class' THEN ClassName
               WHEN N'Campus' THEN CampusName END
    ORDER BY [Group];
    SELECT COALESCE(SUM(GrossAmount), 0) [Gross Billed], COALESCE(SUM(DiscountAmount), 0) Discounts,
           COALESCE(SUM(NetAmount), 0) [Net Billed] FROM #Billing;
END;


-- GetBillingCycleStatus

GO
CREATE OR ALTER PROCEDURE dbo.GetFeeReceiptGroup
@UserId INT, @ReceiptNumber NVARCHAR (40)
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.View';
    IF @ReceiptNumber IS NULL
       OR LTRIM(RTRIM(@ReceiptNumber)) = N''
        THROW 59910, 'A receipt number is required.', 1;
    DECLARE @Issued AS DECIMAL (12, 2), @Reversed AS DECIMAL (12, 2), @PayerCount AS INT;
    SELECT @Issued = ISNULL(SUM(FP.TotalAmount), 0),
           @PayerCount = COUNT(DISTINCT FP.AdmissionId)
    FROM   dbo.FeePayments AS FP
    WHERE  FP.ReceiptNumber = @ReceiptNumber
           AND FP.RelatedFeePaymentId IS NULL;
    SELECT @Reversed = ISNULL(SUM(-R.TotalAmount), 0)
    FROM   dbo.FeePayments AS R
           INNER JOIN
           dbo.FeePayments AS P
           ON P.FeePaymentId = R.RelatedFeePaymentId
    WHERE  P.ReceiptNumber = @ReceiptNumber
           AND R.TotalAmount <> 0 AND R.IsCreditApplication=0 AND R.Status=N'Completed';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeePayments AS FP
                   WHERE  FP.ReceiptNumber = @ReceiptNumber)
        THROW 59911, 'No payments were found for that receipt number.', 1;
    DECLARE @ConcessionJson nvarchar(max)=(SELECT TOP(1) ConcessionLinesJson FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber ORDER BY FeePaymentId);
    SET @PayerCount=@PayerCount+(SELECT COUNT(DISTINCT J.AdmissionId) FROM OPENJSON(@ConcessionJson) WITH(AdmissionId int) J
      WHERE NOT EXISTS(SELECT 1 FROM dbo.FeePayments P WHERE P.ReceiptNumber=@ReceiptNumber AND P.AdmissionId=J.AdmissionId));
    SELECT @ReceiptNumber AS ReceiptNumber,
           MIN(FP.PaymentDate) AS PaymentDate,
           MAX(FP.PaymentMethod) AS PaymentMethod,
           MAX(FP.ReferenceNumber) AS ReferenceNumber,
           @PayerCount AS StudentCount,
           @Issued AS IssuedAmount,
           @Reversed AS ReversedAmount,
           @Issued - @Reversed AS LiveAmount,
           CASE WHEN @Reversed <= 0 THEN N'Issued' WHEN @Issued - @Reversed <= 0 THEN N'Fully reversed' ELSE N'Partially reversed' END AS ReceiptStatus
    FROM   dbo.FeePayments AS FP
    WHERE  FP.ReceiptNumber = @ReceiptNumber
           AND FP.RelatedFeePaymentId IS NULL;
    EXEC dbo.GetFeeReceiptLines @UserId,@ReceiptNumber;
END;

GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'17')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'17',SYSDATETIME(),SUSER_SNAME(),N'Effective-dated concession labels, zero-payable family lines and preserved credit receipt lines.');
GO
