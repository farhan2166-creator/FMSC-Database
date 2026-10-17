-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.ProcessFamilyFeeTransaction
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
