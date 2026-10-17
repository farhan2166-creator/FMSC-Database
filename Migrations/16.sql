-- Student credit is an append-only financial history. Charge settlement still uses FeePaymentAllocations.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH(N'dbo.FeePayments',N'IsCreditApplication') IS NULL
 ALTER TABLE dbo.FeePayments ADD IsCreditApplication bit NOT NULL CONSTRAINT DF_FeePayments_IsCreditApplication DEFAULT(0);
IF OBJECT_ID(N'dbo.StudentCreditEntries',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.StudentCreditEntries(
  CreditEntryId bigint IDENTITY PRIMARY KEY,
  AdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),
  Amount decimal(12,2) NOT NULL CONSTRAINT CK_StudentCreditEntries_Amount CHECK(Amount<>0 AND Amount=FLOOR(Amount)),
  EntryKind nvarchar(20) NOT NULL CONSTRAINT CK_StudentCreditEntries_Kind CHECK(EntryKind IN(N'Deposit',N'Application',N'Refund',N'TransferIn',N'TransferOut',N'BillReduction')),
  FeePaymentId int NULL REFERENCES dbo.FeePayments(FeePaymentId),
  RequestId uniqueidentifier NOT NULL,
  CounterpartyAdmissionId int NULL REFERENCES dbo.Students(AdmissionId),
  Reason nvarchar(500) NOT NULL,
  CreatedByUserId int NOT NULL REFERENCES dbo.Users(UserId),
  CreatedAt datetime2(0) NOT NULL DEFAULT SYSDATETIME(),
  CONSTRAINT UX_StudentCreditEntries_Request UNIQUE(RequestId,AdmissionId,EntryKind)
 );
 CREATE INDEX IX_StudentCreditEntries_Admission ON dbo.StudentCreditEntries(AdmissionId) INCLUDE(Amount);
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentCredit @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 SELECT @AdmissionId AdmissionId,COALESCE(SUM(Amount),0) AvailableCredit FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId;
 SELECT CreditEntryId,EntryKind,Amount,FeePaymentId,CounterpartyAdmissionId,Reason,CreatedAt
 FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId DESC;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ApplyStudentCredit_Internal @AdmissionId int,@UserId int=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 54900,'Credit application requires a transaction.',1;
 DECLARE @Credit decimal(12,2),@Lock int,@Resource nvarchar(255)=CONCAT(N'FMSC.StudentCredit.',@AdmissionId),@Actor int;
 EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @Lock<0 THROW 54901,'The student credit account is busy. Try again.',1;
 SELECT @Credit=COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @Credit<=0 RETURN;
 SELECT TOP(1) @Actor=COALESCE(@UserId,CreatedByUserId) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId DESC;
 DECLARE @Charge bigint,@Enrollment int,@Balance decimal(12,2),@Use decimal(12,2),@Payment int,
  @Receipt nvarchar(40),@Request uniqueidentifier;
 -- Apply only to obligations whose charge date has arrived. Never consume credit against a future opening bill early.
 DECLARE Charges CURSOR LOCAL FAST_FORWARD FOR
  SELECT B.BillingChargeId,B.EnrollmentId,dbo.GetFeeChargeBalance(B.BillingChargeId)
  FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled' AND B.ChargeDate<=CONVERT(date,GETDATE())
   AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
  ORDER BY CASE WHEN F.FeeTypeCode=N'TUITION' THEN 0 ELSE 1 END,COALESCE(B.BillingMonth,B.ChargeDate),B.BillingChargeId;
 OPEN Charges; FETCH NEXT FROM Charges INTO @Charge,@Enrollment,@Balance;
 WHILE @@FETCH_STATUS=0 AND @Credit>0
 BEGIN
  SET @Use=CASE WHEN @Credit>@Balance THEN @Balance ELSE @Credit END;
  SET @Request=NEWID();SET @Receipt=CONCAT(N'CR-',CONVERT(nvarchar(36),@Request));
  INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,CreatedByUserId,IsCreditApplication)
   VALUES(@AdmissionId,@Enrollment,CONVERT(date,GETDATE()),@Receipt,@Use,N'Account Credit',N'Existing credit applied; no new money received',N'Completed',@Actor,1);
  SET @Payment=CONVERT(int,SCOPE_IDENTITY());
  INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount,Remarks)
   VALUES(@Payment,@AdmissionId,@Charge,@Use,N'Existing student credit');
  INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,FeePaymentId,RequestId,Reason,CreatedByUserId)
   VALUES(@AdmissionId,-@Use,N'Application',@Payment,@Request,N'Existing credit applied to fees',@Actor);
  UPDATE dbo.BillingCharges SET Status=CASE WHEN dbo.GetFeeChargeBalance(@Charge)=0 THEN N'Paid' ELSE N'PartiallyPaid' END WHERE BillingChargeId=@Charge;
  SET @Credit=@Credit-@Use;
  FETCH NEXT FROM Charges INTO @Charge,@Enrollment,@Balance;
 END;
 CLOSE Charges; DEALLOCATE Charges;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ProcessStudentCredit
 @UserId int,@AdmissionId int,@Action nvarchar(20),@Amount decimal(12,2),@RequestId uniqueidentifier,
 @Reason nvarchar(500),@TargetAdmissionId int=NULL,@PaymentMethod nvarchar(30)=N'Cash',@ReceiptNumber nvarchar(40)=NULL,@TransactionDate date=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 SET @TransactionDate=COALESCE(@TransactionDate,CONVERT(date,GETDATE()));
 IF @TransactionDate>CONVERT(date,GETDATE()) THROW 54902,'Credit transactions cannot be dated in the future.',1;
 IF @Action IS NULL OR @Action NOT IN(N'Deposit',N'Refund',N'Transfer') OR @Amount IS NULL OR @Amount<=0 OR @Amount<>FLOOR(@Amount)
   OR @RequestId IS NULL OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
  THROW 54902,'Choose a credit action, whole-rupee amount and reason.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId) THROW 54903,'Student not found.',1;
 IF @Action=N'Deposit' AND @PaymentMethod IN(N'Refund',N'Correction',N'Account Credit') THROW 54902,'Select a money-received payment method.',1;
 IF @Action=N'Transfer' AND (@TargetAdmissionId IS NULL OR @TargetAdmissionId=@AdmissionId OR NOT EXISTS(
   SELECT 1 FROM dbo.FamilyMembers A JOIN dbo.FamilyMembers B ON B.FamilyId=A.FamilyId
    WHERE A.AdmissionId=@AdmissionId AND B.AdmissionId=@TargetAdmissionId)) THROW 54904,'Select a different student in the same family.',1;
 DECLARE @Lock int,@Resource nvarchar(255),@First int=@AdmissionId,@Second int,@Available decimal(12,2),@Enrollment int,@Payment int,@Existing int;
 IF @Action=N'Transfer' BEGIN SET @First=CASE WHEN @AdmissionId<@TargetAdmissionId THEN @AdmissionId ELSE @TargetAdmissionId END;SET @Second=CASE WHEN @First=@AdmissionId THEN @TargetAdmissionId ELSE @AdmissionId END;END;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  SET @Resource=CONCAT(N'FMSC.StudentCredit.',@First);
  EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54901,'Credit account is busy. Try again.',1;
  IF @Second IS NOT NULL BEGIN
   SET @Resource=CONCAT(N'FMSC.StudentCredit.',@Second);
   EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
   IF @Lock<0 THROW 54901,'Sibling credit account is busy. Try again.',1;
  END;
  IF EXISTS(SELECT 1 FROM dbo.StudentCreditEntries WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.StudentCreditEntries WHERE RequestId=@RequestId AND AdmissionId=@AdmissionId
    AND Amount=CASE WHEN @Action=N'Deposit' THEN @Amount ELSE -@Amount END
    AND EntryKind=CASE WHEN @Action=N'Transfer' THEN N'TransferOut' ELSE @Action END
    AND ISNULL(CounterpartyAdmissionId,0)=ISNULL(@TargetAdmissionId,0)) THROW 54905,'This request belongs to another credit operation.',1;
   COMMIT;
   SELECT E.FeePaymentId,P.ReceiptNumber,@AdmissionId AdmissionId,COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId),0) AvailableCredit
    FROM dbo.StudentCreditEntries E LEFT JOIN dbo.FeePayments P ON P.FeePaymentId=E.FeePaymentId
    WHERE E.RequestId=@RequestId AND E.AdmissionId=@AdmissionId;RETURN;
  END;
  SELECT @Available=COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @Action<>N'Deposit' AND @Amount>@Available THROW 54906,'The amount exceeds available student credit. Refresh the account.',1;
  IF @Action IN(N'Deposit',N'Refund')
  BEGIN
   SELECT TOP(1) @Enrollment=EnrollmentId FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY CASE WHEN Status=N'Active' THEN 0 ELSE 1 END,EnrollmentId DESC;
   IF @Enrollment IS NULL THROW 54907,'A student placement is required to issue a credit receipt.',1;
   IF @ReceiptNumber IS NULL BEGIN DECLARE @Date date=@TransactionDate; EXEC dbo.GetNextFeeReceiptNumber @ReceiptDate=@Date,@ReceiptNumber=@ReceiptNumber OUTPUT; END;
   INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Remarks,Status,IsGroupPayment,CreatedByUserId)
    VALUES(@AdmissionId,@Enrollment,@TransactionDate,@ReceiptNumber,CASE WHEN @Action=N'Deposit' THEN @Amount ELSE -@Amount END,
     CASE WHEN @Action=N'Deposit' THEN @PaymentMethod ELSE N'Refund' END,@Reason,N'Completed',1,@UserId);
   SET @Payment=CONVERT(int,SCOPE_IDENTITY());
  END;
  INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,FeePaymentId,RequestId,CounterpartyAdmissionId,Reason,CreatedByUserId)
   VALUES(@AdmissionId,CASE WHEN @Action=N'Deposit' THEN @Amount ELSE -@Amount END,
    CASE WHEN @Action=N'Transfer' THEN N'TransferOut' ELSE @Action END,@Payment,@RequestId,@TargetAdmissionId,@Reason,@UserId);
  IF @Action=N'Transfer'
   INSERT dbo.StudentCreditEntries(AdmissionId,Amount,EntryKind,RequestId,CounterpartyAdmissionId,Reason,CreatedByUserId)
    VALUES(@TargetAdmissionId,@Amount,N'TransferIn',@RequestId,@AdmissionId,@Reason,@UserId);
  COMMIT;
  SELECT @Payment FeePaymentId,@ReceiptNumber ReceiptNumber,@AdmissionId AdmissionId,
   COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId),0) AvailableCredit;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ProcessFamilyFeeTransaction
    @TransactionType NVARCHAR(20),
    @TransactionDate DATE,
    @PaymentMethod   NVARCHAR(30),
    @ReferenceNumber NVARCHAR(100),
    @Remarks         NVARCHAR(500),
    @Allocations     dbo.FamilyFeeAllocationType READONLY,
    @UserId          INT,
    @ReceivedAmount decimal(12,2)=NULL,@CreditAdmissionId int=NULL,@CreditRequestId uniqueidentifier=NULL
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
CREATE OR ALTER PROCEDURE dbo.PostSelectedMonthlyFees
 @UserId int,@BillingMonth date,@Selection dbo.PostingSelectionType READONLY,
 @Reason nvarchar(500)=NULL,@AcademicYearId int,@AllowRepost bit=1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @BillingMonth IS NULL THROW 54720,'Choose a billing month.',1;
 IF NOT EXISTS(SELECT 1 FROM @Selection) THROW 54721,'No charges selected.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
   THROW 54743,'Select a valid academic year.',1;
 DECLARE @Month date=DATEFROMPARTS(YEAR(@BillingMonth),MONTH(@BillingMonth),1),@Lock int,@Count int;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
  BEGIN TRAN;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Monthly billing is already running. Try again shortly.',1;
  SELECT @Count=COUNT(*) FROM dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK)
   JOIN @Selection S ON S.EnrollmentId=E.EnrollmentId;
  SELECT @Count=COUNT(*) FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
   JOIN @Selection S ON S.EnrollmentId=A.EnrollmentId AND S.FeeTypeId=A.FeeTypeId;
  SELECT @Count=COUNT(*) FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK)
   JOIN @Selection S ON S.EnrollmentId=B.EnrollmentId AND S.FeeTypeId=B.FeeTypeId WHERE B.BillingMonth=@Month;
  IF EXISTS(SELECT 1 FROM @Selection S LEFT JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
    WHERE E.EnrollmentId IS NULL OR E.AcademicYearId<>@AcademicYearId
      OR DATEFROMPARTS(YEAR(S.BillingMonth),MONTH(S.BillingMonth),1)<>@Month)
    THROW 54744,'The selection changed or belongs to another month/year. Refresh the review.',1;
  IF @AllowRepost=1 AND EXISTS(SELECT 1 FROM @Selection S
    JOIN dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C ON C.EnrollmentId=S.EnrollmentId AND C.FeeTypeId=S.FeeTypeId
    WHERE C.PreviouslyCancelled=1 AND C.BillingChargeId IS NULL)
    AND NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL
    THROW 54746,'Enter a reason before charging a previously reversed fee again.',1;
  INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,
    BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,Remarks,ChargeKind)
  SELECT C.StudentFeeAssignmentId,C.EnrollmentId,C.AdmissionId,C.FeeTypeId,@Month,@Month,@Month,
    C.Amount,C.DiscountAmount,COALESCE(NULLIF(LTRIM(RTRIM(@Reason)),N''),N'Monthly fee posting'),N'Monthly'
  FROM dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C
  JOIN @Selection S ON S.EnrollmentId=C.EnrollmentId AND S.FeeTypeId=C.FeeTypeId
  WHERE C.BlockReason IS NULL AND C.BillingChargeId IS NULL AND (@AllowRepost=1 OR C.PreviouslyCancelled=0);
  DECLARE @Created int=@@ROWCOUNT,@Requested int=(SELECT COUNT(*) FROM @Selection);
  DECLARE @CreditStudent int;
  DECLARE CreditStudents CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT E.AdmissionId FROM @Selection S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId ORDER BY E.AdmissionId;
  OPEN CreditStudents; FETCH NEXT FROM CreditStudents INTO @CreditStudent;
  WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.ApplyStudentCredit_Internal @CreditStudent,@UserId; FETCH NEXT FROM CreditStudents INTO @CreditStudent; END;
  CLOSE CreditStudents; DEALLOCATE CreditStudents;
  COMMIT;
  SELECT @Month BillingMonth,@Requested RowsRequested,@Created ChargesCreated,@Requested-@Created RowsNotPosted;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
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
           @OpeningMonth,
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
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
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
           AND SFA.Frequency = N'Monthly'
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
            @OpeningMonth,@ChargeDate,@ChargeDate,A.Amount,A.DiscountAmount,N'Manual'
        FROM dbo.StudentFeeAssignments A
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND A.Frequency=N'As Needed'
            AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=@OpeningMonth)
            AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=@OpeningMonth)
            AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@EnrollmentId
                AND B.FeeTypeId=A.FeeTypeId AND B.ChargeKind=N'Manual' AND B.Status<>N'Cancelled');
    DECLARE @CreditStudent int=(SELECT AdmissionId FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId);
    IF @@TRANCOUNT>0 EXEC dbo.ApplyStudentCredit_Internal @CreditStudent;
