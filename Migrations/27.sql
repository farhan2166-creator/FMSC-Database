-- Quick counter bill evidence only. No charges, payments or student records are rewritten.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 27 requires the installer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>26
 THROW 51000,'Migration 27 requires schema 26.',1;
GO
CREATE OR ALTER PROCEDURE dbo.GetFamilyAdmissionBillWaivers
 @UserId int,@Anchor int,@Ids nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.View';
 IF ISJSON(@Ids)<>1 OR LEFT(LTRIM(@Ids),1)<>N'[' THROW 51000,'Select the family children before printing.',1;
 DECLARE @Selected table(AdmissionId int PRIMARY KEY);
 IF EXISTS(SELECT 1 FROM OPENJSON(@Ids) WHERE [type]<>2 OR TRY_CONVERT(int,[value]) IS NULL OR TRY_CONVERT(int,[value])<=0)
  THROW 51000,'Invalid family selection.',1;
 INSERT @Selected SELECT DISTINCT CONVERT(int,[value]) FROM OPENJSON(@Ids);
 DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Anchor),
         @Month date=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);
 IF NOT EXISTS(SELECT 1 FROM @Selected) OR NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@Anchor)
  OR EXISTS(SELECT 1 FROM @Selected I WHERE I.AdmissionId<>@Anchor AND NOT EXISTS(
    SELECT 1 FROM dbo.FamilyMembers FM WHERE FM.AdmissionId=I.AdmissionId AND FM.FamilyId=@Family))
  THROW 51000,'The selected family changed. Refresh before printing.',1;

 SELECT COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,
   CONCAT(F.FeeTypeName,N' - Waived') FeeTypeName,F.DisplayOrder FeeDisplayOrder,
   B.BillingMonth,B.DiscountAmount,CONVERT(decimal(12,2),0) Due,B.BillingChargeId,S.AdmissionId
 FROM dbo.BillingCharges B JOIN @Selected I ON I.AdmissionId=B.AdmissionId
 JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
 JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
 WHERE E.Status IN(N'Active',N'Pending') AND B.Status<>N'Cancelled' AND B.NetAmount=0 AND B.DiscountAmount>0
   AND (B.ChargeKind<>N'Monthly' OR B.BillingMonth>=@Month)
 UNION ALL
 SELECT COALESCE(S.AdmissionNumber,S.QuickReference),S.StudentName,
   CONCAT(F.FeeTypeName,CASE WHEN A.Frequency=N'Monthly' THEN N' - Waived monthly plan' ELSE N' - Waived' END),F.DisplayOrder,
   CASE WHEN A.Frequency=N'Monthly' THEN P.FirstMonth ELSE DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1) END,
   A.DiscountAmount,CONVERT(decimal(12,2),0),-CONVERT(bigint,A.StudentFeeAssignmentId),S.AdmissionId
 FROM dbo.StudentFeeAssignments A JOIN @Selected I ON I.AdmissionId=A.AdmissionId
 JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
 JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
 CROSS APPLY(SELECT MAX(M) FirstMonth FROM(VALUES(@Month),
   (DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1)),
   (DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)),
   (CASE WHEN A.EffectiveFrom IS NULL THEN @Month ELSE
    CASE WHEN DAY(A.EffectiveFrom)=1 THEN A.EffectiveFrom ELSE DATEADD(day,1,EOMONTH(A.EffectiveFrom)) END END)) V(M)) P
 WHERE E.Status IN(N'Active',N'Pending') AND S.LegacySetupPending=0
   AND (E.Status=N'Pending' OR (E.BillingReady=1 AND E.PlacementConfirmedAt IS NOT NULL))
   AND A.CancelledAt IS NULL AND A.Amount>0 AND A.Amount=A.DiscountAmount
   AND (A.Frequency<>N'Monthly' OR (E.BillingStartDate IS NOT NULL AND P.FirstMonth<=Y.EndDate
     AND (A.EffectiveTo IS NULL OR P.FirstMonth<=A.EffectiveTo)))
   AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId
      AND B.Status<>N'Cancelled' AND (A.Frequency<>N'Monthly' OR B.BillingMonth=P.FirstMonth));
END;
GO
-- Sum typed decimals so an all-zero waiver bill does not pass through XQuery floating-point notation.
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
   OR @SnapshotXml.value('sum(for $a in /*/FeeReceiptData/Amount return xs:decimal($a))','decimal(12,2)')<>@Expected
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
IF OBJECT_ID(N'comm.IngestMeta',N'P') IS NOT NULL
BEGIN
 DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>26',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>26',N'FROM dbo.DatabaseVersion)<>27');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'27',SYSDATETIME(),SUSER_SNAME(),N'Quick counter bill evidence for current and future fully waived fees; no financial records changed');
GO
