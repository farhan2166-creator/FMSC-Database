-- Visit-based documents. Existing financial rows and saved snapshots are retained.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 56 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>55
 THROW 51000,'Migration 56 requires schema 55.',1;
ALTER TABLE dbo.CollectionReceipts ADD VisitSequence bigint NULL;
GO
;WITH Ordered AS(SELECT R.CollectionReceiptId,ROW_NUMBER() OVER(ORDER BY R.CreatedAt,
 COALESCE((SELECT MAX(L.FeePaymentId) FROM dbo.CollectionReceiptPayments L WHERE L.CollectionReceiptId=R.CollectionReceiptId),0),R.ReceiptNumber) Position
 FROM dbo.CollectionReceipts R)
UPDATE R SET VisitSequence=O.Position FROM dbo.CollectionReceipts R JOIN Ordered O ON O.CollectionReceiptId=R.CollectionReceiptId;
ALTER TABLE dbo.CollectionReceipts ALTER COLUMN VisitSequence bigint NOT NULL;
DECLARE @Next bigint=(SELECT COALESCE(MAX(VisitSequence),0)+1 FROM dbo.CollectionReceipts);
DECLARE @CreateSequence nvarchar(max)=CONCAT(N'CREATE SEQUENCE dbo.CollectionVisitSequence AS bigint START WITH ',@Next,N' INCREMENT BY 1;');
EXEC sys.sp_executesql @CreateSequence;
GO
ALTER TABLE dbo.CollectionReceipts ADD CONSTRAINT DF_CollectionReceipts_VisitSequence DEFAULT(NEXT VALUE FOR dbo.CollectionVisitSequence) FOR VisitSequence;
CREATE UNIQUE INDEX UX_CollectionReceipts_VisitSequence ON dbo.CollectionReceipts(VisitSequence);
GO
CREATE OR ALTER FUNCTION dbo.FeeDocumentAccounts(@Ids nvarchar(max),@Payments nvarchar(max),@Lines nvarchar(max))
RETURNS TABLE AS RETURN
(
 SELECT B.BillingChargeId ChargeId,B.AdmissionId,B.EnrollmentId,
  CONVERT(decimal(12,2),CASE WHEN B.Status=N'Cancelled' THEN 0 ELSE COALESCE(T.NewGross,B.GrossAmount) END) GrossAmount,
  CONVERT(decimal(12,2),CASE WHEN B.Status=N'Cancelled' THEN 0 ELSE COALESCE(T.NewGross,B.GrossAmount)-Q.Net END) Discount,
  CONVERT(decimal(12,2),Q.Net-Q.Balance-P.Cash-P.Credit) PreviouslyPaid,
  P.Cash,P.Credit,Q.Balance BalanceAfter,
  F.FeeTypeName,F.DisplayOrder FeeDisplayOrder,COALESCE(B.BillingMonth,B.ChargeDate) FeePeriod,
  C.ClassName,SEC.SectionName,CA.CampusName,B.Status FeeStatus,B.GrossAmount CancelledAmount,
  COALESCE(T.Reason,B.Remarks) ChangeReason,COALESCE(T.FeeBillCorrectionId,0) CorrectionId,
  CASE WHEN B.Status=N'Cancelled' THEN N'Cancelled'
   WHEN COALESCE(T.NewGross,B.GrossAmount)-Q.Net>0 AND (Q.Net=0 OR T.Reason=N'One-time fee waiver from Fee Collection' OR A.ConcessionLabel IN(N'Free',N'Waived')) THEN N'Waived'
   WHEN COALESCE(T.NewGross,B.GrossAmount)-Q.Net>0 THEN COALESCE(A.ConcessionLabel,N'Discount') ELSE N'' END ConcessionKind,
  COALESCE(D.CollectionDiscount,0) CollectionDiscount
 FROM dbo.BillingCharges B
 JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
 JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
 LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
 LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
 LEFT JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
 LEFT JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=B.StudentFeeAssignmentId
 OUTER APPLY(SELECT TOP(1) X.NewGross,X.Reason,X.FeeBillCorrectionId FROM dbo.FeeBillCorrections X
  WHERE X.BillingChargeId=B.BillingChargeId ORDER BY X.FeeBillCorrectionId DESC) T
 CROSS APPLY(SELECT COALESCE(SUM(CASE WHEN X.Status<>N'Cancelled' THEN X.NetAmount ELSE 0 END),0) Net,
  COALESCE(SUM(CASE WHEN X.Status<>N'Cancelled' THEN dbo.GetFeeChargeBalance(X.BillingChargeId) ELSE 0 END),0) Balance
  FROM dbo.BillingCharges X WHERE X.BillingChargeId=B.BillingChargeId OR X.CorrectionOfBillingChargeId=B.BillingChargeId) Q
 CROSS APPLY(SELECT COALESCE(SUM(CASE WHEN P.IsCreditApplication=0 THEN FA.Amount ELSE 0 END),0) Cash,
  COALESCE(SUM(CASE WHEN P.IsCreditApplication=1 THEN FA.Amount ELSE 0 END),0) Credit
  FROM dbo.FeePaymentAllocations FA JOIN dbo.FeePayments P ON P.FeePaymentId=FA.FeePaymentId
  JOIN dbo.BillingCharges X ON X.BillingChargeId=FA.BillingChargeId
  WHERE P.Status=N'Completed' AND P.FeePaymentId IN(SELECT TRY_CONVERT(int,value) FROM OPENJSON(@Payments))
   AND (X.BillingChargeId=B.BillingChargeId OR X.CorrectionOfBillingChargeId=B.BillingChargeId)) P
 OUTER APPLY(SELECT SUM(L.Discount) CollectionDiscount FROM OPENJSON(@Lines) WITH(ChargeId bigint,Discount decimal(12,2)) L
  JOIN dbo.BillingCharges X ON X.BillingChargeId=L.ChargeId WHERE X.BillingChargeId=B.BillingChargeId OR X.CorrectionOfBillingChargeId=B.BillingChargeId) D
 WHERE B.CorrectionOfBillingChargeId IS NULL AND B.AdmissionId IN(SELECT TRY_CONVERT(int,value) FROM OPENJSON(@Ids))
 UNION ALL
 -- Zero-payable monthly agreements are deliberately not posted as charges.
 -- Retain their parent-visible concession, once per fee/period on a receipt.
 SELECT -CONVERT(bigint,A.StudentFeeAssignmentId),A.AdmissionId,A.EnrollmentId,A.Amount,A.DiscountAmount,
  CONVERT(decimal(12,2),0),CONVERT(decimal(12,2),0),CONVERT(decimal(12,2),0),CONVERT(decimal(12,2),0),
  F.FeeTypeName,F.DisplayOrder,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),C.ClassName,SEC.SectionName,CA.CampusName,
  N'Paid',CONVERT(decimal(12,2),0),A.Remarks,0,COALESCE(A.ConcessionLabel,N'Waived'),CONVERT(decimal(12,2),0)
 FROM dbo.StudentFeeAssignments A JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId
 JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
 JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
 LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
 LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
 LEFT JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
 WHERE A.AdmissionId IN(SELECT TRY_CONVERT(int,value) FROM OPENJSON(@Ids))
  AND S.LegacySetupPending=0 AND E.Status IN(N'Active',N'Pending') AND E.BillingStartDate IS NOT NULL
  AND (E.Status=N'Pending' OR (E.BillingReady=1 AND E.PlacementConfirmedAt IS NOT NULL))
  AND CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate
  AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.ConcessionLabel IS NOT NULL AND A.Amount=A.DiscountAmount
  AND DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)>=DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1)
  AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
  AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
  AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=A.EnrollmentId AND B.FeeTypeId=A.FeeTypeId
   AND B.Status<>N'Cancelled' AND B.BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
);
GO
CREATE OR ALTER FUNCTION dbo.FeeVisitLines(@Accounts nvarchar(max),@BeforeSequence bigint)
RETURNS TABLE AS RETURN
(
 WITH Account AS(
  SELECT * FROM OPENJSON(@Accounts) WITH(ChargeId bigint,AdmissionId int,EnrollmentId int,GrossAmount decimal(12,2),Discount decimal(12,2),
   PreviouslyPaid decimal(12,2),Cash decimal(12,2),Credit decimal(12,2),BalanceAfter decimal(12,2),FeeTypeName nvarchar(200),FeePeriod date,
   ClassName nvarchar(100),SectionName nvarchar(100),CampusName nvarchar(100),FeeStatus nvarchar(30),CancelledAmount decimal(12,2),
   ChangeReason nvarchar(500),CorrectionId int,ConcessionKind nvarchar(30),FeeDisplayOrder int,CollectionDiscount decimal(12,2))
 ), Baseline AS(
  SELECT A.*,P.ReceiptNumber PreviousReceiptNumber,P.VisitSequence PreviousVisitSequence,
   COALESCE(P.BalanceAfter,CASE WHEN P.VisitSequence IS NOT NULL THEN
    COALESCE(OldTerms.NewGross,B.GrossAmount)-COALESCE(OldTerms.NewDiscount,B.GrossAmount-FirstTerms.PreviousNet,A.Discount-COALESCE(A.CollectionDiscount,0))-COALESCE(PastPaid.Amount,0) END) OpeningBalance,
   CASE WHEN P.SnapshotVersion>=2 THEN P.GrossAmount ELSE COALESCE(OldTerms.NewGross,B.GrossAmount) END PriorGross,
   CASE WHEN P.SnapshotVersion>=2 THEN P.Discount
    WHEN P.BalanceAfter IS NOT NULL THEN COALESCE(OldTerms.NewGross,B.GrossAmount)-P.BalanceAfter-COALESCE(PastPaid.Amount,0)
    ELSE COALESCE(OldTerms.NewDiscount,B.GrossAmount-FirstTerms.PreviousNet,A.Discount-COALESCE(A.CollectionDiscount,0)) END PriorDiscount,
   CASE WHEN P.SnapshotVersion>=2 THEN P.PreviouslyPaid+P.Cash+P.Credit ELSE COALESCE(PastPaid.Amount,0) END PriorSettled
  FROM Account A
  LEFT JOIN dbo.BillingCharges B ON B.BillingChargeId=A.ChargeId
  OUTER APPLY(SELECT TOP(1) R.CollectionReceiptId,R.ReceiptNumber,R.VisitSequence,R.CreatedAt,R.SnapshotVersion,L.GrossAmount,L.Discount,L.PreviouslyPaid,L.Cash,L.Credit,L.BalanceAfter
   FROM dbo.CollectionReceipts R OUTER APPLY(SELECT * FROM OPENJSON(COALESCE(JSON_QUERY(R.SnapshotJson,'$.AccountLines'),JSON_QUERY(R.SnapshotJson,'$.Lines')))
    WITH(ChargeId bigint,GrossAmount decimal(12,2),Discount decimal(12,2),PreviouslyPaid decimal(12,2),Cash decimal(12,2),Credit decimal(12,2),BalanceAfter decimal(12,2),FeePeriod date) L
     WHERE L.ChargeId=A.ChargeId AND (A.ChargeId>0 OR L.FeePeriod=A.FeePeriod)) L
   WHERE R.VisitSequence<@BeforeSequence AND (L.ChargeId=A.ChargeId OR (R.SnapshotVersion=1 AND B.CreatedAt<=R.CreatedAt
    AND EXISTS(SELECT 1 FROM OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int) S WHERE S.AdmissionId=A.AdmissionId)))
   ORDER BY R.VisitSequence DESC) P
  OUTER APPLY(SELECT TOP(1) C.NewGross,C.NewDiscount FROM dbo.FeeBillCorrections C WHERE C.BillingChargeId=A.ChargeId AND C.CreatedAt<=P.CreatedAt ORDER BY C.FeeBillCorrectionId DESC) OldTerms
  OUTER APPLY(SELECT TOP(1) C.PreviousNet FROM dbo.FeeBillCorrections C WHERE C.BillingChargeId=A.ChargeId ORDER BY C.FeeBillCorrectionId) FirstTerms
  OUTER APPLY(SELECT SUM(FA.Amount) Amount FROM dbo.FeePaymentAllocations FA JOIN dbo.FeePayments FP ON FP.FeePaymentId=FA.FeePaymentId
   JOIN dbo.BillingCharges X ON X.BillingChargeId=FA.BillingChargeId
   WHERE (X.BillingChargeId=A.ChargeId OR X.CorrectionOfBillingChargeId=A.ChargeId) AND FP.Status=N'Completed'
    AND (FP.CreatedAt<P.CreatedAt OR (FP.CreatedAt=P.CreatedAt AND FP.FeePaymentId<=COALESCE((SELECT MAX(L.FeePaymentId) FROM dbo.CollectionReceiptPayments L WHERE L.CollectionReceiptId=P.CollectionReceiptId),0)))) PastPaid
 ), Changes AS(
  SELECT *,CONVERT(decimal(12,2),CASE WHEN PreviousVisitSequence IS NULL THEN GrossAmount ELSE OpeningBalance END) BaseAmount,
   CONVERT(decimal(12,2),CASE WHEN PreviousVisitSequence IS NULL THEN 0 ELSE GrossAmount-PriorGross END) RateAdjustment,
   CONVERT(decimal(12,2),CASE WHEN PreviousVisitSequence IS NULL OR Discount>=PriorDiscount THEN 0 ELSE PriorDiscount-Discount END) DiscountReversed,
   CONVERT(decimal(12,2),CASE WHEN PreviousVisitSequence IS NULL THEN -PreviouslyPaid
    WHEN PriorSettled IS NOT NULL THEN PriorSettled-PreviouslyPaid
    ELSE PriorGross-PriorDiscount-OpeningBalance-PreviouslyPaid END) SettlementAdjustment,
   CONVERT(decimal(12,2),CASE WHEN PreviousVisitSequence IS NULL THEN Discount WHEN Discount>PriorDiscount THEN Discount-PriorDiscount ELSE 0 END) VisitDiscount
  FROM Baseline
 ), Printable AS(
  SELECT *,CONVERT(decimal(12,2),BaseAmount+RateAdjustment+DiscountReversed+SettlementAdjustment) VisitGross
  FROM Changes
 )
 SELECT ChargeId,AdmissionId,EnrollmentId,VisitGross GrossAmount,VisitGross AmountBefore,VisitDiscount Discount,
  CONVERT(decimal(12,2),0) PreviouslyPaid,Cash,Credit,BalanceAfter,FeeTypeName,FeePeriod,ClassName,SectionName,CampusName,
  FeeStatus,CancelledAmount,ChangeReason,CorrectionId,ConcessionKind,FeeDisplayOrder,
  BaseAmount,RateAdjustment,DiscountReversed,SettlementAdjustment,OpeningBalance,PreviousReceiptNumber,
  CONVERT(bit,CASE WHEN PreviousVisitSequence IS NOT NULL THEN 1 ELSE 0 END) IsCarriedForward,
  GrossAmount AccountGrossAmount,Discount AccountDiscountAmount,PreviouslyPaid+Cash+Credit AccountSettledAmount
 FROM Printable
 WHERE VisitGross<>0 OR VisitDiscount<>0 OR Cash<>0 OR Credit<>0 OR BalanceAfter<>0
  OR (PreviousVisitSequence IS NOT NULL AND (RateAdjustment<>0 OR DiscountReversed<>0 OR SettlementAdjustment<>0))
);
GO
CREATE OR ALTER FUNCTION dbo.FeeDocumentStudentOrder(@Admission int)
RETURNS TABLE AS RETURN
(
 SELECT COALESCE(C.DisplayOrder,2147483647) StudentClassOrder,COALESCE(AP.DisplayOrder,2147483647) StudentProgramOrder,
  CASE WHEN AP.ProgramType=N'School' THEN 0 WHEN AP.ProgramType=N'College' THEN 1 ELSE 2 END StudentProgramGroup,
  COALESCE(AP.AcademicProgramId,2147483647) StudentProgramId,COALESCE(C.ClassId,2147483647) StudentClassId
 FROM (SELECT @Admission AdmissionId) S
 OUTER APPLY(SELECT TOP(1) E.ClassSectionId FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
  ORDER BY CASE WHEN E.Status=N'Active' THEN 0 WHEN E.Status=N'Pending' THEN 1 ELSE 2 END,E.EnrollmentId DESC) E
 LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
 LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId LEFT JOIN dbo.AcademicPrograms AP ON AP.AcademicProgramId=C.AcademicProgramId
);
GO
CREATE OR ALTER PROCEDURE dbo.GetFeeVisitCharges @UserId int,@StudentIdsJson nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 DECLARE @Accounts nvarchar(max)=(SELECT * FROM dbo.FeeDocumentAccounts(@StudentIdsJson,N'[]',N'[]') FOR JSON PATH),
  @Before bigint=(SELECT COALESCE(MAX(VisitSequence),0)+1 FROM dbo.CollectionReceipts);
 IF EXISTS(SELECT 1 FROM dbo.FeeVisitLines(@Accounts,@Before) WHERE GrossAmount<0 OR GrossAmount-Discount<>BalanceAfter)
  THROW 54930,'The carried fee breakdown could not be reconciled. Reload before printing.',1;
 SELECT COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,S.FatherName,
  V.FeeTypeName,V.FeeDisplayOrder,V.FeePeriod BillingMonth,V.Discount DiscountAmount,V.BalanceAfter Due,V.ChargeId BillingChargeId,
  V.AdmissionId,V.CampusName,V.GrossAmount,V.ClassName,V.SectionName,V.BaseAmount,V.RateAdjustment,V.DiscountReversed,V.SettlementAdjustment,
  V.OpeningBalance,V.PreviousReceiptNumber,V.IsCarriedForward,CONVERT(bit,1) IsVisitBill,
  COALESCE(C.DisplayOrder,-1) StudentClassOrder,COALESCE(AP.DisplayOrder,-1) StudentProgramOrder,
  CASE WHEN AP.ProgramType=N'School' THEN 0 WHEN AP.ProgramType=N'College' THEN 1 ELSE 2 END StudentProgramGroup,AP.AcademicProgramId StudentProgramId,C.ClassId StudentClassId,
  V.AccountGrossAmount,V.AccountDiscountAmount
 FROM dbo.FeeVisitLines(@Accounts,@Before) V JOIN dbo.Students S ON S.AdmissionId=V.AdmissionId
 OUTER APPLY(SELECT TOP(1) E.ClassSectionId FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
  ORDER BY CASE WHEN E.Status=N'Active' THEN 0 WHEN E.Status=N'Pending' THEN 1 ELSE 2 END,E.EnrollmentId DESC) E
 LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
 LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId LEFT JOIN dbo.AcademicPrograms AP ON AP.AcademicProgramId=C.AcademicProgramId;