END

GO
CREATE OR ALTER PROCEDURE dbo.GetCollectionsAndReceiptsReport
    @UserId int,
    @View nvarchar(30),
    @GroupBy nvarchar(30) = N'Day',
    @FromDate date = NULL,
    @ToDate date = NULL,
    @CampusId int = 0,
    @ClassId int = 0,
    @SectionId int = 0,
    @PaymentMethod nvarchar(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    IF @View NOT IN (N'Receipts', N'Detailed Transactions', N'Summary')
        THROW 59901, 'The collections report view is not supported.', 1;
    IF @View = N'Summary' AND @GroupBy NOT IN
       (N'Day', N'Week', N'Month', N'Payment Method', N'Class', N'Campus', N'Fee Type')
        THROW 59902, 'The collections report grouping is not supported.', 1;
    IF @FromDate IS NOT NULL AND @ToDate IS NOT NULL AND @FromDate > @ToDate
        THROW 59903, 'The collections report date range is invalid.', 1;

    SELECT FP.FeePaymentId, FP.ReceiptNumber,
           COALESCE(NULLIF(FP.ReceiptNumber, N''), CONCAT(N'PAYMENT-', FP.FeePaymentId)) ReceiptKey,
           FP.PaymentDate, FP.PaymentMethod, FP.ReferenceNumber, FP.TotalAmount,
           FP.RelatedFeePaymentId, FP.IsGroupPayment, FP.AdmissionId,
           S.RegistrationNumber, S.StudentName, CAMP.CampusName, C.ClassName, SEC.SectionName,
           U.Username ReceivedBy
    INTO #Payments
    FROM dbo.FeePayments FP
    JOIN dbo.Students S ON S.AdmissionId = FP.AdmissionId
    JOIN dbo.Enrollments E ON E.EnrollmentId = FP.EnrollmentId AND E.AdmissionId = FP.AdmissionId
    LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    LEFT JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    LEFT JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    LEFT JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    LEFT JOIN dbo.Users U ON U.UserId = FP.CreatedByUserId
    WHERE FP.Status = N'Completed' AND FP.IsCreditApplication=0
      AND (@FromDate IS NULL OR FP.PaymentDate >= @FromDate) AND (@ToDate IS NULL OR FP.PaymentDate <= @ToDate)
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
      AND (@PaymentMethod IS NULL OR FP.PaymentMethod = @PaymentMethod);

    IF @View = N'Receipts'
    BEGIN
        SELECT MAX(ReceiptNumber) [Receipt No.], MIN(PaymentDate) [Date],
               CASE WHEN COUNT(DISTINCT AdmissionId) > 1 THEN N'Family receipt'
                    ELSE MAX(StudentName) END [Student / Family],
               CASE WHEN COUNT(DISTINCT ClassName) > 1 THEN N'Multiple' ELSE MAX(ClassName) END [Class],
               CASE WHEN COUNT(DISTINCT PaymentMethod) > 1 THEN N'Mixed' ELSE MAX(PaymentMethod) END [Payment Method],
               SUM(CASE WHEN TotalAmount > 0 THEN TotalAmount ELSE 0 END) [Gross Received],
               -SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Refund' THEN TotalAmount ELSE 0 END) [Refunds],
               -SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Correction' THEN TotalAmount ELSE 0 END) [Corrections],
               SUM(TotalAmount) [Net Amount], MAX(ReceivedBy) [Received By]
        FROM #Payments GROUP BY ReceiptKey ORDER BY MIN(PaymentDate), MAX(ReceiptNumber);
        SELECT COUNT(DISTINCT ReceiptKey) Receipts,
               COALESCE(SUM(CASE WHEN TotalAmount > 0 THEN TotalAmount ELSE 0 END), 0) [Gross Received],
               -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Refund' THEN TotalAmount ELSE 0 END), 0) Refunds,
               -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Correction' THEN TotalAmount ELSE 0 END), 0) Corrections,
               COALESCE(SUM(TotalAmount), 0) [Net Collection] FROM #Payments;
        RETURN;
    END;

    IF @View = N'Detailed Transactions'
    BEGIN
        SELECT P.ReceiptNumber [Receipt No.], P.PaymentDate [Date],
               COALESCE(CS.StudentName, P.StudentName) [Student],
               FT.FeeTypeName [Fee Type], COALESCE(CONVERT(nvarchar(20), BC.BillingMonth, 106), BC.Remarks) [Charge / Month],
               COALESCE(FPA.Amount,P.TotalAmount) [Amount], P.PaymentMethod [Payment Method],
               OP.ReceiptNumber [Original Receipt], P.ReferenceNumber [Reference]
        FROM #Payments P
        LEFT JOIN dbo.FeePaymentAllocations FPA ON FPA.FeePaymentId = P.FeePaymentId
        LEFT JOIN dbo.BillingCharges BC ON BC.BillingChargeId = FPA.BillingChargeId
        LEFT JOIN dbo.Students CS ON CS.AdmissionId = BC.AdmissionId
        LEFT JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
        LEFT JOIN dbo.FeePayments OP ON OP.FeePaymentId = P.RelatedFeePaymentId
        ORDER BY P.PaymentDate, P.ReceiptNumber, FPA.FeePaymentAllocationId;
        SELECT COUNT(DISTINCT ReceiptKey) Receipts,
               COALESCE(SUM(CASE WHEN TotalAmount > 0 THEN TotalAmount ELSE 0 END), 0) [Gross Received],
               -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Refund' THEN TotalAmount ELSE 0 END), 0) Refunds,
               -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Correction' THEN TotalAmount ELSE 0 END), 0) Corrections,
               COALESCE(SUM(TotalAmount), 0) [Net Collection] FROM #Payments;
        RETURN;
    END;

    IF @GroupBy = N'Fee Type'
    BEGIN
        SELECT COALESCE(FT.FeeTypeName, N'Student Credit') [Group],
               COUNT(DISTINCT P.ReceiptKey) [Receipts],
               COALESCE(SUM(CASE WHEN COALESCE(FPA.Amount,P.TotalAmount) > 0 THEN COALESCE(FPA.Amount,P.TotalAmount) ELSE 0 END), 0) [Gross Received],
               -COALESCE(SUM(CASE WHEN COALESCE(FPA.Amount,P.TotalAmount) < 0 AND P.PaymentMethod = N'Refund' THEN COALESCE(FPA.Amount,P.TotalAmount) ELSE 0 END), 0) [Refunds],
               -COALESCE(SUM(CASE WHEN COALESCE(FPA.Amount,P.TotalAmount) < 0 AND P.PaymentMethod = N'Correction' THEN COALESCE(FPA.Amount,P.TotalAmount) ELSE 0 END), 0) [Corrections],
               COALESCE(SUM(COALESCE(FPA.Amount,P.TotalAmount)), 0) [Net Collection]
        FROM #Payments P
        LEFT JOIN dbo.FeePaymentAllocations FPA ON FPA.FeePaymentId = P.FeePaymentId
        LEFT JOIN dbo.BillingCharges BC ON BC.BillingChargeId = FPA.BillingChargeId
        LEFT JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
        GROUP BY COALESCE(FT.FeeTypeName, N'Student Credit')
        ORDER BY [Group];
    END
    ELSE
    BEGIN
        ;WITH S AS
        (
            SELECT CASE @GroupBy
                     WHEN N'Week' THEN CONCAT(CONVERT(char(10), DATEADD(day, -(DATEDIFF(day, 0, PaymentDate) % 7), PaymentDate), 23), N' week')
                     WHEN N'Month' THEN CONVERT(char(7), PaymentDate, 126)
                     WHEN N'Payment Method' THEN PaymentMethod
                     WHEN N'Class' THEN COALESCE(ClassName, N'Unassigned')
                     WHEN N'Campus' THEN COALESCE(CampusName, N'Unassigned')
                     ELSE CONVERT(char(10), PaymentDate, 23) END [Group],
                   ReceiptKey, TotalAmount, PaymentMethod
            FROM #Payments
        )
        SELECT [Group], COUNT(DISTINCT ReceiptKey) [Receipts],
               COALESCE(SUM(CASE WHEN TotalAmount > 0 THEN TotalAmount ELSE 0 END), 0) [Gross Received],
               -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Refund' THEN TotalAmount ELSE 0 END), 0) [Refunds],
               -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Correction' THEN TotalAmount ELSE 0 END), 0) [Corrections],
               COALESCE(SUM(TotalAmount), 0) [Net Collection]
        FROM S GROUP BY [Group] ORDER BY [Group];
    END;

    SELECT COUNT(DISTINCT ReceiptKey) Receipts,
           COALESCE(SUM(CASE WHEN TotalAmount > 0 THEN TotalAmount ELSE 0 END), 0) [Gross Received],
           -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Refund' THEN TotalAmount ELSE 0 END), 0) Refunds,
           -COALESCE(SUM(CASE WHEN TotalAmount < 0 AND PaymentMethod = N'Correction' THEN TotalAmount ELSE 0 END), 0) Corrections,
           COALESCE(SUM(TotalAmount), 0) [Net Collection] FROM #Payments;
END;

GO
CREATE OR ALTER PROCEDURE dbo.GetDashboardSummary
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Reports.View';
    DECLARE @Today AS DATE = CAST (GETDATE() AS DATE);
    DECLARE @MonthStart AS DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    SELECT (SELECT COUNT(*)
            FROM   dbo.Students) AS TotalStudents,
           (SELECT COUNT(*)
            FROM   dbo.Students
            WHERE  Status = N'Active') AS ActiveStudents,
           (SELECT COUNT(*)
            FROM   dbo.Classes) AS TotalClasses,
           (SELECT COUNT(*)
            FROM   dbo.Staff
            WHERE  Status = N'Active') AS ActiveStaff,
           (SELECT COUNT(*)
            FROM   dbo.Campuses) AS ActiveCampuses,
           (SELECT COUNT(*)
            FROM   dbo.Students
            WHERE  AdmissionDate >= @MonthStart
                   AND AdmissionDate <= @Today) AS NewAdmissionsThisMonth,
           (SELECT ISNULL(SUM(FP.TotalAmount), 0)
            FROM   dbo.FeePayments AS FP
            WHERE  FP.Status = N'Completed' AND FP.IsCreditApplication=0
                   AND FP.PaymentDate >= @MonthStart
                   AND FP.PaymentDate <= @Today) AS FeeCollectedThisMonth,
           (SELECT ISNULL(SUM(FP.TotalAmount), 0)
            FROM   dbo.FeePayments AS FP
            WHERE  FP.Status = N'Completed' AND FP.IsCreditApplication=0
                   AND FP.PaymentDate = @Today) AS FeeCollectedToday,
           (SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId)), 0)
            FROM   dbo.BillingCharges AS BC
            WHERE  BC.Status <> N'Cancelled'
                   AND dbo.GetFeeChargeBalance(BC.BillingChargeId) > 0) AS FeeOutstanding;
