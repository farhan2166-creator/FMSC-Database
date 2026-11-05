-- Remove retired certificate cancellation columns and all active cancellation logic.
-- Intended upgrade: clean restored snapshots with no previously cancelled copies.
-- Refuse to revive historical cancelled records from a different deployment.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 52 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>51
 THROW 51000,'Migration 52 requires schema 51.',1;
IF EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in SportsCertificates; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in NoObjectionCertificates; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in ProvisionalCertificates; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.RegularLecturerCertificates WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in RegularLecturerCertificates; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.BankAccountOpeningCertificates WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in BankAccountOpeningCertificates; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.RegularStudentCertificateIssues WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in RegularStudentCertificateIssues; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssues WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in CharacterCertificateIssues; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificateIssues WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in DateOfBirthCertificateIssues; column removal would revive them. Migration stopped without changing records.',1;
IF EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssues WHERE VoidedAt IS NOT NULL)
 THROW 51000,'Historical cancelled copies exist in LeaveCertificateIssues; column removal would revive them. Migration stopped without changing records.',1;
GO

CREATE OR ALTER TRIGGER dbo.TR_Sports_CollectedLock ON dbo.SportsCertificates AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_NOC_CollectedLock ON dbo.NoObjectionCertificates AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Provisional_CollectedLock ON dbo.ProvisionalCertificates AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Lecturer_CollectedLock ON dbo.RegularLecturerCertificates AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Bank_CollectedLock ON dbo.BankAccountOpeningCertificates AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Regular_CollectedLock ON dbo.RegularStudentCertificateIssues AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Character_CollectedLock ON dbo.CharacterCertificateIssues AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_DOB_CollectedLock ON dbo.DateOfBirthCertificateIssues AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Leave_CollectedLock ON dbo.LeaveCertificateIssues AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE CollectedAt IS NOT NULL)
 THROW 58900,'This certificate copy has been collected and is permanently locked. Use Issue Again for a new copy.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Character_SnapshotLock ON dbo.CharacterCertificateIssueSnapshots AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted D JOIN dbo.CharacterCertificateIssues I ON I.CharacterCertificateIssueId=D.CharacterCertificateIssueId WHERE I.CollectedAt IS NOT NULL)
 THROW 58900,'The collected certificate details are locked. Issue another copy to make corrections.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_DOB_SnapshotLock ON dbo.DateOfBirthCertificateIssueSnapshots AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted D JOIN dbo.DateOfBirthCertificateIssues I ON I.DateOfBirthCertificateIssueId=D.DateOfBirthCertificateIssueId WHERE I.CollectedAt IS NOT NULL)
 THROW 58900,'The collected certificate details are locked. Issue another copy to make corrections.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Leave_SnapshotLock ON dbo.LeaveCertificateIssueSnapshots AFTER UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted D JOIN dbo.LeaveCertificateIssues I ON I.LeaveCertificateIssueId=D.LeaveCertificateIssueId WHERE I.CollectedAt IS NOT NULL)
 THROW 58900,'The collected certificate details are locked. Issue another copy to make corrections.',1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_Provisional_SubjectLock ON dbo.ProvisionalCertificateSubjects AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM (SELECT ProvisionalCertificateId FROM inserted UNION SELECT ProvisionalCertificateId FROM deleted) X JOIN dbo.ProvisionalCertificates C ON C.ProvisionalCertificateId=X.ProvisionalCertificateId WHERE C.CollectedAt IS NOT NULL)
 THROW 58900,'Subjects on a collected certificate are locked.',1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetRegularStudentCertificateRecords @UserId int,@AdmissionId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 SELECT C.RegularStudentCertificateId,I.RegularStudentCertificateIssueId,C.AdmissionId,I.StudentName,I.FatherName,I.DateOfBirth,I.ClassName,I.CertificateDate,
 I.IssuedAt CreatedAt,CONVERT(datetime2,NULL) UpdatedAt,I.IssuedByUserId,U.Username IssuedBy,C.EnrollmentId,I.AdmissionNumber,I.ContentVersion,I.PreviousIssueId,I.CollectedAt,I.CollectedBy,CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.RegularStudentCertificates C JOIN dbo.RegularStudentCertificateIssues I ON I.RegularStudentCertificateId=C.RegularStudentCertificateId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId WHERE @AdmissionId=0 OR C.AdmissionId=@AdmissionId
 ORDER BY I.IssuedAt DESC,I.RegularStudentCertificateIssueId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetLeaveCertificateRecords @UserId int,@AdmissionId int=0,@CertificateType nvarchar(20)=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Students.Leave';
 SET @CertificateType=NULLIF(LTRIM(RTRIM(@CertificateType)),N'');
 SELECT C.LeaveRecordId,I.LeaveCertificateIssueId,C.AdmissionId,C.CertificateType,P.StudentName,P.FatherName,P.ClassStudied,P.ClassPromotedTo,P.LeaveDate,
  I.IssuedAt,I.IssuedByUserId,U.Username IssuedBy,I.IssueType,I.CollectedAt,I.CollectedBy,
  CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus,C.EnrollmentId,P.ContentVersion,P.AdmissionNumber,P.PreviousIssueId,P.AcademicRecord,P.AttendanceRecord,P.Remarks,P.DateOfBirth,P.DateOfAdmission AdmissionDate
 FROM dbo.LeaveCertificates C JOIN dbo.LeaveCertificateIssues I ON I.LeaveRecordId=C.LeaveRecordId
 JOIN dbo.LeaveCertificateIssueSnapshots P ON P.LeaveCertificateIssueId=I.LeaveCertificateIssueId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId
 WHERE (@AdmissionId=0 OR C.AdmissionId=@AdmissionId) AND (@CertificateType IS NULL OR C.CertificateType=@CertificateType)
 ORDER BY C.LeaveRecordId DESC,I.IssuedAt DESC,I.LeaveCertificateIssueId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetCharacterCertificateRecords @UserId int,@AdmissionId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 SELECT C.CertificateRecordId,I.CharacterCertificateIssueId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.Gender,
 P.ExamType,P.LastExamYear,P.RollNumber,P.Remarks,P.CertificateDate,P.PreviousIssueId,P.CorrectionReason,
 I.IssuedAt,I.IssuedByUserId,U.Username IssuedBy,I.CollectedAt,I.CollectedBy,P.AdmissionNumber,P.ContentVersion,
 CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.CharacterCertificates C JOIN dbo.CharacterCertificateIssues I ON I.CertificateRecordId=C.CertificateRecordId
 JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=I.CharacterCertificateIssueId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId WHERE @AdmissionId=0 OR C.AdmissionId=@AdmissionId
 ORDER BY I.IssuedAt DESC,I.CharacterCertificateIssueId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetDateOfBirthCertificateRecords @UserId int,@AdmissionId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 SELECT C.CertificateRecordId,I.DateOfBirthCertificateIssueId,C.AdmissionId,P.StudentName,P.FatherName,P.DateOfBirth,P.CertificateDate,
 I.IssuedAt,I.IssuedByUserId,U.Username IssuedBy,I.CollectedAt,I.CollectedBy,P.AdmissionNumber,P.ContentVersion,
 CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus,P.PreviousIssueId
 FROM dbo.DateOfBirthCertificates C JOIN dbo.DateOfBirthCertificateIssues I ON I.CertificateRecordId=C.CertificateRecordId
 JOIN dbo.DateOfBirthCertificateIssueSnapshots P ON P.DateOfBirthCertificateIssueId=I.DateOfBirthCertificateIssueId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId WHERE @AdmissionId=0 OR C.AdmissionId=@AdmissionId
 ORDER BY I.IssuedAt DESC,I.DateOfBirthCertificateIssueId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.IssueRegularStudentCertificate
 @UserId int,@CertificateRecordId int,@ExpectedLatestIssueId int,@ExpectedDateOfBirth date,@CertificateDate date,@ExpectedSourceHash varbinary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @CertificateDate IS NULL OR @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58531,'Choose a certificate date that is not in the future.',1;
 IF @ExpectedDateOfBirth IS NULL THROW 58505,'Record the student date of birth before reissuing.',1;
 BEGIN TRANSACTION;
 DECLARE @AdmissionId int,@DOB date,@Name nvarchar(500),@Father nvarchar(500),@Last int,@Class nvarchar(100);
 SELECT @AdmissionId=AdmissionId FROM dbo.RegularStudentCertificates WITH(UPDLOCK,HOLDLOCK) WHERE RegularStudentCertificateId=@CertificateRecordId;
 IF @AdmissionId IS NULL THROW 58514,'The regular student certificate was not found.',1;
 SELECT @DOB=DateOfBirth,@Name=StudentName,@Father=FatherName FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @ExpectedSourceHash IS NULL OR @ExpectedSourceHash<>dbo.CertificateAdmissionFingerprint(@AdmissionId) THROW 58532,'Admission details changed after review. Reload and review this reissue again.',1;
 IF @DOB IS NULL THROW 58505,'Record the student date of birth before reissuing.',1;
 IF @DOB<>@ExpectedDateOfBirth THROW 58532,'The school birth date changed after review. Reload and review the reissue again.',1;
 SELECT TOP(1) @Last=RegularStudentCertificateIssueId,@Class=ClassName FROM dbo.RegularStudentCertificateIssues WITH(UPDLOCK,HOLDLOCK)
 WHERE RegularStudentCertificateId=@CertificateRecordId ORDER BY RegularStudentCertificateIssueId DESC;
 IF @Last IS NULL OR @ExpectedLatestIssueId IS NULL OR @Last<>@ExpectedLatestIssueId THROW 58533,'A newer certificate copy exists. Reload its history before reissuing.',1;
 IF EXISTS(SELECT 1 FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateIssueId=@Last AND CollectedAt IS NULL) THROW 58907,'Collect the current copy before issuing again.',1;
 INSERT dbo.RegularStudentCertificateIssues(RegularStudentCertificateId,PreviousIssueId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,IssuedAt,IssuedByUserId)
 VALUES(@CertificateRecordId,@Last,@Name,@Father,@DOB,@Class,@CertificateDate,SYSDATETIME(),@UserId);
 DECLARE @Issue int=CONVERT(int,SCOPE_IDENTITY());
 COMMIT;
 SELECT @CertificateRecordId CertificateRecordId,@Issue IssueId,@AdmissionId AdmissionId,@DOB DateOfBirth,@CertificateDate CertificateDate;
