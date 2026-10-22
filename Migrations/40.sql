-- Validate individual bill amounts, source identities and student ownership.
-- Preserve saved bill documents, finance numbering and all financial records.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 40 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>39
 THROW 51000,'Migration 40 requires schema 39.',1;
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
  -- Bind every printed fee amount to its own saved charge and student.
  -- The source set and grand total cannot detect offsetting line changes.
  IF @SnapshotXml.exist('/*/FeeReceiptData[not(BillChargeId) or not(BillAdmissionId) or not(Amount)]')=1
   THROW 54942,'The bill is missing charge details. Reload before printing.',1;
  DECLARE @Lines table(ChargeId bigint,AdmissionId int,Amount decimal(12,2));
  INSERT @Lines
   SELECT N.value('(BillChargeId/text())[1]','bigint'),N.value('(BillAdmissionId/text())[1]','int'),
          N.value('(Amount/text())[1]','decimal(12,2)')
   FROM @SnapshotXml.nodes('/*/FeeReceiptData') R(N);
  IF NOT EXISTS(SELECT 1 FROM @Lines)
   OR EXISTS(SELECT 1 FROM @Lines WHERE ChargeId=0 OR AdmissionId NOT IN(SELECT AdmissionId FROM @Ids) OR Amount<0)
   OR EXISTS(SELECT ChargeId FROM @Lines GROUP BY ChargeId HAVING COUNT(*)<>1)
   OR EXISTS(SELECT ChargeId FROM @Lines EXCEPT SELECT ChargeId FROM @Sources)
   OR EXISTS(SELECT ChargeId FROM @Sources EXCEPT SELECT ChargeId FROM @Lines)
   THROW 54942,'The bill line references have changed. Reload before printing.',1;
  IF EXISTS(SELECT 1 FROM @Lines L JOIN dbo.BillingCharges B ON B.BillingChargeId=L.ChargeId
      WHERE L.ChargeId>0 AND (L.AdmissionId<>B.AdmissionId OR L.Amount<>dbo.GetFeeChargeBalance(B.BillingChargeId)))
   OR EXISTS(SELECT 1 FROM @Lines L WHERE L.ChargeId<0 AND (L.Amount<>0 OR NOT EXISTS(
      SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.StudentFeeAssignmentId=-L.ChargeId
       AND A.AdmissionId=L.AdmissionId AND A.CancelledAt IS NULL AND A.Amount=A.DiscountAmount)))
   THROW 54942,'A bill line balance or student has changed. Reload before printing.',1;
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
 IF @Definition IS NULL OR CHARINDEX(N'FROM dbo.DatabaseVersion)<>39',@Definition)=0 THROW 51000,'Unexpected communication callback definition.',1;
 SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>39',N'FROM dbo.DatabaseVersion)<>40');
 SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition)); EXEC sys.sp_executesql @Definition;
END;
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'40',SYSDATETIME(),SUSER_SNAME(),N'Validate every issued bill line against its saved charge balance and student');
GO
