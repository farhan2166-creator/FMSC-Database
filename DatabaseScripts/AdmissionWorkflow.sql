-- Apply after PlacementConfirmation.sql and StudentCorrectionGuards.sql.
-- Existing school records are marked for review, never silently activated or re-dated.
IF OBJECT_ID(N'dbo.LegacyAdmissionSetup',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.LegacyAdmissionSetup(SetupId int NOT NULL PRIMARY KEY CHECK(SetupId=1),
  CompletedAt datetime2 NULL,CompletedBy int NULL,GoLiveDate date NULL,BillingStart date NULL);
 INSERT dbo.LegacyAdmissionSetup(SetupId) VALUES(1);
END;
IF COL_LENGTH(N'dbo.Students',N'LegacySetupPending') IS NULL
BEGIN
 ALTER TABLE dbo.Students ADD LegacySetupPending bit NOT NULL CONSTRAINT DF_Students_LegacySetupPending DEFAULT(0),
  LegacyVerifiedAt datetime2 NULL,LegacyVerifiedBy int NULL,QuickReference varchar(40) NULL;
 EXEC(N'UPDATE dbo.Students SET LegacySetupPending=1;');
END;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Students') AND name=N'AdmissionNumber' AND is_nullable=0)
BEGIN
 DROP INDEX UX_Students_AdmissionNumber ON dbo.Students;
 ALTER TABLE dbo.Students DROP CONSTRAINT CK_Students_AdmissionNumber;
 ALTER TABLE dbo.Students DROP COLUMN AdmissionNumberBase,AdmissionNumberSuffix;
 ALTER TABLE dbo.Students ALTER COLUMN AdmissionNumber varchar(40) NULL;
 ALTER TABLE dbo.Students ADD AdmissionNumberBase AS (TRY_CAST(LEFT(AdmissionNumber,PATINDEX('%[^0-9]%',AdmissionNumber+'x')-1) AS bigint)) PERSISTED,
  AdmissionNumberSuffix AS (SUBSTRING(AdmissionNumber,PATINDEX('%[^0-9]%',AdmissionNumber+'x'),40)) PERSISTED;
 ALTER TABLE dbo.Students ADD CONSTRAINT CK_Students_AdmissionNumber CHECK(AdmissionNumber IS NULL OR
  (dbo.NormalizeAdmissionNumber(AdmissionNumber) IS NOT NULL AND AdmissionNumber COLLATE Latin1_General_100_BIN2=dbo.NormalizeAdmissionNumber(AdmissionNumber) COLLATE Latin1_General_100_BIN2));
 CREATE UNIQUE INDEX UX_Students_AdmissionNumber ON dbo.Students(AdmissionNumber) WHERE AdmissionNumber IS NOT NULL;
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Students') AND name=N'UX_Students_QuickReference')
 CREATE UNIQUE INDEX UX_Students_QuickReference ON dbo.Students(QuickReference) WHERE QuickReference IS NOT NULL;
ALTER TABLE dbo.Students DROP CONSTRAINT CK_Students_Status;
ALTER TABLE dbo.Students ADD CONSTRAINT CK_Students_Status CHECK(Status IN(N'Active',N'Left',N'Pending',N'Cancelled'));
ALTER TABLE dbo.Enrollments DROP CONSTRAINT CK_Enrollments_Status;
ALTER TABLE dbo.Enrollments ADD CONSTRAINT CK_Enrollments_Status CHECK(Status IN(N'Active',N'Left',N'Promoted',N'Pending',N'Cancelled'));
IF OBJECT_ID(N'dbo.CK_Students_AdmissionStage',N'C') IS NULL
 ALTER TABLE dbo.Students ADD CONSTRAINT CK_Students_AdmissionStage CHECK(
  (Status NOT IN(N'Active',N'Left') OR AdmissionNumber IS NOT NULL) AND (Status<>N'Pending' OR AdmissionNumber IS NULL));
GO
CREATE OR ALTER TRIGGER dbo.GuardProvisionalPlacement ON dbo.Enrollments AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId
  WHERE I.Status=N'Active' AND S.Status IN(N'Pending',N'Cancelled'))
  THROW 59752,'Complete Admission before activating this registration. Cancelled records cannot have an active placement.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardConfirmedAcademicYear ON dbo.Enrollments AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.EnrollmentId=I.EnrollmentId JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId
  WHERE D.PlacementConfirmedAt IS NOT NULL AND S.LegacySetupPending=0
    AND (I.AcademicYearId<>D.AcademicYearId OR I.PlacementConfirmedAt IS NULL))
  THROW 59640,'Academic year is confirmed. Use Promotion for a move to another academic year.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.ClearLegacyStudentVerification ON dbo.Students AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF TRIGGER_NESTLEVEL()>1 RETURN;
 IF UPDATE(StudentName) OR UPDATE(FatherName) OR UPDATE(DateOfBirth) OR UPDATE(Gender) OR UPDATE(ContactNumber)
  OR UPDATE(Address) OR UPDATE(AdmissionDate) OR UPDATE(AdmissionNumber) OR UPDATE(Status) OR UPDATE(BFormNumber) OR UPDATE(Religion) OR UPDATE(AlternateContactNumber) OR UPDATE(PlaceOfBirth) OR UPDATE(Disability) OR UPDATE(WithdrawalDate) OR UPDATE(AdmissionCampusId) OR UPDATE(PreviousSchool) OR UPDATE(FatherOccupation) OR UPDATE(BiseEnrolmentNumber) OR UPDATE(CasteTribe)
  UPDATE S SET LegacyVerifiedAt=NULL,LegacyVerifiedBy=NULL FROM dbo.Students S JOIN inserted I ON I.AdmissionId=S.AdmissionId
  WHERE S.LegacySetupPending=1;
END;
GO
CREATE OR ALTER TRIGGER dbo.ClearLegacyPlacementVerification ON dbo.Enrollments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 UPDATE S SET LegacyVerifiedAt=NULL,LegacyVerifiedBy=NULL FROM dbo.Students S
 WHERE S.LegacySetupPending=1 AND S.AdmissionId IN(SELECT AdmissionId FROM inserted UNION SELECT AdmissionId FROM deleted);
END;
GO
CREATE OR ALTER TRIGGER dbo.ClearLegacyFeeVerification ON dbo.StudentFeeAssignments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 UPDATE S SET LegacyVerifiedAt=NULL,LegacyVerifiedBy=NULL FROM dbo.Students S
 WHERE S.LegacySetupPending=1 AND S.AdmissionId IN(SELECT AdmissionId FROM inserted UNION SELECT AdmissionId FROM deleted);