END;
GO

CREATE OR ALTER PROCEDURE dbo.IssueCharacterCertificate
 @UserId int,@CertificateRecordId int,@Remarks nvarchar(1000)=NULL,@ExpectedLatestIssueId int=NULL,@ExpectedSourceHash varbinary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 BEGIN TRANSACTION;
 DECLARE @Admission int,@Type nvarchar(10),@Last int,@Collected datetime2,@Issue int,@Name nvarchar(500),@Father nvarchar(500),@Gender nvarchar(20);
 SELECT @Admission=AdmissionId,@Type=ExamType FROM dbo.CharacterCertificates WITH(UPDLOCK,HOLDLOCK) WHERE CertificateRecordId=@CertificateRecordId;
 IF @Admission IS NULL THROW 58101,'The character certificate was not found.',1;
 IF @Type IS NULL THROW 58010,'Review and save the existing certificate SSC/HSSC type before reissuing.',1;
 SELECT @Name=StudentName,@Father=FatherName,@Gender=Gender FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
 IF @Name IS NULL THROW 58006,'The student was not found.',1;
 IF @ExpectedSourceHash IS NULL OR @ExpectedSourceHash<>dbo.CertificateAdmissionFingerprint(@Admission)
  THROW 58532,'Admission details changed after review. Reload and review this reissue again.',1;
 SELECT TOP(1) @Last=CharacterCertificateIssueId,@Collected=CollectedAt FROM dbo.CharacterCertificateIssues WITH(UPDLOCK,HOLDLOCK)
 WHERE CertificateRecordId=@CertificateRecordId ORDER BY CharacterCertificateIssueId DESC;
 IF @ExpectedLatestIssueId IS NULL OR ISNULL(@Last,0)<>@ExpectedLatestIssueId THROW 58533,'A newer copy exists. Reload and review its history.',1;
 IF @Collected IS NULL THROW 58102,'The current certificate must be collected before issuing again.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Last
  AND (LastExamYear>YEAR(SYSDATETIME()) OR Remarks NOT IN(N'Good',N'Very Good',N'Excellent')))
  THROW 58017,'Open the certificate, correct its passing year and conduct, and Save Changes before issuing again.',1;
 INSERT dbo.CharacterCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId,Remarks) VALUES(@CertificateRecordId,SYSDATETIME(),@UserId,@Remarks);
 SET @Issue=CONVERT(int,SCOPE_IDENTITY());
 UPDATE N SET PreviousIssueId=@Last,ExamType=P.ExamType,StudentName=@Name,FatherName=@Father,Gender=@Gender,
 LastExamYear=P.LastExamYear,RollNumber=P.RollNumber,Remarks=P.Remarks,CertificateDate=CONVERT(date,SYSDATETIME())
 FROM dbo.CharacterCertificateIssueSnapshots N JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=@Last WHERE N.CharacterCertificateIssueId=@Issue;
 COMMIT;
 SELECT @CertificateRecordId CertificateRecordId,@Issue CharacterCertificateIssueId,@Issue IssueId,@Admission AdmissionId,
 CertificateDate FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Issue;
END;
GO

CREATE OR ALTER PROCEDURE dbo.IssueDateOfBirthCertificate
 @UserId int,@CertificateRecordId int,@Remarks nvarchar(1000)=NULL,
 @ExpectedLatestIssueId int=NULL,@ExpectedDateOfBirth date=NULL,@CertificateDate date=NULL,@ExpectedSourceHash varbinary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @CertificateRecordId<=0 THROW 58600,'A valid date of birth certificate is required.',1;
 SET @CertificateDate=ISNULL(@CertificateDate,CONVERT(date,SYSDATETIME()));
 IF @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58531,'Choose a certificate date that is not in the future.',1;
 BEGIN TRANSACTION;
 DECLARE @AdmissionId int,@DOB date,@Name nvarchar(500),@Father nvarchar(500),@Gender nvarchar(20),@Last int,@Collected datetime2;
 SELECT @AdmissionId=AdmissionId FROM dbo.DateOfBirthCertificates WITH(UPDLOCK,HOLDLOCK) WHERE CertificateRecordId=@CertificateRecordId;
 IF @AdmissionId IS NULL THROW 58601,'The date of birth certificate was not found.',1;
 SELECT @DOB=DateOfBirth,@Name=StudentName,@Father=FatherName,@Gender=Gender FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @ExpectedSourceHash IS NULL OR @ExpectedSourceHash<>dbo.CertificateAdmissionFingerprint(@AdmissionId) THROW 58532,'Admission details changed after review. Reload and review this reissue again.',1;
 IF @DOB IS NULL THROW 58505,'Record the student date of birth before reissuing.',1;
 IF @ExpectedDateOfBirth IS NOT NULL AND @DOB<>@ExpectedDateOfBirth THROW 58532,'The school birth date changed after review. Reload and review the reissue again.',1;
 SELECT TOP(1) @Last=DateOfBirthCertificateIssueId,@Collected=CollectedAt FROM dbo.DateOfBirthCertificateIssues WITH(UPDLOCK,HOLDLOCK)
 WHERE CertificateRecordId=@CertificateRecordId ORDER BY IssuedAt DESC,DateOfBirthCertificateIssueId DESC;
 IF @ExpectedLatestIssueId IS NOT NULL AND ISNULL(@Last,-1)<>@ExpectedLatestIssueId THROW 58533,'A newer certificate copy exists. Reload its history before reissuing.',1;
 IF @Last IS NOT NULL AND @Collected IS NULL THROW 58602,'The currently issued certificate has not been collected yet.',1;
 IF @Name IS NULL THROW 58601,'The earlier certificate snapshot could not be read.',1;
 INSERT dbo.DateOfBirthCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId,Remarks) VALUES(@CertificateRecordId,SYSDATETIME(),@UserId,@Remarks);
 DECLARE @Issue int=CONVERT(int,SCOPE_IDENTITY());
 -- Only the newly inserted issue receives the reviewed current details. Earlier snapshots remain intact.
 UPDATE dbo.DateOfBirthCertificateIssueSnapshots SET PreviousIssueId=@Last,StudentName=@Name,FatherName=@Father,Gender=@Gender,DateOfBirth=@DOB,CertificateDate=@CertificateDate
 WHERE DateOfBirthCertificateIssueId=@Issue;
 COMMIT;
 SELECT @Issue DateOfBirthCertificateIssueId,@CertificateRecordId CertificateRecordId,@Issue IssueId,@AdmissionId AdmissionId,@DOB DateOfBirth,@CertificateDate CertificateDate;
END;
GO

