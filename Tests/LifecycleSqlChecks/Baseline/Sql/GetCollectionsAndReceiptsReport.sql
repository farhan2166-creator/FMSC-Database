CREATE   PROCEDURE dbo.GetCollectionsAndReceiptsReport
 @UserId int,@View nvarchar(30),@GroupBy nvarchar(30)=N'Day',
 @FromDate date=NULL,@ToDate date=NULL,@CampusId int=0,@ClassId int=0,@SectionId int=0,@PaymentMethod nvarchar(40)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Reports.View';
 IF @View NOT IN(N'Receipts',N'Detailed Transactions',N'Summary') THROW 59901,'The collections report view is not supported.',1;
 IF @View=N'Summary' AND @GroupBy NOT IN(N'Day',N'Week',N'Month',N'Payment Method',N'Class',N'Campus',N'Fee Type') THROW 59902,'The collections report grouping is not supported.',1;
 IF @FromDate IS NOT NULL AND @ToDate IS NOT NULL AND @FromDate>@ToDate THROW 59903,'The collections report date range is invalid.',1;

 -- Resolve public receipt identity before filtering, including credit-only/mixed receipts.
 -- Older unissued credit applications remain visible as entries, not invented receipts.
 SELECT P.FeePaymentId,
  COALESCE(R.ReceiptNumber,CASE WHEN P.IsCreditApplication=0 THEN NULLIF(P.ReceiptNumber,N'') END,
   CONCAT(CASE WHEN P.IsCreditApplication=1 THEN N'Credit #' ELSE N'Payment #' END,P.FeePaymentId)) ReceiptKey,
  CONVERT(bit,CASE WHEN R.CollectionReceiptId IS NOT NULL OR (P.IsCreditApplication=0 AND NULLIF(P.ReceiptNumber,N'') IS NOT NULL) THEN 1 ELSE 0 END) IsIssuedReceipt,
  CONVERT(bit,CASE WHEN R.ReceiptNumber LIKE N'FR-%' OR P.ReceiptNumber LIKE N'FR-%'
   OR (SELECT COUNT(*) FROM OPENJSON(R.SnapshotJson,'$.Students'))>1
   OR (SELECT COUNT(DISTINCT H.AdmissionId) FROM dbo.FeePayments H WHERE H.ReceiptNumber=P.ReceiptNumber)>1
   THEN 1 ELSE 0 END) IsFamilyReceipt,
  P.PaymentDate,P.CreatedAt,P.PaymentMethod,P.ReferenceNumber,P.RelatedFeePaymentId,P.AdmissionId,
  P.IsCreditApplication,
  CASE WHEN P.IsCreditApplication=0 THEN P.TotalAmount ELSE CONVERT(decimal(12,2),0) END MoneyAmount,
  CASE WHEN P.IsCreditApplication=1 THEN P.TotalAmount ELSE CONVERT(decimal(12,2),0) END CreditAmount,
  S.StudentName,CA.CampusName,C.ClassName,
  CONCAT(C.ClassName,CASE WHEN NULLIF(LTRIM(RTRIM(SEC.SectionName)),N'') IS NULL THEN N'' ELSE CONCAT(N' â€“ ',SEC.SectionName) END) ClassSection,
  COALESCE(CA.DisplayOrder,2147483647) CampusSort,COALESCE(C.DisplayOrder,2147483647) ClassSort,
  U.Username ReceivedBy,Members.FamilyNames,R.SnapshotJson ReceiptSnapshotJson
 INTO #Payments
 FROM dbo.FeePayments P
 JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId
 JOIN dbo.Enrollments E ON E.EnrollmentId=P.EnrollmentId AND E.AdmissionId=P.AdmissionId
 LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
 LEFT JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
 LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
 LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
 LEFT JOIN dbo.Users U ON U.UserId=P.CreatedByUserId
 LEFT JOIN dbo.CollectionReceiptPayments RP ON RP.FeePaymentId=P.FeePaymentId
 LEFT JOIN dbo.CollectionReceipts R ON R.CollectionReceiptId=RP.CollectionReceiptId
 OUTER APPLY(
  SELECT STRING_AGG(CONVERT(nvarchar(max),N.StudentName),N', ') WITHIN GROUP(ORDER BY N.AdmissionId) FamilyNames
  FROM(
   SELECT J.AdmissionId,J.StudentName FROM OPENJSON(R.SnapshotJson,'$.Students')
    WITH(AdmissionId int,StudentName nvarchar(200)) J
   UNION
   SELECT H.AdmissionId,HS.StudentName FROM dbo.FeePayments H JOIN dbo.Students HS ON HS.AdmissionId=H.AdmissionId
    WHERE R.CollectionReceiptId IS NULL AND H.ReceiptNumber=P.ReceiptNumber
  ) N
 ) Members
 WHERE P.Status=N'Completed'
 AND (@FromDate IS NULL OR P.PaymentDate>=@FromDate) AND (@ToDate IS NULL OR P.PaymentDate<=@ToDate)
 AND (@CampusId=0 OR CCS.CampusId=@CampusId) AND (@ClassId=0 OR CCS.ClassId=@ClassId) AND (@SectionId=0 OR CCS.SectionId=@SectionId)
 AND (@PaymentMethod IS NULL OR P.PaymentMethod=@PaymentMethod);

 IF @View=N'Receipts'
 BEGIN
  -- Aggregate the financial rows once. Roster enrichment cannot multiply amounts.
  SELECT A.[Receipt / Reference],A.[Date],A.[Scope],A.[Student / Family],A.[Campus],A.[Class / Section],
   Methods.PaymentMethods [Payment Method],A.[Money Received],A.[Refunds],A.[Corrections],
   A.[Credit Applied],A.[Adjusted Collection],A.[Received By],A.__ReceiptSnapshot,Roster.StudentsJson [__ReceiptStudents]
  FROM(
   SELECT ReceiptKey [Receipt / Reference],MIN(PaymentDate) [Date],
    CASE WHEN MAX(CONVERT(int,IsFamilyReceipt))=1 THEN N'Family' ELSE N'Individual' END [Scope],
    CASE WHEN MAX(CONVERT(int,IsFamilyReceipt))=1 THEN MAX(FamilyNames) ELSE MAX(StudentName) END [Student / Family],
    CASE WHEN COUNT(DISTINCT CampusName)>1 THEN N'Multiple' ELSE MAX(CampusName) END [Campus],
    CASE WHEN COUNT(DISTINCT ClassSection)>1 THEN N'Multiple' ELSE MAX(ClassSection) END [Class / Section],
    SUM(CASE WHEN MoneyAmount>0 THEN MoneyAmount ELSE 0 END) [Money Received],
    -SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Refund' THEN MoneyAmount ELSE 0 END) [Refunds],
    -SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Correction' THEN MoneyAmount ELSE 0 END) [Corrections],
    SUM(CreditAmount) [Credit Applied],SUM(MoneyAmount) [Adjusted Collection],MAX(ReceivedBy) [Received By],
    MAX(ReceiptSnapshotJson) __ReceiptSnapshot,MIN(CreatedAt) CreatedSort,MIN(FeePaymentId) PaymentSort
   FROM #Payments GROUP BY ReceiptKey
  ) A
  OUTER APPLY(
   SELECT STRING_AGG(CONVERT(nvarchar(max),M.PaymentMethod),N' + ') WITHIN GROUP(ORDER BY M.CreditSort,M.PaymentMethod) PaymentMethods
   FROM(
    SELECT P.PaymentMethod,MIN(CONVERT(int,P.IsCreditApplication)) CreditSort
    FROM #Payments P WHERE P.ReceiptKey=A.[Receipt / Reference]
    GROUP BY P.PaymentMethod
   ) M
  ) Methods
  OUTER APPLY(
   SELECT(
    SELECT S.AdmissionId,S.StudentName,S.FatherName,S.Gender,
     JSON_QUERY((
      SELECT CA.CampusName,C.ClassName,SEC.SectionName
      FROM dbo.FeePayments H
      JOIN dbo.Enrollments E ON E.EnrollmentId=H.EnrollmentId AND E.AdmissionId=H.AdmissionId
      LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
      LEFT JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
      LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
      LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
      WHERE H.AdmissionId=S.AdmissionId AND H.Status=N'Completed'
       AND (H.ReceiptNumber=A.[Receipt / Reference] OR EXISTS(
        SELECT 1 FROM #Payments P WHERE P.ReceiptKey=A.[Receipt / Reference] AND P.FeePaymentId=H.FeePaymentId))
      GROUP BY CA.CampusName,C.ClassName,SEC.SectionName
      ORDER BY MIN(H.FeePaymentId),CA.CampusName,C.ClassName,SEC.SectionName
      FOR JSON PATH
     )) Placements
    FROM dbo.Students S
    WHERE EXISTS(SELECT 1 FROM #Payments P WHERE P.ReceiptKey=A.[Receipt / Reference] AND P.AdmissionId=S.AdmissionId)
     OR EXISTS(SELECT 1 FROM OPENJSON(A.__ReceiptSnapshot,'$.Students') WITH(AdmissionId int) J WHERE J.AdmissionId=S.AdmissionId)
     OR EXISTS(SELECT 1 FROM dbo.FeePayments H WHERE H.Status=N'Completed' AND H.ReceiptNumber=A.[Receipt / Reference] AND H.AdmissionId=S.AdmissionId)
    ORDER BY S.AdmissionId
    FOR JSON PATH
   ) StudentsJson
  ) Roster
  ORDER BY A.[Date],A.CreatedSort,A.PaymentSort,A.[Receipt / Reference];
 END
 ELSE IF @View=N'Detailed Transactions'
 BEGIN
  SELECT P.ReceiptKey [Receipt / Reference],P.PaymentDate [Date],P.StudentName [Student],
   P.CampusName [Campus],P.ClassSection [Class / Section],
   COALESCE(F.FeeTypeName,N'Student Credit') [Fee Type],FORMAT(B.BillingMonth,N'MMM yyyy') [Period],
   CASE WHEN P.IsCreditApplication=0 THEN COALESCE(A.Amount,P.MoneyAmount) ELSE CONVERT(decimal(12,2),0) END [Collection Amount],
   CASE WHEN P.IsCreditApplication=1 THEN COALESCE(A.Amount,P.CreditAmount) ELSE CONVERT(decimal(12,2),0) END [Credit Applied],
   P.PaymentMethod [Payment Method],O.ReceiptNumber [Original Receipt],P.ReferenceNumber [Reference]
  FROM #Payments P LEFT JOIN dbo.FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
  LEFT JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
  LEFT JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  LEFT JOIN dbo.FeePayments O ON O.FeePaymentId=P.RelatedFeePaymentId
  ORDER BY P.PaymentDate,P.CreatedAt,P.FeePaymentId,B.BillingMonth,F.DisplayOrder,A.FeePaymentAllocationId;
 END
 ELSE IF @GroupBy=N'Fee Type'
 BEGIN
  SELECT COALESCE(F.FeeTypeName,N'Student Credit') [Group],P.CampusName [Campus],
   COUNT(DISTINCT CASE WHEN P.IsIssuedReceipt=1 THEN P.ReceiptKey END) [Receipts],
   SUM(CASE WHEN P.IsCreditApplication=0 AND COALESCE(A.Amount,P.MoneyAmount)>0 THEN COALESCE(A.Amount,P.MoneyAmount) ELSE 0 END) [Money Received],
   -SUM(CASE WHEN P.IsCreditApplication=0 AND P.PaymentMethod=N'Refund' THEN COALESCE(A.Amount,P.MoneyAmount) ELSE 0 END) [Refunds],
   -SUM(CASE WHEN P.IsCreditApplication=0 AND P.PaymentMethod=N'Correction' THEN COALESCE(A.Amount,P.MoneyAmount) ELSE 0 END) [Corrections],
   SUM(CASE WHEN P.IsCreditApplication=1 THEN COALESCE(A.Amount,P.CreditAmount) ELSE 0 END) [Credit Applied],
   SUM(CASE WHEN P.IsCreditApplication=0 THEN COALESCE(A.Amount,P.MoneyAmount) ELSE 0 END) [Adjusted Collection]
  FROM #Payments P LEFT JOIN dbo.FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
  LEFT JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId
  LEFT JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  GROUP BY F.FeeTypeName,P.CampusName ORDER BY MIN(P.CampusSort),MIN(F.DisplayOrder),F.FeeTypeName;
 END
 ELSE
 BEGIN
  ;WITH Grouped AS(
   SELECT *,CASE @GroupBy
    WHEN N'Week' THEN CONCAT(CONVERT(char(10),DATEADD(day,-(DATEDIFF(day,0,PaymentDate)%7),PaymentDate),23),N' week')
    WHEN N'Month' THEN CONVERT(char(7),PaymentDate,126)
    WHEN N'Payment Method' THEN PaymentMethod
    WHEN N'Class' THEN COALESCE(ClassName,N'Unassigned')
    WHEN N'Campus' THEN COALESCE(CampusName,N'Unassigned')
    ELSE CONVERT(char(10),PaymentDate,23) END GroupLabel
   FROM #Payments
  )
  SELECT GroupLabel [Group],CampusName [Campus],
   COUNT(DISTINCT CASE WHEN IsIssuedReceipt=1 THEN ReceiptKey END) [Receipts],
   SUM(CASE WHEN MoneyAmount>0 THEN MoneyAmount ELSE 0 END) [Money Received],
   -SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Refund' THEN MoneyAmount ELSE 0 END) [Refunds],
   -SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Correction' THEN MoneyAmount ELSE 0 END) [Corrections],
   SUM(CreditAmount) [Credit Applied],SUM(MoneyAmount) [Adjusted Collection]
  FROM Grouped GROUP BY GroupLabel,CampusName
  ORDER BY CASE WHEN @GroupBy IN(N'Campus',N'Class') THEN MIN(CampusSort) ELSE 0 END,
   CASE WHEN @GroupBy=N'Class' THEN MIN(ClassSort) ELSE 0 END,GroupLabel,MIN(CampusSort),CampusName;
 END;
 SELECT COUNT(DISTINCT CASE WHEN IsIssuedReceipt=1 THEN ReceiptKey END) Receipts,
  COALESCE(SUM(CASE WHEN MoneyAmount>0 THEN MoneyAmount ELSE 0 END),0) [Money Received],
  -COALESCE(SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Refund' THEN MoneyAmount ELSE 0 END),0) Refunds,
  -COALESCE(SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Correction' THEN MoneyAmount ELSE 0 END),0) Corrections,
  COALESCE(SUM(CreditAmount),0) [Credit Applied],COALESCE(SUM(MoneyAmount),0) [Adjusted Collection]
 FROM #Payments;
END;