END

GO
CREATE OR ALTER PROCEDURE dbo.GetAdjustableFeePayments
    @UserId int, @AdmissionId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId,N'Fees.View';
    IF @AdmissionId<=0 OR NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId)
        THROW 54001,'A valid student is required.',1;
    SELECT FP.FeePaymentId, FP.EnrollmentId, FP.PaymentDate AS TransactionDate,
           FP.ReceiptNumber, FP.PaymentMethod, N'PAYMENT' AS TransactionType,
           FP.TotalAmount AS PaidAmount, FP.TotalAmount-ISNULL(R.ReducedAmount,0) AS RemainingAmount
    FROM dbo.FeePayments FP
    OUTER APPLY (SELECT -SUM(A.Amount) AS ReducedAmount
                 FROM dbo.FeePayments P JOIN dbo.FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
                 WHERE P.RelatedFeePaymentId=FP.FeePaymentId AND P.Status=N'Completed'
                   AND P.TotalAmount<0 AND A.Amount<0) R
    WHERE FP.AdmissionId=@AdmissionId AND FP.Status=N'Completed' AND FP.IsCreditApplication=0
      AND NOT EXISTS(SELECT 1 FROM dbo.StudentCreditEntries C WHERE C.FeePaymentId=FP.FeePaymentId)
      AND FP.RelatedFeePaymentId IS NULL AND FP.TotalAmount>0
      AND FP.TotalAmount-ISNULL(R.ReducedAmount,0)>0
    ORDER BY FP.PaymentDate DESC,FP.FeePaymentId DESC;