CREATE OR ALTER PROCEDURE dbo.IssueLeaveCertificate
 @UserId int,@LeaveRecordId int,@Remarks nvarchar(2000)=NULL,@ExpectedLatestIssueId int=NULL,@ExpectedSourceHash varbinary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Students.Leave';
 BEGIN TRANSACTION;
 DECLARE @Admission int,@Last int,@Collected datetime2,@Issue int,@Name nvarchar(500),@Father nvarchar(500),@DOB date,@AdmissionDate date;
 SELECT @Admission=AdmissionId FROM dbo.LeaveCertificates WITH(UPDLOCK,HOLDLOCK) WHERE LeaveRecordId=@LeaveRecordId;
 IF @Admission IS NULL THROW 57401,'The leave certificate was not found.',1;
 SELECT @Name=StudentName,@Father=FatherName,@DOB=DateOfBirth,@AdmissionDate=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
 IF @Name IS NULL THROW 57401,'The student was not found.',1;
 IF @ExpectedSourceHash IS NULL OR @ExpectedSourceHash<>dbo.CertificateAdmissionFingerprint(@Admission)
  THROW 58532,'Admission details changed after review. Reload and review this reissue again.',1;
 SELECT TOP(1) @Last=LeaveCertificateIssueId,@Collected=CollectedAt FROM dbo.LeaveCertificateIssues WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaveRecordId=@LeaveRecordId ORDER BY LeaveCertificateIssueId DESC;
 IF @ExpectedLatestIssueId IS NULL OR ISNULL(@Last,0)<>@ExpectedLatestIssueId THROW 58533,'A newer certificate copy exists. Reload its history.',1;
 IF @Collected IS NULL THROW 57402,'The current certificate must be collected before issuing again.',1;
 IF (SELECT COUNT(*) FROM dbo.LeaveCertificateIssues WHERE LeaveRecordId=@LeaveRecordId AND IssueType=N'Duplicate')>=2
  THROW 57403,'This certificate has already been issued twice as a duplicate. No further duplicate copies are allowed.',1;
 INSERT dbo.LeaveCertificateIssues(LeaveRecordId,IssuedAt,IssuedByUserId,Remarks,IssueType) VALUES(@LeaveRecordId,SYSDATETIME(),@UserId,@Remarks,N'Duplicate');
 SET @Issue=CONVERT(int,SCOPE_IDENTITY());
 UPDATE N SET PreviousIssueId=@Last,StudentName=@Name,FatherName=@Father,DateOfBirth=@DOB,DateOfAdmission=@AdmissionDate,
  ClassStudied=P.ClassStudied,ClassPromotedTo=P.ClassPromotedTo,LeaveDate=P.LeaveDate,AcademicRecord=P.AcademicRecord,AttendanceRecord=P.AttendanceRecord,Remarks=P.Remarks
 FROM dbo.LeaveCertificateIssueSnapshots N JOIN dbo.LeaveCertificateIssueSnapshots P ON P.LeaveCertificateIssueId=@Last WHERE N.LeaveCertificateIssueId=@Issue;
 COMMIT;
 SELECT @Issue LeaveCertificateIssueId,@LeaveRecordId LeaveRecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.MarkCharacterCertificateCollected @UserId int,@CharacterCertificateIssueId int,@CollectedBy nvarchar(200),@ExpectedVersion binary(8)=NULL
AS BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF NULLIF(LTRIM(RTRIM(@CollectedBy)),N'') IS NULL THROW 58901,'Enter the person collecting the certificate.',1;
 BEGIN TRANSACTION;
 IF @ExpectedVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssueSnapshots WITH(UPDLOCK,HOLDLOCK) WHERE CharacterCertificateIssueId=@CharacterCertificateIssueId AND ContentVersion=@ExpectedVersion) THROW 58911,'This copy changed after review. Refresh before collection.',1;
 UPDATE dbo.CharacterCertificateIssues SET CollectedAt=SYSDATETIME(),CollectedBy=LTRIM(RTRIM(@CollectedBy)) WHERE CharacterCertificateIssueId=@CharacterCertificateIssueId AND CollectedAt IS NULL;
 IF @@ROWCOUNT<>1 THROW 58903,'This copy was not found, or has already been collected.',1;
 COMMIT; SELECT * FROM dbo.CharacterCertificateIssues WHERE CharacterCertificateIssueId=@CharacterCertificateIssueId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.MarkDateOfBirthCertificateCollected @UserId int,@DateOfBirthCertificateIssueId int,@CollectedBy nvarchar(200),@ExpectedVersion binary(8)=NULL
AS BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF NULLIF(LTRIM(RTRIM(@CollectedBy)),N'') IS NULL THROW 58901,'Enter the person collecting the certificate.',1;
 BEGIN TRANSACTION;
 IF @ExpectedVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificateIssueSnapshots WITH(UPDLOCK,HOLDLOCK) WHERE DateOfBirthCertificateIssueId=@DateOfBirthCertificateIssueId AND ContentVersion=@ExpectedVersion) THROW 58911,'This copy changed after review. Refresh before collection.',1;
 UPDATE dbo.DateOfBirthCertificateIssues SET CollectedAt=SYSDATETIME(),CollectedBy=LTRIM(RTRIM(@CollectedBy)) WHERE DateOfBirthCertificateIssueId=@DateOfBirthCertificateIssueId AND CollectedAt IS NULL;
 IF @@ROWCOUNT<>1 THROW 58903,'This copy was not found, or has already been collected.',1;
 COMMIT; SELECT * FROM dbo.DateOfBirthCertificateIssues WHERE DateOfBirthCertificateIssueId=@DateOfBirthCertificateIssueId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.MarkLeaveCertificateCollected @UserId int,@LeaveCertificateIssueId int,@CollectedBy nvarchar(400),@ExpectedVersion binary(8)=NULL
