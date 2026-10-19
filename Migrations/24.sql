-- Stable B/FB bills and R/FR receipts. Existing numbers remain unchanged.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 24 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>23
 THROW 51000,'Migration 24 requires schema 23.',1;
CREATE TABLE dbo.FinanceDocumentCounters(
 DocumentType varchar(2) NOT NULL,DocumentYear int NOT NULL,LastNumber bigint NOT NULL,
 CONSTRAINT PK_FinanceDocumentCounters PRIMARY KEY(DocumentType,DocumentYear),
 CONSTRAINT CK_FinanceDocumentCounters_Type CHECK(DocumentType IN('B','FB','R','FR')),
 CONSTRAINT CK_FinanceDocumentCounters_Number CHECK(LastNumber>0)
);
CREATE TABLE dbo.IssuedFeeBills(
 IssuedFeeBillId uniqueidentifier NOT NULL CONSTRAINT PK_IssuedFeeBills PRIMARY KEY,
 BillNumber nvarchar(40) NOT NULL CONSTRAINT UQ_IssuedFeeBills_Number UNIQUE,
 DocumentType varchar(2) NOT NULL,DocumentYear int NOT NULL,
 PrimaryAdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),
 StudentIdsJson nvarchar(max) NOT NULL CHECK(ISJSON(StudentIdsJson)=1),
 ContentHash binary(32) NOT NULL,SnapshotXml xml NOT NULL,
 IssuedAt datetime2(0) NOT NULL,IssuedByUserId int NOT NULL REFERENCES dbo.Users(UserId),
 CONSTRAINT CK_IssuedFeeBills_Type CHECK(DocumentType IN('B','FB')),
 CONSTRAINT UQ_IssuedFeeBills_Content UNIQUE(DocumentType,DocumentYear,ContentHash)
);
GO
CREATE OR ALTER PROCEDURE dbo.GetNextFinanceDocumentNumber
 @DocumentType varchar(2),@DocumentDate date,@DocumentNumber nvarchar(40) OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @DocumentType IS NULL OR @DocumentType NOT IN('B','FB','R','FR')
  THROW 54940,'Unknown financial document type.',1;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 IF @OwnTransaction=1 BEGIN TRANSACTION;
 BEGIN TRY
  DECLARE @Year int=YEAR(COALESCE(@DocumentDate,CONVERT(date,GETDATE()))),@Number bigint,@Lock int;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.FinanceDocumentNumbers',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54941,'Document numbering is busy. Try again.',1;
  SELECT @Number=LastNumber FROM dbo.FinanceDocumentCounters WITH(UPDLOCK,HOLDLOCK)
   WHERE DocumentType=@DocumentType AND DocumentYear=@Year;
  SET @Number=COALESCE(@Number,0)+1;
  IF EXISTS(SELECT 1 FROM dbo.FinanceDocumentCounters WHERE DocumentType=@DocumentType AND DocumentYear=@Year)
   UPDATE dbo.FinanceDocumentCounters SET LastNumber=@Number WHERE DocumentType=@DocumentType AND DocumentYear=@Year;
  ELSE INSERT dbo.FinanceDocumentCounters VALUES(@DocumentType,@Year,@Number);
  DECLARE @Digits nvarchar(20)=CONVERT(nvarchar(20),@Number);
  SET @DocumentNumber=CONCAT(@DocumentType,N'-',@Year,N'-',REPLICATE(N'0',CASE WHEN LEN(@Digits)<5 THEN 5-LEN(@Digits) ELSE 0 END),@Digits);
  IF @OwnTransaction=1 COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF @OwnTransaction=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetNextFeeReceiptNumber @ReceiptDate date,@ReceiptNumber nvarchar(40) OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.GetNextFinanceDocumentNumber @DocumentType='R',@DocumentDate=@ReceiptDate,@DocumentNumber=@ReceiptNumber OUTPUT;