END;

GO
CREATE OR ALTER PROCEDURE dbo.ProcessFeeTransaction
@GroupReceiptNumber NVARCHAR (40)=NULL, @AdmissionId INT, @EnrollmentId INT, @TransactionType NVARCHAR (20), @Amount DECIMAL (12, 2), @TransactionDate DATE, @PaymentMethod NVARCHAR (30), @ReferenceNumber NVARCHAR (100), @Remarks NVARCHAR (500), @FeePaymentId INT, @BillingChargeId BIGINT, @Allocations dbo.FeePaymentAllocationType READONLY, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    SET @TransactionType = LTRIM(RTRIM(@TransactionType));
    IF @TransactionType NOT IN (N'Collection', N'Correction', N'Refund', N'Reversal')
        THROW 54001, 'Invalid fee transaction type.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 54002, 'The selected student was not found.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId
                          AND AdmissionId = @AdmissionId)
        THROW 54003, 'The selected enrollment does not belong to this student.', 1;
    IF @TransactionDate IS NULL
        THROW 54005, 'The transaction date is required.', 1;
    IF @TransactionDate > CAST (GETDATE() AS DATE)
        THROW 54004, 'The transaction date cannot be in the future.', 1;
    IF @TransactionType<>N'Collection' EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
    IF @TransactionType<>N'Collection' AND EXISTS(SELECT 1 FROM dbo.StudentCreditEntries WHERE FeePaymentId=@FeePaymentId)
      THROW 54910,'Use Student Credit or the bill correction workflow for this credit transaction.',1;
    BEGIN TRANSACTION;
    DECLARE @ReceiptNumber AS NVARCHAR (40) = NULL, @ResultPaymentId AS INT = NULL, @NewBalance AS DECIMAL (12, 2) = NULL, @PaymentAmount AS DECIMAL (12, 2) = 0, @DiscountTotal AS DECIMAL (12, 2) = 0, @EnrollmentDate AS DATE = NULL;
    SELECT @EnrollmentDate = EnrollmentDate
    FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
    WHERE  EnrollmentId = @EnrollmentId
           AND AdmissionId = @AdmissionId;
    IF @EnrollmentDate IS NULL
        THROW 54006, 'The selected enrollment has no valid enrollment date.', 1;
    IF @TransactionType = N'Collection'
        BEGIN
            IF @TransactionDate < @EnrollmentDate
                THROW 54007, 'The transaction date cannot be before the enrollment date.', 1;
            SELECT @PaymentAmount = ISNULL(SUM(CASE WHEN Amount > 0 THEN Amount ELSE 0 END), 0),
                   @DiscountTotal = ISNULL(-SUM(CASE WHEN Amount < 0 THEN Amount ELSE 0 END), 0)
            FROM   @Allocations;
            IF @DiscountTotal>0 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
            IF @PaymentAmount <= 0
               AND @DiscountTotal <= 0
                THROW 54010, 'Enter a payment or a discount.', 1;
            IF ABS(@Amount - @PaymentAmount) > 0.005
                THROW 54011, 'The payment total does not match the Pay Now allocations.', 1;
            IF EXISTS (SELECT 1
                       FROM   @Allocations AS A
                              LEFT OUTER JOIN
                              dbo.BillingCharges AS BC WITH (UPDLOCK, HOLDLOCK)
                              ON BC.BillingChargeId = A.BillingChargeId CROSS APPLY (SELECT ISNULL(SUM(CASE WHEN X.Amount > 0 THEN X.Amount ELSE 0 END), 0) AS PaymentPart,
                                                                                            ISNULL(-SUM(CASE WHEN X.Amount < 0 THEN X.Amount ELSE 0 END), 0) AS DiscountPart
                                                                                     FROM   @Allocations AS X
                                                                                     WHERE  X.BillingChargeId = A.BillingChargeId) AS V
                       WHERE  BC.BillingChargeId IS NULL
                              OR BC.AdmissionId <> @AdmissionId
                              OR BC.EnrollmentId <> @EnrollmentId
                              OR BC.Status = N'Cancelled'
                              OR BC.ChargeDate IS NULL
                              OR V.DiscountPart > dbo.GetFeeChargeBalance(A.BillingChargeId)
                              OR V.PaymentPart + V.DiscountPart > dbo.GetFeeChargeBalance(A.BillingChargeId))
                THROW 54013, 'One or more selected charges is invalid: it has been cancelled, belongs to another student or enrollment, has no charge date, or its remaining balance no longer covers this allocation. Refresh and try again.', 1;
            IF @PaymentAmount > 0
               AND NULLIF (LTRIM(RTRIM(@PaymentMethod)), N'') IS NULL
                THROW 54014, 'Select a payment method.', 1;
            UPDATE BC
            SET    DiscountAmount = BC.DiscountAmount + D.DiscountAmount,
                   Remarks        = CASE WHEN NULLIF (LTRIM(RTRIM(@Remarks)), N'') IS NULL THEN BC.Remarks WHEN NULLIF (BC.Remarks, N'') IS NULL THEN @Remarks ELSE BC.Remarks + N' | ' + @Remarks END
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT   BillingChargeId,
                             -SUM(CASE WHEN Amount < 0 THEN Amount ELSE 0 END) AS DiscountAmount
                    FROM     @Allocations
                    GROUP BY BillingChargeId
                    HAVING   SUM(CASE WHEN Amount < 0 THEN Amount ELSE 0 END) < 0) AS D
                   ON D.BillingChargeId = BC.BillingChargeId;
            IF @PaymentAmount > 0
                BEGIN
                    IF @GroupReceiptNumber IS NULL
                        BEGIN
                            EXECUTE dbo.GetNextFeeReceiptNumber @ReceiptDate = @TransactionDate, @ReceiptNumber = @ReceiptNumber OUTPUT;
                        END
                    ELSE
                        BEGIN
                            SET @ReceiptNumber = @GroupReceiptNumber;
                        END
                    INSERT  dbo.FeePayments (AdmissionId, EnrollmentId, PaymentDate, ReceiptNumber, TotalAmount, PaymentMethod, ReferenceNumber, Remarks, Status, IsGroupPayment, CreatedByUserId)
                    VALUES                 (@AdmissionId, @EnrollmentId, @TransactionDate, @ReceiptNumber, @PaymentAmount, @PaymentMethod, NULLIF (LTRIM(RTRIM(@ReferenceNumber)), N''), NULLIF (LTRIM(RTRIM(@Remarks)), N''), N'Completed', CASE WHEN @GroupReceiptNumber IS NULL THEN 0 ELSE 1 END, @UserId);
                    SET @ResultPaymentId = CONVERT (INT, SCOPE_IDENTITY());
                    INSERT dbo.FeePaymentAllocations (FeePaymentId, AdmissionId, BillingChargeId, Amount, Remarks)
                    SELECT @ResultPaymentId,
                           @AdmissionId,
                           BillingChargeId,
                           Amount,
                           NULL
                    FROM   @Allocations
                    WHERE  Amount > 0;
                END
            UPDATE BC
            SET    Status = CASE WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) <= 0 THEN N'Paid' WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) < BC.NetAmount THEN N'PartiallyPaid' ELSE N'Open' END
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT DISTINCT BillingChargeId
                    FROM   @Allocations) AS A
                   ON A.BillingChargeId = BC.BillingChargeId;
            SELECT TOP (1) @NewBalance = dbo.GetFeeChargeBalance(BC.BillingChargeId)
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT   TOP (1) BillingChargeId
                    FROM     @Allocations
                    ORDER BY BillingChargeId) AS A
                   ON A.BillingChargeId = BC.BillingChargeId;
        END
    IF @TransactionType IN (N'Correction', N'Refund')
        BEGIN
            IF @FeePaymentId IS NULL
                THROW 54020, 'Select a completed payment.', 1;
            DECLARE @OriginalTotal AS DECIMAL (12, 2), @OriginalPaymentDate AS DATE, @AlreadyReduced AS DECIMAL (12, 2), @RemainingReducible AS DECIMAL (12, 2), @ReductionAmount AS DECIMAL (12, 2);
            SELECT @OriginalTotal = TotalAmount,
                   @OriginalPaymentDate = PaymentDate
            FROM   dbo.FeePayments WITH (UPDLOCK, HOLDLOCK)
            WHERE  FeePaymentId = @FeePaymentId
                   AND AdmissionId = @AdmissionId
                   AND EnrollmentId = @EnrollmentId
                   AND Status = N'Completed'
                   AND TotalAmount > 0;
            IF @OriginalTotal IS NULL
                THROW 54022, 'The selected payment is not an active completed payment.', 1;
            IF @OriginalPaymentDate IS NULL
                THROW 54026, 'The selected payment has no valid payment date.', 1;
            IF @TransactionDate < @OriginalPaymentDate
                THROW 54027, 'The transaction date cannot be before the original payment date.', 1;
            SELECT @AlreadyReduced = ISNULL(-SUM(FPA.Amount), 0)
            FROM   dbo.FeePayments AS P
                   INNER JOIN
                   dbo.FeePaymentAllocations AS FPA
                   ON FPA.FeePaymentId = P.FeePaymentId
            WHERE  P.RelatedFeePaymentId = @FeePaymentId
                   AND P.Status = N'Completed'
                   AND P.TotalAmount < 0
                   AND FPA.Amount < 0;
            SET @AlreadyReduced = ISNULL(@AlreadyReduced, 0);
            SET @RemainingReducible = @OriginalTotal - @AlreadyReduced;
            IF @TransactionType = N'Correction'
                BEGIN
                    IF @Amount < 0
                        THROW 54021, 'The corrected amount cannot be negative. To remove the entire payment, correct it to 0 or use Reversal.', 1;
                    IF @Amount >= @RemainingReducible
                        THROW 54023, 'The corrected amount must be less than the currently collected amount.', 1;
                    SET @ReductionAmount = @RemainingReducible - @Amount;
                END
            ELSE
                BEGIN
                    IF @Amount <= 0
                        THROW 54036, 'The refund amount must be greater than zero.', 1;
                    SET @ReductionAmount = @Amount;
                END
            IF @ReductionAmount <= 0
                THROW 54024, 'No reduction is required for the selected amount.', 1;
            EXECUTE dbo.GetNextFeeReceiptNumber @ReceiptDate = @TransactionDate, @ReceiptNumber = @ReceiptNumber OUTPUT;
            INSERT  dbo.FeePayments (AdmissionId, EnrollmentId, PaymentDate, ReceiptNumber, TotalAmount, PaymentMethod, ReferenceNumber, Remarks, Status, CreatedByUserId, RelatedFeePaymentId)
            VALUES                 (@AdmissionId, @EnrollmentId, @TransactionDate, @ReceiptNumber, -@ReductionAmount, CASE WHEN @TransactionType = N'Correction' THEN N'Correction' ELSE N'Refund' END, COALESCE (NULLIF (LTRIM(RTRIM(@ReferenceNumber)), N''), (SELECT ReceiptNumber
                                                                                                                                                                                                                                                                 FROM   dbo.FeePayments
                                                                                                                                                                                                                                                                 WHERE  FeePaymentId = @FeePaymentId)), NULLIF (LTRIM(RTRIM(@Remarks)), N''), N'Completed', @UserId, @FeePaymentId);
            SET @ResultPaymentId = CONVERT (INT, SCOPE_IDENTITY());
            DECLARE @RemainingReduction AS DECIMAL (12, 2) = @ReductionAmount, @ChargeId AS BIGINT, @OriginalAllocation AS DECIMAL (12, 2), @AlreadyOnCharge AS DECIMAL (12, 2), @Available AS DECIMAL (12, 2), @Take AS DECIMAL (12, 2);
            DECLARE ReductionCursor CURSOR LOCAL FAST_FORWARD
                FOR SELECT   FPA.BillingChargeId,
                             FPA.Amount,
                             ISNULL((SELECT -SUM(RA.Amount)
                                     FROM   dbo.FeePayments AS RP
                                            INNER JOIN
                                            dbo.FeePaymentAllocations AS RA
                                            ON RA.FeePaymentId = RP.FeePaymentId
                                     WHERE  RP.RelatedFeePaymentId = @FeePaymentId
                                            AND RP.Status = N'Completed'
                                            AND RP.TotalAmount < 0
                                            AND RA.BillingChargeId = FPA.BillingChargeId
                                            AND RA.Amount < 0), 0) AS AlreadyOnCharge
                    FROM     dbo.FeePaymentAllocations AS FPA
                    WHERE    FPA.FeePaymentId = @FeePaymentId
                             AND FPA.Amount > 0
                    ORDER BY FPA.FeePaymentAllocationId;
            OPEN ReductionCursor;
            FETCH NEXT FROM ReductionCursor INTO @ChargeId, @OriginalAllocation, @AlreadyOnCharge;
            WHILE @@FETCH_STATUS = 0
                  AND @RemainingReduction > 0
                BEGIN
                    SET @Available = @OriginalAllocation - ISNULL(@AlreadyOnCharge, 0);
                    IF @Available > 0
                        BEGIN
                            SET @Take = CASE WHEN @Available < @RemainingReduction THEN @Available ELSE @RemainingReduction END;
                            INSERT  dbo.FeePaymentAllocations (FeePaymentId, AdmissionId, BillingChargeId, Amount, Remarks)
                            VALUES                           (@ResultPaymentId, @AdmissionId, @ChargeId, -@Take, CASE WHEN @TransactionType = N'Correction' THEN N'Correction' ELSE N'Refund' END);
                            SET @RemainingReduction -= @Take;
                        END
                    FETCH NEXT FROM ReductionCursor INTO @ChargeId, @OriginalAllocation, @AlreadyOnCharge;
                END
            CLOSE ReductionCursor;
            DEALLOCATE ReductionCursor;
            IF @RemainingReduction > 0
                THROW 54025, 'The amount could not be fully allocated against the selected payment.', 1;
            UPDATE BC
            SET    Status  = CASE WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) <= 0 THEN N'Paid' WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) < BC.NetAmount THEN N'PartiallyPaid' ELSE N'Open' END,
                   Remarks = CASE WHEN NULLIF (LTRIM(RTRIM(@Remarks)), N'') IS NULL THEN BC.Remarks WHEN NULLIF (BC.Remarks, N'') IS NULL THEN @Remarks ELSE BC.Remarks + N' | ' + @Remarks END
            FROM   dbo.BillingCharges AS BC
            WHERE  BC.BillingChargeId IN (SELECT DISTINCT BillingChargeId
                                          FROM   dbo.FeePaymentAllocations
                                          WHERE  FeePaymentId = @ResultPaymentId);
            SELECT TOP (1) @NewBalance = dbo.GetFeeChargeBalance(BC.BillingChargeId)
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   (SELECT   TOP (1) BillingChargeId
                    FROM     dbo.FeePaymentAllocations
                    WHERE    FeePaymentId = @ResultPaymentId
                    ORDER BY BillingChargeId) AS A
                   ON A.BillingChargeId = BC.BillingChargeId;
        END
    IF @TransactionType = N'Reversal'
        BEGIN
            IF @FeePaymentId IS NULL
                THROW 54030, 'Select a completed payment to reverse.', 1;
            IF EXISTS (SELECT 1
                       FROM   dbo.FeePayments
                       WHERE  RelatedFeePaymentId = @FeePaymentId
                              AND Status = N'Completed'
                              AND TotalAmount < 0)
                THROW 54031, 'This payment already has a refund or correction. Reverse it through the appropriate transaction instead.', 1;
            DECLARE @AffectedCharges TABLE (
                BillingChargeId BIGINT PRIMARY KEY);
            INSERT @AffectedCharges (BillingChargeId)
            SELECT DISTINCT BillingChargeId
            FROM   dbo.FeePaymentAllocations
            WHERE  FeePaymentId = @FeePaymentId;
            DECLARE @OriginalReversalPaymentDate AS DATE = NULL;
            SELECT @Amount = TotalAmount,
                   @OriginalReversalPaymentDate = PaymentDate
            FROM   dbo.FeePayments WITH (UPDLOCK, HOLDLOCK)
            WHERE  FeePaymentId = @FeePaymentId
                   AND AdmissionId = @AdmissionId
                   AND EnrollmentId = @EnrollmentId
                   AND Status = N'Completed'
                   AND TotalAmount > 0;
            IF @Amount IS NULL
               OR @Amount <= 0
                THROW 54032, 'The selected payment is already reversed or is not a valid completed payment.', 1;
            IF @OriginalReversalPaymentDate IS NULL
                THROW 54034, 'The selected payment has no valid payment date.', 1;
            IF @TransactionDate < @OriginalReversalPaymentDate
                THROW 54035, 'The reversal date cannot be before the original payment date.', 1;
            UPDATE dbo.FeePayments
            SET    Status = N'Reversed'
            WHERE  FeePaymentId = @FeePaymentId
                   AND AdmissionId = @AdmissionId
                   AND EnrollmentId = @EnrollmentId
                   AND Status = N'Completed';
            IF @@ROWCOUNT = 0
                THROW 54033, 'The selected payment could not be reversed.', 1;
            UPDATE BC
            SET    Status = CASE WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) <= 0 THEN N'Paid' WHEN dbo.GetFeeChargeBalance(BC.BillingChargeId) < BC.NetAmount THEN N'PartiallyPaid' ELSE N'Open' END
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   @AffectedCharges AS AC
                   ON AC.BillingChargeId = BC.BillingChargeId;
        END