AS BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Leave';
 IF NULLIF(LTRIM(RTRIM(@CollectedBy)),N'') IS NULL THROW 58901,'Enter the person collecting the certificate.',1;
 BEGIN TRANSACTION;
 IF @ExpectedVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssueSnapshots WITH(UPDLOCK,HOLDLOCK) WHERE LeaveCertificateIssueId=@LeaveCertificateIssueId AND ContentVersion=@ExpectedVersion) THROW 58911,'This copy changed after review. Refresh before collection.',1;
 UPDATE dbo.LeaveCertificateIssues SET CollectedAt=SYSDATETIME(),CollectedBy=LTRIM(RTRIM(@CollectedBy)) WHERE LeaveCertificateIssueId=@LeaveCertificateIssueId AND CollectedAt IS NULL;
 IF @@ROWCOUNT<>1 THROW 58903,'This copy was not found, or has already been collected.',1;
 COMMIT; SELECT * FROM dbo.LeaveCertificateIssues WHERE LeaveCertificateIssueId=@LeaveCertificateIssueId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetCharacterCertificatePrintData @UserId int,@CertificateRecordId int,@CharacterCertificateIssueId int=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Print';
 IF @CharacterCertificateIssueId IS NULL
  SELECT @CharacterCertificateIssueId=MAX(CharacterCertificateIssueId) FROM dbo.CharacterCertificateIssues WHERE CertificateRecordId=@CertificateRecordId;
 IF NOT EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssues WHERE CharacterCertificateIssueId=@CharacterCertificateIssueId AND CertificateRecordId=@CertificateRecordId)
  THROW 58301,'The selected issuance does not belong to this certificate.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@CharacterCertificateIssueId AND ExamType IS NULL)
  THROW 58010,'Review and save the original SSC/HSSC type before printing.',1;

 SELECT C.CertificateRecordId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.AdmissionNumber,P.Gender,P.LastExamYear,P.RollNumber,P.Remarks,P.CertificateDate,
 I.IssuedAt DateOfIssue,P.ExamType,CASE P.ExamType WHEN N'SSC' THEN N'S.S.C (Matric)' WHEN N'HSSC' THEN N'H.S.S.C (Intermediate)' END ExamTypeDisplay
 FROM dbo.CharacterCertificates C JOIN dbo.CharacterCertificateIssues I ON I.CertificateRecordId=C.CertificateRecordId
 JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=I.CharacterCertificateIssueId
 WHERE C.CertificateRecordId=@CertificateRecordId AND I.CharacterCertificateIssueId=@CharacterCertificateIssueId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetDateOfBirthCertificatePrintData @UserId int,@CertificateRecordId int,@DateOfBirthCertificateIssueId int=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Print';
 IF @CertificateRecordId<=0 THROW 58800,'A valid date of birth certificate is required.',1;
 SELECT C.CertificateRecordId,I.DateOfBirthCertificateIssueId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.AdmissionNumber,P.Gender,P.DateOfBirth,
 CONVERT(nvarchar(10),P.DateOfBirth,105) DateOfBirthInFigures,
 CONCAT(RIGHT(N'0'+CONVERT(nvarchar(2),DAY(P.DateOfBirth)),2),
 CASE WHEN DAY(P.DateOfBirth) IN(1,21,31) THEN N'st' WHEN DAY(P.DateOfBirth) IN(2,22) THEN N'nd' WHEN DAY(P.DateOfBirth) IN(3,23) THEN N'rd' ELSE N'th' END,
 N', ',DATENAME(MONTH,P.DateOfBirth),N', ',CONVERT(nvarchar(4),YEAR(P.DateOfBirth))) DateOfBirthInWords,
 P.CertificateDate,I.IssuedAt DateOfIssue,SP.SchoolName
 FROM dbo.DateOfBirthCertificates C
 CROSS APPLY(SELECT TOP(1) * FROM dbo.DateOfBirthCertificateIssues WHERE CertificateRecordId=C.CertificateRecordId
 AND (@DateOfBirthCertificateIssueId IS NULL OR DateOfBirthCertificateIssueId=@DateOfBirthCertificateIssueId)
 ORDER BY DateOfBirthCertificateIssueId) I
 JOIN dbo.DateOfBirthCertificateIssueSnapshots P ON P.DateOfBirthCertificateIssueId=I.DateOfBirthCertificateIssueId
 LEFT JOIN dbo.SchoolProfile SP ON SP.SchoolProfileId=1 WHERE C.CertificateRecordId=@CertificateRecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetLeaveCertificatePrintData @UserId int,@LeaveRecordId int,@LeaveCertificateIssueId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Students.Leave';
 IF @LeaveCertificateIssueId=0
  SELECT @LeaveCertificateIssueId=MIN(LeaveCertificateIssueId) FROM dbo.LeaveCertificateIssues WHERE LeaveRecordId=@LeaveRecordId;
 IF NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssues WHERE LeaveRecordId=@LeaveRecordId AND LeaveCertificateIssueId=@LeaveCertificateIssueId)
  THROW 57311,'The selected issuance does not belong to this leave certificate.',1;
 SELECT C.LeaveRecordId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.Gender,P.AdmissionNumber,P.DateOfBirth,P.DateOfAdmission,
  P.ClassStudied,P.ClassPromotedTo,P.LeaveDate,P.AcademicRecord,P.AttendanceRecord,P.Remarks,I.IssuedAt DateOfIssue,I.IssueType CopyType
 FROM dbo.LeaveCertificates C JOIN dbo.LeaveCertificateIssues I ON I.LeaveRecordId=C.LeaveRecordId
 JOIN dbo.LeaveCertificateIssueSnapshots P ON P.LeaveCertificateIssueId=I.LeaveCertificateIssueId
 WHERE C.LeaveRecordId=@LeaveRecordId AND I.LeaveCertificateIssueId=@LeaveCertificateIssueId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetRegularStudentCertificatePrintData @UserId int,@RegularStudentCertificateId int,@RegularStudentCertificateIssueId int=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Print';
 IF @RegularStudentCertificateId<=0 THROW 58520,'A valid regular student certificate is required.',1;
 SELECT C.RegularStudentCertificateId,I.RegularStudentCertificateIssueId,C.AdmissionId,C.CertificateNumber,
 I.StudentName,I.FatherName,I.Gender,I.AdmissionNumber,I.DateOfBirth,I.ClassName,I.CertificateDate,I.IssuedAt DateOfIssue,
 LTRIM(RTRIM(CONCAT(SP.SchoolName,N' ',CP.CampusName,N' ',COALESCE(NULLIF(LTRIM(RTRIM(CP.City)),N''),SP.City)))) InstitutionLine
 FROM dbo.RegularStudentCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
 JOIN dbo.Campuses CP ON CP.CampusId=S.AdmissionCampusId CROSS JOIN dbo.SchoolProfile SP
 CROSS APPLY(SELECT TOP(1) * FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=C.RegularStudentCertificateId
 AND (@RegularStudentCertificateIssueId IS NULL OR RegularStudentCertificateIssueId=@RegularStudentCertificateIssueId)
 ORDER BY RegularStudentCertificateIssueId) I
 WHERE C.RegularStudentCertificateId=@RegularStudentCertificateId AND SP.SchoolProfileId=1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CollectCertificateCopy @UserId int,@Kind nvarchar(30),@CopyId int,@CollectedBy nvarchar(200),@ExpectedVersion binary(8)=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF NULLIF(LTRIM(RTRIM(@CollectedBy)),N'') IS NULL THROW 58901,'Enter the person collecting the certificate.',1;
 DECLARE @Table sysname,@Key sysname,@Sql nvarchar(max);
 SELECT @Table=T,@Key=K FROM (VALUES
 (N'Sports',N'SportsCertificates',N'SportsCertificateId'),(N'NOC',N'NoObjectionCertificates',N'NoObjectionCertificateId'),
 (N'Provisional',N'ProvisionalCertificates',N'ProvisionalCertificateId'),(N'Lecturer',N'RegularLecturerCertificates',N'RegularLecturerCertificateId'),
 (N'Bank',N'BankAccountOpeningCertificates',N'BankAccountOpeningCertificateId'),(N'Regular',N'RegularStudentCertificateIssues',N'RegularStudentCertificateIssueId')) X(N,T,K) WHERE N=@Kind;
 IF @Table IS NULL THROW 58902,'Unknown certificate type.',1;
 SET @Sql=N'UPDATE dbo.'+QUOTENAME(@Table)+N' SET CollectedAt=SYSDATETIME(),CollectedBy=LTRIM(RTRIM(@Name)),CollectedByUserId=@Actor WHERE '+QUOTENAME(@Key)+N'=@Id AND CollectedAt IS NULL AND (@Version IS NULL OR ContentVersion=@Version); IF @@ROWCOUNT<>1 THROW 58903,''This copy changed, or has already been collected. Refresh its history.'',1;';
 EXEC sys.sp_executesql @Sql,N'@Id int,@Name nvarchar(200),@Actor int,@Version binary(8)',@CopyId,@CollectedBy,@UserId,@ExpectedVersion;
END;
GO

