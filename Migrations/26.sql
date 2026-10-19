-- Report-only changes. Business records and saved receipt snapshots remain unchanged.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 26 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>25
 THROW 51000,'Migration 26 requires schema 25.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetCollectionsAndReceiptsReport
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
  CONCAT(C.ClassName,CASE WHEN NULLIF(LTRIM(RTRIM(SEC.SectionName)),N'') IS NULL THEN N'' ELSE CONCAT(N' – ',SEC.SectionName) END) ClassSection,
  COALESCE(CA.DisplayOrder,2147483647) CampusSort,COALESCE(C.DisplayOrder,2147483647) ClassSort,
  U.Username ReceivedBy,Members.FamilyNames
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
  SELECT ReceiptKey [Receipt / Reference],MIN(PaymentDate) [Date],
   CASE WHEN MAX(CONVERT(int,IsFamilyReceipt))=1 THEN N'Family' ELSE N'Individual' END [Scope],
   CASE WHEN MAX(CONVERT(int,IsFamilyReceipt))=1 THEN MAX(FamilyNames) ELSE MAX(StudentName) END [Student / Family],
   CASE WHEN COUNT(DISTINCT CampusName)>1 THEN N'Multiple' ELSE MAX(CampusName) END [Campus],
   CASE WHEN COUNT(DISTINCT ClassSection)>1 THEN N'Multiple' ELSE MAX(ClassSection) END [Class / Section],
   CASE WHEN COUNT(DISTINCT PaymentMethod)>1 THEN N'Mixed' ELSE MAX(PaymentMethod) END [Payment Method],
   SUM(CASE WHEN MoneyAmount>0 THEN MoneyAmount ELSE 0 END) [Money Received],
   -SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Refund' THEN MoneyAmount ELSE 0 END) [Refunds],
   -SUM(CASE WHEN MoneyAmount<0 AND PaymentMethod=N'Correction' THEN MoneyAmount ELSE 0 END) [Corrections],
   SUM(CreditAmount) [Credit Applied],SUM(MoneyAmount) [Adjusted Collection],MAX(ReceivedBy) [Received By]
  FROM #Payments GROUP BY ReceiptKey
  ORDER BY MIN(PaymentDate),MIN(CreatedAt),MIN(FeePaymentId),ReceiptKey;
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
GO
CREATE OR ALTER PROCEDURE dbo.GetOutstandingAndAgingReport
    @UserId int,
    @View nvarchar(20),
    @CampusId int = 0,
    @AcademicYearId int = 0,
    @ClassId int = 0,
    @SectionId int = 0,
    @AsOfDate date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';
    SET @AsOfDate = COALESCE(@AsOfDate, CONVERT(date, SYSDATETIME()));

    SELECT BC.BillingChargeId, BC.AdmissionId, S.RegistrationNumber, COALESCE(NULLIF(S.AdmissionNumber,''),NULLIF(S.QuickReference,''),CONCAT(N'ID-',S.AdmissionId)) AdmissionNumber, S.StudentName,
           CAMP.CampusName, CAMP.DisplayOrder CampusSort,C.DisplayOrder ClassSort,SEC.DisplayOrder SectionSort,FT.DisplayOrder FeeSort,S.AdmissionNumberBase,S.AdmissionNumberSuffix, CONCAT(C.ClassName,CASE WHEN NULLIF(LTRIM(RTRIM(SEC.SectionName)),N'') IS NULL THEN N'' ELSE CONCAT(N' – ',SEC.SectionName) END) ClassName, SEC.SectionName, FT.FeeTypeName,
           BC.BillingMonth, BC.ChargeDate, BC.DueDate, BC.GrossAmount, BC.DiscountAmount,
           BC.NetAmount, BC.NetAmount - B.Balance PaidAmount, B.Balance Outstanding,
           CASE WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) < 0 THEN N'Not Yet Due'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) = 0 THEN N'Due Today'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) <= 30 THEN N'1-30 days'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) <= 60 THEN N'31-60 days'
                WHEN DATEDIFF(day, BC.DueDate, @AsOfDate) <= 90 THEN N'61-90 days'
                ELSE N'90+ days' END AgingBucket
    INTO #Charges
    FROM dbo.BillingCharges BC
    CROSS APPLY (SELECT dbo.GetFeeChargeBalance(BC.BillingChargeId) Balance) B
    JOIN dbo.Students S ON S.AdmissionId = BC.AdmissionId
    JOIN dbo.Enrollments E ON E.EnrollmentId = BC.EnrollmentId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
    WHERE BC.Status <> N'Cancelled' AND B.Balance > 0
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND (@AcademicYearId = 0 OR E.AcademicYearId = @AcademicYearId)
      AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId);

    IF @View = N'Charges'
    BEGIN
        SELECT AdmissionNumber [Adm. / QR No.], StudentName [Student], CampusName [Campus], ClassName [Class / Section], FeeTypeName [Fee Type],
               BillingMonth [Charge Month], ChargeDate [Charge Date], DueDate [Due Date], GrossAmount [Original Amount],
               DiscountAmount [Discount], NetAmount [Net Charge], PaidAmount [Settled], Outstanding, AgingBucket [Aging]
        FROM #Charges ORDER BY CampusSort,ClassSort,SectionSort,COALESCE(AdmissionNumberBase,TRY_CONVERT(bigint,REPLACE(AdmissionNumber,N'QR-',N''))),AdmissionNumberSuffix,AdmissionNumber,StudentName,BillingMonth,FeeSort,BillingChargeId;
        SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
               SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
        RETURN;
    END;
    IF @View = N'Aging'
    BEGIN
        SELECT CampusName [Campus], AgingBucket [Aging Bucket], COUNT(DISTINCT AdmissionId) [Students], COUNT(*) [Charges], SUM(Outstanding) [Outstanding]
        FROM #Charges GROUP BY CampusName,AgingBucket
        ORDER BY MIN(CampusSort),CampusName,CASE AgingBucket WHEN N'Not Yet Due' THEN 0 WHEN N'Due Today' THEN 1 WHEN N'1-30 days' THEN 2 WHEN N'31-60 days' THEN 3 WHEN N'61-90 days' THEN 4 ELSE 5 END;
        SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
               SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
        RETURN;
    END;
    IF @View = N'Families'
    BEGIN
        ;WITH Members AS
        (
            SELECT AdmissionId, MAX(StudentName) StudentName,CASE WHEN COUNT(DISTINCT CampusName)>1 THEN N'Multiple' ELSE MAX(CampusName) END CampusName,MIN(CampusSort) CampusSort,SUM(Outstanding) Outstanding, MIN(DueDate) OldestDueDate
            FROM #Charges GROUP BY AdmissionId
        )
        SELECT F.FamilyCode [Family],CASE WHEN COUNT(DISTINCT M.CampusName)>1 THEN N'Multiple' ELSE MAX(M.CampusName) END [Campus],COUNT(*) [Students], STRING_AGG(M.StudentName, N', ') [Members],
               SUM(M.Outstanding) [Outstanding], MIN(M.OldestDueDate) [Oldest Due Date]
        FROM Members M JOIN dbo.FamilyMembers FM ON FM.AdmissionId = M.AdmissionId
        JOIN dbo.Families F ON F.FamilyId = FM.FamilyId
        GROUP BY F.FamilyId, F.FamilyCode ORDER BY MIN(M.CampusSort),TRY_CONVERT(bigint,REPLACE(F.FamilyCode,N'FAM-',N'')),F.FamilyCode;
        SELECT COUNT(DISTINCT C.AdmissionId) [Students Owing], SUM(C.Outstanding) [Total Outstanding],
               SUM(CASE WHEN C.DueDate < @AsOfDate THEN C.Outstanding ELSE 0 END) Overdue,
               SUM(CASE WHEN DATEDIFF(day, C.DueDate, @AsOfDate) > 90 THEN C.Outstanding ELSE 0 END) [90+ Days]
        FROM #Charges C JOIN dbo.FamilyMembers FM ON FM.AdmissionId = C.AdmissionId;
        RETURN;
    END;
    SELECT AdmissionNumber [Adm. / QR No.], StudentName [Student], CampusName [Campus], ClassName [Class / Section],
           SUM(NetAmount) [Open Charges (Net)], SUM(PaidAmount) [Settled on Open Charges], SUM(Outstanding) [Outstanding],
           MIN(DueDate) [Oldest Due Date],
           CASE WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 90 THEN N'90+ days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 60 THEN N'61-90 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 30 THEN N'31-60 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) > 0 THEN N'1-30 days'
                WHEN MAX(DATEDIFF(day, DueDate, @AsOfDate)) = 0 THEN N'Due Today' ELSE N'Not Yet Due' END [Aging]
    FROM #Charges GROUP BY AdmissionId, RegistrationNumber, AdmissionNumber, StudentName, CampusName, ClassName
    ORDER BY MIN(CampusSort),MIN(ClassSort),MIN(SectionSort),MIN(COALESCE(AdmissionNumberBase,TRY_CONVERT(bigint,REPLACE(AdmissionNumber,N'QR-',N'')))),MIN(AdmissionNumberSuffix),AdmissionNumber,StudentName;
    SELECT COUNT(DISTINCT AdmissionId) [Students Owing], SUM(Outstanding) [Total Outstanding],
           SUM(CASE WHEN DueDate < @AsOfDate THEN Outstanding ELSE 0 END) Overdue,
           SUM(CASE WHEN DATEDIFF(day, DueDate, @AsOfDate) > 90 THEN Outstanding ELSE 0 END) [90+ Days] FROM #Charges;
END;
GO
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>25',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>25',N'FROM dbo.DatabaseVersion)<>26');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'26',SYSDATETIME(),SUSER_SNAME(),N'Collections with separate credit and stable family identity; campus and natural finance report ordering');
GO
