-- One durable printable document per collection, including cash and credit.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 23 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>22
 THROW 51000,'Migration 23 requires schema 22.',1;
CREATE TABLE dbo.CollectionReceipts(
 CollectionReceiptId uniqueidentifier NOT NULL CONSTRAINT PK_CollectionReceipts PRIMARY KEY,
 ReceiptNumber nvarchar(40) NOT NULL CONSTRAINT UQ_CollectionReceipts_Number UNIQUE,
 PrimaryAdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),
 TransactionDate datetime2(0) NOT NULL, CreatedAt datetime2(0) NOT NULL DEFAULT SYSDATETIME(),
 CreatedByUserId int NOT NULL REFERENCES dbo.Users(UserId),
 CashReceived decimal(12,2) NOT NULL, CashApplied decimal(12,2) NOT NULL,
 CreditApplied decimal(12,2) NOT NULL, DiscountApplied decimal(12,2) NOT NULL,
 BalanceRemaining decimal(12,2) NOT NULL, CreditRemaining decimal(12,2) NOT NULL,
 PaymentMethod nvarchar(30) NOT NULL, Remarks nvarchar(500) NULL,
 SnapshotJson nvarchar(max) NOT NULL CONSTRAINT CK_CollectionReceipts_Json CHECK(ISJSON(SnapshotJson)=1),
 CONSTRAINT CK_CollectionReceipts_Amounts CHECK(CashReceived>=CashApplied AND CashApplied>=0 AND CreditApplied>=0 AND DiscountApplied>=0 AND BalanceRemaining>=0 AND CreditRemaining>=0)
);
CREATE TABLE dbo.CollectionReceiptPayments(
 CollectionReceiptId uniqueidentifier NOT NULL REFERENCES dbo.CollectionReceipts(CollectionReceiptId),
 FeePaymentId int NOT NULL REFERENCES dbo.FeePayments(FeePaymentId),
 CONSTRAINT PK_CollectionReceiptPayments PRIMARY KEY(CollectionReceiptId,FeePaymentId),
 CONSTRAINT UQ_CollectionReceiptPayments_Payment UNIQUE(FeePaymentId)
);
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
 IF @Receipt IS NULL EXEC dbo.GetNextFeeReceiptNumber @ReceiptDate=@Date,@ReceiptNumber=@Receipt OUTPUT;
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
CREATE OR ALTER PROCEDURE dbo.GetCollectionReceipt @UserId int,@ReceiptNumber nvarchar(40)
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 DECLARE @Id uniqueidentifier=(SELECT CollectionReceiptId FROM dbo.CollectionReceipts WHERE ReceiptNumber=@ReceiptNumber);
 IF @Id IS NULL SELECT TOP(1) @Id=L.CollectionReceiptId FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE P.ReceiptNumber=@ReceiptNumber;
 -- Older or automatic credit movements have no collection batch. Print the
 -- identified application on its own; do not infer a batch from timestamps.
 IF @Id IS NULL AND EXISTS(SELECT 1 FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber AND IsCreditApplication=1)
 BEGIN
  DECLARE @Payment int=(SELECT TOP(1) FeePaymentId FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber AND IsCreditApplication=1);
  SELECT CONCAT(N'FMSC-CR-',YEAR(P.PaymentDate),N'-',RIGHT(N'0000'+CONVERT(nvarchar(20),P.FeePaymentId),CASE WHEN LEN(CONVERT(nvarchar(20),P.FeePaymentId))>4 THEN LEN(CONVERT(nvarchar(20),P.FeePaymentId)) ELSE 4 END)) ReceiptNumber,
   P.AdmissionId PrimaryAdmissionId,P.CreatedAt TransactionDate,CONVERT(decimal(12,2),0) CashReceived,CONVERT(decimal(12,2),0) CashApplied,
   P.TotalAmount CreditApplied,CONVERT(decimal(12,2),0) DiscountApplied,
   COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE AdmissionId=P.AdmissionId AND Status<>N'Cancelled'),0) BalanceRemaining,
   COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=P.AdmissionId),0) CreditRemaining,
   N'Account Credit' PaymentMethod,N'Historical credit application; balances shown are current.'+CASE WHEN P.Status<>N'Completed' THEN N' Status: '+P.Status ELSE N'' END ReceiptStatus
  FROM dbo.FeePayments P WHERE FeePaymentId=@Payment;
  SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference) AdmissionNumber,S.StudentName,S.FatherName,
   COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE AdmissionId=S.AdmissionId AND Status<>N'Cancelled'),0) BalanceRemaining,
   COALESCE((SELECT SUM(Amount) FROM dbo.StudentCreditEntries WHERE AdmissionId=S.AdmissionId),0) CreditRemaining,
   CONVERT(decimal(12,2),0) CashApplied,P.TotalAmount CreditApplied,CONVERT(decimal(12,2),0) DiscountApplied
  FROM dbo.FeePayments P JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId WHERE P.FeePaymentId=@Payment;
  SELECT B.BillingChargeId ChargeId,B.AdmissionId,B.EnrollmentId,B.NetAmount AmountBefore,CONVERT(decimal(12,2),0) Discount,CONVERT(decimal(12,2),0) Cash,A.Amount Credit,
   F.FeeTypeName,COALESCE(B.BillingMonth,B.ChargeDate) FeePeriod,C.ClassName,SEC.SectionName,CA.CampusName,dbo.GetFeeChargeBalance(B.BillingChargeId) BalanceAfter
  FROM dbo.FeePaymentAllocations A JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
  JOIN dbo.Classes C ON C.ClassId=CCS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
  WHERE A.FeePaymentId=@Payment ORDER BY COALESCE(B.BillingMonth,B.ChargeDate),F.DisplayOrder,B.BillingChargeId;
  RETURN;
 END;
 SELECT R.*,CASE WHEN EXISTS(SELECT 1 FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE L.CollectionReceiptId=R.CollectionReceiptId AND
  (P.Status<>N'Completed' OR EXISTS(SELECT 1 FROM dbo.FeePayments X WHERE X.RelatedFeePaymentId=P.FeePaymentId AND X.Status=N'Completed')))
  THEN N'Original receipt; later adjustments recorded. See Payment History.' ELSE N'' END ReceiptStatus FROM dbo.CollectionReceipts R WHERE CollectionReceiptId=@Id;
 SELECT S.* FROM dbo.CollectionReceipts R CROSS APPLY OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int,AdmissionNumber nvarchar(50),StudentName nvarchar(200),FatherName nvarchar(200),BalanceRemaining decimal(12,2),CreditRemaining decimal(12,2),CashApplied decimal(12,2),CreditApplied decimal(12,2),DiscountApplied decimal(12,2)) S WHERE R.CollectionReceiptId=@Id;
 SELECT L.* FROM dbo.CollectionReceipts R CROSS APPLY OPENJSON(R.SnapshotJson,'$.Lines') WITH(ChargeId bigint,AdmissionId int,EnrollmentId int,AmountBefore decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),Credit decimal(12,2),FeeTypeName nvarchar(200),FeePeriod date,ClassName nvarchar(100),SectionName nvarchar(100),CampusName nvarchar(100),BalanceAfter decimal(12,2)) L WHERE R.CollectionReceiptId=@Id;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentCollectionReceipts @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 SELECT R.CollectionReceiptId,R.ReceiptNumber,R.TransactionDate,R.PaymentMethod,R.CashApplied+R.CreditApplied TotalSettled,
  S.CashApplied,S.CreditApplied,S.DiscountApplied,(SELECT COUNT(*) FROM OPENJSON(R.SnapshotJson,'$.Students')) StudentCount,
  S.CashApplied+S.CreditApplied-COALESCE(N.NetSettled,0) Adjusted,COALESCE(N.NetSettled,0) NetSettled
 FROM dbo.CollectionReceipts R CROSS APPLY OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int,CashApplied decimal(12,2),CreditApplied decimal(12,2),DiscountApplied decimal(12,2)) S
 OUTER APPLY(SELECT SUM(CASE WHEN P.Status<>N'Completed' THEN 0 ELSE CASE WHEN COALESCE(A.Applied,0)+COALESCE(X.Adjusted,0)<0 THEN 0 ELSE COALESCE(A.Applied,0)+COALESCE(X.Adjusted,0) END END) NetSettled
  FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId
  OUTER APPLY(SELECT SUM(Amount) Applied FROM dbo.FeePaymentAllocations WHERE FeePaymentId=P.FeePaymentId) A
  OUTER APPLY(SELECT SUM(FA.Amount) Adjusted FROM dbo.FeePayments FP JOIN dbo.FeePaymentAllocations FA ON FA.FeePaymentId=FP.FeePaymentId WHERE FP.RelatedFeePaymentId=P.FeePaymentId AND FP.Status=N'Completed') X
  WHERE L.CollectionReceiptId=R.CollectionReceiptId AND P.AdmissionId=@AdmissionId) N
 WHERE S.AdmissionId=@AdmissionId ORDER BY R.TransactionDate DESC,R.CreatedAt DESC;
 SELECT L.FeePaymentId FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE P.AdmissionId=@AdmissionId;
END;
GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>22',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>22',N'FROM dbo.DatabaseVersion)<>23');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'23',SYSDATETIME(),SUSER_SNAME(),N'Unified printable cash and credit collection receipts with immutable issue snapshots');