CREATE OR ALTER PROCEDURE dbo.IssueCertificateCopy @UserId int,@Kind nvarchar(30),@CopyId int,@RequestId uniqueidentifier
AS BEGIN SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 BEGIN TRANSACTION;
 DECLARE @NewId int,@Person int,@PriorUser int,@PriorKind nvarchar(30),@PriorSource int,@LockResult int;
 DECLARE @Resource nvarchar(255)=CONCAT(N'Certificate.Copy.',CONVERT(nvarchar(36),@RequestId));
 EXEC @LockResult=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @LockResult<0 THROW 58905,'The certificate request is still processing. Check it again.',1;
 SELECT @NewId=CopyId,@PriorUser=UserId,@PriorKind=CertificateKind,@PriorSource=SourceId FROM dbo.CertificateCopyRequests WHERE RequestId=@RequestId;
 IF @NewId IS NOT NULL BEGIN
  IF @PriorUser<>@UserId OR @PriorKind<>@Kind OR @PriorSource<>@CopyId THROW 58906,'This request belongs to different certificate details.',1;
  COMMIT; SELECT @NewId CopyId; RETURN;
 END;

 IF @Kind=N'Sports' BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.SportsCertificates WITH(UPDLOCK,HOLDLOCK) WHERE SportsCertificateId=@CopyId AND (CollectedAt IS NOT NULL)) THROW 58907,'Collect the current copy before issuing again.',1;
 DECLARE @NewerSports bit=0;
 ;WITH Descendants AS (SELECT SportsCertificateId FROM dbo.SportsCertificates WITH(UPDLOCK,HOLDLOCK) WHERE PreviousCertificateId=@CopyId UNION ALL SELECT C.SportsCertificateId FROM dbo.SportsCertificates C WITH(UPDLOCK,HOLDLOCK) JOIN Descendants D ON C.PreviousCertificateId=D.SportsCertificateId) SELECT @NewerSports=1 FROM Descendants OPTION(MAXRECURSION 32767);
 IF @NewerSports=1 THROW 58908,'A newer copy already exists. Open it in history.',1;
 INSERT dbo.SportsCertificates(AdmissionId,StudentName,FatherName,SportName,Performance,CertificateDate,CreatedByUserId,Gender,PreviousCertificateId) SELECT C.AdmissionId,P.StudentName,P.FatherName,C.SportName,C.Performance,CONVERT(date,SYSDATETIME()),@UserId,P.Gender,@CopyId FROM dbo.SportsCertificates C JOIN dbo.Students P ON P.AdmissionId=C.AdmissionId WHERE C.SportsCertificateId=@CopyId;
 SET @NewId=CONVERT(int,SCOPE_IDENTITY());
 END

 ELSE IF @Kind=N'NOC' BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WITH(UPDLOCK,HOLDLOCK) WHERE NoObjectionCertificateId=@CopyId AND (CollectedAt IS NOT NULL)) THROW 58907,'Collect the current copy before issuing again.',1;
 DECLARE @NewerNOC bit=0;
 ;WITH Descendants AS (SELECT NoObjectionCertificateId FROM dbo.NoObjectionCertificates WITH(UPDLOCK,HOLDLOCK) WHERE PreviousCertificateId=@CopyId UNION ALL SELECT C.NoObjectionCertificateId FROM dbo.NoObjectionCertificates C WITH(UPDLOCK,HOLDLOCK) JOIN Descendants D ON C.PreviousCertificateId=D.NoObjectionCertificateId) SELECT @NewerNOC=1 FROM Descendants OPTION(MAXRECURSION 32767);
 IF @NewerNOC=1 THROW 58908,'A newer copy already exists. Open it in history.',1;
 INSERT dbo.NoObjectionCertificates(AdmissionId,StudentName,FatherName,CertificateDate,CreatedByUserId,Gender,PreviousCertificateId) SELECT C.AdmissionId,P.StudentName,P.FatherName,CONVERT(date,SYSDATETIME()),@UserId,P.Gender,@CopyId FROM dbo.NoObjectionCertificates C JOIN dbo.Students P ON P.AdmissionId=C.AdmissionId WHERE C.NoObjectionCertificateId=@CopyId;
 SET @NewId=CONVERT(int,SCOPE_IDENTITY());
 END

 ELSE IF @Kind=N'Provisional' BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WITH(UPDLOCK,HOLDLOCK) WHERE ProvisionalCertificateId=@CopyId AND (CollectedAt IS NOT NULL)) THROW 58907,'Collect the current copy before issuing again.',1;
 DECLARE @NewerProvisional bit=0;
 ;WITH Descendants AS (SELECT ProvisionalCertificateId FROM dbo.ProvisionalCertificates WITH(UPDLOCK,HOLDLOCK) WHERE PreviousCertificateId=@CopyId UNION ALL SELECT C.ProvisionalCertificateId FROM dbo.ProvisionalCertificates C WITH(UPDLOCK,HOLDLOCK) JOIN Descendants D ON C.PreviousCertificateId=D.ProvisionalCertificateId) SELECT @NewerProvisional=1 FROM Descendants OPTION(MAXRECURSION 32767);
 IF @NewerProvisional=1 THROW 58908,'A newer copy already exists. Open it in history.',1;
 IF EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId WHERE C.ProvisionalCertificateId=@CopyId AND S.DateOfBirth IS NULL) THROW 58913,'Record the student birth date before preparing a new provisional copy.',1;
 INSERT dbo.ProvisionalCertificates(AdmissionId,StudentName,FatherName,Gender,DateOfBirth,ExamType,ExamYear,BoardName,RollNumber,MarksObtained,MarksOutOf,Grade,OtherSubjects,CertificateDate,CreatedByUserId,PreviousCertificateId) SELECT C.AdmissionId,P.StudentName,P.FatherName,P.Gender,P.DateOfBirth,C.ExamType,C.ExamYear,C.BoardName,C.RollNumber,C.MarksObtained,C.MarksOutOf,C.Grade,C.OtherSubjects,CONVERT(date,SYSDATETIME()),@UserId,@CopyId FROM dbo.ProvisionalCertificates C JOIN dbo.Students P ON P.AdmissionId=C.AdmissionId WHERE C.ProvisionalCertificateId=@CopyId;
 SET @NewId=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.ProvisionalCertificateSubjects(ProvisionalCertificateId,SubjectId,SubjectName,DisplayOrder) SELECT @NewId,SubjectId,SubjectName,DisplayOrder FROM dbo.ProvisionalCertificateSubjects WHERE ProvisionalCertificateId=@CopyId;
 END

 ELSE IF @Kind=N'Lecturer' BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.RegularLecturerCertificates WITH(UPDLOCK,HOLDLOCK) WHERE RegularLecturerCertificateId=@CopyId AND (CollectedAt IS NOT NULL)) THROW 58907,'Collect the current copy before issuing again.',1;
 DECLARE @NewerLecturer bit=0;
 ;WITH Descendants AS (SELECT RegularLecturerCertificateId FROM dbo.RegularLecturerCertificates WITH(UPDLOCK,HOLDLOCK) WHERE PreviousCertificateId=@CopyId UNION ALL SELECT C.RegularLecturerCertificateId FROM dbo.RegularLecturerCertificates C WITH(UPDLOCK,HOLDLOCK) JOIN Descendants D ON C.PreviousCertificateId=D.RegularLecturerCertificateId) SELECT @NewerLecturer=1 FROM Descendants OPTION(MAXRECURSION 32767);
 IF @NewerLecturer=1 THROW 58908,'A newer copy already exists. Open it in history.',1;
 INSERT dbo.RegularLecturerCertificates(StaffId,StaffName,FatherName,Gender,RoleTitle,SubjectTaught,CertificateDate,CreatedByUserId,PreviousCertificateId) SELECT C.StaffId,P.StaffName,P.FatherName,P.Gender,C.RoleTitle,C.SubjectTaught,CONVERT(date,SYSDATETIME()),@UserId,@CopyId FROM dbo.RegularLecturerCertificates C JOIN dbo.Staff P ON P.StaffId=C.StaffId WHERE C.RegularLecturerCertificateId=@CopyId;
 SET @NewId=CONVERT(int,SCOPE_IDENTITY());
 END

 ELSE IF @Kind=N'Bank' BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.BankAccountOpeningCertificates WITH(UPDLOCK,HOLDLOCK) WHERE BankAccountOpeningCertificateId=@CopyId AND (CollectedAt IS NOT NULL)) THROW 58907,'Collect the current copy before issuing again.',1;
 DECLARE @NewerBank bit=0;
 ;WITH Descendants AS (SELECT BankAccountOpeningCertificateId FROM dbo.BankAccountOpeningCertificates WITH(UPDLOCK,HOLDLOCK) WHERE PreviousCertificateId=@CopyId UNION ALL SELECT C.BankAccountOpeningCertificateId FROM dbo.BankAccountOpeningCertificates C WITH(UPDLOCK,HOLDLOCK) JOIN Descendants D ON C.PreviousCertificateId=D.BankAccountOpeningCertificateId) SELECT @NewerBank=1 FROM Descendants OPTION(MAXRECURSION 32767);
 IF @NewerBank=1 THROW 58908,'A newer copy already exists. Open it in history.',1;
 INSERT dbo.BankAccountOpeningCertificates(StaffId,StaffName,FatherName,Gender,CNIC,Designation,BankName,BranchName,MonthlyIncome,Purpose,CertificateDate,CreatedByUserId,PreviousCertificateId) SELECT C.StaffId,P.StaffName,P.FatherName,P.Gender,P.CNIC,C.Designation,C.BankName,C.BranchName,C.MonthlyIncome,C.Purpose,CONVERT(date,SYSDATETIME()),@UserId,@CopyId FROM dbo.BankAccountOpeningCertificates C JOIN dbo.Staff P ON P.StaffId=C.StaffId WHERE C.BankAccountOpeningCertificateId=@CopyId;
 SET @NewId=CONVERT(int,SCOPE_IDENTITY());
 END
 ELSE THROW 58902,'Unknown certificate type.',1;
 INSERT dbo.CertificateCopyRequests(RequestId,UserId,CertificateKind,SourceId,CopyId) VALUES(@RequestId,@UserId,@Kind,@CopyId,@NewId);
 COMMIT; SELECT @NewId CopyId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetSportsCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        SC.SportsCertificateId,
        SC.AdmissionId,

        SC.StudentName,
        SC.FatherName,
        SC.SportName,
        SC.Performance,
        SC.CertificateDate,

        SC.CreatedAt,
        SC.UpdatedAt,
 SC.AdmissionNumber,SC.ContentVersion,SC.CollectedAt,SC.CollectedBy,SC.PreviousCertificateId,CASE WHEN SC.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.SportsCertificates AS SC

    WHERE
        @AdmissionId = 0
        OR SC.AdmissionId = @AdmissionId

    ORDER BY
        SC.SportsCertificateId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetSportsCertificatePrintData
    @UserId INT,
    @SportsCertificateId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @SportsCertificateId <= 0
        THROW 58420, 'A valid sports certificate is required.', 1;

    SELECT
        SC.SportsCertificateId, SC.AdmissionNumber,
        SC.AdmissionId,

        SC.CertificateNumber AS CertificateNumber,

        SC.StudentName,
        SC.FatherName,
        SC.SportName,
        SC.Performance,
        SC.CertificateDate,
        SC.CreatedAt AS DateOfIssue,
 SC.Gender
 FROM dbo.SportsCertificates AS SC
    WHERE SC.SportsCertificateId = @SportsCertificateId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateSportsCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE, @SportName NVARCHAR (200), @Performance NVARCHAR (1000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58400, 'A valid student is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58401, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58402, 'Certificate date cannot be in the future.', 1;
    IF @SportName IS NULL
       OR LTRIM(RTRIM(@SportName)) = N''
        THROW 58403, 'Sport Name is required.', 1;
    IF @Performance IS NULL
       OR LTRIM(RTRIM(@Performance)) = N''
        THROW 58404, 'Performance is required.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @SportsCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58405, 'The student was not found.', 1;
    DECLARE @ExistingCertificateId int;
    SELECT TOP(1) @ExistingCertificateId=SportsCertificateId FROM dbo.SportsCertificates C WITH(UPDLOCK,HOLDLOCK) WHERE C.AdmissionId=@AdmissionId AND ISNULL(CONVERT(nvarchar(max),C.CertificateDate),N'')=ISNULL(CONVERT(nvarchar(max),@CertificateDate),N'') AND ISNULL(CONVERT(nvarchar(max),C.SportName),N'')=ISNULL(CONVERT(nvarchar(max),@SportName),N'') AND ISNULL(CONVERT(nvarchar(max),C.Performance),N'')=ISNULL(CONVERT(nvarchar(max),@Performance),N'') ORDER BY C.SportsCertificateId DESC;
    IF @ExistingCertificateId IS NOT NULL BEGIN COMMIT; SELECT @ExistingCertificateId SportsCertificateId,@AdmissionId AdmissionId; RETURN; END;
    INSERT  dbo.SportsCertificates (AdmissionId, StudentName, FatherName, SportName, Performance, CertificateDate, CreatedByUserId)
    VALUES                        (@AdmissionId, @StudentName, @FatherName, LTRIM(RTRIM(@SportName)), LTRIM(RTRIM(@Performance)), @CertificateDate, @UserId);
    SET @SportsCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @SportsCertificateId AS SportsCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.GetNoObjectionCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        NOC.NoObjectionCertificateId,
        NOC.AdmissionId,

        NOC.StudentName,
        NOC.FatherName,
        NOC.CertificateDate,

        NOC.CreatedAt,
        NOC.UpdatedAt,
 NOC.AdmissionNumber,NOC.ContentVersion,NOC.CollectedAt,NOC.CollectedBy,NOC.PreviousCertificateId,CASE WHEN NOC.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.NoObjectionCertificates AS NOC

    WHERE
        @AdmissionId = 0
        OR NOC.AdmissionId = @AdmissionId

    ORDER BY
        NOC.NoObjectionCertificateId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetNoObjectionCertificatePrintData
    @UserId INT,
    @NoObjectionCertificateId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @NoObjectionCertificateId <= 0
        THROW 58620, 'A valid NOC is required.', 1;

    SELECT
        NOC.NoObjectionCertificateId, NOC.AdmissionNumber,
        NOC.AdmissionId,

        NOC.CertificateNumber AS CertificateNumber,

        NOC.StudentName,
        NOC.FatherName,
        NOC.CertificateDate,
        NOC.CreatedAt AS DateOfIssue,
 NOC.Gender
 FROM dbo.NoObjectionCertificates AS NOC
    WHERE NOC.NoObjectionCertificateId = @NoObjectionCertificateId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateNoObjectionCertificate
@UserId INT, @AdmissionId INT, @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58600, 'A valid student is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58601, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58602, 'Certificate date cannot be in the future.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @NoObjectionCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58603, 'The student was not found.', 1;
    DECLARE @ExistingCertificateId int;
    SELECT TOP(1) @ExistingCertificateId=NoObjectionCertificateId FROM dbo.NoObjectionCertificates C WITH(UPDLOCK,HOLDLOCK) WHERE C.AdmissionId=@AdmissionId AND ISNULL(CONVERT(nvarchar(max),C.CertificateDate),N'')=ISNULL(CONVERT(nvarchar(max),@CertificateDate),N'') ORDER BY C.NoObjectionCertificateId DESC;
    IF @ExistingCertificateId IS NOT NULL BEGIN COMMIT; SELECT @ExistingCertificateId NoObjectionCertificateId,@AdmissionId AdmissionId; RETURN; END;
    INSERT  dbo.NoObjectionCertificates (AdmissionId, StudentName, FatherName, CertificateDate, CreatedByUserId)
    VALUES                             (@AdmissionId, @StudentName, @FatherName, @CertificateDate, @UserId);
    SET @NoObjectionCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @NoObjectionCertificateId AS NoObjectionCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.GetProvisionalCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        PC.ProvisionalCertificateId,
        PC.AdmissionId,

        PC.StudentName,
        PC.FatherName,

        PC.ExamType,
        CASE PC.ExamType
            WHEN N'SSC'   THEN N'SSC (Matric)'
            WHEN N'HSSC1' THEN N'HSSC Part I'
            WHEN N'HSSC2' THEN N'HSSC Part II'
        END AS ExamTypeDisplay,

        PC.ExamYear,
        PC.BoardName,
        PC.RollNumber,
        PC.MarksObtained,
        PC.MarksOutOf,
        PC.Grade,
        PC.CertificateDate,

        PC.CreatedAt,
        PC.UpdatedAt,

        -- Appended at the END: the editor needs the stored value when a row is
        -- selected, or Update writes the box over it.
        PC.OtherSubjects,
 PC.AdmissionNumber,PC.DateOfBirth,PC.ContentVersion,PC.CollectedAt,PC.CollectedBy,PC.PreviousCertificateId,CASE WHEN PC.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.ProvisionalCertificates AS PC

    WHERE
        @AdmissionId = 0
        OR PC.AdmissionId = @AdmissionId

    ORDER BY
        PC.ProvisionalCertificateId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetProvisionalCertificatePrintData
@UserId INT, @ProvisionalCertificateId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Print';
    IF @ProvisionalCertificateId <= 0
        THROW 58840, 'A valid certificate is required.', 1;
    DECLARE @OtherSubjects AS NVARCHAR (300);
    DECLARE @PickedCount AS INT;
    SELECT @OtherSubjects = PC.OtherSubjects
    FROM   dbo.ProvisionalCertificates AS PC
    WHERE  PC.ProvisionalCertificateId = @ProvisionalCertificateId;
    SELECT @PickedCount = COUNT(*)
    FROM   dbo.ProvisionalCertificateSubjects
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    DECLARE @SubjectsPassedLine AS NVARCHAR (MAX);
    WITH   PickedSubjects
    AS     (SELECT PCS.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY PCS.DisplayOrder, PCS.SubjectName) AS Seq
            FROM   dbo.ProvisionalCertificateSubjects AS PCS
            WHERE  PCS.ProvisionalCertificateId = @ProvisionalCertificateId),
           OtherSubjectsList
    AS     (SELECT LTRIM(RTRIM(SPL.value)) AS SubjectName,
                   @PickedCount + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Seq
            FROM   STRING_SPLIT (COALESCE (@OtherSubjects, N''), N',') AS SPL
            WHERE  LTRIM(RTRIM(SPL.value)) <> N''),
           AllSubjects
    AS     (SELECT SubjectName,
                   Seq
            FROM   PickedSubjects
            UNION ALL
            SELECT SubjectName,
                   Seq
            FROM   OtherSubjectsList)
    SELECT @SubjectsPassedLine = STRING_AGG(CONCAT(N'(', Seq, N')  ', SubjectName), N'   ') WITHIN GROUP (ORDER BY Seq)
    FROM   AllSubjects;
    SELECT PC.ProvisionalCertificateId, PC.AdmissionNumber,
           PC.AdmissionId,
           PC.CertificateNumber AS CertificateNumber,
           PC.StudentName,
           PC.FatherName,
           PC.Gender,
           PC.DateOfBirth,
           dbo.fn_DateToWords(PC.DateOfBirth) AS DateOfBirthWords,
           PC.ExamType,
           CASE PC.ExamType WHEN N'SSC' THEN N'Secondary School Certificate' WHEN N'HSSC1' THEN N'Higher Secondary School Certificate Part-I' WHEN N'HSSC2' THEN N'Higher Secondary School Certificate Part-II' END AS ExamTypeLabel,
           PC.ExamYear,
           PC.BoardName,
           PC.RollNumber,
           PC.MarksObtained,
           PC.MarksOutOf,
           PC.Grade,
           COALESCE (@SubjectsPassedLine, N'') AS SubjectsPassedLine,
           PC.CertificateDate,
           PC.CreatedAt AS DateOfIssue,
           SP.SchoolName AS InstitutionLine
    FROM   dbo.ProvisionalCertificates AS PC CROSS JOIN dbo.SchoolProfile AS SP
    WHERE  PC.ProvisionalCertificateId = @ProvisionalCertificateId
           AND SP.SchoolProfileId = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.CreateProvisionalCertificate
