-- On the authorized test database, set SESSION_CONTEXT keys
-- FMSC_BillLineTestAdmission and FMSC_BillLineTestUser before executing.
-- Every issued bill, counter change and temporary waiver edit is rolled back.
SET NOCOUNT ON; SET XACT_ABORT ON; SET LOCK_TIMEOUT 5000;
IF @@TRANCOUNT<>0 THROW 51000,'Run bill line checks without an existing transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),0)<>40
 THROW 51000,'Bill line checks require schema 40.',1;
DECLARE @Admission int=TRY_CONVERT(int,SESSION_CONTEXT(N'FMSC_BillLineTestAdmission')),
        @User int=TRY_CONVERT(int,SESSION_CONTEXT(N'FMSC_BillLineTestUser'));
IF @Admission IS NULL OR @User IS NULL THROW 51000,'Choose an authorized test student and operator first.',1;
DECLARE @Lines table(ChargeId bigint,AdmissionId int,Amount decimal(12,2));
INSERT @Lines SELECT TOP(2) BillingChargeId,AdmissionId,dbo.GetFeeChargeBalance(BillingChargeId)
 FROM dbo.BillingCharges WHERE AdmissionId=@Admission AND Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BillingChargeId)>1
 ORDER BY BillingChargeId;
IF (SELECT COUNT(*) FROM @Lines)<>2 THROW 51000,'The test student needs two outstanding charges.',1;
DECLARE @Total decimal(12,2)=(SELECT SUM(Amount) FROM @Lines),@First bigint=(SELECT MIN(ChargeId) FROM @Lines),
        @Keys nvarchar(max)=(SELECT STRING_AGG(CONVERT(nvarchar(max),ChargeId),N',') FROM @Lines),
        @Ids nvarchar(max)=CONCAT(N'[',@Admission,N']');
DECLARE @Cases table(Id int IDENTITY,Label nvarchar(100),SnapshotXml xml,Reject bit);
INSERT @Cases VALUES(N'Current individual line amounts',
 (SELECT ChargeId BillChargeId,AdmissionId BillAdmissionId,@Keys BillSourceKey,@Total TotalPaid,Amount FROM @Lines FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),0);
INSERT @Cases VALUES(N'Offsetting stale amounts',
 (SELECT ChargeId BillChargeId,AdmissionId BillAdmissionId,@Keys BillSourceKey,@Total TotalPaid,Amount+CASE WHEN ChargeId=@First THEN 1 ELSE -1 END Amount FROM @Lines FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),1);
INSERT @Cases VALUES(N'Wrong student on a line',
 (SELECT ChargeId BillChargeId,CASE WHEN ChargeId=@First THEN 0 ELSE AdmissionId END BillAdmissionId,@Keys BillSourceKey,@Total TotalPaid,Amount FROM @Lines FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),1);
INSERT @Cases VALUES(N'Omitted charge with unchanged total',
 (SELECT @First BillChargeId,@Admission BillAdmissionId,@Keys BillSourceKey,@Total TotalPaid,@Total Amount FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),1);
INSERT @Cases VALUES(N'Duplicate charge with unchanged total',
 (SELECT @First BillChargeId,@Admission BillAdmissionId,@Keys BillSourceKey,@Total TotalPaid,@Total/2 Amount FROM @Lines FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),1);
INSERT @Cases VALUES(N'Missing per-line charge identity',
 (SELECT @Keys BillSourceKey,@Total TotalPaid,Amount FROM @Lines FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),1);
INSERT @Cases VALUES(N'Incorrect total remains rejected',
 (SELECT ChargeId BillChargeId,AdmissionId BillAdmissionId,@Keys BillSourceKey,@Total+1 TotalPaid,Amount FROM @Lines FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet')),1);
DECLARE @Case int=1,@Label nvarchar(100),@Snapshot xml,@Reject bit,@Checks int=0;
WHILE @Case<=(SELECT COUNT(*) FROM @Cases)
BEGIN
 SELECT @Label=Label,@Snapshot=SnapshotXml,@Reject=Reject FROM @Cases WHERE Id=@Case;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.IssueFeeBill @UserId=@User,@PrimaryAdmissionId=@Admission,@StudentIdsJson=@Ids,@SnapshotXml=@Snapshot;
  IF @Reject=1 THROW 51000,'An invalid bill was accepted.',1;
  ROLLBACK TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  IF @Reject=0 OR ERROR_NUMBER()<>54942 THROW;
 END CATCH;
 SET @Checks+=1;PRINT CONCAT(N'PASS: ',@Label);SET @Case+=1;
END;
-- Fully waived, unposted plan evidence is represented by a negative assignment ID.
DECLARE @Assignment int=(SELECT TOP(1) StudentFeeAssignmentId FROM dbo.StudentFeeAssignments WHERE AdmissionId=@Admission AND CancelledAt IS NULL ORDER BY StudentFeeAssignmentId);
IF @Assignment IS NULL THROW 51000,'The test student needs a fee assignment for the waiver check.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 UPDATE dbo.StudentFeeAssignments SET Amount=100,DiscountAmount=100 WHERE StudentFeeAssignmentId=@Assignment;
 SET @Snapshot=(SELECT -CONVERT(bigint,@Assignment) BillChargeId,@Admission BillAdmissionId,CONVERT(nvarchar(30),-CONVERT(bigint,@Assignment)) BillSourceKey,
  CONVERT(decimal(12,2),0) TotalPaid,CONVERT(decimal(12,2),0) Amount FOR XML PATH('FeeReceiptData'),ROOT('NewDataSet'));
 EXEC dbo.IssueFeeBill @UserId=@User,@PrimaryAdmissionId=@Admission,@StudentIdsJson=@Ids,@SnapshotXml=@Snapshot;
 ROLLBACK TRANSACTION;
 SET @Checks+=1;PRINT N'PASS: Zero-value waiver evidence remains issuable';
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
PRINT CONCAT(N'PASS: ',@Checks,N' SQL bill line checks; all test writes rolled back.');