END;
GO
CREATE OR ALTER PROCEDURE dbo.CompleteQuickAdmission @UserId int,@AdmissionId int,@AdmissionNumber varchar(40)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Admit';
 BEGIN TRAN;
 DECLARE @Date date,@Enrollment int,@Old nvarchar(max),@New nvarchar(max),@Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@AdmissionId);
 SELECT @Date=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Pending' AND QuickReference IS NOT NULL AND AdmissionNumber IS NULL;
 IF @Date IS NULL THROW 59701,'Select a pending registration. It may already have been completed or cancelled.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND (NULLIF(LTRIM(RTRIM(StudentName)),N'') IS NULL OR
  NULLIF(LTRIM(RTRIM(FatherName)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(ContactNumber)),N'') IS NULL OR Gender IS NULL OR
  NULLIF(LTRIM(RTRIM(Address)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(PlaceOfBirth)),N'') IS NULL))
  THROW 59702,'Complete and save the required details in Quick Registration review before confirming admission.',1;
 SELECT @Enrollment=EnrollmentId FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Pending';
 IF @Enrollment IS NULL THROW 59703,'The pending academic placement could not be found.',1;
 SELECT @Old=(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.ReserveAdmissionNumber @AdmissionNumber OUTPUT;
 DECLARE @Registration nvarchar(40);
 EXEC dbo.GetNextRegistrationNumber @RegistrationDate=@Date,@RegistrationNumber=@Registration OUTPUT;
 UPDATE dbo.Students SET AdmissionNumber=@AdmissionNumber,RegistrationNumber=@Registration,Status=N'Active',UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 UPDATE dbo.Enrollments SET Status=N'Active',PlacementConfirmedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 -- Same placement and fee assignments: opening bills already issued under the registration remain untouched.
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
 SELECT EnrollmentId,AdmissionId,N'ADMISSION',@Date,ClassSectionId,N'Pending',N'Active',N'Quick registration completed',@UserId FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment;
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,N'Pending',N'Active',N'Admission completed',@Date,@UserId);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'COMPLETE_ADMISSION',@Key);
 SELECT @New=(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N'Students',@Key,N'UPDATE',@Old,@New;
 COMMIT;
 SELECT @AdmissionNumber AdmissionNumber;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CloseStudentAdmission @UserId int,@AdmissionId int,@LeavingDate date,@Reason nvarchar(500),
 @Cancel bit=0,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@ReverseFutureUnpaid bit=0
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59710,'Enter the reason for leaving or cancellation.',1;
 IF @LeavingDate IS NULL OR @LeavingDate>CONVERT(date,GETDATE()) THROW 59711,'Choose a leaving date that is not in the future.',1;
 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Enrollment int,@Start date,@Old nvarchar(max),@New nvarchar(max),@Snapshot varchar(64),@Raw nvarchar(max),@Lock int;
 SELECT @State=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Enrollment=EnrollmentId,@Start=EnrollmentDate FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending');
 IF @Enrollment IS NULL OR @State NOT IN(N'Active',N'Pending') THROW 59712,'This record has no open placement to close.',1;
 IF @Cancel=1 AND (@State<>N'Pending' OR EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND AdmissionNumber IS NOT NULL))
  THROW 59720,'An officially admitted student cannot be cancelled. Use Mark as Left for departure, or correct details and fees in their respective workspaces.',1;
 IF @State=N'Pending' AND @Cancel=0 THROW 59713,'Use Cancel Registration for a pending registration.',1;
 IF @LeavingDate<@Start THROW 59714,'Leaving date cannot be before the placement start.',1;
 SELECT @Lock=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment) THROW 59715,'This placement already has a leaving certificate. Review its recorded departure in Admission.',1;
 IF @Cancel=1 AND EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.AdmissionId=@AdmissionId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)
  THROW 59716,'Payments exist. Review refunds or reversals in Fee Corrections before cancellation; history must be retained. For an admitted student who attended, use Mark as Left.',1;
 IF @Cancel=1 AND (EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE AdmissionId=@AdmissionId) OR
   EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@Enrollment))
  THROW 59717,'This admission has academic or certificate history. Use Mark as Left rather than cancellation.',1;
 DECLARE @FutureCount int,@FutureAmount decimal(12,2),@FuturePaid int,@Blocked int;
 SELECT @FutureCount=COUNT(*),@FutureAmount=COALESCE(SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)),0)
 FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled'
  AND COALESCE(B.BillingMonth,B.ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1);
 SELECT @FuturePaid=COUNT(*) FROM dbo.BillingCharges B WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled'
  AND COALESCE(B.BillingMonth,B.ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1) AND
  EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
   WHERE A.BillingChargeId=B.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0);
 SET @Blocked=CASE WHEN @Cancel=0 AND (@FuturePaid>0 OR (@FutureCount>0 AND @ReverseFutureUnpaid=0)) THEN 1 ELSE 0 END;
 SET @Raw=CONCAT(CONVERT(varchar(10),@LeavingDate,120),@Reason,CONVERT(char(1),@Cancel),CONVERT(char(1),@ReverseFutureUnpaid),(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@State CurrentStatus,@Cancel CancelAdmission,@Blocked BlockedCount,@FutureCount FutureCount,@FutureAmount FutureAmount,@FuturePaid FuturePaid,COUNT(*) ChargeCount,
   COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled';
  COMMIT;RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59719,'The record changed. Review the closure again.',1;
 IF @Blocked>0 THROW 59718,'Review charges after the leaving month. Choose reversal for unpaid future charges, or resolve paid charges in Fee Corrections first.',1;
 SELECT @Old=(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF @Cancel=1 OR (@ReverseFutureUnpaid=1 AND @FutureCount>0)
 BEGIN
  EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
  DECLARE @Charge bigint; DECLARE @Reverse TABLE(BillingChargeId bigint,AdmissionId int,FeeTypeName nvarchar(100),BillingMonth date,AmountReversed decimal(12,2),Reason nvarchar(500),Status nvarchar(20));
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled'
   AND (@Cancel=1 OR COALESCE(BillingMonth,ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1));
  OPEN C;FETCH NEXT FROM C INTO @Charge;
  WHILE @@FETCH_STATUS=0 BEGIN INSERT @Reverse EXEC dbo.ReverseBillingCharge @UserId,@Charge,@Reason;FETCH NEXT FROM C INTO @Charge;END;
  CLOSE C;DEALLOCATE C;
 END;
 DECLARE @NewStatus nvarchar(30)=CASE WHEN @Cancel=1 THEN N'Cancelled' ELSE N'Left' END;
 UPDATE dbo.Enrollments SET Status=@NewStatus,LeavingDate=@LeavingDate,BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=@NewStatus,WithdrawalDate=@LeavingDate,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId) VALUES(@Enrollment,@AdmissionId,N'STATUS_CHANGE',@LeavingDate,@State,@NewStatus,@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,@State,@NewStatus,@Reason,@LeavingDate,@UserId);
 DECLARE @Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@Enrollment);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'CLOSE_ADMISSION',@Reason);
 SELECT @New=(SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N'Enrollments',@Key,N'UPDATE',@Old,@New;
 COMMIT;
END;
GO
CREATE OR ALTER PROCEDURE dbo.VerifyLegacyStudent @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND LegacySetupPending=1)
  THROW 59730,'This is not a legacy record awaiting setup.',1;
 IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WITH(UPDLOCK,HOLDLOCK) WHERE CompletedAt IS NOT NULL)
  THROW 59731,'Legacy setup is already complete.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND (NULLIF(LTRIM(RTRIM(StudentName)),N'') IS NULL OR AdmissionNumber IS NULL))
  THROW 59732,'Complete the student name and admission number first.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Active') AND
  (SELECT COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Active')<>1
  THROW 59733,'An active student needs exactly one verified academic placement.',1;
 UPDATE dbo.Students SET LegacyVerifiedAt=SYSDATETIME(),LegacyVerifiedBy=@UserId WHERE AdmissionId=@AdmissionId;
 DECLARE @Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@AdmissionId),@New nvarchar(max);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'VERIFY_LEGACY',@Key);
 SELECT @New=(SELECT AdmissionId,LegacyVerifiedAt,LegacyVerifiedBy FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N'Students',@Key,N'UPDATE',NULL,@New;
 COMMIT;
END;
GO
CREATE OR ALTER PROCEDURE dbo.FinishLegacyAdmissionSetup @UserId int,@GoLiveDate date,@BillingStart date,@PlacementStart date=NULL,
 @Preview bit=1,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF @GoLiveDate IS NULL OR @GoLiveDate>CONVERT(date,GETDATE()) OR @BillingStart IS NULL OR DAY(@BillingStart)<>1
  THROW 59740,'Choose a go-live date up to today and a billing start month.',1;
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WITH(UPDLOCK,HOLDLOCK) WHERE CompletedAt IS NOT NULL)
  THROW 59741,'Legacy setup has already been completed. It cannot be run again.',1;
 DECLARE @Locked int,@Snapshot varchar(64),@Raw nvarchar(max),@Blocked int,@Old nvarchar(max);
 SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE LegacySetupPending=1;
 SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId IN(SELECT AdmissionId FROM dbo.Students WHERE LegacySetupPending=1);
 CREATE TABLE #Issues(AdmissionId int NOT NULL,Reason nvarchar(500) NOT NULL);
 INSERT #Issues SELECT AdmissionId,N'Open Admission, review and mark the legacy record verified'
 FROM dbo.Students WHERE LegacySetupPending=1 AND Status=N'Active' AND LegacyVerifiedAt IS NULL;
 INSERT #Issues SELECT AdmissionId,N'An active student needs exactly one academic placement; correct placement history in Admission'
 FROM dbo.Students S WHERE LegacySetupPending=1 AND Status=N'Active' AND
  (SELECT COUNT(*) FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.Status=N'Active')<>1;
 SELECT E.EnrollmentId,E.AdmissionId,COALESCE(@PlacementStart,E.EnrollmentDate) StartDate,@BillingStart BillingStart INTO #Ready
 FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE E.Status=N'Active' AND S.LegacySetupPending=1 AND S.LegacyVerifiedAt IS NOT NULL;
 INSERT #Issues SELECT R.AdmissionId,CONCAT(N'Placement/billing dates must fit ',Y.YearName,N' (',CONVERT(nvarchar(10),Y.StartDate,120),N' to ',CONVERT(nvarchar(10),Y.EndDate,120),N'), admission date and go-live date')
 FROM #Ready R JOIN dbo.Enrollments E ON E.EnrollmentId=R.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  JOIN dbo.Students S ON S.AdmissionId=R.AdmissionId WHERE R.StartDate NOT BETWEEN Y.StartDate AND Y.EndDate OR R.StartDate<S.AdmissionDate OR R.StartDate>@GoLiveDate
   OR @BillingStart<DATEFROMPARTS(YEAR(R.StartDate),MONTH(R.StartDate),1) OR @BillingStart>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1);
 INSERT #Issues SELECT R.AdmissionId,CONCAT(N'Missing mandatory fee: ',F.FeeTypeName,N'. Add/review it in Admission')
 FROM #Ready R CROSS JOIN dbo.FeeTypes F WHERE F.IsMandatory=1 AND NOT EXISTS(
   SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=R.EnrollmentId AND A.FeeTypeId=F.FeeTypeId AND A.CancelledAt IS NULL);
 -- No date overwrite may invalidate an existing charge/payment or overlap historical placement.
 IF @PlacementStart IS NOT NULL INSERT #Issues SELECT R.AdmissionId,N'The replacement placement date conflicts with existing bills or placement history. Keep the recorded date or review the record in Admission'
 FROM #Ready R WHERE EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=R.EnrollmentId
   AND B.Status<>N'Cancelled' AND B.ChargeDate<R.StartDate) OR EXISTS(SELECT 1 FROM dbo.Enrollments H WHERE H.AdmissionId=R.AdmissionId AND H.EnrollmentId<>R.EnrollmentId
   AND (H.LeavingDate IS NULL OR H.LeavingDate>=R.StartDate));
 SELECT @Blocked=COUNT(*) FROM #Issues;
 SET @Raw=CONCAT(CONVERT(varchar(10),@GoLiveDate,120),CONVERT(varchar(10),@BillingStart,120),CONVERT(varchar(10),@PlacementStart,120),
  (SELECT * FROM dbo.Students WHERE LegacySetupPending=1 ORDER BY AdmissionId FOR JSON PATH),
  (SELECT E.* FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY E.EnrollmentId FOR JSON PATH),
  (SELECT A.* FROM dbo.StudentFeeAssignments A JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY A.StudentFeeAssignmentId FOR JSON PATH),
  (SELECT B.* FROM dbo.BillingCharges B JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId WHERE S.LegacySetupPending=1 ORDER BY B.BillingChargeId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1 BEGIN
  SELECT @Snapshot Snapshot,@Blocked BlockedCount,(SELECT COUNT(*) FROM #Ready R WHERE NOT EXISTS(SELECT 1 FROM #Issues I WHERE I.AdmissionId=R.AdmissionId)) ActiveStudents,
   (SELECT COUNT(*) FROM dbo.Students WHERE LegacySetupPending=1 AND Status=N'Active' AND LegacyVerifiedAt IS NULL) UnverifiedStudents,
   (SELECT STRING_AGG(CONVERT(nvarchar(max),CONCAT(D.AdmissionNumber,N' · ',D.StudentName,N' · ',D.Reason)),CHAR(13)+CHAR(10))
    FROM(SELECT TOP(200) S.AdmissionNumber,S.StudentName,I.Reason FROM #Issues I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId ORDER BY S.AdmissionId,I.Reason) D) ReviewDetails;
  SELECT S.AdmissionNumber,S.StudentName,R.StartDate PlacementStartDate,R.BillingStart FROM #Ready R JOIN dbo.Students S ON S.AdmissionId=R.AdmissionId;
  COMMIT;RETURN;
 END;
 IF @Blocked>0 THROW 59742,'Legacy setup has unresolved records, missing fee assignments or conflicting dates. Review Data Review and Admission first.',1;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59743,'Records changed after review. Preview Go Live again.',1;
 DECLARE @Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=N'1',@New nvarchar(max);
 SELECT @Old=(SELECT E.* FROM dbo.Enrollments E JOIN #Ready R ON R.EnrollmentId=E.EnrollmentId FOR JSON PATH);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'LEGACY_GO_LIVE',N'Reviewed legacy activation');
 -- Remove the legacy lock only within this atomic activation; opening charges still use the usual deduplication.
 UPDATE dbo.Students SET LegacySetupPending=0 WHERE LegacySetupPending=1;
 UPDATE E SET EnrollmentDate=R.StartDate,BillingStartDate=R.BillingStart,BillingReady=1,PlacementConfirmedAt=COALESCE(E.PlacementConfirmedAt,SYSDATETIME()),UpdatedAt=SYSDATETIME()
 FROM dbo.Enrollments E JOIN #Ready R ON R.EnrollmentId=E.EnrollmentId;
 DECLARE @Enrollment int;
 DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT EnrollmentId FROM #Ready;
 OPEN C;FETCH NEXT FROM C INTO @Enrollment;
 WHILE @@FETCH_STATUS=0 BEGIN EXEC dbo.PostEnrollmentOpeningCharges @Enrollment;FETCH NEXT FROM C INTO @Enrollment;END;
 CLOSE C;DEALLOCATE C;
 UPDATE dbo.LegacyAdmissionSetup SET CompletedAt=SYSDATETIME(),CompletedBy=@UserId,GoLiveDate=@GoLiveDate,BillingStart=@BillingStart WHERE SetupId=1;
 SELECT @New=(SELECT @GoLiveDate GoLiveDate,@BillingStart BillingStart,@PlacementStart PlacementStart,(SELECT * FROM #Ready FOR JSON PATH) Placements FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N'LegacyAdmissionSetup',@Key,N'UPDATE',@Old,@New;
 COMMIT;
END;
GO

-- AdmitStudent
CREATE OR ALTER PROCEDURE dbo.AdmitStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @QuickRegistration bit=0, @FamilyStudentId int=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts)
        EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @AdmissionDate < @YearStartDate
       OR @AdmissionDate > @YearEndDate
        THROW 51022, 'The admission date must fall within the selected academic year''s date range.', 1;
    DECLARE @MissingMandatoryFees AS NVARCHAR (1000);
    SELECT @MissingMandatoryFees = STRING_AGG(FT.FeeTypeName, N', ')
    FROM   dbo.FeeTypes AS FT
    WHERE  FT.IsMandatory = 1
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.FeeConfigurations AS FC
                           WHERE  FC.CampusId = @AdmissionCampusId
                                  AND FC.ClassId = @ClassId
                                  AND FC.FeeTypeId = FT.FeeTypeId);
    IF @MissingMandatoryFees IS NOT NULL
        BEGIN
            DECLARE @MandatoryFeeErrorMsg AS NVARCHAR (1100) = N'Required fee configuration is missing for the selected class: ' + @MissingMandatoryFees + N'.';
            THROW 51019, @MandatoryFeeErrorMsg, 1;
        END
    DECLARE @CutoffDay AS INT;
    SELECT TOP (1) @CutoffDay = FeeProrationCutoffDay
    FROM   dbo.SchoolProfile;
    DECLARE @BillingStartDate AS DATE;
    SET @BillingStartDate = CASE WHEN @CutoffDay BETWEEN 1 AND 28
                                      AND DAY(@AdmissionDate) >= @CutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(@AdmissionDate), MONTH(@AdmissionDate), 1)) ELSE @AdmissionDate END;
    BEGIN TRANSACTION;
    IF @QuickRegistration=0 EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT; ELSE SET @AdmissionNumber=NULL;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    IF @QuickRegistration=0 EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF @QuickRegistration=1 UPDATE dbo.Students SET QuickReference=CONCAT('QR-',@AdmissionId) WHERE AdmissionId=@AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, BillingReady, PlacementConfirmedAt)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @AdmissionDate, @BillingStartDate, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, @UsesTransport, N'Created by admission',1,CASE WHEN @QuickRegistration=0 THEN SYSDATETIME() END);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT @EnrollmentId,
           @AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           N'Created by admission'
    FROM   dbo.FeeConfigurations AS FC
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FC.CampusId = @AdmissionCampusId
           AND FC.ClassId = @ClassId
           AND (FT.IsMandatory = 1
                OR (FT.IsTransportLinked = 1
                    AND @UsesTransport = 1)
                OR FC.FeeTypeId IN (SELECT FeeTypeId
                                    FROM   @SelectedFeeTypeIds));
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts AS D
                      LEFT OUTER JOIN
                      dbo.StudentFeeAssignments AS SFA
                      ON SFA.EnrollmentId = @EnrollmentId
                         AND SFA.FeeTypeId = D.FeeTypeId
               WHERE  SFA.StudentFeeAssignmentId IS NULL
                      OR D.DiscountAmount > SFA.Amount)
        THROW 51024, 'A fee discount is invalid or exceeds the assigned fee.', 1;
    UPDATE SFA
    SET    DiscountAmount = D.DiscountAmount,
           UpdatedAt      = SYSDATETIME()
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           @AdmissionDiscounts AS D
           ON D.FeeTypeId = SFA.FeeTypeId
    WHERE  SFA.EnrollmentId = @EnrollmentId;
    IF @FamilyStudentId IS NOT NULL EXEC dbo.AddStudentSibling @AdmissionId,@FamilyStudentId,N'Sibling',@UserId;
    EXEC dbo.PostEnrollmentOpeningCharges @EnrollmentId;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Initial placement created', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, CASE WHEN @QuickRegistration=1 THEN N'Pending' ELSE N'Active' END, N'Admission', @AdmissionDate, N'Initial student status', @UserId);
    DECLARE @OperationId AS UNIQUEIDENTIFIER;
    SET @OperationId = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ADMISSION', CAST (@AdmissionId AS NVARCHAR (200)));
    DECLARE @AuditRecordId AS NVARCHAR (200);
    SET @AuditRecordId = CAST (@AdmissionId AS NVARCHAR (200));
    DECLARE @AuditValues AS NVARCHAR (MAX);
    SELECT @AuditValues = (SELECT @AdmissionId AS AdmissionId,
                                  @EnrollmentId AS EnrollmentId,
                                  @RegistrationNumber AS RegistrationNumber,
                                  @AdmissionDate AS AdmissionDate,
                                  @BillingStartDate AS BillingStartDate,
                                  @AdmissionCampusId AS AdmissionCampusId,
                                  @AcademicYearId AS AcademicYearId,
                                  @ClassSectionId AS ClassSectionId,
                                  N'Active' AS Status
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber, (SELECT QuickReference FROM dbo.Students WHERE AdmissionId=@AdmissionId) QuickReference;
END




GO

-- PostEnrollmentOpeningCharges
CREATE OR ALTER PROCEDURE dbo.PostEnrollmentOpeningCharges
@EnrollmentId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @BillingStartDate AS DATE, @ChargeDate AS DATE, @Status AS NVARCHAR (30), @BillingReady AS BIT;
    SELECT @BillingStartDate = ISNULL(E.BillingStartDate, E.EnrollmentDate),
           @ChargeDate = COALESCE (CONVERT (DATE, E.ReturnConfirmedAt), E.EnrollmentDate),
           @Status = E.Status,
           @BillingReady = E.BillingReady
    FROM   dbo.Enrollments AS E
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @BillingStartDate IS NULL
        THROW 59401, 'The enrollment does not exist.', 1;
    IF @Status NOT IN(N'Active',N'Pending')
       OR @BillingReady <> 1
        THROW 59402, 'Confirm admission before posting opening charges.', 1;
    DECLARE @OpeningMonth AS DATE = DATEFROMPARTS(YEAR(@BillingStartDate), MONTH(@BillingStartDate), 1);
    IF @@TRANCOUNT > 0
        BEGIN
            DECLARE @LockResult AS INT;
            DECLARE @LockResource AS NVARCHAR (255) = N'FMSC.GenerateEnrollmentCharges.' + CONVERT (NVARCHAR (20), @EnrollmentId);
            EXECUTE @LockResult = sys.sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @LockResult < 0
                THROW 59403, 'Charges for this enrollment are already being posted.', 1;
        END
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @ChargeDate,
           @ChargeDate,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Annual'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Yearly'
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.ChargeKind = N'Annual'
                                  );
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @OpeningMonth,
           @OpeningMonth,
           @OpeningMonth,
           SFA.Amount,
           SFA.DiscountAmount,
           N'Monthly'
    FROM   dbo.StudentFeeAssignments AS SFA
    WHERE  SFA.EnrollmentId = @EnrollmentId
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly'
           AND (SFA.EffectiveFrom IS NULL
                OR @OpeningMonth >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @OpeningMonth <= SFA.EffectiveTo)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = @EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @OpeningMonth
                                  AND BC.ChargeKind = N'Monthly'
                                  );