END;
GO
-- GENERATED VISIT PROCEDURES
CREATE OR ALTER PROCEDURE dbo.IssueCollectionReceipt
 @UserId int,@PrimaryAdmissionId int,@TransactionDate datetime2(0),@PaymentMethod nvarchar(30),@Remarks nvarchar(500),
 @StudentIdsJson nvarchar(max),@PaymentIdsJson nvarchar(max),@LinesJson nvarchar(max),@CashReceived decimal(12,2),@CreditApplied decimal(12,2),@DiscountApplied decimal(12,2),@Acknowledgement bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @@TRANCOUNT=0 THROW 54900,'Receipt issue requires the collection transaction.',1;
 DECLARE @BillingLock int;
 EXEC @BillingLock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @BillingLock<0 THROW 54901,'Billing is busy. Try again.',1;
 IF @Acknowledgement=1 AND (@CashReceived<>0 OR @CreditApplied<>0 OR @DiscountApplied<>0 OR EXISTS(SELECT 1 FROM OPENJSON(@PaymentIdsJson)) OR EXISTS(SELECT 1 FROM OPENJSON(@LinesJson)))
  THROW 54930,'A fee-change acknowledgement cannot receive money or apply credit.',1;
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
  OR (@CashReceived=0 AND @CreditApplied=0 AND @DiscountApplied=0 AND @Acknowledgement=0)
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
 -- Preserve full account state separately from this visit's printable lines.
 SELECT * INTO #Account FROM dbo.FeeDocumentAccounts(@StudentIdsJson,@PaymentIdsJson,@LinesJson);
 DECLARE @AccountJson nvarchar(max)=(SELECT * FROM #Account ORDER BY AdmissionId,ChargeId FOR JSON PATH),
  @Before bigint=(SELECT COALESCE(MAX(VisitSequence),0)+1 FROM dbo.CollectionReceipts);
 SELECT * INTO #Printed FROM dbo.FeeVisitLines(@AccountJson,@Before);
 IF EXISTS(SELECT 1 FROM #Printed WHERE GrossAmount<0 OR Discount<0 OR GrossAmount-Discount-Cash-Credit<>BalanceAfter)
  THROW 54930,'The visit fee breakdown does not reconcile. Reload before collecting.',1;
 IF (SELECT COALESCE(SUM(Cash),0) FROM #Printed)<>(SELECT COALESCE(SUM(Cash),0) FROM @Lines)
  OR (SELECT COALESCE(SUM(Credit),0) FROM #Printed)<>@CreditApplied
  OR (SELECT COALESCE(SUM(BalanceAfter),0) FROM #Printed)<>(SELECT COALESCE(SUM(BalanceAfter),0) FROM #Account)
  THROW 54930,'The visit receipt must include every payment and remaining balance.',1;
 DECLARE @PrintedDiscount decimal(12,2)=(SELECT COALESCE(SUM(Discount),0) FROM #Printed);
 DECLARE @Students nvarchar(max)=(SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference) AdmissionNumber,S.StudentName,S.FatherName,
  COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)) FROM dbo.BillingCharges B WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled'),0) BalanceRemaining,
  COALESCE((SELECT SUM(C.Amount) FROM dbo.StudentCreditEntries C WHERE C.AdmissionId=S.AdmissionId),0) CreditRemaining,
  COALESCE((SELECT SUM(L.Cash) FROM @Lines L WHERE L.AdmissionId=S.AdmissionId),0) CashApplied,
  COALESCE((SELECT SUM(L.Credit) FROM @Lines L WHERE L.AdmissionId=S.AdmissionId),0) CreditApplied,
  COALESCE((SELECT SUM(L.Discount) FROM #Printed L WHERE L.AdmissionId=S.AdmissionId),0) DiscountApplied,
  COALESCE((SELECT SUM(L.Discount) FROM @Lines L WHERE L.AdmissionId=S.AdmissionId),0) CollectionDiscountApplied,
  O.StudentClassOrder,O.StudentProgramOrder,O.StudentProgramGroup,O.StudentProgramId,O.StudentClassId,
  COALESCE((SELECT SUM(P.TotalAmount) FROM @Payments I JOIN dbo.FeePayments P ON P.FeePaymentId=I.FeePaymentId WHERE P.AdmissionId=S.AdmissionId AND P.IsCreditApplication=0),0) CashReceived
  FROM @Ids I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId OUTER APPLY dbo.FeeDocumentStudentOrder(S.AdmissionId) O ORDER BY S.AdmissionId FOR JSON PATH);
 DECLARE @Details nvarchar(max)=(SELECT P.*
  FROM #Printed P ORDER BY AdmissionId,FeePeriod,ChargeId FOR JSON PATH);
 DECLARE @Agreements nvarchar(max)=(SELECT A.AdmissionId,A.EnrollmentId,F.FeeTypeName,A.Amount,A.DiscountAmount,A.Frequency,
  A.EffectiveFrom,A.EffectiveTo,A.CancelledAt,A.ConcessionLabel
  FROM dbo.StudentFeeAssignments A JOIN @Ids I ON I.AdmissionId=A.AdmissionId JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
  ORDER BY A.AdmissionId,A.StudentFeeAssignmentId FOR JSON PATH);
 DECLARE @Balance decimal(12,2)=(SELECT SUM(BalanceRemaining) FROM OPENJSON(@Students) WITH(BalanceRemaining decimal(12,2))),
  @Credit decimal(12,2)=(SELECT SUM(CreditRemaining) FROM OPENJSON(@Students) WITH(CreditRemaining decimal(12,2))),
  @CashApplied decimal(12,2)=COALESCE((SELECT SUM(Cash) FROM @Lines),0);
 SET @Students=COALESCE(NULLIF(@Students,N''),N'[]');SET @Details=COALESCE(NULLIF(@Details,N''),N'[]');
 SET @Agreements=COALESCE(NULLIF(@Agreements,N''),N'[]');SET @AccountJson=COALESCE(NULLIF(@AccountJson,N''),N'[]');
 INSERT dbo.CollectionReceipts(CollectionReceiptId,ReceiptNumber,PrimaryAdmissionId,TransactionDate,CreatedByUserId,CashReceived,CashApplied,CreditApplied,DiscountApplied,BalanceRemaining,CreditRemaining,PaymentMethod,Remarks,SnapshotJson,SnapshotVersion,PrintedDiscountAmount)
 VALUES(@ReceiptId,@Receipt,@PrimaryAdmissionId,@TransactionDate,@UserId,@CashReceived,@CashApplied,@CreditApplied,@DiscountApplied,@Balance,@Credit,CASE WHEN @Acknowledgement=1 THEN N'Fee changes' WHEN @CashReceived=0 AND @CreditApplied>0 THEN N'Account Credit' WHEN @CreditApplied>0 THEN CONCAT(@PaymentMethod,N' + credit') ELSE @PaymentMethod END,@Remarks,CONCAT(N'{"Students":',@Students,N',"Lines":',@Details,N',"Agreements":',@Agreements,N',"AccountLines":',@AccountJson,N'}'),3,@PrintedDiscount);
 INSERT dbo.CollectionReceiptPayments SELECT @ReceiptId,FeePaymentId FROM @Payments;
 SELECT @ReceiptId CollectionReceiptId,@Receipt ReceiptNumber,@PrimaryAdmissionId AdmissionId,@Balance Balance;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetCollectionReceipt @UserId int,@ReceiptNumber nvarchar(40),@ReadPermission nvarchar(100)=N'Fees.View'
AS
BEGIN
 SET NOCOUNT ON;
 IF @ReadPermission NOT IN(N'Fees.View',N'Fees.Statement.View') THROW 54930,'Invalid receipt read permission.',1;
 EXEC dbo.RequirePermission @UserId,@ReadPermission;
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
 DECLARE @Version int,@Snapshot nvarchar(max),@Sequence bigint,@ReadLines nvarchar(max);
 SELECT @Version=SnapshotVersion,@Snapshot=SnapshotJson,@Sequence=VisitSequence FROM dbo.CollectionReceipts WHERE CollectionReceiptId=@Id;
 SET @ReadLines=CASE WHEN @Version=2 THEN (SELECT * FROM dbo.FeeVisitLines(JSON_QUERY(@Snapshot,'$.Lines'),@Sequence) ORDER BY AdmissionId,FeePeriod,ChargeId FOR JSON PATH)
  ELSE JSON_QUERY(@Snapshot,'$.Lines') END;
 SELECT R.CollectionReceiptId,R.ReceiptNumber,R.PrimaryAdmissionId,R.TransactionDate,R.CreatedAt,R.CashReceived,R.CashApplied,R.CreditApplied,
  CASE WHEN @Version>=2 THEN COALESCE((SELECT SUM(Discount) FROM OPENJSON(@ReadLines) WITH(Discount decimal(12,2))),0) ELSE R.DiscountApplied END DiscountApplied,
  R.BalanceRemaining,R.CreditRemaining,R.PaymentMethod,R.Remarks,CASE WHEN @Version>=2 THEN 3 ELSE @Version END SnapshotVersion,
  CASE WHEN EXISTS(SELECT 1 FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE L.CollectionReceiptId=R.CollectionReceiptId AND
   (P.Status<>N'Completed' OR EXISTS(SELECT 1 FROM dbo.FeePayments X WHERE X.RelatedFeePaymentId=P.FeePaymentId AND X.Status=N'Completed')))
   THEN N'Original receipt; later adjustments recorded. See Payment History.' ELSE N'' END ReceiptStatus
 FROM dbo.CollectionReceipts R WHERE CollectionReceiptId=@Id;
 SELECT S.AdmissionId,S.AdmissionNumber,S.StudentName,S.FatherName,S.BalanceRemaining,S.CreditRemaining,S.CashApplied,S.CreditApplied,
  CASE WHEN @Version>=2 THEN COALESCE((SELECT SUM(L.Discount) FROM OPENJSON(@ReadLines) WITH(AdmissionId int,Discount decimal(12,2)) L WHERE L.AdmissionId=S.AdmissionId),0) ELSE S.DiscountApplied END DiscountApplied,
  S.CashReceived,COALESCE(S.StudentClassOrder,C.DisplayOrder,-1) StudentClassOrder,COALESCE(S.StudentProgramOrder,AP.DisplayOrder,-1) StudentProgramOrder,
  COALESCE(S.StudentProgramGroup,CASE WHEN AP.ProgramType=N'School' THEN 0 WHEN AP.ProgramType=N'College' THEN 1 ELSE 2 END) StudentProgramGroup,COALESCE(S.StudentProgramId,AP.AcademicProgramId) StudentProgramId,COALESCE(S.StudentClassId,C.ClassId) StudentClassId
 FROM OPENJSON(@Snapshot,'$.Students') WITH(AdmissionId int,AdmissionNumber nvarchar(50),StudentName nvarchar(200),FatherName nvarchar(200),
  BalanceRemaining decimal(12,2),CreditRemaining decimal(12,2),CashApplied decimal(12,2),CreditApplied decimal(12,2),DiscountApplied decimal(12,2),CashReceived decimal(12,2),StudentClassOrder int,StudentProgramOrder int,StudentProgramGroup int,StudentProgramId int,StudentClassId int) S
 OUTER APPLY(SELECT TOP(1) L.ClassName FROM OPENJSON(@Snapshot,'$.Lines') WITH(AdmissionId int,ClassName nvarchar(100)) L WHERE L.AdmissionId=S.AdmissionId AND L.ClassName IS NOT NULL) Placement
 OUTER APPLY(SELECT TOP(1) C.* FROM dbo.Classes C WHERE C.ClassName=Placement.ClassName ORDER BY C.ClassId) C
 LEFT JOIN dbo.AcademicPrograms AP ON AP.AcademicProgramId=C.AcademicProgramId;
 SELECT L.* FROM OPENJSON(@ReadLines) WITH(ChargeId bigint,AdmissionId int,EnrollmentId int,AmountBefore decimal(12,2),Discount decimal(12,2),Cash decimal(12,2),Credit decimal(12,2),
  FeeTypeName nvarchar(200),FeePeriod date,ClassName nvarchar(100),SectionName nvarchar(100),CampusName nvarchar(100),BalanceAfter decimal(12,2),GrossAmount decimal(12,2),PreviouslyPaid decimal(12,2),
  FeeStatus nvarchar(30),CancelledAmount decimal(12,2),ChangeReason nvarchar(500),CorrectionId int,ConcessionKind nvarchar(30),FeeDisplayOrder int,
  BaseAmount decimal(12,2),RateAdjustment decimal(12,2),DiscountReversed decimal(12,2),SettlementAdjustment decimal(12,2),OpeningBalance decimal(12,2),PreviousReceiptNumber nvarchar(40),IsCarriedForward bit,
  AccountGrossAmount decimal(12,2),AccountDiscountAmount decimal(12,2),AccountSettledAmount decimal(12,2)) L;
 SELECT A.* FROM OPENJSON(@Snapshot,'$.Agreements') WITH(AdmissionId int,EnrollmentId int,FeeTypeName nvarchar(200),Amount decimal(12,2),DiscountAmount decimal(12,2),Frequency nvarchar(30),EffectiveFrom date,EffectiveTo date,CancelledAt datetime2(7),ConcessionLabel nvarchar(30)) A;
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
    WHERE I.ChargeId>0 AND (B.BillingChargeId IS NULL OR B.AdmissionId NOT IN(SELECT AdmissionId FROM @Ids)))
   OR EXISTS(SELECT 1 FROM @Sources I WHERE I.ChargeId<0 AND NOT EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A
    WHERE A.StudentFeeAssignmentId=-I.ChargeId AND A.AdmissionId IN(SELECT AdmissionId FROM @Ids) AND A.CancelledAt IS NULL AND A.Amount=A.DiscountAmount))
   THROW 54942,'The bill charges have changed. Reload before printing.',1;
  DECLARE @AccountJson nvarchar(max)=(SELECT * FROM dbo.FeeDocumentAccounts(@StudentIdsJson,N'[]',N'[]') FOR JSON PATH),
   @Before bigint=(SELECT COALESCE(MAX(VisitSequence),0)+1 FROM dbo.CollectionReceipts);
  SELECT * INTO #Visit FROM dbo.FeeVisitLines(@AccountJson,@Before);
  -- Validate the financial terms as well as the final balance. A same-net
  -- rate/discount edit must not issue a stale parent-facing breakdown.
  IF EXISTS(SELECT 1 FROM @SnapshotXml.nodes('/*/FeeReceiptData') X(R)
   LEFT JOIN #Visit V ON V.ChargeId=X.R.value('(BillChargeId/text())[1]','bigint')
   WHERE X.R.value('(BillIsVisit/text())[1]','bit')=1 AND (V.ChargeId IS NULL OR
    X.R.value('(BillGrossAmount/text())[1]','decimal(12,2)')<>V.GrossAmount OR
    X.R.value('(BillLineDiscount/text())[1]','decimal(12,2)')<>V.Discount OR
    X.R.value('(BillAccountGross/text())[1]','decimal(12,2)')<>V.AccountGrossAmount OR
    X.R.value('(BillAccountDiscount/text())[1]','decimal(12,2)')<>V.AccountDiscountAmount))
   THROW 54943,'Bill fees or concessions changed after review. Reload before printing.',1;
  DECLARE @Expected decimal(12,2)=COALESCE((SELECT SUM(A.BalanceAfter) FROM @Sources I JOIN dbo.FeeDocumentAccounts(@StudentIdsJson,N'[]',N'[]') A ON A.ChargeId=I.ChargeId),0);
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
CREATE OR ALTER PROCEDURE dbo.GetStudentCollectionReceipts @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 SELECT R.CollectionReceiptId,R.ReceiptNumber,R.TransactionDate,R.PaymentMethod,R.CashApplied+R.CreditApplied TotalSettled,
  S.CashApplied,S.CreditApplied,CASE WHEN R.SnapshotVersion=2 THEN COALESCE(S.CollectionDiscountApplied,0) ELSE S.DiscountApplied END DiscountApplied,(SELECT COUNT(*) FROM OPENJSON(R.SnapshotJson,'$.Students')) StudentCount,
  S.CashApplied+S.CreditApplied-COALESCE(N.NetSettled,0) Adjusted,COALESCE(N.NetSettled,0) NetSettled
 FROM dbo.CollectionReceipts R CROSS APPLY OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int,CashApplied decimal(12,2),CreditApplied decimal(12,2),DiscountApplied decimal(12,2),CollectionDiscountApplied decimal(12,2)) S
 OUTER APPLY(SELECT SUM(CASE WHEN P.Status<>N'Completed' THEN 0 ELSE CASE WHEN COALESCE(A.Applied,0)+COALESCE(X.Adjusted,0)<0 THEN 0 ELSE COALESCE(A.Applied,0)+COALESCE(X.Adjusted,0) END END) NetSettled
  FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId
  OUTER APPLY(SELECT SUM(Amount) Applied FROM dbo.FeePaymentAllocations WHERE FeePaymentId=P.FeePaymentId) A
  OUTER APPLY(SELECT SUM(FA.Amount) Adjusted FROM dbo.FeePayments FP JOIN dbo.FeePaymentAllocations FA ON FA.FeePaymentId=FP.FeePaymentId WHERE FP.RelatedFeePaymentId=P.FeePaymentId AND FP.Status=N'Completed') X
  WHERE L.CollectionReceiptId=R.CollectionReceiptId AND P.AdmissionId=@AdmissionId) N
 WHERE S.AdmissionId=@AdmissionId ORDER BY R.TransactionDate DESC,R.CreatedAt DESC;
 SELECT L.FeePaymentId FROM dbo.CollectionReceiptPayments L JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE P.AdmissionId=@AdmissionId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetCollectionReviewState
 @UserId int,@AdmissionId int,@ExpectedToken varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.View';
 DECLARE @Raw nvarchar(max)=CONCAT(
  (SELECT B.* FROM dbo.BillingCharges B WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT P.* FROM dbo.FeePayments P WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT A.* FROM dbo.FeePaymentAllocations A WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT A.* FROM dbo.StudentFeeAssignments A WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT C.* FROM dbo.StudentCreditEntries C WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT C.* FROM dbo.FeeBillCorrections C JOIN dbo.BillingCharges B ON B.BillingChargeId=C.BillingChargeId WHERE B.AdmissionId=@AdmissionId ORDER BY C.FeeBillCorrectionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT R.CollectionReceiptId,R.VisitSequence,R.SnapshotJson FROM dbo.CollectionReceipts R
   WHERE EXISTS(SELECT 1 FROM OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int) S WHERE S.AdmissionId=@AdmissionId)
   ORDER BY R.VisitSequence FOR JSON PATH,INCLUDE_NULL_VALUES));
 DECLARE @Token varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @ExpectedToken IS NOT NULL AND @ExpectedToken<>@Token
  THROW 53104,'Fees, concessions, payments or credit changed after loading. Reload the family before collecting; nothing was saved.',1;
 SELECT @Token ReviewToken;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'56',SYSDATETIME(),SUSER_SNAME(),N'Visit-based bills and receipt snapshots: carry outstanding, disclose subsequent concessions only, retain settled history and explicit visit ordering');
GO
