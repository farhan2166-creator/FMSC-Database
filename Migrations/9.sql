SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
CREATE OR ALTER PROCEDURE dbo.GetLifecycleRepairSnapshot
 @AdmissionId int,@Snapshot nvarchar(max) OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET @Snapshot=(SELECT S.AdmissionId,S.AdmissionDate,S.Status,S.WithdrawalDate,
  JSON_QUERY((SELECT E.EnrollmentId,E.AcademicYearId,E.EnrollmentDate,E.LeavingDate,E.BillingStartDate,E.Status,E.UpdatedAt,
   JSON_QUERY((SELECT L.LeaveRecordId,L.LeaveDate FROM dbo.LeaveCertificates L WHERE L.EnrollmentId=E.EnrollmentId
      ORDER BY L.LeaveRecordId FOR JSON PATH,INCLUDE_NULL_VALUES)) Certificates
   FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId ORDER BY E.EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Enrollments
  FROM dbo.Students S WHERE S.AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
END;
GO
CREATE OR ALTER PROCEDURE dbo.PreviewLifecycleDateRepair @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 DECLARE @Snapshot nvarchar(max);
 EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@Snapshot OUTPUT;
 SELECT AdmissionNumber,StudentName,AdmissionDate,Status,WithdrawalDate,@Snapshot Snapshot FROM dbo.Students WHERE AdmissionId=@AdmissionId;
 SELECT E.EnrollmentId,AY.YearName,E.Status,E.EnrollmentDate,E.LeavingDate,E.BillingStartDate,
  C.CampusName+N' / '+CL.ClassName+N' - '+SEC.SectionName Placement,
  (SELECT COUNT(*) FROM dbo.BillingCharges B WHERE B.EnrollmentId=E.EnrollmentId) Charges,
  (SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)),0) FROM dbo.BillingCharges B WHERE B.EnrollmentId=E.EnrollmentId AND B.Status<>N'Cancelled') Outstanding
 FROM dbo.Enrollments E JOIN dbo.AcademicYears AY ON AY.AcademicYearId=E.AcademicYearId
 JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId JOIN dbo.Campuses C ON C.CampusId=CCS.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=CCS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
 WHERE E.AdmissionId=@AdmissionId ORDER BY E.EnrollmentId;
 SELECT L.EnrollmentId,L.CertificateNumber,L.LeaveDate,L.CreatedAt RecordedAt,
  (SELECT MIN(I.IssuedAt) FROM dbo.LeaveCertificateIssues I WHERE I.LeaveRecordId=L.LeaveRecordId) FirstIssuedAt
 FROM dbo.LeaveCertificates L WHERE L.AdmissionId=@AdmissionId ORDER BY L.EnrollmentId,L.LeaveRecordId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.RepairLifecycleDates
 @UserId int,@AdmissionId int,@ExpectedSnapshot nvarchar(max),@Changes nvarchar(max),@Reason nvarchar(500),
 @AllowLegacyExceptions bit=0,@KeepFinancialHistory bit=0
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 57200,'A reason is required for this repair.',1;
 IF ISJSON(@Changes)<>1 THROW 57201,'Reload the date repair before saving.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Locked int,@Actual nvarchar(max),@AdmissionDate date,@Status nvarchar(30),@Withdrawal date;
  SELECT @Status=Status,@AdmissionDate=AdmissionDate,@Withdrawal=WithdrawalDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @Status IS NULL THROW 57201,'The student no longer exists.',1;
  SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  SELECT @Locked=COUNT(*) FROM dbo.LeaveCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@Actual OUTPUT;
  IF @ExpectedSnapshot IS NULL OR @Actual<>@ExpectedSnapshot THROW 57202,'Records changed while this repair was open. Reload and review again.',1;
  DECLARE @Dates TABLE(EnrollmentId int PRIMARY KEY,EnrollmentDate date NOT NULL,LeavingDate date NULL,BillingStartDate date NULL);
  INSERT @Dates SELECT EnrollmentId,EnrollmentDate,LeavingDate,BillingStartDate FROM OPENJSON(@Changes)
   WITH(EnrollmentId int,EnrollmentDate date,LeavingDate date,BillingStartDate date);
  IF EXISTS(SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId EXCEPT SELECT EnrollmentId FROM @Dates)
    OR EXISTS(SELECT EnrollmentId FROM @Dates EXCEPT SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId)
   THROW 57203,'Review every enrollment belonging to this student together.',1;
  IF EXISTS(SELECT 1 FROM @Dates WHERE LeavingDate<EnrollmentDate)
   THROW 57204,'A leaving date cannot precede its enrollment start. Correct the start date as well if it is wrong.',1;
  IF EXISTS(SELECT 1 FROM @Dates WHERE EnrollmentDate>CONVERT(date,SYSDATETIME()) OR LeavingDate>CONVERT(date,SYSDATETIME()))
   THROW 57205,'Historical enrollment and leaving dates cannot be in the future.',1;
  IF EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId
     WHERE E.Status=N'Active' AND D.LeavingDate IS NOT NULL)
   THROW 57206,'An active enrollment cannot have a leaving date. Correct its status separately if necessary.',1;
  IF EXISTS(SELECT 1 FROM @Dates D JOIN dbo.LeaveCertificates L ON L.EnrollmentId=D.EnrollmentId WHERE D.LeavingDate IS NULL)
   THROW 57207,'An enrollment with a leave certificate needs a leaving date.',1;
  IF @AllowLegacyExceptions=0 AND (EXISTS(SELECT 1 FROM @Dates D WHERE D.EnrollmentDate<@AdmissionDate)
    OR EXISTS(SELECT 1 FROM @Dates A JOIN @Dates B ON A.EnrollmentId<B.EnrollmentId
      WHERE A.EnrollmentDate<=ISNULL(B.LeavingDate,'99991231') AND B.EnrollmentDate<=ISNULL(A.LeavingDate,'99991231'))
    OR EXISTS(SELECT 1 FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
      WHERE D.EnrollmentDate<Y.StartDate OR D.EnrollmentDate>Y.EndDate OR D.LeavingDate>Y.EndDate))
   THROW 57208,'Dates overlap, precede admission, or fall outside an academic year. Correct them or explicitly acknowledge a legacy exception.',1;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
  IF @KeepFinancialHistory=0 AND EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId)
   THROW 57209,'Explicitly acknowledge that existing bills and payments retain their original dates and amounts.',1;
  DECLARE @Before nvarchar(max)=@Actual,@Changed int;
  UPDATE E SET EnrollmentDate=D.EnrollmentDate,LeavingDate=D.LeavingDate,BillingStartDate=D.BillingStartDate,UpdatedAt=SYSDATETIME()
   FROM dbo.Enrollments E JOIN @Dates D ON D.EnrollmentId=E.EnrollmentId
   WHERE E.EnrollmentDate<>D.EnrollmentDate OR ISNULL(E.LeavingDate,'19000101')<>ISNULL(D.LeavingDate,'19000101')
     OR ISNULL(E.BillingStartDate,'19000101')<>ISNULL(D.BillingStartDate,'19000101');
  SET @Changed=@@ROWCOUNT;
  UPDATE L SET LeaveDate=D.LeavingDate FROM dbo.LeaveCertificates L JOIN @Dates D ON D.EnrollmentId=L.EnrollmentId WHERE L.LeaveDate<>D.LeavingDate;
  DECLARE @Certificates int=@@ROWCOUNT,@FinalWithdrawal date=@Withdrawal;
  IF @Status=N'Left' AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND Status=N'Active')
   SELECT TOP(1) @FinalWithdrawal=D.LeavingDate FROM @Dates D JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE E.Status=N'Left' ORDER BY D.EnrollmentDate DESC,D.EnrollmentId DESC;
  ELSE IF @Status=N'Active' SET @FinalWithdrawal=NULL;
  UPDATE dbo.Students SET WithdrawalDate=@FinalWithdrawal,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId
    AND ISNULL(WithdrawalDate,'19000101')<>ISNULL(@FinalWithdrawal,'19000101');
  INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   SELECT E.EnrollmentId,@AdmissionId,N'CORRECTION',CONVERT(date,SYSDATETIME()),E.ClassSectionId,E.ClassSectionId,E.Status,E.Status,
    LEFT(N'Lifecycle dates repaired: '+@Reason,500),@UserId FROM dbo.Enrollments E WHERE E.AdmissionId=@AdmissionId;
  INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,Remarks,CreatedBy)
   VALUES(@AdmissionId,@Status,@Status,N'Lifecycle dates repaired',ISNULL(@FinalWithdrawal,CONVERT(date,SYSDATETIME())),@Reason,@UserId);
  DECLARE @Operation uniqueidentifier=NEWID(),@After nvarchar(max),@Audit nvarchar(max);
  EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@After OUTPUT;
  SET @Audit=(SELECT JSON_QUERY(@After) Records,@Reason Reason,@AllowLegacyExceptions LegacyException,
    N'Existing charges, payments, original history entries and creation/issuance timestamps retained.' FinancialHistory FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'LIFECYCLE_DATE_REPAIR',CONVERT(nvarchar(200),@AdmissionId));
  EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@UserId,@TableName=N'Enrollments',@RecordId=NULL,@Action=N'CORRECTION',@OldValues=@Before,@NewValues=@Audit;
  COMMIT;
  SELECT @Changed EnrollmentsChanged,@Certificates CertificatesUpdated,@FinalWithdrawal WithdrawalDate;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