END



GO
CREATE OR ALTER TRIGGER dbo.GuardClosedEnrollmentCharges ON dbo.BillingCharges AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- Unpaid charge cancellation is allowed through the audited reversal workflow; closed bills cannot be rewritten.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status NOT IN(N'Active',N'Pending') AND (D.BillingChargeId IS NULL OR I.EnrollmentId<>D.EnrollmentId
    OR I.StudentFeeAssignmentId<>D.StudentFeeAssignmentId OR I.AdmissionId<>D.AdmissionId OR I.FeeTypeId<>D.FeeTypeId
    OR I.GrossAmount<>D.GrossAmount OR I.DiscountAmount<>D.DiscountAmount
    OR ISNULL(I.BillingMonth,'19000101')<>ISNULL(D.BillingMonth,'19000101')
    OR I.ChargeDate<>D.ChargeDate OR I.DueDate<>D.DueDate OR I.ChargeKind<>D.ChargeKind
    OR (I.Status<>D.Status AND D.Status=N'Cancelled')))
  THROW 57102,'This enrollment is closed. Its charges cannot be posted, changed or reversed here. Outstanding fees can still be collected.',1;
 IF EXISTS(SELECT 1 FROM deleted D LEFT JOIN inserted I ON I.BillingChargeId=D.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=D.EnrollmentId WHERE I.BillingChargeId IS NULL AND E.Status NOT IN(N'Active',N'Pending'))
  THROW 57102,'Closed enrollment charges must remain in history.',1;
 -- New bills must fit the enrollment and academic year, including backdated closures.
 IF EXISTS(SELECT 1 FROM inserted I LEFT JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
    JOIN dbo.Enrollments E ON E.EnrollmentId=I.EnrollmentId
    JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE D.BillingChargeId IS NULL AND I.Status<>N'Cancelled' AND ((E.PlacementConfirmedAt IS NULL AND E.Status<>N'Pending') OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=E.AdmissionId AND S.LegacySetupPending=1) OR 
      COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)
      OR COALESCE(I.BillingMonth,I.ChargeDate)<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
      OR (E.Status=N'Pending' AND COALESCE(I.BillingMonth,I.ChargeDate)<>DATEFROMPARTS(YEAR(COALESCE(E.BillingStartDate,E.EnrollmentDate)),MONTH(COALESCE(E.BillingStartDate,E.EnrollmentDate)),1)) OR (Y.EndDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1))
      OR (E.LeavingDate IS NOT NULL AND COALESCE(I.BillingMonth,I.ChargeDate)>DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1))))
    THROW 59633,'The charge period is outside the enrollment or academic year. Correct placement or billing dates in Admission before posting.',1;
 -- Protect payments even if an alternative reversal entry point bypasses the form.
 IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId
   WHERE I.Status=N'Cancelled' AND D.Status<>N'Cancelled' AND EXISTS(
    SELECT 1 FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.BillingChargeId=I.BillingChargeId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0))
  THROW 57103,'A paid or partially paid charge cannot be cancelled. Use the payment correction or refund workflow.',1;
END;

GO


-- GetStudents
CREATE OR ALTER PROCEDURE dbo.GetStudents
@UserId INT, @CampusId INT=0, @AcademicYearId INT=0, @ClassId INT=0, @ClassSectionId INT=0, @Search NVARCHAR (200)=NULL, @IncludeFormerStudents BIT=0, @ProgramType NVARCHAR (20)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    SELECT   S.AdmissionId,
             COALESCE(S.AdmissionNumber,S.QuickReference) AdmissionNumber,
             S.RegistrationNumber,
             S.StudentName,
             S.FatherName,
             S.DateOfBirth,
             S.AdmissionDate,
             S.AdmissionCampusId,
             AC.CampusName AS AdmissionCampusName,
             S.Status,
             E.EnrollmentId,
             E.AcademicYearId,
             AY.YearName,
             AY.EndDate AS AcademicYearEndDate,
             E.ClassSectionId,
             C.ClassId,
             C.ClassName,
             SEC.SectionId,
             SEC.SectionName,
             COALESCE (CCS.CampusId, S.AdmissionCampusId) AS CurrentCampusId,
             COALESCE (CC.CampusName, AC.CampusName) AS CurrentCampusName,
             E.Status AS EnrollmentStatus,
             C.AcademicProgramId,
             AP.ProgramType
    FROM     dbo.Students AS S
             INNER JOIN
             dbo.Campuses AS AC
             ON AC.CampusId = S.AdmissionCampusId OUTER APPLY (SELECT   TOP (1) E1.*
                                                               FROM     dbo.Enrollments AS E1
                                                               WHERE    E1.AdmissionId = S.AdmissionId
                                                               ORDER BY CASE WHEN E1.Status = N'Active' THEN 0 ELSE 1 END, E1.EnrollmentDate DESC, E1.EnrollmentId DESC) AS E
             LEFT OUTER JOIN
             dbo.AcademicYears AS AY
             ON AY.AcademicYearId = E.AcademicYearId
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Campuses AS CC
             ON CC.CampusId = CCS.CampusId
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             LEFT OUTER JOIN
             dbo.AcademicPrograms AS AP
             ON AP.AcademicProgramId = C.AcademicProgramId
    WHERE    (@CampusId = 0
              OR COALESCE (CCS.CampusId, S.AdmissionCampusId) = @CampusId)
             AND (@AcademicYearId = 0
                  OR E.AcademicYearId = @AcademicYearId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@ClassSectionId = 0
                  OR E.ClassSectionId = @ClassSectionId)
             AND (@IncludeFormerStudents = 1
                  OR S.Status = N'Active')
             AND (NULLIF (LTRIM(RTRIM(@ProgramType)), N'') IS NULL
                  OR AP.ProgramType = @ProgramType)
             AND (NULLIF (LTRIM(RTRIM(@Search)), N'') IS NULL
                  OR S.StudentName LIKE N'%' + LTRIM(RTRIM(@Search)) + N'%'
                  OR S.FatherName LIKE N'%' + LTRIM(RTRIM(@Search)) + N'%'
                  OR S.RegistrationNumber LIKE N'%' + LTRIM(RTRIM(@Search)) + N'%'
                  OR S.QuickReference = LTRIM(RTRIM(@Search)) OR S.AdmissionNumber = LTRIM(RTRIM(@Search)))
    ORDER BY S.AdmissionNumberBase, S.AdmissionNumberSuffix, S.AdmissionId;
END



GO