SELECT @NewBalance=COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled';
COMMIT TRANSACTION;
    SELECT @ResultPaymentId AS FeePaymentId,
           @ReceiptNumber AS ReceiptNumber,
           @TransactionType AS TransactionType,
           @Amount AS Amount,
           @NewBalance AS Balance;
END

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
           AND R.TotalAmount <> 0;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeePayments AS FP
                   WHERE  FP.ReceiptNumber = @ReceiptNumber)
        THROW 59911, 'No payments were found for that receipt number.', 1;
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
    SELECT * FROM (SELECT   S.StudentName,
             FP.AdmissionId,
             FT.FeeTypeName,
             BC.BillingMonth,
             FPA.Amount,
             FP.ReceiptNumber,
             FP.PaymentDate
    FROM     dbo.FeePayments AS FP
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = FP.AdmissionId
             INNER JOIN
             dbo.FeePaymentAllocations AS FPA
             ON FPA.FeePaymentId = FP.FeePaymentId
             INNER JOIN
             dbo.BillingCharges AS BC
             ON BC.BillingChargeId = FPA.BillingChargeId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId
    WHERE    FP.ReceiptNumber = @ReceiptNumber
             AND FP.RelatedFeePaymentId IS NULL
             AND FPA.Amount <> 0
    UNION ALL
    SELECT S.StudentName,P.AdmissionId,
      CASE WHEN C.EntryKind=N'Deposit' THEN N'Credit toward future fees' ELSE N'Unused credit refunded' END,
      NULL,C.Amount,P.ReceiptNumber,P.PaymentDate
    FROM dbo.StudentCreditEntries C JOIN dbo.FeePayments P ON P.FeePaymentId=C.FeePaymentId
    JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
    WHERE P.ReceiptNumber=@ReceiptNumber AND C.EntryKind IN(N'Deposit',N'Refund')
    ) Lines ORDER BY StudentName,BillingMonth,FeeTypeName;
END

GO
CREATE OR ALTER VIEW dbo.DailyClosingPayments AS
SELECT P.FeePaymentId,P.PaymentDate,P.PaymentMethod,P.TotalAmount,P.ReceiptNumber,P.RelatedFeePaymentId,P.ReferenceNumber,P.CreatedByUserId
FROM dbo.FeePayments P JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId
JOIN dbo.Enrollments E ON E.EnrollmentId=P.EnrollmentId AND E.AdmissionId=P.AdmissionId
WHERE P.Status=N'Completed' AND P.IsCreditApplication=0;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'16')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'16',SYSDATETIME(),SUSER_SNAME(),N'Student credit deposits, application, refunds and sibling transfers; no new cash on credit application.');
GO