DECLARE @scan nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.CheckStudentConsistency'));
IF @scan NOT LIKE N'%CERTIFICATE_ENROLLMENT_DATE_MISMATCH%'
BEGIN
 SET @scan=REPLACE(REPLACE(@scan,N'CREATE   PROCEDURE',N'ALTER PROCEDURE'),N'CREATE PROCEDURE',N'ALTER PROCEDURE');
 SET @scan=REPLACE(@scan,N'WHERE  OFA.AdmissionId = S.AdmissionId',N'WHERE OFA.AdmissionId = S.AdmissionId AND 1=0');
 SET @scan=REPLACE(@scan,N'AND EE.EarliestEnrollmentDate < S.AdmissionDate) AS Findings',N'AND EE.EarliestEnrollmentDate < S.AdmissionDate
    UNION ALL SELECT N''CERTIFICATE_ENROLLMENT_DATE_MISMATCH'',N''High'',
      N''Certificate ''+L.CertificateNumber+N'' leave date differs from enrollment #''+CONVERT(nvarchar(20),E.EnrollmentId)+N''.'',
      N''Use Repair Lifecycle Dates to review and synchronize the enrollment, certificate and current withdrawal date together.'',
      E.EnrollmentId,CAST(NULL AS nvarchar(30)),CAST(NULL AS date)
      FROM dbo.LeaveCertificates L JOIN dbo.Enrollments E ON E.EnrollmentId=L.EnrollmentId
      WHERE L.AdmissionId=S.AdmissionId AND (E.LeavingDate IS NULL OR L.LeaveDate<>E.LeavingDate)
    UNION ALL SELECT N''LEAVING_BEFORE_ENROLLMENT'',N''High'',
      N''Enrollment #''+CONVERT(nvarchar(20),E.EnrollmentId)+N'' ends before it starts.'',
      N''Review the full enrollment sequence using Repair Lifecycle Dates.'',E.EnrollmentId,CAST(NULL AS nvarchar(30)),CAST(NULL AS date)
      FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.LeavingDate<E.EnrollmentDate
    UNION ALL SELECT N''WITHDRAWAL_ENROLLMENT_DATE_MISMATCH'',N''High'',
      N''Student withdrawal date differs from the latest closed enrollment.'',
      N''Use Repair Lifecycle Dates to reconcile all related departure dates.'',E.EnrollmentId,CAST(NULL AS nvarchar(30)),E.LeavingDate
      FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND S.Status=N''Left'' AND E.Status=N''Left''
      AND E.EnrollmentId=(SELECT TOP(1) X.EnrollmentId FROM dbo.Enrollments X WHERE X.AdmissionId=S.AdmissionId AND X.Status=N''Left'' ORDER BY X.EnrollmentDate DESC,X.EnrollmentId DESC)
      AND ISNULL(S.WithdrawalDate,''19000101'')<>ISNULL(E.LeavingDate,''19000101'')) AS Findings');
 IF @scan NOT LIKE N'%CERTIFICATE_ENROLLMENT_DATE_MISMATCH%' THROW 57210,'The consistency scan migration could not locate its findings query.',1;
 EXEC sys.sp_executesql @scan;
END;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'9')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'9',SYSDATETIME(),SUSER_SNAME(),N'Atomic console lifecycle date repair with explicit legacy exceptions and retained financial history.');
COMMIT;