-- SaveStudentDetailsWithFirstEnrollment
CREATE OR ALTER PROCEDURE dbo.SaveStudentDetailsWithFirstEnrollment
@UserId INT, @AdmissionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500)=NULL, @DateOfBirth DATE=NULL, @Gender NVARCHAR (20)=NULL, @BFormNumber NVARCHAR (50)=NULL, @Religion NVARCHAR (50)=NULL, @BloodGroup NVARCHAR (10)=NULL, @ContactNumber NVARCHAR (30)=NULL, @AlternateContactNumber NVARCHAR (30)=NULL, @Address NVARCHAR (500)=NULL, @PreviousSchool NVARCHAR (500)=NULL, @PlaceOfBirth NVARCHAR (500)=NULL, @FatherOccupation NVARCHAR (250)=NULL, @Disability NVARCHAR (3)=N'No', @UsesTransport BIT=NULL, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @Discounts dbo.StudentFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @CreateFirstEnrollment BIT=0, @PlacementCampusId INT=NULL, @PlacementAcademicYearId INT=NULL, @PlacementClassSectionId INT=NULL, @PlacementEnrollmentDate DATE=NULL, @PlacementStatus NVARCHAR (30)=NULL, @PlacementLeavingDate DATE=NULL, @PlacementUsesTransport BIT=0, @CompleteRegistration bit=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51710, 'The selected student was not found.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts
               WHERE  DiscountAmount < 0)
        THROW 51711, 'A fee discount cannot be negative. Nothing was changed.', 1;
    IF EXISTS (SELECT 1
               FROM   @Discounts AS D
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.StudentFeeAssignments AS SFA
                                         LEFT OUTER JOIN
                                         dbo.Enrollments AS E
                                         ON E.EnrollmentId = SFA.EnrollmentId
                                  WHERE  SFA.StudentFeeAssignmentId = D.StudentFeeAssignmentId
                                         AND (SFA.AdmissionId = @AdmissionId
                                              OR E.AdmissionId = @AdmissionId)))
        THROW 51712, 'A fee discount in this save does not belong to the student being saved. Nothing was changed.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Pending') EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer'; ELSE EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        DECLARE @StoredStatus AS NVARCHAR (30), @StoredAdmissionDate AS DATE, @StoredWithdrawalDate AS DATE;
        SELECT @StoredStatus = Status,
               @StoredAdmissionDate = AdmissionDate,
               @StoredWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @AdmissionId;
        IF @StoredStatus IS NULL
            THROW 51710, 'The selected student was not found.', 1;
        IF @CreateFirstEnrollment = 1
            BEGIN
                EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
                IF EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @AdmissionId)
                    THROW 51720, 'This student already has an enrollment. Reload the record and use enrollment corrections.', 1;
                IF @PlacementCampusId IS NULL
                   OR @PlacementAcademicYearId IS NULL
                   OR @PlacementClassSectionId IS NULL
                    THROW 51721, 'Confirm the campus, academic year, class and section.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.Campuses
                               WHERE  CampusId = @PlacementCampusId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.AcademicYears
                                  WHERE  AcademicYearId = @PlacementAcademicYearId)
                   OR NOT EXISTS (SELECT 1
                                  FROM   dbo.CampusClassSections
                                  WHERE  ClassSectionId = @PlacementClassSectionId
                                         AND CampusId = @PlacementCampusId)
                    THROW 51721, 'The verified placement is not a valid campus, year and class section.', 1;
                IF @PlacementEnrollmentDate IS NULL
                   OR @PlacementEnrollmentDate < @StoredAdmissionDate
                   OR @PlacementEnrollmentDate > CONVERT (DATE, SYSDATETIME())
                    THROW 51722, 'Enter the confirmed enrollment date, on or after admission and not in the future.', 1;
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.AcademicYears
                               WHERE  AcademicYearId = @PlacementAcademicYearId
                                      AND @PlacementEnrollmentDate BETWEEN StartDate AND EndDate)
                    THROW 51722, 'The enrollment date must fall within the selected academic year.', 1;
                IF @PlacementStatus IS NULL
                   OR @PlacementStatus NOT IN (N'Active', N'Left')
                    THROW 51723, 'Choose Active or Left for the verified enrollment.', 1;
                IF @StoredStatus = N'Left'
                   AND @PlacementStatus <> N'Left'
                    THROW 51723, 'An already-left student needs a historical Left enrollment. Use Re-Admit to return them.', 1;
                IF @PlacementStatus = N'Active'
                   AND (@PlacementLeavingDate IS NOT NULL
                        OR @StoredWithdrawalDate IS NOT NULL)
                    THROW 51724, 'An active enrollment cannot have a withdrawal date.', 1;
                IF @StoredWithdrawalDate IS NOT NULL
                   AND @PlacementLeavingDate IS NOT NULL
                   AND @StoredWithdrawalDate <> @PlacementLeavingDate
                    THROW 51724, 'The confirmed withdrawal date differs from the stored date. Correct the stored date through the status workflow first.', 1;
                SET @PlacementLeavingDate = COALESCE (@PlacementLeavingDate, @StoredWithdrawalDate);
                IF @PlacementLeavingDate IS NOT NULL
                   AND (@PlacementLeavingDate < @PlacementEnrollmentDate
                        OR @PlacementLeavingDate > CONVERT (DATE, SYSDATETIME()))
                    THROW 51724, 'Withdrawal date must be on or after enrollment and not in the future.', 1;
                IF EXISTS (SELECT 1
                           FROM   @Discounts)
                    THROW 51725, 'First placement cannot include discounts from another enrollment.', 1;
            END
        IF @AdmissionNumber IS NOT NULL AND @CompleteRegistration=0
            EXECUTE dbo.SetStudentAdmissionNumber @UserId, @AdmissionId, @AdmissionNumber;
        EXECUTE dbo.SaveStudentDetails @UserId = @UserId, @AdmissionId = @AdmissionId, @StudentName = @StudentName, @FatherName = @FatherName, @DateOfBirth = @DateOfBirth, @Gender = @Gender, @BFormNumber = @BFormNumber, @Religion = @Religion, @BloodGroup = @BloodGroup, @ContactNumber = @ContactNumber, @AlternateContactNumber = @AlternateContactNumber, @Address = @Address, @PreviousSchool = @PreviousSchool, @PlaceOfBirth = @PlaceOfBirth, @FatherOccupation = @FatherOccupation, @Disability = @Disability, @UsesTransport = @UsesTransport, @BiseEnrolmentNumber = @BiseEnrolmentNumber, @CasteTribe = @CasteTribe;
        IF EXISTS (SELECT 1
                   FROM   @Discounts)
            BEGIN
                DECLARE @StudentFeeAssignmentId AS INT, @DiscountAmount AS DECIMAL (12, 2);
                DECLARE DiscountRows CURSOR LOCAL FAST_FORWARD
                    FOR SELECT   StudentFeeAssignmentId,
                                 DiscountAmount
                        FROM     @Discounts
                        ORDER BY StudentFeeAssignmentId;
                OPEN DiscountRows;
                FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                WHILE @@FETCH_STATUS = 0
                    BEGIN
                        EXECUTE dbo.SaveStudentFeeDiscount @UserId = @UserId, @StudentFeeAssignmentId = @StudentFeeAssignmentId, @DiscountAmount = @DiscountAmount;
                        FETCH NEXT FROM DiscountRows INTO @StudentFeeAssignmentId, @DiscountAmount;
                    END
                CLOSE DiscountRows;
                DEALLOCATE DiscountRows;
            END
        IF @CreateFirstEnrollment = 1
            BEGIN
                DECLARE @NewEnrollmentId AS INT, @PlacementOperationId AS UNIQUEIDENTIFIER = NEWID();
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, UsesTransport, Remarks, BillingStartDate, BillingReady)
                VALUES                 (@AdmissionId, @PlacementAcademicYearId, @PlacementClassSectionId, @PlacementEnrollmentDate, @PlacementLeavingDate, @PlacementStatus, @PlacementUsesTransport, N'First placement verified from physical school record on Admission form; no fees generated.', NULL, 0);
                SET @NewEnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, NewClassSectionId, NewStatus, Remarks, UserId)
                VALUES                       (@NewEnrollmentId, @AdmissionId, N'VERIFIED_FIRST_PLACEMENT', CONVERT (DATE, SYSDATETIME()), @PlacementClassSectionId, @PlacementStatus, N'Confirmed from physical school record.', @UserId);
                IF @StoredStatus <> @PlacementStatus
                   OR ISNULL(@StoredWithdrawalDate, '00010101') <> ISNULL(@PlacementLeavingDate, '00010101')
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = @PlacementStatus,
                               WithdrawalDate = @PlacementLeavingDate,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @StoredStatus, @PlacementStatus, N'Physical record verification', COALESCE (@PlacementLeavingDate, @PlacementEnrollmentDate), N'First verified enrollment recorded on Admission form.', @UserId);
                    END
                INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                VALUES                     (@PlacementOperationId, @UserId, N'VERIFIED_FIRST_PLACEMENT', CONVERT (NVARCHAR (200), @AdmissionId));
                DECLARE @PlacementNewValues AS NVARCHAR (MAX), @PlacementRecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @NewEnrollmentId);
                SET @PlacementNewValues = (SELECT @AdmissionId AS AdmissionId,
                                                  @NewEnrollmentId AS EnrollmentId,
                                                  @PlacementAcademicYearId AS AcademicYearId,
                                                  @PlacementClassSectionId AS ClassSectionId,
                                                  @PlacementEnrollmentDate AS EnrollmentDate,
                                                  @PlacementStatus AS Status,
                                                  @PlacementLeavingDate AS LeavingDate,
                                                  CAST (0 AS BIT) AS BillingReady
                                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                EXECUTE dbo.WriteAuditLog @OperationId = @PlacementOperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @PlacementRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @PlacementNewValues;
                IF @StoredStatus <> @PlacementStatus
                   OR ISNULL(@StoredWithdrawalDate, '00010101') <> ISNULL(@PlacementLeavingDate, '00010101')
                    BEGIN
                        DECLARE @StudentOld AS NVARCHAR (MAX), @StudentNew AS NVARCHAR (MAX), @StudentRecord AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @AdmissionId);
                        SET @StudentOld = (SELECT @StoredStatus AS Status,
                                                  @StoredWithdrawalDate AS WithdrawalDate
                                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        SET @StudentNew = (SELECT @PlacementStatus AS Status,
                                                  @PlacementLeavingDate AS WithdrawalDate
                                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        EXECUTE dbo.WriteAuditLog @OperationId = @PlacementOperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @StudentRecord, @Action = N'UPDATE', @OldValues = @StudentOld, @NewValues = @StudentNew;
                    END
            END
        DELETE dbo.AuditOperations WHERE OperationId=@PlacementOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@PlacementOperationId);
IF @CompleteRegistration=1 EXEC dbo.CompleteQuickAdmission @UserId,@AdmissionId,@AdmissionNumber;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
    SELECT @AdmissionId AS AdmissionId;
END



GO
CREATE OR ALTER TRIGGER dbo.GuardFeeAssignmentPeriods ON dbo.StudentFeeAssignments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted WHERE CancelledAt IS NULL AND EffectiveFrom>EffectiveTo)
  THROW 57104,'A fee period cannot end before it starts.',1;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status NOT IN(N'Active',N'Pending')) OR EXISTS(SELECT 1 FROM deleted D JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=D.EnrollmentId WHERE E.Status NOT IN(N'Active',N'Pending'))
  THROW 57105,'This enrollment is closed. Its fee assignments are preserved as history.',1;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK) ON A.EnrollmentId=I.EnrollmentId AND A.FeeTypeId=I.FeeTypeId
   AND A.StudentFeeAssignmentId<>I.StudentFeeAssignmentId WHERE I.CancelledAt IS NULL AND A.CancelledAt IS NULL
   AND ISNULL(I.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231')
   AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(I.EffectiveTo,'99991231'))
  THROW 57106,'Fee periods overlap. Review the existing periods before applying this change.',1;
END;

GO

/* ================================================================
   40. DATA-INTEGRITY TRIGGERS FOR CROSS-CONTEXT RULES
   ================================================================ */
