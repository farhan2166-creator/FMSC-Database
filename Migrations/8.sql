SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @name sysname,@sql nvarchar(max);
DECLARE procs CURSOR LOCAL FAST_FORWARD FOR
 SELECT name FROM sys.procedures WHERE schema_id=SCHEMA_ID(N'dbo')
 AND name IN(N'PreviewMonthlyFees',N'PostSelectedMonthlyFees',N'GetPostableMonths');
OPEN procs; FETCH NEXT FROM procs INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.'+@name));
 SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,N'E.Status IN (N''Active'', N''Promoted'', N''Left'')',N'E.Status = N''Active''');
 EXEC sys.sp_executesql @sql;
 FETCH NEXT FROM procs INTO @name;
END;
CLOSE procs; DEALLOCATE procs;
-- Filter closed enrollments before bulk reversal, so they cannot abort an otherwise valid batch.
DECLARE revs CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.procedures
 WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN(N'PreviewPostingReversal',N'ReverseMonthlyFees');
OPEN revs; FETCH NEXT FROM revs INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.'+@name));
 SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,N'BC.BillingMonth = @Month',N'BC.BillingMonth = @Month AND E.Status=N''Active''');
 EXEC sys.sp_executesql @sql;
 FETCH NEXT FROM revs INTO @name;
END;
CLOSE revs; DEALLOCATE revs;
-- Explicit readmission decisions are required even for callers outside the form.
SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.PostEnrollmentOpeningCharges'));
SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
-- Opening-charge automation must not recreate a deliberately reversed opening bill.
SET @sql=REPLACE(@sql,N'AND BC.Status <> N''Cancelled''',N'');
EXEC sys.sp_executesql @sql;
SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.PreviewMonthlyFees'));
SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
IF @sql NOT LIKE N'%Previously reversed%'
 SET @sql=REPLACE(@sql,N'ELSE N''Eligible'' END AS EligibilityNote',N'WHEN EXISTS(SELECT 1 FROM dbo.BillingCharges R WHERE R.EnrollmentId=SFA.EnrollmentId AND R.FeeTypeId=SFA.FeeTypeId AND R.BillingMonth=@Month AND R.ChargeKind=N''Monthly'' AND R.Status=N''Cancelled'') THEN N''Previously reversed — selecting this creates a new charge'' ELSE N''Eligible'' END AS EligibilityNote');