@UserId INT, @AdmissionId INT, @ExamType NVARCHAR (10), @ExamYear INT, @BoardName NVARCHAR (150), @RollNumber NVARCHAR (30), @MarksObtained INT, @MarksOutOf INT, @Grade NVARCHAR (10), @OtherSubjects NVARCHAR (300), @CertificateDate DATE, @SubjectIds NVARCHAR (MAX)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58800, 'A valid student is required.', 1;
    IF @ExamType NOT IN (N'SSC', N'HSSC1', N'HSSC2')
        THROW 58801, 'A valid exam type is required.', 1;
    IF @ExamYear IS NULL
       OR @ExamYear < 1990
       OR @ExamYear > YEAR(SYSDATETIME()) + 1
        THROW 58802, 'A valid exam year is required.', 1;
    IF @BoardName IS NULL
       OR LTRIM(RTRIM(@BoardName)) = N''
        THROW 58803, 'Board name is required.', 1;
    IF @RollNumber IS NULL
       OR LTRIM(RTRIM(@RollNumber)) = N''
        THROW 58804, 'Roll number is required.', 1;
    IF @MarksObtained IS NULL
       OR @MarksObtained < 0
        THROW 58805, 'Marks obtained is required.', 1;
    IF @MarksOutOf IS NULL
       OR @MarksOutOf <= 0
        THROW 58806, 'Marks out of is required.', 1;
    IF @MarksObtained > @MarksOutOf
        THROW 58807, 'Marks obtained cannot exceed marks out of.', 1;
    IF @CertificateDate IS NULL
        THROW 58808, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58809, 'Certificate date cannot be in the future.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @Gender AS NVARCHAR (20), @DateOfBirth AS DATE, @ProvisionalCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @Gender = S.Gender,
           @DateOfBirth = S.DateOfBirth
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58810, 'The student was not found.', 1;
    IF @DateOfBirth IS NULL THROW 58913,'Record the student date of birth before creating this certificate.',1;
    DECLARE @ExistingCertificateId int;
    SELECT TOP(1) @ExistingCertificateId=ProvisionalCertificateId FROM dbo.ProvisionalCertificates C WITH(UPDLOCK,HOLDLOCK) WHERE C.AdmissionId=@AdmissionId AND ISNULL(CONVERT(nvarchar(max),C.ExamType),N'')=ISNULL(CONVERT(nvarchar(max),@ExamType),N'') AND ISNULL(CONVERT(nvarchar(max),C.ExamYear),N'')=ISNULL(CONVERT(nvarchar(max),@ExamYear),N'') AND ISNULL(CONVERT(nvarchar(max),C.BoardName),N'')=ISNULL(CONVERT(nvarchar(max),@BoardName),N'') AND ISNULL(CONVERT(nvarchar(max),C.RollNumber),N'')=ISNULL(CONVERT(nvarchar(max),@RollNumber),N'') ORDER BY C.ProvisionalCertificateId DESC;
    IF @ExistingCertificateId IS NOT NULL BEGIN COMMIT; SELECT @ExistingCertificateId ProvisionalCertificateId,@AdmissionId AdmissionId; RETURN; END;
    INSERT  dbo.ProvisionalCertificates (AdmissionId, StudentName, FatherName, Gender, DateOfBirth, ExamType, ExamYear, BoardName, RollNumber, MarksObtained, MarksOutOf, Grade, OtherSubjects, CertificateDate, CreatedByUserId)
    VALUES                             (@AdmissionId, @StudentName, @FatherName, @Gender, @DateOfBirth, @ExamType, @ExamYear, LTRIM(RTRIM(@BoardName)), LTRIM(RTRIM(@RollNumber)), @MarksObtained, @MarksOutOf, NULLIF (LTRIM(RTRIM(@Grade)), N''), NULLIF (LTRIM(RTRIM(@OtherSubjects)), N''), @CertificateDate, @UserId);
    SET @ProvisionalCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    IF @SubjectIds IS NOT NULL
       AND LTRIM(RTRIM(@SubjectIds)) <> N''
        BEGIN
            INSERT dbo.ProvisionalCertificateSubjects (ProvisionalCertificateId, SubjectId, SubjectName, DisplayOrder)
            SELECT @ProvisionalCertificateId,
                   SU.SubjectId,
                   SU.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY SU.SubjectName, SU.SubjectId)
            FROM   STRING_SPLIT (@SubjectIds, N',') AS SPL
                   INNER JOIN
                   dbo.Subjects AS SU
                   ON SU.SubjectId = TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value)))
            WHERE  TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value))) IS NOT NULL;
        END
    COMMIT TRANSACTION;
    SELECT @ProvisionalCertificateId AS ProvisionalCertificateId,
           @AdmissionId AS AdmissionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.GetRegularLecturerCertificateRecords
    @UserId INT,
    @StaffId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        RLC.RegularLecturerCertificateId,
        RLC.StaffId,

        RLC.StaffName,
        RLC.FatherName,
        RLC.RoleTitle,
        RLC.SubjectTaught,
        RLC.CertificateDate,

        RLC.CreatedAt,
        RLC.UpdatedAt,
 RLC.ContentVersion,RLC.CollectedAt,RLC.CollectedBy,RLC.PreviousCertificateId,CASE WHEN RLC.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.RegularLecturerCertificates AS RLC

    WHERE
        @StaffId = 0
        OR RLC.StaffId = @StaffId

    ORDER BY
        RLC.RegularLecturerCertificateId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetRegularLecturerCertificatePrintData
    @UserId INT,
    @RegularLecturerCertificateId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @RegularLecturerCertificateId <= 0
        THROW 58720, 'A valid certificate is required.', 1;

    SELECT
        RLC.RegularLecturerCertificateId,
        RLC.StaffId,

        RLC.CertificateNumber AS CertificateNumber,

        RLC.StaffName,
        RLC.FatherName,
        RLC.Gender,
        RLC.RoleTitle,
        RLC.SubjectTaught,
        RLC.CertificateDate,
        RLC.CreatedAt AS DateOfIssue,

        SP.SchoolName AS InstitutionLine

    FROM dbo.RegularLecturerCertificates AS RLC

    CROSS JOIN dbo.SchoolProfile AS SP

    WHERE RLC.RegularLecturerCertificateId = @RegularLecturerCertificateId
      AND SP.SchoolProfileId = 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateRegularLecturerCertificate
@UserId INT, @StaffId INT, @RoleTitle NVARCHAR (30), @SubjectTaught NVARCHAR (100), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @StaffId <= 0
        THROW 58700, 'A valid staff member is required.', 1;
    IF @RoleTitle IS NULL
       OR LTRIM(RTRIM(@RoleTitle)) = N''
        THROW 58701, 'Designation (Teacher/Lecturer) is required.', 1;
    IF @SubjectTaught IS NULL
       OR LTRIM(RTRIM(@SubjectTaught)) = N''
        THROW 58702, 'Subject taught is required.', 1;
    IF @CertificateDate IS NULL
        THROW 58703, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58704, 'Certificate date cannot be in the future.', 1;
    DECLARE @StaffName AS NVARCHAR (150), @FatherName AS NVARCHAR (150), @Gender AS NVARCHAR (20), @RegularLecturerCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StaffName = S.StaffName,
           @FatherName = S.FatherName,
           @Gender = S.Gender
    FROM   dbo.Staff AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.StaffId = @StaffId;
    IF @StaffName IS NULL
        THROW 58705, 'The staff member was not found.', 1;
    DECLARE @ExistingCertificateId int;
    SELECT TOP(1) @ExistingCertificateId=RegularLecturerCertificateId FROM dbo.RegularLecturerCertificates C WITH(UPDLOCK,HOLDLOCK) WHERE C.StaffId=@StaffId AND ISNULL(CONVERT(nvarchar(max),C.RoleTitle),N'')=ISNULL(CONVERT(nvarchar(max),@RoleTitle),N'') AND ISNULL(CONVERT(nvarchar(max),C.SubjectTaught),N'')=ISNULL(CONVERT(nvarchar(max),@SubjectTaught),N'') AND ISNULL(CONVERT(nvarchar(max),C.CertificateDate),N'')=ISNULL(CONVERT(nvarchar(max),@CertificateDate),N'') ORDER BY C.RegularLecturerCertificateId DESC;
    IF @ExistingCertificateId IS NOT NULL BEGIN COMMIT; SELECT @ExistingCertificateId RegularLecturerCertificateId,@StaffId StaffId; RETURN; END;
    INSERT  dbo.RegularLecturerCertificates (StaffId, StaffName, FatherName, Gender, RoleTitle, SubjectTaught, CertificateDate, CreatedByUserId)
    VALUES                                 (@StaffId, @StaffName, @FatherName, @Gender, LTRIM(RTRIM(@RoleTitle)), LTRIM(RTRIM(@SubjectTaught)), @CertificateDate, @UserId);
    SET @RegularLecturerCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @RegularLecturerCertificateId AS RegularLecturerCertificateId,
           @StaffId AS StaffId;