CREATE OR ALTER TRIGGER dbo.TR_Students_RegistrationNumberImmutable
ON dbo.Students
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(RegistrationNumber)
       AND EXISTS
       (
           SELECT 1
           FROM inserted I
           INNER JOIN deleted D ON D.AdmissionId=I.AdmissionId
           WHERE ISNULL(I.RegistrationNumber,N'')<>ISNULL(D.RegistrationNumber,N'') AND NOT(D.Status=N'Pending' AND D.RegistrationNumber IS NULL AND I.Status=N'Active')
       )
    BEGIN
        THROW 59101,'Registration Number is a permanent reference and cannot be changed after admission.',1;
    END;
END;

GO

-- Canonical single-student details, status and current placement read.

CREATE OR ALTER PROCEDURE dbo.GetStudentDetails
(
    @UserId int,
    @AdmissionId int
)
AS

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.View';

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Students
        WHERE AdmissionId = @AdmissionId
    )
        THROW 51700, 'The selected student was not found.', 1;

    SELECT
        S.AdmissionId,
        S.AdmissionNumber,
        S.RegistrationNumber, S.QuickReference, S.LegacySetupPending, S.LegacyVerifiedAt,
        S.BiseEnrolmentNumber,
        S.CasteTribe,
        S.StudentName,
        S.FatherName,
        S.DateOfBirth,
        S.Gender,
        S.BFormNumber,
        S.Religion,
        S.BloodGroup,
        S.ContactNumber,
        S.AlternateContactNumber,
        S.Address,
        S.PreviousSchool,
        S.PlaceOfBirth,
        S.FatherOccupation,
        S.Disability,
        S.Status,
        S.WithdrawalDate,
        S.AdmissionDate,

        S.AdmissionCampusId,
        AC.CampusName AS AdmissionCampusName,

        E.EnrollmentId,
        E.EnrollmentDate,
        E.AcademicYearId,
        AY.YearName,
        E.ClassSectionId,
        E.UsesTransport,
        E.Status AS EnrollmentStatus,

        CCS.CampusId AS CurrentCampusId,
        CC.CampusName AS CurrentCampusName,

        C.ClassId,
        C.ClassCode,
        C.ClassName,

        SEC.SectionId,
        SEC.SectionName

    FROM dbo.Students AS S

    INNER JOIN dbo.Campuses AS AC
        ON AC.CampusId = S.AdmissionCampusId

    OUTER APPLY
    (
        SELECT TOP (1) *
        FROM dbo.Enrollments AS EE
        WHERE EE.AdmissionId = S.AdmissionId
        ORDER BY
            CASE WHEN EE.Status = N'Active' THEN 0 ELSE 1 END,
            EE.EnrollmentDate DESC,
            EE.EnrollmentId DESC
    ) AS E

    LEFT JOIN dbo.AcademicYears AS AY
        ON AY.AcademicYearId = E.AcademicYearId


    LEFT JOIN dbo.CampusClassSections AS CCS
        ON CCS.ClassSectionId = E.ClassSectionId

    LEFT JOIN dbo.Campuses AS CC
        ON CC.CampusId = CCS.CampusId

    LEFT JOIN dbo.Classes AS C
        ON C.ClassId = CCS.ClassId

    LEFT JOIN dbo.Sections AS SEC
        ON SEC.SectionId = CCS.SectionId

    WHERE S.AdmissionId = @AdmissionId;
END;


GO