EXEC sys.sp_executesql @sql;
SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.ReAdmitStudent'));
IF @sql NOT LIKE N'%@MonthlyStartMonth%'
BEGIN
 SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,N'@UsesTransport BIT',N'@UsesTransport BIT, @MonthlyStartMonth date=NULL, @ChargeYearlyFees bit=NULL');
 SET @sql=REPLACE(@sql,N'    BEGIN TRANSACTION;',N'
    IF @MonthlyStartMonth IS NULL OR @ChargeYearlyFees IS NULL
        THROW 57100, ''Choose the monthly start month and yearly-fee decision before readmission.'',1;
    BEGIN TRANSACTION;');
 SET @sql=REPLACE(@sql,N'    INSERT  dbo.Enrollments',N'
    IF DAY(@MonthlyStartMonth)<>1 OR @MonthlyStartMonth<DATEFROMPARTS(YEAR(@EnrollmentDate),MONTH(@EnrollmentDate),1)
       OR @MonthlyStartMonth>DATEFROMPARTS(YEAR(@YearEndDate),MONTH(@YearEndDate),1)
        THROW 57101,''Choose a monthly start month within the remaining academic year.'',1;
    SET @BillingStartDate=@MonthlyStartMonth;
    INSERT  dbo.Enrollments');
 SET @sql=REPLACE(@sql,N'           0,'+CHAR(13)+CHAR(10)+N'           FC.Frequency,',N'           CASE WHEN FC.Frequency=N''Yearly'' AND @ChargeYearlyFees=0 THEN FC.Amount ELSE 0 END,'+CHAR(13)+CHAR(10)+N'           FC.Frequency,');
 IF @sql NOT LIKE N'%CASE WHEN FC.Frequency=N''Yearly''%' THROW 57109,'Readmission migration could not locate assignment discount.',1;
 SET @sql=REPLACE(@sql,N'           N''Created by re-admission''',N'           CASE WHEN FC.Frequency=N''Yearly'' AND @ChargeYearlyFees=0 THEN N''Yearly fee explicitly waived at re-admission'' ELSE N''Created by re-admission'' END');
 SET @sql=REPLACE(@sql,N'@UsesTransport AS UsesTransport,',N'@UsesTransport AS UsesTransport, @ChargeYearlyFees AS ChargeYearlyFees,');
 EXEC sys.sp_executesql @sql;
END;
SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.ReAdmitStudent'));
IF @sql NOT LIKE N'%@ReviewedRates%'
BEGIN
 SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,N'@ChargeYearlyFees bit=NULL',N'@ChargeYearlyFees bit=NULL, @ReviewedRates nvarchar(max)=NULL');
 SET @sql=REPLACE(@sql,N'    DECLARE @CutoffDay AS INT;',N'
    IF @ReviewedRates IS NULL OR ISJSON(@ReviewedRates)<>1
      THROW 57107,''Review the readmission rates before confirming.'',1;
    DECLARE @Rates TABLE(FeeTypeId int,Amount decimal(12,2),Frequency nvarchar(40));
    INSERT @Rates SELECT FeeTypeId,Amount,Frequency FROM OPENJSON(@ReviewedRates)
       WITH(FeeTypeId int,Amount decimal(12,2),Frequency nvarchar(40));
    IF EXISTS(SELECT FC.FeeTypeId,FC.Amount,FC.Frequency FROM dbo.FeeConfigurations FC WITH(UPDLOCK,HOLDLOCK)
       JOIN dbo.FeeTypes FT ON FT.FeeTypeId=FC.FeeTypeId WHERE FC.CampusId=@CampusId AND FC.ClassId=@ClassId
       AND (@UsesTransport=1 OR FT.IsTransportLinked=0) EXCEPT SELECT * FROM @Rates)
       OR EXISTS(SELECT * FROM @Rates EXCEPT SELECT FC.FeeTypeId,FC.Amount,FC.Frequency FROM dbo.FeeConfigurations FC
       JOIN dbo.FeeTypes FT ON FT.FeeTypeId=FC.FeeTypeId WHERE FC.CampusId=@CampusId AND FC.ClassId=@ClassId
       AND (@UsesTransport=1 OR FT.IsTransportLinked=0))
      THROW 57108,''Readmission rates changed. Review them again; nothing was saved.'',1;
    DECLARE @CutoffDay AS INT;');
 EXEC sys.sp_executesql @sql;
END;
-- Review all current enrollment dues before closing, not only advance bills.
SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.CreateReviewedLeaveCertificate'));
SET @sql=REPLACE(REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
SET @sql=REPLACE(@sql,N'AND (BillingMonth>@Month OR (BillingMonth IS NULL AND ChargeDate>@LeaveDate));',
 N'AND (dbo.GetFeeChargeBalance(BillingChargeId)>0 OR BillingMonth>@Month OR (BillingMonth IS NULL AND ChargeDate>@LeaveDate));');
SET @sql=REPLACE(@sql,N'Only reviewed, wholly uncollected later charges can be reversed.',N'Only reviewed, wholly uncollected charges on the current enrollment can be reversed.');
EXEC sys.sp_executesql @sql;
GO
CREATE OR ALTER TRIGGER dbo.GuardClosedEnrollmentCharges ON dbo.BillingCharges AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- Payment status changes remain allowed; cancelling or rewriting a closed bill does not.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status<>N'Active' AND (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId
    OR I.StudentFeeAssignmentId<>D.StudentFeeAssignmentId OR I.AdmissionId<>D.AdmissionId OR I.FeeTypeId<>D.FeeTypeId
    OR I.GrossAmount<>D.GrossAmount OR I.DiscountAmount<>D.DiscountAmount
    OR ISNULL(I.BillingMonth,'19000101')<>ISNULL(D.BillingMonth,'19000101')
    OR I.ChargeDate<>D.ChargeDate OR I.DueDate<>D.DueDate OR I.ChargeKind<>D.ChargeKind
    OR (I.Status<>D.Status AND (I.Status=N'Cancelled' OR D.Status=N'Cancelled'))))
  THROW 57102,'This enrollment is closed. Its charges cannot be posted, changed or reversed here. Outstanding fees can still be collected.',1;
 IF EXISTS(SELECT 1 FROM deleted D LEFT JOIN inserted I ON I.BillingChargeId=D.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE I.BillingChargeId IS NULL AND E.Status<>N'Active')
  THROW 57102,'Closed enrollment charges must remain in history.',1;
 -- Protect payments even if an alternative reversal entry point bypasses the form.
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   WHERE I.Status=N'Cancelled' AND D.Status<>N'Cancelled' AND EXISTS(
    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.BillingChargeId=I.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0))
  THROW 57103,'A paid or partially paid charge cannot be cancelled. Use the payment correction or refund workflow.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardFeeAssignmentPeriods ON dbo.StudentFeeAssignments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted WHERE CancelledAt IS NULL AND EffectiveFrom>EffectiveTo)
  THROW 57104,'A fee period cannot end before it starts.',1;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status<>N'Active') OR EXISTS(SELECT 1 FROM deleted D JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=D.EnrollmentId WHERE E.Status<>N'Active')
  THROW 57105,'This enrollment is closed. Its fee assignments are preserved as history.',1;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK) ON A.EnrollmentId=I.EnrollmentId AND A.FeeTypeId=I.FeeTypeId
   AND A.StudentFeeAssignmentId<>I.StudentFeeAssignmentId WHERE I.CancelledAt IS NULL AND A.CancelledAt IS NULL
   AND ISNULL(I.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231')
   AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(I.EffectiveTo,'99991231'))
  THROW 57106,'Fee periods overlap. Review the existing periods before applying this change.',1;
END;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'8')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'8',SYSDATETIME(),SUSER_SNAME(),N'Closed enrollment financial guards and explicit readmission fee decisions.');
COMMIT TRANSACTION;