END
GO

CREATE OR ALTER PROCEDURE dbo.GetBankAccountOpeningCertificateRecords
    @UserId INT,
    @StaffId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        BAC.BankAccountOpeningCertificateId,
        BAC.StaffId,

        BAC.StaffName,
        BAC.FatherName,
        BAC.Designation,
        BAC.BankName,
        BAC.BranchName,
        BAC.MonthlyIncome,
        BAC.Purpose,
        BAC.CertificateDate,

        BAC.CreatedAt,
        BAC.UpdatedAt,
 BAC.ContentVersion,BAC.CollectedAt,BAC.CollectedBy,BAC.PreviousCertificateId,CASE WHEN BAC.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.BankAccountOpeningCertificates AS BAC

    WHERE
        @StaffId = 0
        OR BAC.StaffId = @StaffId

    ORDER BY
        BAC.BankAccountOpeningCertificateId DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.GetBankAccountOpeningCertificatePrintData
    @UserId INT,
    @BankAccountOpeningCertificateId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @BankAccountOpeningCertificateId <= 0
        THROW 58820, 'A valid certificate is required.', 1;

    SELECT
        BAC.BankAccountOpeningCertificateId,
        BAC.StaffId,

        BAC.CertificateNumber AS CertificateNumber,

        BAC.StaffName,
        BAC.FatherName,
        BAC.Gender,
        BAC.CNIC,
        BAC.Designation,
        BAC.BankName,
        BAC.BranchName,
        BAC.MonthlyIncome,
        BAC.Purpose,
        BAC.CertificateDate,
        BAC.CreatedAt AS DateOfIssue,

        SP.SchoolName AS InstitutionLine

    FROM dbo.BankAccountOpeningCertificates AS BAC

    CROSS JOIN dbo.SchoolProfile AS SP

    WHERE BAC.BankAccountOpeningCertificateId = @BankAccountOpeningCertificateId
      AND SP.SchoolProfileId = 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.CreateBankAccountOpeningCertificate
@UserId INT, @StaffId INT, @Designation NVARCHAR (100), @BankName NVARCHAR (100), @BranchName NVARCHAR (150), @MonthlyIncome DECIMAL (18, 2)=NULL, @Purpose NVARCHAR (200), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @StaffId <= 0
        THROW 58800, 'A valid staff member is required.', 1;
    IF @Designation IS NULL
       OR LTRIM(RTRIM(@Designation)) = N''
        THROW 58801, 'Designation is required.', 1;
    IF @BankName IS NULL
       OR LTRIM(RTRIM(@BankName)) = N''
        THROW 58802, 'Bank name is required.', 1;
    IF @BranchName IS NULL
       OR LTRIM(RTRIM(@BranchName)) = N''
        THROW 58803, 'Branch name is required.', 1;
    IF @Purpose IS NULL
       OR LTRIM(RTRIM(@Purpose)) = N''
        THROW 58804, 'Purpose is required.', 1;
    IF @MonthlyIncome IS NOT NULL
       AND @MonthlyIncome < 0
        THROW 58805, 'Monthly income cannot be negative.', 1;
    IF @CertificateDate IS NULL
        THROW 58806, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58807, 'Certificate date cannot be in the future.', 1;
    DECLARE @StaffName AS NVARCHAR (150), @FatherName AS NVARCHAR (150), @Gender AS NVARCHAR (20), @CNIC AS NVARCHAR (20), @BankAccountOpeningCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StaffName = S.StaffName,
           @FatherName = S.FatherName,
           @Gender = S.Gender,
           @CNIC = S.CNIC
    FROM   dbo.Staff AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.StaffId = @StaffId;
    IF @StaffName IS NULL
        THROW 58808, 'The staff member was not found.', 1;
    DECLARE @ExistingCertificateId int;
    SELECT TOP(1) @ExistingCertificateId=BankAccountOpeningCertificateId FROM dbo.BankAccountOpeningCertificates C WITH(UPDLOCK,HOLDLOCK) WHERE C.StaffId=@StaffId AND ISNULL(CONVERT(nvarchar(max),C.Designation),N'')=ISNULL(CONVERT(nvarchar(max),@Designation),N'') AND ISNULL(CONVERT(nvarchar(max),C.BankName),N'')=ISNULL(CONVERT(nvarchar(max),@BankName),N'') AND ISNULL(CONVERT(nvarchar(max),C.BranchName),N'')=ISNULL(CONVERT(nvarchar(max),@BranchName),N'') AND ISNULL(CONVERT(nvarchar(max),C.MonthlyIncome),N'')=ISNULL(CONVERT(nvarchar(max),@MonthlyIncome),N'') AND ISNULL(CONVERT(nvarchar(max),C.Purpose),N'')=ISNULL(CONVERT(nvarchar(max),@Purpose),N'') AND ISNULL(CONVERT(nvarchar(max),C.CertificateDate),N'')=ISNULL(CONVERT(nvarchar(max),@CertificateDate),N'') ORDER BY C.BankAccountOpeningCertificateId DESC;
    IF @ExistingCertificateId IS NOT NULL BEGIN COMMIT; SELECT @ExistingCertificateId BankAccountOpeningCertificateId,@StaffId StaffId; RETURN; END;
    INSERT  dbo.BankAccountOpeningCertificates (StaffId, StaffName, FatherName, Gender, CNIC, Designation, BankName, BranchName, MonthlyIncome, Purpose, CertificateDate, CreatedByUserId)
    VALUES                                    (@StaffId, @StaffName, @FatherName, @Gender, @CNIC, LTRIM(RTRIM(@Designation)), LTRIM(RTRIM(@BankName)), LTRIM(RTRIM(@BranchName)), NULLIF (@MonthlyIncome, 0), LTRIM(RTRIM(@Purpose)), @CertificateDate, @UserId);
    SET @BankAccountOpeningCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    COMMIT TRANSACTION;
    SELECT @BankAccountOpeningCertificateId AS BankAccountOpeningCertificateId,
           @StaffId AS StaffId;
END
GO

ALTER TABLE dbo.SportsCertificates DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.NoObjectionCertificates DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.ProvisionalCertificates DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.RegularLecturerCertificates DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.BankAccountOpeningCertificates DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.RegularStudentCertificateIssues DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.CharacterCertificateIssues DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.DateOfBirthCertificateIssues DROP COLUMN VoidedAt,VoidReason;
GO
ALTER TABLE dbo.LeaveCertificateIssues DROP COLUMN VoidedAt,VoidReason;
GO

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'52',SYSDATETIME(),SUSER_SNAME(),N'Remove certificate cancellation columns, queries and branches; preserve students, leaving certificates, collected-copy locks and request records');
GO