-- SaveStudentDetails
CREATE OR ALTER PROCEDURE dbo.SaveStudentDetails
@UserId INT, @AdmissionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500)=NULL, @DateOfBirth DATE=NULL, @Gender NVARCHAR (20)=NULL, @BFormNumber NVARCHAR (50)=NULL, @Religion NVARCHAR (50)=NULL, @BloodGroup NVARCHAR (10)=NULL, @ContactNumber NVARCHAR (30)=NULL, @AlternateContactNumber NVARCHAR (30)=NULL, @Address NVARCHAR (500)=NULL, @PreviousSchool NVARCHAR (500)=NULL, @PlaceOfBirth NVARCHAR (500)=NULL, @FatherOccupation NVARCHAR (250)=NULL, @Disability NVARCHAR (3)=N'No', @UsesTransport BIT=NULL, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND Status=N'Pending') EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer'; ELSE EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51701, 'The selected student was not found.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51702, 'Student name is required.', 1;
    DECLARE @AdmissionDate AS DATE;
    SELECT @AdmissionDate = AdmissionDate
    FROM   dbo.Students
    WHERE  AdmissionId = @AdmissionId;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51703, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability IS NULL
       OR @Disability NOT IN (N'Yes', N'No')
        THROW 51704, 'Disability must be Yes or No.', 1;
    DECLARE @OldStudentName AS NVARCHAR (500), @OldFatherName AS NVARCHAR (500), @OldDateOfBirth AS DATE, @OldGender AS NVARCHAR (20), @OldBFormNumber AS NVARCHAR (50), @OldReligion AS NVARCHAR (50), @OldBloodGroup AS NVARCHAR (10), @OldContactNumber AS NVARCHAR (30), @OldAlternateContactNumber AS NVARCHAR (30), @OldAddress AS NVARCHAR (500), @OldPreviousSchool AS NVARCHAR (500), @OldPlaceOfBirth AS NVARCHAR (500), @OldFatherOccupation AS NVARCHAR (250), @OldDisability AS NVARCHAR (3), @OldBiseEnrolmentNumber AS NVARCHAR (50), @OldCasteTribe AS NVARCHAR (100);
    SELECT @OldStudentName = StudentName,
           @OldFatherName = FatherName,
           @OldDateOfBirth = DateOfBirth,
           @OldGender = Gender,
           @OldBFormNumber = BFormNumber,
           @OldReligion = Religion,
           @OldBloodGroup = BloodGroup,
           @OldContactNumber = ContactNumber,
           @OldAlternateContactNumber = AlternateContactNumber,
           @OldAddress = Address,
           @OldPreviousSchool = PreviousSchool,
           @OldPlaceOfBirth = PlaceOfBirth,
           @OldFatherOccupation = FatherOccupation,
           @OldDisability = Disability,
           @OldBiseEnrolmentNumber = BiseEnrolmentNumber,
           @OldCasteTribe = CasteTribe
    FROM   dbo.Students
    WHERE  AdmissionId = @AdmissionId;
    DECLARE @NewStudentName AS NVARCHAR (500), @NewFatherName AS NVARCHAR (500), @NewBFormNumber AS NVARCHAR (50), @NewContactNumber AS NVARCHAR (30), @NewAlternateContactNumber AS NVARCHAR (30), @NewAddress AS NVARCHAR (500), @NewPreviousSchool AS NVARCHAR (500), @NewPlaceOfBirth AS NVARCHAR (500), @NewFatherOccupation AS NVARCHAR (250), @NewBiseEnrolmentNumber AS NVARCHAR (50), @NewCasteTribe AS NVARCHAR (100);
    SET @NewStudentName = LTRIM(RTRIM(@StudentName));
    SET @NewFatherName = NULLIF (LTRIM(RTRIM(@FatherName)), N'');
    SET @NewBFormNumber = NULLIF (LTRIM(RTRIM(@BFormNumber)), N'');
    SET @NewContactNumber = NULLIF (LTRIM(RTRIM(@ContactNumber)), N'');
    SET @NewAlternateContactNumber = NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N'');
    SET @NewAddress = NULLIF (LTRIM(RTRIM(@Address)), N'');
    SET @NewPreviousSchool = NULLIF (LTRIM(RTRIM(@PreviousSchool)), N'');
    SET @NewPlaceOfBirth = NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N'');
    SET @NewFatherOccupation = NULLIF (LTRIM(RTRIM(@FatherOccupation)), N'');
    SET @NewBiseEnrolmentNumber = NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N'');
    SET @NewCasteTribe = NULLIF (LTRIM(RTRIM(@CasteTribe)), N'');
    DECLARE @StudentChanged AS BIT = 0;
    IF ISNULL(@OldStudentName, N'') <> ISNULL(@NewStudentName, N'')
       OR ISNULL(@OldFatherName, N'') <> ISNULL(@NewFatherName, N'')
       OR ISNULL(@OldDateOfBirth, '19000101') <> ISNULL(@DateOfBirth, '19000101')
       OR ISNULL(@OldGender, N'') <> ISNULL(@Gender, N'')
       OR ISNULL(@OldBFormNumber, N'') <> ISNULL(@NewBFormNumber, N'')
       OR ISNULL(@OldReligion, N'') <> ISNULL(@Religion, N'')
       OR ISNULL(@OldBloodGroup, N'') <> ISNULL(@BloodGroup, N'')
       OR ISNULL(@OldContactNumber, N'') <> ISNULL(@NewContactNumber, N'')
       OR ISNULL(@OldAlternateContactNumber, N'') <> ISNULL(@NewAlternateContactNumber, N'')
       OR ISNULL(@OldAddress, N'') <> ISNULL(@NewAddress, N'')
       OR ISNULL(@OldPreviousSchool, N'') <> ISNULL(@NewPreviousSchool, N'')
       OR ISNULL(@OldPlaceOfBirth, N'') <> ISNULL(@NewPlaceOfBirth, N'')
       OR ISNULL(@OldFatherOccupation, N'') <> ISNULL(@NewFatherOccupation, N'')
       OR ISNULL(@OldDisability, N'') <> ISNULL(@Disability, N'')
       OR ISNULL(@OldBiseEnrolmentNumber, N'') <> ISNULL(@NewBiseEnrolmentNumber, N'')
       OR ISNULL(@OldCasteTribe, N'') <> ISNULL(@NewCasteTribe, N'')
        BEGIN
            SET @StudentChanged = 1;
        END
    DECLARE @EnrollmentId AS INT = NULL, @OldUsesTransport AS BIT = NULL;
    SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                     @OldUsesTransport = E.UsesTransport
    FROM     dbo.Enrollments AS E
    WHERE    E.AdmissionId = @AdmissionId
    ORDER BY CASE WHEN E.Status = N'Active' THEN 0 ELSE 1 END, E.EnrollmentDate DESC, E.EnrollmentId DESC;
    DECLARE @TransportChanged AS BIT = 0;
    IF @UsesTransport IS NOT NULL
       AND @EnrollmentId IS NOT NULL
       AND (@OldUsesTransport IS NULL
            OR @OldUsesTransport <> @UsesTransport)
        BEGIN
            SET @TransportChanged = 1;
        END
    IF @StudentChanged = 0
       AND @TransportChanged = 0
        BEGIN
            SELECT @AdmissionId AS AdmissionId;
            RETURN;
        END
    BEGIN TRANSACTION;
    IF @StudentChanged = 1
        BEGIN
            UPDATE dbo.Students
            SET    StudentName            = @NewStudentName,
                   FatherName             = @NewFatherName,
                   DateOfBirth            = @DateOfBirth,
                   Gender                 = @Gender,
                   BFormNumber            = @NewBFormNumber,
                   Religion               = @Religion,
                   BloodGroup             = @BloodGroup,
                   ContactNumber          = @NewContactNumber,
                   AlternateContactNumber = @NewAlternateContactNumber,
                   Address                = @NewAddress,
                   PreviousSchool         = @NewPreviousSchool,
                   PlaceOfBirth           = @NewPlaceOfBirth,
                   FatherOccupation       = @NewFatherOccupation,
                   Disability             = @Disability,
                   BiseEnrolmentNumber    = @NewBiseEnrolmentNumber,
                   CasteTribe             = @NewCasteTribe,
                   UpdatedAt              = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId;
            IF @@ROWCOUNT = 0
                THROW 51705, 'The selected student record was not found.', 1;
        END
    IF @TransportChanged = 1
        BEGIN
            UPDATE dbo.Enrollments
            SET    UsesTransport = @UsesTransport,
                   UpdatedAt     = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId;
            IF @@ROWCOUNT = 0
                THROW 51707, 'The student enrollment could not be updated.', 1;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'STUDENT_UPDATE', CONVERT (NVARCHAR (200), @AdmissionId));
    IF @StudentChanged = 1
        BEGIN
            DECLARE @AuditOldValues AS NVARCHAR (MAX);
            DECLARE @AuditNewValues AS NVARCHAR (MAX);
            DECLARE @AuditRecordId AS NVARCHAR (200);
            SELECT @AuditOldValues = (SELECT @OldStudentName AS StudentName,
                                             @OldFatherName AS FatherName,
                                             @OldDateOfBirth AS DateOfBirth,
                                             @OldGender AS Gender,
                                             @OldBFormNumber AS BFormNumber,
                                             @OldReligion AS Religion,
                                             @OldBloodGroup AS BloodGroup,
                                             @OldContactNumber AS ContactNumber,
                                             @OldAlternateContactNumber AS AlternateContactNumber,
                                             @OldAddress AS Address,
                                             @OldPreviousSchool AS PreviousSchool,
                                             @OldPlaceOfBirth AS PlaceOfBirth,
                                             @OldFatherOccupation AS FatherOccupation,
                                             @OldDisability AS Disability,
                                             @OldBiseEnrolmentNumber AS BiseEnrolmentNumber,
                                             @OldCasteTribe AS CasteTribe
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SELECT @AuditNewValues = (SELECT @AdmissionId AS AdmissionId,
                                             @NewStudentName AS StudentName,
                                             @NewFatherName AS FatherName,
                                             @DateOfBirth AS DateOfBirth,
                                             @Gender AS Gender,
                                             @NewBFormNumber AS BFormNumber,
                                             @Religion AS Religion,
                                             @BloodGroup AS BloodGroup,
                                             @NewContactNumber AS ContactNumber,
                                             @NewAlternateContactNumber AS AlternateContactNumber,
                                             @NewAddress AS Address,
                                             @NewPreviousSchool AS PreviousSchool,
                                             @NewPlaceOfBirth AS PlaceOfBirth,
                                             @NewFatherOccupation AS FatherOccupation,
                                             @Disability AS Disability,
                                             @NewBiseEnrolmentNumber AS BiseEnrolmentNumber,
                                             @NewCasteTribe AS CasteTribe
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SET @AuditRecordId = CONVERT (NVARCHAR (200), @AdmissionId);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AuditRecordId, @Action = N'UPDATE', @OldValues = @AuditOldValues, @NewValues = @AuditNewValues;
        END
    IF @TransportChanged = 1
        BEGIN
            DECLARE @TransportOldValues AS NVARCHAR (MAX), @TransportNewValues AS NVARCHAR (MAX), @TransportAuditRecordId AS NVARCHAR (200);
            SELECT @TransportOldValues = (SELECT @EnrollmentId AS EnrollmentId,
                                                 @AdmissionId AS AdmissionId,
                                                 @OldUsesTransport AS UsesTransport
                                          FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SELECT @TransportNewValues = (SELECT @EnrollmentId AS EnrollmentId,
                                                 @AdmissionId AS AdmissionId,
                                                 @UsesTransport AS UsesTransport
                                          FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SET @TransportAuditRecordId = CONVERT (NVARCHAR (200), @EnrollmentId);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @TransportAuditRecordId, @Action = N'UPDATE', @OldValues = @TransportOldValues, @NewValues = @TransportNewValues;
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId;
END



GO


-- AddStudentFeeAssignment
CREATE OR ALTER PROCEDURE dbo.AddStudentFeeAssignment
@UserId INT, @EnrollmentId INT, @FeeTypeId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId)
        THROW 53110, 'The selected enrollment does not exist.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Enrollments
                   WHERE  EnrollmentId = @EnrollmentId
                          AND (BillingReady = 1 OR PlacementConfirmedAt IS NULL))
        THROW 53114, 'Confirm return admission before adding fees.', 1;
    DECLARE @CampusId AS INT, @ClassId AS INT, @AdmissionId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId;
    DECLARE @FmscCutoffDay AS INT;
    SELECT TOP (1) @FmscCutoffDay = FeeProrationCutoffDay
    FROM   dbo.SchoolProfile;
    DECLARE @FmscOpeningMonth AS DATE = CASE WHEN @FmscCutoffDay BETWEEN 1 AND 28
                                                  AND DAY(GETDATE()) >= @FmscCutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) ELSE DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1) END;
    DECLARE @BillingYearStart date,@BillingYearEnd date,@BillingEnrollmentStart date;
    SELECT @BillingYearStart=Y.StartDate,@BillingYearEnd=Y.EndDate,@BillingEnrollmentStart=E.EnrollmentDate
    FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
    WHERE E.EnrollmentId=@EnrollmentId AND E.Status IN(N'Active',N'Pending');
    IF @BillingYearStart IS NULL THROW 59630,'Correct or select an active enrollment in Admission before adding fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NULL OR EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1)))
        SELECT @FmscOpeningMonth=DATEFROMPARTS(YEAR(COALESCE(BillingStartDate,EnrollmentDate)),MONTH(COALESCE(BillingStartDate,EnrollmentDate)),1)
        FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
    IF @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingYearStart),MONTH(@BillingYearStart),1)
       OR (@BillingYearEnd IS NOT NULL AND @FmscOpeningMonth>DATEFROMPARTS(YEAR(@BillingYearEnd),MONTH(@BillingYearEnd),1))
       OR @FmscOpeningMonth<DATEFROMPARTS(YEAR(@BillingEnrollmentStart),MONTH(@BillingEnrollmentStart),1)
       THROW 59631,'The opening fee month is outside this enrollment. Correct its placement in Admission; use reviewed historical posting for an old year.',1;
    BEGIN TRANSACTION;
    DECLARE @StudentFeeAssignmentId AS INT;
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT TOP (1) StudentFeeAssignmentId,
                                                           EnrollmentId,
                                                           AdmissionId,
                                                           FeeTypeId,
                                                           Amount,
                                                           DiscountAmount,
                                                           Frequency,
                                                           EffectiveFrom,
                                                           EffectiveTo,
                                                           CancelledAt
                                            FROM   dbo.StudentFeeAssignments WITH (UPDLOCK, HOLDLOCK)
                                            WHERE  EnrollmentId = @EnrollmentId
                                                   AND FeeTypeId = @FeeTypeId
                                                   AND EffectiveTo IS NULL
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @AuditAction AS NVARCHAR (20);
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments
               WHERE  EnrollmentId = @EnrollmentId
                      AND FeeTypeId = @FeeTypeId
                      AND EffectiveTo IS NULL)
        BEGIN
            DECLARE @ExistingAssignmentId AS INT;
            SELECT @ExistingAssignmentId = StudentFeeAssignmentId
            FROM   dbo.StudentFeeAssignments
            WHERE  EnrollmentId = @EnrollmentId
                   AND FeeTypeId = @FeeTypeId
                   AND EffectiveTo IS NULL;
            DECLARE @HasBillingHistory AS BIT = CASE WHEN EXISTS (SELECT 1
                                                                  FROM   dbo.BillingCharges
                                                                  WHERE  StudentFeeAssignmentId = @ExistingAssignmentId) THEN 1 ELSE 0 END;
            SET @AuditAction = N'UPDATE';
            IF @HasBillingHistory = 0
                BEGIN
                    UPDATE SFA
                    SET    SFA.CancelledAt    = NULL,
                           SFA.Amount         = FC.Amount,
                           SFA.Frequency      = FC.Frequency,
                           SFA.DiscountAmount = 0,
                           SFA.UpdatedAt      = SYSDATETIME()
                    FROM   dbo.StudentFeeAssignments AS SFA
                           INNER JOIN
                           dbo.FeeConfigurations AS FC
                           ON FC.FeeTypeId = SFA.FeeTypeId
                              AND FC.CampusId = @CampusId
                              AND FC.ClassId = @ClassId
                    WHERE  SFA.StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, 'This fee is not configured for the student''s campus/class.', 1;
                        END
                    SET @StudentFeeAssignmentId = @ExistingAssignmentId;
                END
            ELSE
                BEGIN
                    DECLARE @ReactivateEffectiveFrom AS DATE = @FmscOpeningMonth;
                    DECLARE @LastBilledMonth AS DATE;
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @ReactivateEffectiveFrom <= @LastBilledMonth
                        BEGIN
                            ROLLBACK;
                            THROW 53113, 'This fee cannot be reactivated starting this month because a later month has already been billed under the previous version. Please review Fee Setup.', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @ReactivateEffectiveFrom),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @ExistingAssignmentId
                           AND EffectiveTo IS NULL;
                    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                    SELECT @EnrollmentId,
                           @AdmissionId,
                           FC.FeeTypeId,
                           FC.Amount,
                           0,
                           FC.Frequency,
                           N'Reactivated - new version starting current month',
                           @ReactivateEffectiveFrom,
                           NULL
                    FROM   dbo.FeeConfigurations AS FC
                    WHERE  FC.FeeTypeId = @FeeTypeId
                           AND FC.CampusId = @CampusId
                           AND FC.ClassId = @ClassId;
                    IF @@ROWCOUNT = 0
                        BEGIN
                            ROLLBACK;
                            THROW 53111, 'This fee is not configured for the student''s campus/class.', 1;
                        END
                    SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
                END
        END
    ELSE
        BEGIN
            SET @AuditAction = N'INSERT';
            DECLARE @EffectiveFromForNewRow AS DATE = @FmscOpeningMonth;
            INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
            SELECT @EnrollmentId,
                   @AdmissionId,
                   FC.FeeTypeId,
                   FC.Amount,
                   0,
                   FC.Frequency,
                   N'Added from Student Admission',
                   @EffectiveFromForNewRow,
                   NULL
            FROM   dbo.FeeConfigurations AS FC
            WHERE  FC.FeeTypeId = @FeeTypeId
                   AND FC.CampusId = @CampusId
                   AND FC.ClassId = @ClassId;
            IF @@ROWCOUNT = 0
                BEGIN
                    ROLLBACK;
                    THROW 53112, 'This fee is not configured for the student''s campus/class.', 1;
                END
            SET @StudentFeeAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'FEE_ASSIGNMENT_ADD', CONVERT (NVARCHAR (200), @StudentFeeAssignmentId));
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT StudentFeeAssignmentId,
                                                   EnrollmentId,
                                                   AdmissionId,
                                                   FeeTypeId,
                                                   Amount,
                                                   DiscountAmount,
                                                   Frequency,
                                                   EffectiveFrom,
                                                   EffectiveTo,
                                                   CancelledAt
                                            FROM   dbo.StudentFeeAssignments
                                            WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @StudentFeeAssignmentId, @Action = @AuditAction, @OldValues = @OldValues, @NewValues = @NewValues;
    DECLARE @FmscFrequency AS NVARCHAR (30), @FmscAmount AS DECIMAL (18, 2), @FmscDiscount AS DECIMAL (18, 2), @FmscFeeTypeId AS INT, @FmscEnrollmentId AS INT, @FmscAdmissionId AS INT, @FmscStartMonth AS DATE, @FmscPostMonth AS DATE;
    SELECT @FmscFrequency = Frequency,
           @FmscAmount = Amount,
           @FmscDiscount = DiscountAmount,
           @FmscFeeTypeId = FeeTypeId,
           @FmscEnrollmentId = EnrollmentId,
           @FmscAdmissionId = AdmissionId,
           @FmscStartMonth = DATEFROMPARTS(YEAR(ISNULL(EffectiveFrom, @FmscOpeningMonth)), MONTH(ISNULL(EffectiveFrom, @FmscOpeningMonth)), 1)
    FROM   dbo.StudentFeeAssignments
    WHERE  StudentFeeAssignmentId = @StudentFeeAssignmentId;
    SET @FmscPostMonth = CASE WHEN @FmscStartMonth > @FmscOpeningMonth THEN @FmscStartMonth ELSE @FmscOpeningMonth END;
    IF @FmscFrequency IN (N'Monthly', N'Yearly') AND EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId AND (PlacementConfirmedAt IS NOT NULL OR Status=N'Pending') AND NOT EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=Enrollments.AdmissionId AND S.LegacySetupPending=1))
        BEGIN
            INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, ChargeKind)
            SELECT @StudentFeeAssignmentId,
                   @FmscEnrollmentId,
                   @FmscAdmissionId,
                   @FmscFeeTypeId,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscPostMonth,
                   @FmscAmount,
                   @FmscDiscount,
                   CASE WHEN @FmscFrequency = N'Yearly' THEN N'Annual' ELSE N'Monthly' END
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.BillingCharges AS BC
                               WHERE  BC.EnrollmentId = @FmscEnrollmentId
                                      AND BC.FeeTypeId = @FmscFeeTypeId
                                      AND BC.Status <> N'Cancelled'
                                      AND ((@FmscFrequency = N'Yearly'
                                            AND BC.ChargeKind = N'Annual')
                                           OR (@FmscFrequency = N'Monthly'
                                               AND BC.ChargeKind = N'Monthly'
                                               AND BC.BillingMonth = @FmscPostMonth)));
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
END