END;
GO
CREATE OR ALTER PROCEDURE dbo.IssueFeeBill
 @UserId int,@PrimaryAdmissionId int,@StudentIdsJson nvarchar(max),@SnapshotXml xml
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.View';
 IF @SnapshotXml IS NULL OR @StudentIdsJson IS NULL OR ISJSON(@StudentIdsJson)<>1
  THROW 54942,'Invalid bill document.',1;
 DECLARE @Ids TABLE(AdmissionId int PRIMARY KEY);
 INSERT @Ids SELECT DISTINCT CONVERT(int,value) FROM OPENJSON(@StudentIdsJson);
 IF NOT EXISTS(SELECT 1 FROM @Ids WHERE AdmissionId=@PrimaryAdmissionId)
  OR EXISTS(SELECT 1 FROM @Ids I LEFT JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.AdmissionId IS NULL)
  THROW 54942,'Choose valid students for the bill.',1;
 DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@PrimaryAdmissionId);
 IF EXISTS(SELECT 1 FROM @Ids I WHERE I.AdmissionId<>@PrimaryAdmissionId AND NOT EXISTS(
  SELECT 1 FROM dbo.FamilyMembers F WHERE F.AdmissionId=I.AdmissionId AND F.FamilyId=@Family))
  THROW 54942,'Only siblings can share a family bill.',1;
 DECLARE @Type varchar(2)=CASE WHEN (SELECT COUNT(*) FROM @Ids)>1 THEN 'FB' ELSE 'B' END,
  @Now datetime2(0)=SYSDATETIME(),@Number nvarchar(40),@Lock int;
 DECLARE @Year int=YEAR(@Now),@IdsCanonical nvarchar(max)=(SELECT AdmissionId FROM @Ids ORDER BY AdmissionId FOR JSON PATH);
 DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(nvarchar(max),@SnapshotXml)+@IdsCanonical);
 BEGIN TRANSACTION;
 BEGIN TRY
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54941,'Billing is busy. Try again.',1;
  -- A document stores the posted unpaid balance, never a fee-plan quotation.
  DECLARE @Sources TABLE(ChargeId bigint PRIMARY KEY);
  DECLARE @SourceKeys nvarchar(max)=@SnapshotXml.value('(/*/FeeReceiptData/BillSourceKey/text())[1]','nvarchar(max)');
  INSERT @Sources SELECT DISTINCT TRY_CONVERT(bigint,value) FROM STRING_SPLIT(@SourceKeys,N',');
  IF NOT EXISTS(SELECT 1 FROM @Sources) OR EXISTS(SELECT 1 FROM @Sources WHERE ChargeId IS NULL OR ChargeId=0)
   THROW 54942,'The bill has no saved charge references. Reload before printing.',1;
  IF EXISTS(SELECT 1 FROM @Sources I LEFT JOIN dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) ON B.BillingChargeId=I.ChargeId
    WHERE I.ChargeId>0 AND (B.BillingChargeId IS NULL OR B.Status=N'Cancelled' OR B.AdmissionId NOT IN(SELECT AdmissionId FROM @Ids)))
   OR EXISTS(SELECT 1 FROM @Sources I WHERE I.ChargeId<0 AND NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A
    WHERE A.StudentFeeAssignmentId=-I.ChargeId AND A.AdmissionId IN(SELECT AdmissionId FROM @Ids) AND A.CancelledAt IS NULL AND A.Amount=A.DiscountAmount))
   THROW 54942,'The bill charges have changed. Reload before printing.',1;
  DECLARE @Expected decimal(12,2)=COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)) FROM @Sources I JOIN dbo.BillingCharges B ON B.BillingChargeId=I.ChargeId),0);
  IF @SnapshotXml.value('(/*/FeeReceiptData/TotalPaid/text())[1]','decimal(12,2)')<>@Expected
   OR @SnapshotXml.value('sum(/*/FeeReceiptData/Amount)','decimal(12,2)')<>@Expected
   THROW 54942,'The bill balance has changed. Reload before printing.',1;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.FinanceDocumentNumbers',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54941,'Document numbering is busy. Try again.',1;
  SELECT @Number=BillNumber FROM dbo.IssuedFeeBills WHERE DocumentType=@Type AND DocumentYear=@Year AND ContentHash=@Hash;
  IF @Number IS NULL
  BEGIN
   DECLARE @Date date=CONVERT(date,@Now);
   EXEC dbo.GetNextFinanceDocumentNumber @DocumentType=@Type,@DocumentDate=@Date,@DocumentNumber=@Number OUTPUT;
   INSERT dbo.IssuedFeeBills VALUES(NEWID(),@Number,@Type,@Year,@PrimaryAdmissionId,@StudentIdsJson,@Hash,@SnapshotXml,@Now,@UserId);
  END;
  SELECT BillNumber,IssuedAt,CONVERT(nvarchar(max),SnapshotXml) SnapshotXml FROM dbo.IssuedFeeBills WHERE BillNumber=@Number;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.IssueCollectionReceipt
 @UserId int,@PrimaryAdmissionId int,@TransactionDate datetime2(0),@PaymentMethod nvarchar(30),@Remarks nvarchar(500),
 @StudentIdsJson nvarchar(max),@PaymentIdsJson nvarchar(max),@LinesJson nvarchar(max),@CashReceived decimal(12,2),@CreditApplied decimal(12,2),@DiscountApplied decimal(12,2)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @@TRANCOUNT=0 THROW 54900,'Receipt issue requires the collection transaction.',1;
 IF @TransactionDate IS NULL OR CONVERT(date,@TransactionDate)>CONVERT(date,GETDATE())
  OR @CashReceived<0 OR @CreditApplied<0 OR @DiscountApplied<0
  OR ISJSON(@StudentIdsJson)<>1 OR ISJSON(@PaymentIdsJson)<>1 OR ISJSON(@LinesJson)<>1
  THROW 54930,'Invalid collection receipt details.',1;
 DECLARE @Ids TABLE(AdmissionId int PRIMARY KEY);
 INSERT @Ids SELECT CONVERT(int,value) FROM OPENJSON(@StudentIdsJson);
 DECLARE @Payments TABLE(FeePaymentId int PRIMARY KEY);
 INSERT @Payments SELECT CONVERT(int,value) FROM OPENJSON(@PaymentIdsJson);
 IF NOT EXISTS(SELECT 1 FROM @Ids WHERE AdmissionId=@PrimaryAdmissionId) OR EXISTS(SELECT 1 FROM @Ids I LEFT JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.AdmissionId IS NULL)
  THROW 54930,'Choose valid students for this receipt.',1;
 DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@PrimaryAdmissionId);
 IF EXISTS(SELECT 1 FROM @Ids I WHERE I.AdmissionId<>@PrimaryAdmissionId AND NOT EXISTS(SELECT 1 FROM dbo.FamilyMembers F WHERE F.AdmissionId=I.AdmissionId AND F.FamilyId=@Family))
  THROW 54930,'Only the selected student and their siblings can share this receipt.',1;
 IF EXISTS(SELECT 1 FROM @Payments I LEFT JOIN dbo.FeePayments P WITH(UPDLOCK,HOLDLOCK) ON P.FeePaymentId=I.FeePaymentId
  WHERE P.FeePaymentId IS NULL OR P.Status<>N'Completed' OR P.TotalAmount<=0 OR P.RelatedFeePaymentId IS NOT NULL
   OR P.AdmissionId NOT IN(SELECT AdmissionId FROM @Ids) OR EXISTS(SELECT 1 FROM dbo.CollectionReceiptPayments R WHERE R.FeePaymentId=P.FeePaymentId))
  THROW 54930,'A payment is unavailable or already belongs to a receipt.',1;
 IF COALESCE((SELECT SUM(CASE WHEN P.IsCreditApplication=0 THEN P.TotalAmount ELSE 0 END) FROM @Payments I JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId),0)<>@CashReceived
  OR COALESCE((SELECT SUM(CASE WHEN P.IsCreditApplication=1 THEN P.TotalAmount ELSE 0 END) FROM @Payments I JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId),0)<>@CreditApplied
  THROW 54930,'Receipt cash or credit does not match its recorded payments.',1;
 DECLARE @Lines TABLE(ChargeId bigint PRIMARY KEY,AdmissionId int,EnrollmentId int,AmountBefore decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),Credit decimal(12,2));
 INSERT @Lines SELECT ChargeId,AdmissionId,EnrollmentId,AmountBefore,Discount,Cash,Credit FROM OPENJSON(@LinesJson)
 WITH(ChargeId bigint,AdmissionId int,EnrollmentId int,AmountBefore decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),Credit decimal(12,2));
 IF EXISTS(SELECT 1 FROM @Lines L LEFT JOIN dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) ON B.BillingChargeId=L.ChargeId
  WHERE B.BillingChargeId IS NULL OR B.Status=N'Cancelled' OR B.AdmissionId<>L.AdmissionId OR B.EnrollmentId<>L.EnrollmentId
   OR L.AdmissionId NOT IN(SELECT AdmissionId FROM @Ids) OR L.Discount<0 OR L.Cash<0 OR L.Credit<0
   OR L.AmountBefore-L.Discount-L.Cash-L.Credit<>dbo.GetFeeChargeBalance(B.BillingChargeId)
   OR L.Cash<>COALESCE((SELECT SUM(A.Amount) FROM dbo.FeePaymentAllocations A JOIN @Payments I ON I.FeePaymentId=A.FeePaymentId JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId WHERE A.BillingChargeId=L.ChargeId AND P.IsCreditApplication=0),0)
   OR L.Credit<>COALESCE((SELECT SUM(A.Amount) FROM dbo.FeePaymentAllocations A JOIN @Payments I ON I.FeePaymentId=A.FeePaymentId JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId WHERE A.BillingChargeId=L.ChargeId AND P.IsCreditApplication=1),0))
  THROW 54930,'Receipt lines do not reconcile with the saved bill allocations.',1;
 IF COALESCE((SELECT SUM(Discount) FROM @Lines),0)<>@DiscountApplied OR COALESCE((SELECT SUM(Credit) FROM @Lines),0)<>@CreditApplied
  OR EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN @Payments I ON I.FeePaymentId=A.FeePaymentId WHERE A.Amount<>0 AND NOT EXISTS(SELECT 1 FROM @Lines L WHERE L.ChargeId=A.BillingChargeId))
  OR (@CashReceived=0 AND @CreditApplied=0 AND @DiscountApplied=0)
  THROW 54930,'The receipt must cover the complete collection.',1;
 DECLARE @Receipt nvarchar(40),@ReceiptId uniqueidentifier=NEWID(),@Date date=CONVERT(date,@TransactionDate);
 IF (SELECT COUNT(DISTINCT P.ReceiptNumber) FROM @Payments I JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId WHERE P.IsCreditApplication=0)>1
  THROW 54930,'Cash payments from different collections cannot be combined.',1;
 SELECT @Receipt=MAX(P.ReceiptNumber) FROM @Payments I JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId WHERE P.IsCreditApplication=0;
 DECLARE @ReceiptType varchar(2)=CASE WHEN (SELECT COUNT(*) FROM @Ids)>1 THEN 'FR' ELSE 'R' END;
 -- Historical numbers are preserved. New cash and credit-only collections use
 -- the same public number, with the prefix determined by the issued document.
 IF @Receipt IS NULL OR (@Receipt NOT LIKE CONCAT(@ReceiptType,N'-%') AND @Receipt NOT LIKE N'FMSC-%')
 BEGIN
  EXEC dbo.GetNextFinanceDocumentNumber @DocumentType=@ReceiptType,@DocumentDate=@Date,@DocumentNumber=@Receipt OUTPUT;
  UPDATE P SET ReceiptNumber=@Receipt FROM dbo.FeePayments P JOIN @Payments I ON I.FeePaymentId=P.FeePaymentId WHERE P.IsCreditApplication=0;
 END;
 DECLARE @Students nvarchar(max)=(SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference) AdmissionNumber,S.StudentName,S.FatherName,
  COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)) FROM dbo.BillingCharges B WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled'),0) BalanceRemaining,
  COALESCE((SELECT SUM(C.Amount) FROM dbo.StudentCreditEntries C WHERE C.AdmissionId=S.AdmissionId),0) CreditRemaining,
  COALESCE((SELECT SUM(L.Cash) FROM @Lines L WHERE L.AdmissionId=S.AdmissionId),0) CashApplied,
  COALESCE((SELECT SUM(L.Credit) FROM @Lines L WHERE L.AdmissionId=S.AdmissionId),0) CreditApplied,
  COALESCE((SELECT SUM(L.Discount) FROM @Lines L WHERE L.AdmissionId=S.AdmissionId),0) DiscountApplied
  FROM @Ids I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId ORDER BY S.AdmissionId FOR JSON PATH);
 DECLARE @Details nvarchar(max)=(SELECT L.*,F.FeeTypeName,COALESCE(B.BillingMonth,B.ChargeDate) FeePeriod,
  C.ClassName,SEC.SectionName,CA.CampusName,dbo.GetFeeChargeBalance(B.BillingChargeId) BalanceAfter
  FROM @Lines L JOIN dbo.BillingCharges B ON B.BillingChargeId=L.ChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  JOIN dbo.Enrollments E ON E.EnrollmentId=L.EnrollmentId JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
  JOIN dbo.Classes C ON C.ClassId=CCS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
  ORDER BY L.AdmissionId,COALESCE(B.BillingMonth,B.ChargeDate),F.DisplayOrder,L.ChargeId FOR JSON PATH);
 DECLARE @Balance decimal(12,2)=(SELECT SUM(BalanceRemaining) FROM OPENJSON(@Students) WITH(BalanceRemaining decimal(12,2))),
  @Credit decimal(12,2)=(SELECT SUM(CreditRemaining) FROM OPENJSON(@Students) WITH(CreditRemaining decimal(12,2))),
  @CashApplied decimal(12,2)=COALESCE((SELECT SUM(Cash) FROM @Lines),0);
 INSERT dbo.CollectionReceipts(CollectionReceiptId,ReceiptNumber,PrimaryAdmissionId,TransactionDate,CreatedByUserId,CashReceived,CashApplied,CreditApplied,DiscountApplied,BalanceRemaining,CreditRemaining,PaymentMethod,Remarks,SnapshotJson)
 VALUES(@ReceiptId,@Receipt,@PrimaryAdmissionId,@TransactionDate,@UserId,@CashReceived,@CashApplied,@CreditApplied,@DiscountApplied,@Balance,@Credit,CASE WHEN @CashReceived=0 AND @CreditApplied>0 THEN N'Account Credit' WHEN @CreditApplied>0 THEN CONCAT(@PaymentMethod,N' + credit') ELSE @PaymentMethod END,@Remarks,CONCAT(N'{"Students":',@Students,N',"Lines":',@Details,N'}'));
 INSERT dbo.CollectionReceiptPayments SELECT @ReceiptId,FeePaymentId FROM @Payments;
 SELECT @ReceiptId CollectionReceiptId,@Receipt ReceiptNumber,@PrimaryAdmissionId AdmissionId,@Balance Balance;
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

    DECLARE @ReceiptType varchar(2)=CASE WHEN (SELECT COUNT(DISTINCT AdmissionId) FROM @Groups)>1 OR
      (SELECT COUNT(DISTINCT TRY_CONVERT(int,value)) FROM OPENJSON(CASE WHEN ISJSON(@IncludedStudentsJson)=1 THEN @IncludedStudentsJson ELSE N'[]' END))>1 THEN 'FR' ELSE 'R' END;
    IF @ReceivedAmount>0
    EXEC dbo.GetNextFinanceDocumentNumber
        @DocumentType=@ReceiptType,@DocumentDate = @TransactionDate,
        @DocumentNumber = @FamilyReceiptNumber OUTPUT;

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
        CONCAT(F.FeeTypeName,N' � ',A.ConcessionLabel) FeeTypeName,
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
CREATE OR ALTER PROCEDURE dbo.GetStudentIssuedFeeBills @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 SELECT B.BillNumber,B.IssuedAt,B.DocumentType,CONVERT(nvarchar(max),B.SnapshotXml) SnapshotXml
 FROM dbo.IssuedFeeBills B WHERE EXISTS(SELECT 1 FROM OPENJSON(B.StudentIdsJson) I WHERE CONVERT(int,I.value)=@AdmissionId)
 ORDER BY B.IssuedAt DESC,B.BillNumber DESC;
END;
GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>23',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>23',N'FROM dbo.DatabaseVersion)<>24');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'24',SYSDATETIME(),SUSER_SNAME(),N'Stable issued fee bills and independent B/FB/R/FR calendar-year numbering');
GO