GO

-- BulkPromoteStudents

GO

-- AdmitHistoricalStudent
CREATE OR ALTER PROCEDURE dbo.AdmitHistoricalStudent
@UserId INT, @AdmissionDate DATE, @AdmissionCampusId INT, @AcademicYearId INT, @ClassSectionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500), @DateOfBirth DATE, @PlaceOfBirth NVARCHAR (500), @Gender NVARCHAR (20), @BFormNumber NVARCHAR (50), @Religion NVARCHAR (50), @BloodGroup NVARCHAR (10), @ContactNumber NVARCHAR (30), @AlternateContactNumber NVARCHAR (30), @Address NVARCHAR (500), @PreviousSchool NVARCHAR (500), @FatherOccupation NVARCHAR (250), @Disability NVARCHAR (3), @UsesTransport BIT, @SelectedFeeTypeIds dbo.FeeTypeIdList READONLY, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL, @AdmissionDiscounts dbo.AdmissionFeeDiscountType READONLY, @AdmissionNumber VARCHAR (40)=NULL, @VerifiedEnrollmentDate DATE, @VerifiedStatus NVARCHAR (30), @VerifiedWithdrawalDate DATE=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Admit';
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF NULLIF (LTRIM(RTRIM(@AdmissionNumber)), '') IS NULL
        THROW 51726, 'Enter the admission number from the physical register.', 1;
    IF @VerifiedStatus IS NULL
       OR @VerifiedStatus NOT IN (N'Active', N'Left')
        THROW 51723, 'Confirm Active or Left for the historical record.', 1;
    IF @VerifiedEnrollmentDate IS NULL
       OR @VerifiedEnrollmentDate < @AdmissionDate
       OR @VerifiedEnrollmentDate > CONVERT (DATE, SYSDATETIME())
        THROW 51722, 'Confirm the enrollment date on or after admission, not in the future.', 1;
    IF @VerifiedWithdrawalDate IS NOT NULL
       AND (@VerifiedStatus <> N'Left'
            OR @VerifiedWithdrawalDate < @VerifiedEnrollmentDate
            OR @VerifiedWithdrawalDate > CONVERT (DATE, SYSDATETIME()))
        THROW 51724, 'Confirm a withdrawal date on or after enrollment for a Left student.', 1;
    IF EXISTS (SELECT 1
               FROM   @SelectedFeeTypeIds)
       OR EXISTS (SELECT 1
                  FROM   @AdmissionDiscounts)
        THROW 51725, 'Historical records cannot generate fees or discounts.', 1;
    IF EXISTS (SELECT 1
               FROM   @AdmissionDiscounts
               WHERE  DiscountAmount < 0)
        THROW 51023, 'Fee discount cannot be negative.', 1;
    IF @AdmissionDate > CAST (GETDATE() AS DATE)
        THROW 51010, 'Admission date cannot be in the future.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51011, 'Student name is required.', 1;
    IF LEN(LTRIM(RTRIM(@StudentName))) > 500
        THROW 51021, 'Student name cannot be longer than 500 characters.', 1;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51012, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability NOT IN (N'Yes', N'No')
        THROW 51013, 'Disability must be Yes or No.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Campuses
                   WHERE  CampusId = @AdmissionCampusId)
        THROW 51014, 'The selected campus is invalid.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 51015, 'The selected academic year is invalid.', 1;
    DECLARE @ContextCampusId AS INT, @ClassId AS INT;
    SELECT @ContextCampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @ClassSectionId;
    IF @ContextCampusId IS NULL
        THROW 51017, 'The selected class/section placement does not exist or is inactive.', 1;
    IF @ContextCampusId <> @AdmissionCampusId
        THROW 51018, 'The selected class/section belongs to a different campus.', 1;
    DECLARE @YearStartDate AS DATE, @YearEndDate AS DATE;
    SELECT @YearStartDate = StartDate,
           @YearEndDate = EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @AcademicYearId;
    IF @VerifiedEnrollmentDate < @YearStartDate
       OR @VerifiedEnrollmentDate > @YearEndDate
        THROW 51022, 'The verified enrollment date must fall within the selected academic year''s date range.', 1;
    DECLARE @BillingStartDate AS DATE = NULL;
    BEGIN TRANSACTION;
    EXECUTE dbo.ReserveAdmissionNumber @AdmissionNumber = @AdmissionNumber OUTPUT;
    DECLARE @RegistrationNumber AS NVARCHAR (40);
    EXECUTE dbo.GetNextRegistrationNumber @RegistrationDate = @AdmissionDate, @RegistrationNumber = @RegistrationNumber OUTPUT;
    INSERT  dbo.Students (AdmissionNumber, AdmissionDate, AdmissionCampusId, RegistrationNumber, StudentName, FatherName, DateOfBirth, Gender, BFormNumber, Religion, BloodGroup, ContactNumber, AlternateContactNumber, Address, PreviousSchool, PlaceOfBirth, FatherOccupation, Disability, Status, BiseEnrolmentNumber, CasteTribe)
    VALUES              (@AdmissionNumber, @AdmissionDate, @AdmissionCampusId, @RegistrationNumber, @StudentName, NULLIF (LTRIM(RTRIM(@FatherName)), N''), @DateOfBirth, NULLIF (LTRIM(RTRIM(@Gender)), N''), NULLIF (LTRIM(RTRIM(@BFormNumber)), N''), NULLIF (LTRIM(RTRIM(@Religion)), N''), NULLIF (LTRIM(RTRIM(@BloodGroup)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Address)), N''), NULLIF (LTRIM(RTRIM(@PreviousSchool)), N''), NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N''), NULLIF (LTRIM(RTRIM(@FatherOccupation)), N''), @Disability, @VerifiedStatus, NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N''), NULLIF (LTRIM(RTRIM(@CasteTribe)), N''));
    DECLARE @AdmissionId AS INT;
    SET @AdmissionId = SCOPE_IDENTITY();
    IF EXISTS(SELECT 1 FROM dbo.LegacyAdmissionSetup WHERE CompletedAt IS NULL) UPDATE dbo.Students SET LegacySetupPending=1 WHERE AdmissionId=@AdmissionId;
    UPDATE dbo.Students
    SET    WithdrawalDate = @VerifiedWithdrawalDate
    WHERE  AdmissionId = @AdmissionId;
    INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, Remarks, LeavingDate, BillingReady)
    VALUES                 (@AdmissionId, @AcademicYearId, @ClassSectionId, @VerifiedEnrollmentDate, @BillingStartDate, @VerifiedStatus, @UsesTransport, N'Historical admission verified from physical school record; no fees generated.', @VerifiedWithdrawalDate, 0);
    DECLARE @EnrollmentId AS INT;
    SET @EnrollmentId = SCOPE_IDENTITY();
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'ADMISSION', @AdmissionDate, NULL, @ClassSectionId, NULL, @VerifiedStatus, N'Historical first enrollment confirmed from physical record', @UserId);
    INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
    VALUES                          (@AdmissionId, NULL, @VerifiedStatus, N'Admission', @AdmissionDate, N'Historical student status confirmed from physical record', @UserId);
    DECLARE @OperationId AS UNIQUEIDENTIFIER;
    SET @OperationId = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ADMISSION', CAST (@AdmissionId AS NVARCHAR (200)));
    DECLARE @AuditRecordId AS NVARCHAR (200);
    SET @AuditRecordId = CAST (@AdmissionId AS NVARCHAR (200));
    DECLARE @AuditValues AS NVARCHAR (MAX);
    SELECT @AuditValues = (SELECT @AdmissionId AS AdmissionId,
                                  @EnrollmentId AS EnrollmentId,
                                  @RegistrationNumber AS RegistrationNumber,
                                  @AdmissionDate AS AdmissionDate,
                                  @VerifiedEnrollmentDate AS EnrollmentDate,
                                  @VerifiedWithdrawalDate AS WithdrawalDate,
                                  @AdmissionNumber AS AdmissionNumber,
                                  @BillingStartDate AS BillingStartDate,
                                  @AdmissionCampusId AS AdmissionCampusId,
                                  @AcademicYearId AS AcademicYearId,
                                  @ClassSectionId AS ClassSectionId,
                                  @VerifiedStatus AS Status
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    SET @AuditRecordId = CONVERT (NVARCHAR (200), @EnrollmentId);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @AuditRecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @AuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId,
           @EnrollmentId AS EnrollmentId,
           @RegistrationNumber AS RegistrationNumber,
           @AdmissionNumber AS AdmissionNumber;
END



GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialSportsCertificates ON dbo.SportsCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialRegularStudentCertificates ON dbo.RegularStudentCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialNoObjectionCertificates ON dbo.NoObjectionCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialDateOfBirthCertificates ON dbo.DateOfBirthCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialLeaveCertificates ON dbo.LeaveCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialProvisionalCertificates ON dbo.ProvisionalCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.GuardOfficialCharacterCertificates ON dbo.CharacterCertificates AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId WHERE S.Status IN(N'Pending',N'Cancelled') OR S.AdmissionNumber IS NULL)
  THROW 59750,'Complete Admission before issuing an official student certificate. Cancelled records cannot receive certificates.',1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.BulkPromoteStudents
@UserId INT, @SourceAcademicYearId INT, @DestinationAcademicYearId INT, @SourceCampusId INT, @SourceClassId INT, @SourceSectionId INT, @DestinationClassSectionId INT, @EffectiveDate DATE, @Remarks NVARCHAR (500), @Decisions dbo.BulkPromotionDecisionType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @SourceAcademicYearId <= 0
        THROW 52501, 'A valid source academic year is required.', 1;
    IF @DestinationAcademicYearId <= 0
        THROW 52502, 'A valid destination academic year is required.', 1;
    IF @DestinationAcademicYearId = @SourceAcademicYearId
        THROW 52503, 'Bulk Promotion must move students into a different academic year. Use Transfer Class for same-year movement.', 1;
    IF @SourceCampusId <= 0
       OR @SourceClassId <= 0
        THROW 52504, 'A valid source campus and class are required.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
       AND @DestinationClassSectionId <= 0
        THROW 52506, 'A valid destination class/section is required for promoted students.', 1;
    IF @EffectiveDate IS NULL
        THROW 52508, 'The effective date is required.', 1;
    IF @EffectiveDate > CONVERT (DATE, SYSDATETIME())
        THROW 52509, 'The effective date cannot be in the future.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   @Decisions)
        THROW 52510, 'Select at least one student to process.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision NOT IN (N'PROMOTE', N'HOLD'))
        THROW 52511, 'Bulk promotion contains an invalid decision.', 1;
    DECLARE @SourceStartDate AS DATE, @DestinationStartDate AS DATE, @DestinationEndDate AS DATE, @DestinationCampusId AS INT, @DestinationClassId AS INT;
    SELECT @SourceStartDate = StartDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @SourceAcademicYearId;
    SELECT @DestinationStartDate = StartDate, @DestinationEndDate=EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @DestinationAcademicYearId;
    IF @SourceStartDate IS NULL
        THROW 52512, 'The source academic year is invalid.', 1;
    IF @DestinationStartDate IS NULL
        THROW 52513, 'The destination academic year is invalid.', 1;
    IF @DestinationStartDate <= @SourceStartDate
        THROW 52514, 'The destination academic year must be later than the source academic year.', 1;
    IF @EffectiveDate<@DestinationStartDate OR (@DestinationEndDate IS NOT NULL AND @EffectiveDate>@DestinationEndDate)
        THROW 52520,'The effective date must fall within the destination academic year.',1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            SELECT @DestinationCampusId = CCS.CampusId,
                   @DestinationClassId = CCS.ClassId
            FROM   dbo.CampusClassSections AS CCS
            WHERE  CCS.ClassSectionId = @DestinationClassSectionId;
            IF @DestinationCampusId IS NULL
                THROW 52516, 'The destination class/section is invalid.', 1;
            IF @DestinationCampusId <> @SourceCampusId
                THROW 52517, 'Bulk Promotion cannot move students between campuses.', 1;
        END
    ELSE
        BEGIN
            SET @DestinationClassSectionId = NULL;
        END
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            DECLARE @MissingMandatoryFeesDest AS NVARCHAR (1000);
            SELECT @MissingMandatoryFeesDest = STRING_AGG(FT.FeeTypeName, N', ')
            FROM   dbo.FeeTypes AS FT
            WHERE  FT.IsMandatory = 1
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.FeeConfigurations AS FC
                                   WHERE  FC.CampusId = @DestinationCampusId
                                          AND FC.ClassId = @DestinationClassId
                                          AND FC.FeeTypeId = FT.FeeTypeId);
            IF @MissingMandatoryFeesDest IS NOT NULL
                BEGIN
                    DECLARE @MandatoryFeeErrorMsgDest AS NVARCHAR (1100) = N'Required fee configuration is missing for the destination class: ' + @MissingMandatoryFeesDest + N'.';
                    THROW 52519, @MandatoryFeeErrorMsgDest, 1;
                END
        END
    BEGIN TRANSACTION;
    SELECT E.EnrollmentId,
           E.AdmissionId,
           E.ClassSectionId,
           E.UsesTransport,
           D.Decision,
           C.ClassId AS CurrentClassId,
           CCS.CampusId AS CurrentCampusId
    INTO   #Selected
    FROM   @Decisions AS D
           INNER JOIN
           dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           ON E.AdmissionId = D.AdmissionId
              AND E.Status = N'Active'
              AND E.AcademicYearId = @SourceAcademicYearId
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
           INNER JOIN
           dbo.Classes AS C
           ON C.ClassId = CCS.ClassId
    WHERE  CCS.CampusId = @SourceCampusId
           AND CCS.ClassId = @SourceClassId
           AND (@SourceSectionId = 0
                OR CCS.SectionId = @SourceSectionId);
    IF EXISTS (SELECT 1
               FROM   @Decisions AS D
                      LEFT OUTER JOIN
                      #Selected AS S
                      ON S.AdmissionId = D.AdmissionId
               WHERE  S.AdmissionId IS NULL)
        THROW 52521, 'One or more selected students are no longer active in the selected source class. Reload the candidates and try again.', 1;
    ALTER TABLE #Selected
        ADD NewClassSectionId INT NULL;
    UPDATE #Selected
    SET    NewClassSectionId = CASE WHEN Decision = N'PROMOTE' THEN @DestinationClassSectionId ELSE ClassSectionId END;
    IF EXISTS (SELECT 1
               FROM   #Selected AS S
                      INNER JOIN
                      dbo.CampusClassSections AS CCS
                      ON CCS.ClassSectionId = S.NewClassSectionId
               WHERE  CCS.CampusId <> @SourceCampusId)
        THROW 52522, 'One or more destination class/sections are invalid for the source campus.', 1;
    DECLARE @BatchLockResult AS INT;
    DECLARE @BatchLockResource AS NVARCHAR (255) = N'FMSC.BulkPromotion.' + CONVERT (NVARCHAR (20), @SourceAcademicYearId) + N'.' + CONVERT (NVARCHAR (20), @SourceClassId) + N'.' + CONVERT (NVARCHAR (20), @SourceCampusId);
    EXECUTE @BatchLockResult = sys.sp_getapplock @Resource = @BatchLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
    IF @BatchLockResult < 0
        THROW 52523, 'Another bulk promotion is currently processing this class. Please try again after it completes.', 1;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'BULK_PROMOTION', CONVERT (NVARCHAR (200), @SourceAcademicYearId));
    INSERT  dbo.PromotionBatches (SourceAcademicYearId, DestinationAcademicYearId, SourceCampusId, SourceClassId, SourceSectionId, DestinationClassSectionId, EffectiveDate, Remarks, CreatedByUserId)
    VALUES                      (@SourceAcademicYearId, @DestinationAcademicYearId, @SourceCampusId, @SourceClassId, NULLIF (@SourceSectionId, 0), @DestinationClassSectionId, @EffectiveDate, @Remarks, @UserId);
    DECLARE @PromotionBatchId AS INT = SCOPE_IDENTITY();
    IF EXISTS (SELECT 1
               FROM   (SELECT DISTINCT CCS.CampusId,
                                       CCS.ClassId
                       FROM   #Selected AS S
                              INNER JOIN
                              dbo.CampusClassSections AS CCS
                              ON CCS.ClassSectionId = S.NewClassSectionId) AS D
               WHERE  EXISTS (SELECT 1
                              FROM   dbo.FeeTypes AS FT
                              WHERE  FT.IsMandatory = 1
                                     AND NOT EXISTS (SELECT 1
                                                     FROM   dbo.FeeConfigurations AS FC
                                                     WHERE  FC.CampusId = D.CampusId
                                                            AND FC.ClassId = D.ClassId
                                                            AND FC.FeeTypeId = FT.FeeTypeId)))
        THROW 52524, 'Required fee configuration is missing for one or more destination classes.', 1;
    IF EXISTS(SELECT 1 FROM #Selected X JOIN dbo.Students S ON S.AdmissionId=X.AdmissionId WHERE S.LegacySetupPending=1) THROW 59751,'Finish legacy verification and Go Live before promoting these students.',1;
    IF EXISTS(SELECT 1 FROM #Selected S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId WHERE E.PlacementConfirmedAt IS NULL)
        THROW 59646,'Confirm the source placement in Admission before promotion.',1;
    IF EXISTS(SELECT 1 FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK)
        JOIN #Selected S ON S.EnrollmentId=B.EnrollmentId
        WHERE B.Status<>N'Cancelled' AND COALESCE(B.BillingMonth,B.ChargeDate)>DATEFROMPARTS(YEAR(DATEADD(day,-1,@EffectiveDate)),MONTH(DATEADD(day,-1,@EffectiveDate)),1))
        THROW 59632,'Charges exist after the promotion effective date. Review unpaid charges in Fee Collection or paid charges in Fee Corrections, then retry. No students were promoted.',1;
    UPDATE E
    SET    Status      = N'Promoted',
           LeavingDate = @EffectiveDate,
           UpdatedAt   = SYSDATETIME(),
           Remarks     = COALESCE (NULLIF (@Remarks, N''), E.Remarks)
    FROM   dbo.Enrollments AS E
           INNER JOIN
           #Selected AS S
           ON S.EnrollmentId = E.EnrollmentId
    WHERE  E.Status = N'Active';
    IF @@ROWCOUNT <> (SELECT COUNT(*)
                      FROM   #Selected)
        THROW 52525, 'The source enrollments could not all be closed as historical enrollments.', 1;
    DECLARE @CutoffDay AS INT;
    SELECT TOP (1) @CutoffDay = SP.FeeProrationCutoffDay
    FROM   dbo.SchoolProfile AS SP;
    INSERT dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, BillingReady, Remarks, PlacementConfirmedAt)
    SELECT S.AdmissionId,
           @DestinationAcademicYearId,
           S.NewClassSectionId,
           @EffectiveDate,
           CASE WHEN @CutoffDay BETWEEN 1 AND 28
                     AND DAY(@EffectiveDate) >= @CutoffDay THEN DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(@EffectiveDate), MONTH(@EffectiveDate), 1)) ELSE @EffectiveDate END,
           N'Active',
           0,
           0,
           CASE WHEN S.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END, SYSDATETIME()
    FROM   #Selected AS S;
    DECLARE @NewEnrollments TABLE (
        AdmissionId       INT           NOT NULL,
        OldEnrollmentId   INT           NOT NULL,
        NewEnrollmentId   INT           NOT NULL,
        Decision          NVARCHAR (20) NOT NULL,
        NewClassSectionId INT           NOT NULL,
        UsesTransport     BIT           NOT NULL);
    INSERT INTO @NewEnrollments (AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, NewClassSectionId, UsesTransport)
    SELECT S.AdmissionId,
           S.EnrollmentId,
           E.EnrollmentId,
           S.Decision,
           S.NewClassSectionId,
           0
    FROM   #Selected AS S
           INNER JOIN
           dbo.Enrollments AS E
           ON E.AdmissionId = S.AdmissionId
              AND E.AcademicYearId = @DestinationAcademicYearId
              AND E.Status = N'Active';
    IF (SELECT COUNT(*)
        FROM   @NewEnrollments) <> (SELECT COUNT(*)
                                    FROM   #Selected)
        THROW 52526, 'The new academic-year enrollments could not all be created.', 1;
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT N.NewEnrollmentId,
           N.AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = N.NewClassSectionId
           INNER JOIN
           dbo.FeeConfigurations AS FC
           ON FC.CampusId = CCS.CampusId
              AND FC.ClassId = CCS.ClassId
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FT.IsMandatory = 1
           AND FT.IsTransportLinked = 0;
    INSERT dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    SELECT N.OldEnrollmentId,
           N.AdmissionId,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'BULK_PROMOTE' ELSE N'BULK_HOLD' END,
           @EffectiveDate,
           S.ClassSectionId,
           N.NewClassSectionId,
           N'Active',
           N'Promoted',
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Bulk promoted to EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) ELSE N'Bulk held back/repeated class in new academic year; EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) END,
           @UserId
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    INSERT dbo.PromotionBatchStudents (PromotionBatchId, AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, Remarks)
    SELECT @PromotionBatchId,
           N.AdmissionId,
           N.OldEnrollmentId,
           N.NewEnrollmentId,
           N.Decision,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Promoted to destination class.' ELSE N'Held back/repeated the current class in the destination academic year.' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    DECLARE @ProcessedCount AS INT = (SELECT COUNT(*)
                                      FROM   @NewEnrollments), @PromotedCount AS INT = (SELECT COUNT(*)
                                                                                        FROM   @NewEnrollments
                                                                                        WHERE  Decision = N'PROMOTE'), @HeldBackCount AS INT = (SELECT COUNT(*)
                                                                                                                                                FROM   @NewEnrollments
                                                                                                                                                WHERE  Decision = N'HOLD');
    UPDATE dbo.PromotionBatches
    SET    ProcessedCount = @ProcessedCount,
           PromotedCount  = @PromotedCount,
           HeldBackCount  = @HeldBackCount
    WHERE  PromotionBatchId = @PromotionBatchId;
    DECLARE @AuditValues AS NVARCHAR (MAX);
    SELECT @AuditValues = (SELECT @PromotionBatchId AS PromotionBatchId,
                                  @SourceAcademicYearId AS SourceAcademicYearId,
                                  @DestinationAcademicYearId AS DestinationAcademicYearId,
                                  @SourceCampusId AS SourceCampusId,
                                  @SourceClassId AS SourceClassId,
                                  @SourceSectionId AS SourceSectionId,
                                  @DestinationClassSectionId AS DestinationClassSectionId,
                                  @EffectiveDate AS EffectiveDate,
                                  @ProcessedCount AS ProcessedCount,
                                  @PromotedCount AS PromotedCount,
                                  @HeldBackCount AS HeldBackCount,
                                  @Remarks AS Remarks
                           FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @PromotionBatchAuditRecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @PromotionBatchId);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'PromotionBatches', @RecordId = @PromotionBatchAuditRecordId, @Action = N'BULK_PROMOTION', @OldValues = NULL, @NewValues = @AuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @PromotionBatchId AS PromotionBatchId,
           @ProcessedCount AS ProcessedCount,
           @PromotedCount AS PromotedCount,
           @HeldBackCount AS HeldBackCount;
END

GO
