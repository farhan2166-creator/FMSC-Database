-- Persist character certificate qualification and each issued copy. Never guess legacy SSC/HSSC.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 47 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>46
 THROW 51000,'Migration 47 requires schema 46.',1;
GO
ALTER TABLE dbo.CharacterCertificates ADD ExamType nvarchar(10) NULL;
GO
ALTER TABLE dbo.CharacterCertificates ADD CONSTRAINT CK_CharacterCertificates_ExamType CHECK(ExamType IN(N'SSC',N'HSSC'));
DROP INDEX UX_CharacterCertificates_AdmissionId ON dbo.CharacterCertificates;
CREATE UNIQUE INDEX UX_CharacterCertificates_AdmissionExam ON dbo.CharacterCertificates(AdmissionId,ExamType);
CREATE TABLE dbo.CharacterCertificateIssueSnapshots(
 CharacterCertificateIssueId int NOT NULL CONSTRAINT PK_CharacterCertificateIssueSnapshots PRIMARY KEY
  CONSTRAINT FK_CharacterCertificateIssueSnapshots_Issue REFERENCES dbo.CharacterCertificateIssues(CharacterCertificateIssueId),
 PreviousIssueId int NULL CONSTRAINT FK_CharacterCertificateIssueSnapshots_Previous REFERENCES dbo.CharacterCertificateIssues(CharacterCertificateIssueId),
 ExamType nvarchar(10) NULL CONSTRAINT CK_CharacterCertificateIssueSnapshots_ExamType CHECK(ExamType IN(N'SSC',N'HSSC')),
 StudentName nvarchar(500) NOT NULL,FatherName nvarchar(500) NULL,Gender nvarchar(20) NULL,
 LastExamYear int NOT NULL,RollNumber nvarchar(50) NOT NULL,Remarks nvarchar(1000) NOT NULL,CertificateDate date NOT NULL,
 CorrectionReason nvarchar(500) NULL
);
INSERT dbo.CharacterCertificateIssueSnapshots(CharacterCertificateIssueId,StudentName,FatherName,Gender,LastExamYear,RollNumber,Remarks,CertificateDate)
 SELECT I.CharacterCertificateIssueId,C.StudentName,C.FatherName,C.Gender,C.LastExamYear,ISNULL(C.RollNumber,N''),ISNULL(C.Remarks,N''),C.CertificateDate
 FROM dbo.CharacterCertificateIssues I JOIN dbo.CharacterCertificates C ON C.CertificateRecordId=I.CertificateRecordId;
CREATE TABLE dbo.CharacterCertificateTypeReviews(
 CertificateRecordId int NOT NULL CONSTRAINT PK_CharacterCertificateTypeReviews PRIMARY KEY
  CONSTRAINT FK_CharacterCertificateTypeReviews_Certificate REFERENCES dbo.CharacterCertificates(CertificateRecordId),
 ExamType nvarchar(10) NOT NULL CHECK(ExamType IN(N'SSC',N'HSSC')),Reason nvarchar(500) NOT NULL,
 ReviewedAt datetime2 NOT NULL,ReviewedByUserId int NOT NULL REFERENCES dbo.Users(UserId)
);
ALTER TABLE dbo.MutationRequests DROP CONSTRAINT CK_MutationRequests_Kind;
ALTER TABLE dbo.MutationRequests WITH CHECK ADD CONSTRAINT CK_MutationRequests_Kind CHECK(
 (Kind=N'FeeAdjustment' AND FeePaymentId IS NOT NULL AND BankCertificateId IS NULL) OR
 (Kind=N'BankCertificateCreate' AND BankCertificateId IS NOT NULL AND FeePaymentId IS NULL) OR
 (Kind IN(N'FeeConcession',N'RegularCertificateReissue',N'BirthCertificateReissue',N'CharacterCertificateSave') AND FeePaymentId IS NULL AND BankCertificateId IS NULL));
GO
CREATE TRIGGER dbo.TR_CharacterCertificate_IssueSnapshot ON dbo.CharacterCertificateIssues AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.CharacterCertificateIssueSnapshots(CharacterCertificateIssueId,ExamType,StudentName,FatherName,Gender,LastExamYear,RollNumber,Remarks,CertificateDate)
 SELECT I.CharacterCertificateIssueId,C.ExamType,C.StudentName,C.FatherName,C.Gender,C.LastExamYear,ISNULL(C.RollNumber,N''),ISNULL(C.Remarks,N''),C.CertificateDate
 FROM inserted I JOIN dbo.CharacterCertificates C ON C.CertificateRecordId=I.CertificateRecordId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CreateCharacterCertificate
 @UserId int,@AdmissionId int,@CertificateDate date,@RollNumber nvarchar(50)=NULL,@Remarks nvarchar(1000)=NULL,@ExamType nvarchar(10)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @ExamType IS NULL OR @ExamType NOT IN(N'SSC',N'HSSC') THROW 58009,'Choose SSC or HSSC. This type is saved with the certificate.',1;
 IF @CertificateDate IS NULL OR @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58002,'Choose a certificate date that is not in the future.',1;
 IF NULLIF(LTRIM(RTRIM(@RollNumber)),N'') IS NULL THROW 58003,'BISE Roll Number is required.',1;
 IF NULLIF(LTRIM(RTRIM(@Remarks)),N'') IS NULL THROW 58004,'Remarks are required.',1;
 BEGIN TRANSACTION;
 DECLARE @Name nvarchar(500),@Father nvarchar(500),@Gender nvarchar(20),@Year int,@Certificate int,@Issue int;
 SELECT @Name=StudentName,@Father=FatherName,@Gender=Gender FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @Name IS NULL THROW 58006,'The student was not found.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND ExamType IS NULL)
  THROW 58010,'Review the existing certificate and save its SSC/HSSC type before creating another certificate.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId AND ExamType=@ExamType)
  THROW 58005,'This student already has this exam-type certificate. Select it to issue again or correct its details.',1;
 SELECT TOP(1) @Year=YEAR(A.EndDate) FROM dbo.Enrollments E JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId
 WHERE E.AdmissionId=@AdmissionId ORDER BY E.EnrollmentDate DESC,E.EnrollmentId DESC;
 IF @Year IS NULL THROW 58007,'The student has no academic year to determine the examination year from.',1;
 INSERT dbo.CharacterCertificates(AdmissionId,StudentName,FatherName,Gender,LastExamYear,RollNumber,Remarks,CertificateDate,CreatedByUserId,ExamType)
 VALUES(@AdmissionId,@Name,@Father,@Gender,@Year,LTRIM(RTRIM(@RollNumber)),LTRIM(RTRIM(@Remarks)),@CertificateDate,@UserId,@ExamType);
 SET @Certificate=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.CharacterCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId) VALUES(@Certificate,SYSDATETIME(),@UserId);
 SET @Issue=CONVERT(int,SCOPE_IDENTITY()); COMMIT;
 SELECT @Certificate CertificateRecordId,@Issue CharacterCertificateIssueId,@Issue IssueId,@AdmissionId AdmissionId,@CertificateDate CertificateDate;
END;
GO
CREATE OR ALTER FUNCTION dbo.CertificateAdmissionFingerprint(@AdmissionId int) RETURNS varbinary(32)
AS
BEGIN
 RETURN (SELECT HASHBYTES('SHA2_256',CONCAT(N'Name:',DATALENGTH(StudentName),N':',StudentName,
  N'Father:',ISNULL(DATALENGTH(FatherName),-1),N':',FatherName,N'Gender:',ISNULL(DATALENGTH(Gender),-1),N':',Gender,
  N'DOB:',COALESCE(CONVERT(nvarchar(10),DateOfBirth,23),N'NULL'),N'AdmissionDate:',COALESCE(CONVERT(nvarchar(10),AdmissionDate,23),N'NULL'))) FROM dbo.Students WHERE AdmissionId=@AdmissionId);
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
CREATE OR ALTER PROCEDURE dbo.SaveCharacterCertificateOperation
 @UserId int,@Action nvarchar(20),@CertificateRecordId int=0,@AdmissionId int=0,@ExpectedLatestIssueId int=0,
 @ExamType nvarchar(10)=NULL,@CertificateDate date=NULL,@RollNumber nvarchar(50)=NULL,@Remarks nvarchar(1000)=NULL,
 @Reason nvarchar(500)=NULL,@ExpectedSourceHash varbinary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @Action=N'Create'
 BEGIN
  EXEC dbo.CreateCharacterCertificate @UserId,@AdmissionId,@CertificateDate,@RollNumber,@Remarks,@ExamType; RETURN;
 END;
 IF @Action=N'Issue'
 BEGIN
  EXEC dbo.IssueCharacterCertificate @UserId,@CertificateRecordId,@Remarks,@ExpectedLatestIssueId,@ExpectedSourceHash; RETURN;
 END;
 IF @Action NOT IN(N'Correct',N'ReviewType') OR @Action IS NULL THROW 58011,'Unknown certificate action.',1;
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 58012,'Enter the reason for this correction or type review.',1;
 IF @ExamType IS NULL OR @ExamType NOT IN(N'SSC',N'HSSC') THROW 58009,'Choose SSC or HSSC.',1;
 BEGIN TRANSACTION;
 DECLARE @StoredType nvarchar(10),@Last int,@Issue int,@Owner int;
 SELECT @Owner=AdmissionId FROM dbo.CharacterCertificates WHERE CertificateRecordId=@CertificateRecordId;
 -- All operations on this student's certificate identities use the same first lock.
 SELECT @Owner=AdmissionId FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Owner;
 SELECT @AdmissionId=AdmissionId,@StoredType=ExamType FROM dbo.CharacterCertificates WITH(UPDLOCK,HOLDLOCK) WHERE CertificateRecordId=@CertificateRecordId;
 IF @Owner IS NULL OR @AdmissionId<=0 THROW 58101,'The character certificate was not found.',1;
 SELECT @Last=MAX(CharacterCertificateIssueId) FROM dbo.CharacterCertificateIssues WITH(UPDLOCK,HOLDLOCK) WHERE CertificateRecordId=@CertificateRecordId;
 IF @ExpectedLatestIssueId IS NULL OR ISNULL(@Last,0)<>@ExpectedLatestIssueId THROW 58533,'A newer copy exists. Reload and review its history.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId AND ExamType=@ExamType AND CertificateRecordId<>@CertificateRecordId)
  THROW 58005,'This student already has a certificate for that exam type. Review both records.',1;
 IF @Action=N'ReviewType'
 BEGIN
  IF @StoredType IS NOT NULL THROW 58013,'This certificate already has a saved exam type. Use Correct Details for a mistake.',1;
  INSERT dbo.CharacterCertificateTypeReviews VALUES(@CertificateRecordId,@ExamType,LTRIM(RTRIM(@Reason)),SYSDATETIME(),@UserId);
  UPDATE dbo.CharacterCertificates SET ExamType=@ExamType WHERE CertificateRecordId=@CertificateRecordId;
  UPDATE P SET ExamType=@ExamType FROM dbo.CharacterCertificateIssueSnapshots P JOIN dbo.CharacterCertificateIssues I ON I.CharacterCertificateIssueId=P.CharacterCertificateIssueId
   WHERE I.CertificateRecordId=@CertificateRecordId AND P.ExamType IS NULL;
  SET @Issue=@Last;
 END
 ELSE
 BEGIN
  IF @StoredType IS NULL THROW 58010,'Review the original SSC/HSSC type before correcting its details.',1;
  IF NULLIF(LTRIM(RTRIM(@RollNumber)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@Remarks)),N'') IS NULL
   THROW 58014,'BISE roll number and remarks are required.',1;
  IF @CertificateDate IS NULL OR @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58002,'Choose a certificate date that is not in the future.',1;
  UPDATE dbo.CharacterCertificates SET ExamType=@ExamType WHERE CertificateRecordId=@CertificateRecordId;
  INSERT dbo.CharacterCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId,Remarks) VALUES(@CertificateRecordId,SYSDATETIME(),@UserId,N'Correction: '+@Reason);
  SET @Issue=CONVERT(int,SCOPE_IDENTITY());
  UPDATE N SET PreviousIssueId=@Last,ExamType=@ExamType,StudentName=P.StudentName,FatherName=P.FatherName,Gender=P.Gender,
   LastExamYear=P.LastExamYear,RollNumber=LTRIM(RTRIM(@RollNumber)),Remarks=LTRIM(RTRIM(@Remarks)),CertificateDate=@CertificateDate,CorrectionReason=LTRIM(RTRIM(@Reason))
  FROM dbo.CharacterCertificateIssueSnapshots N JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=@Last WHERE N.CharacterCertificateIssueId=@Issue;
 END;
 COMMIT;
 SELECT @CertificateRecordId CertificateRecordId,@Issue CharacterCertificateIssueId,@Issue IssueId,@AdmissionId AdmissionId,CertificateDate
 FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Issue;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetCharacterCertificateRecords @UserId int,@AdmissionId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 SELECT C.CertificateRecordId,I.CharacterCertificateIssueId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.Gender,
 P.ExamType,P.LastExamYear,P.RollNumber,P.Remarks,P.CertificateDate,P.PreviousIssueId,P.CorrectionReason,
 I.IssuedAt,I.IssuedByUserId,U.Username IssuedBy,I.CollectedAt,I.CollectedBy,
 CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus
 FROM dbo.CharacterCertificates C JOIN dbo.CharacterCertificateIssues I ON I.CertificateRecordId=C.CertificateRecordId
 JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=I.CharacterCertificateIssueId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId WHERE @AdmissionId=0 OR C.AdmissionId=@AdmissionId
 ORDER BY I.IssuedAt DESC,I.CharacterCertificateIssueId DESC;
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
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssues WHERE CharacterCertificateIssueId=@CharacterCertificateIssueId AND CollectedAt IS NULL)
  THROW 58302,'Mark this certificate copy collected before printing.',1;
 SELECT C.CertificateRecordId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.Gender,P.LastExamYear,P.RollNumber,P.Remarks,P.CertificateDate,
 I.IssuedAt DateOfIssue,P.ExamType,CASE P.ExamType WHEN N'SSC' THEN N'S.S.C (Matric)' WHEN N'HSSC' THEN N'H.S.S.C (Intermediate)' END ExamTypeDisplay
 FROM dbo.CharacterCertificates C JOIN dbo.CharacterCertificateIssues I ON I.CertificateRecordId=C.CertificateRecordId
 JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=I.CharacterCertificateIssueId
 WHERE C.CertificateRecordId=@CertificateRecordId AND I.CharacterCertificateIssueId=@CharacterCertificateIssueId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetCertificateReissuePreview @UserId int,@CertificateKind nvarchar(20),@CertificateRecordId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @CertificateKind=N'Regular'
  SELECT C.AdmissionId,I.RegularStudentCertificateIssueId LatestIssueId,I.DateOfBirth SavedDateOfBirth,S.DateOfBirth CurrentDateOfBirth,I.ClassName,I.StudentName SavedStudentName,I.FatherName SavedFatherName,S.StudentName CurrentStudentName,S.FatherName CurrentFatherName,S.Gender CurrentGender,dbo.CertificateAdmissionFingerprint(S.AdmissionId) SourceHash
  FROM dbo.RegularStudentCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
  CROSS APPLY(SELECT TOP(1) * FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=C.RegularStudentCertificateId ORDER BY RegularStudentCertificateIssueId DESC) I
  WHERE C.RegularStudentCertificateId=@CertificateRecordId;
 ELSE IF @CertificateKind=N'DOB'
  SELECT C.AdmissionId,I.DateOfBirthCertificateIssueId LatestIssueId,P.DateOfBirth SavedDateOfBirth,S.DateOfBirth CurrentDateOfBirth,CONVERT(nvarchar(100),N'') ClassName,P.StudentName SavedStudentName,P.FatherName SavedFatherName,S.StudentName CurrentStudentName,S.FatherName CurrentFatherName,S.Gender CurrentGender,dbo.CertificateAdmissionFingerprint(S.AdmissionId) SourceHash
  FROM dbo.DateOfBirthCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
  CROSS APPLY(SELECT TOP(1) * FROM dbo.DateOfBirthCertificateIssues WHERE CertificateRecordId=C.CertificateRecordId ORDER BY IssuedAt DESC,DateOfBirthCertificateIssueId DESC) I
  JOIN dbo.DateOfBirthCertificateIssueSnapshots P ON P.DateOfBirthCertificateIssueId=I.DateOfBirthCertificateIssueId
  WHERE C.CertificateRecordId=@CertificateRecordId;
 ELSE IF @CertificateKind=N'Character'
  SELECT C.AdmissionId,I.CharacterCertificateIssueId LatestIssueId,CONVERT(date,NULL) SavedDateOfBirth,S.DateOfBirth CurrentDateOfBirth,
   CONVERT(nvarchar(100),N'') ClassName,P.StudentName SavedStudentName,P.FatherName SavedFatherName,S.StudentName CurrentStudentName,S.FatherName CurrentFatherName,
   S.Gender CurrentGender,dbo.CertificateAdmissionFingerprint(S.AdmissionId) SourceHash
  FROM dbo.CharacterCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
  CROSS APPLY(SELECT TOP(1) * FROM dbo.CharacterCertificateIssues WHERE CertificateRecordId=C.CertificateRecordId ORDER BY CharacterCertificateIssueId DESC) I
  JOIN dbo.CharacterCertificateIssueSnapshots P ON P.CharacterCertificateIssueId=I.CharacterCertificateIssueId
  WHERE C.CertificateRecordId=@CertificateRecordId;
 ELSE THROW 58530,'Unknown certificate type.',1;
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
 INSERT dbo.RegularStudentCertificateIssues(RegularStudentCertificateId,PreviousIssueId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,IssuedAt,IssuedByUserId)
 VALUES(@CertificateRecordId,@Last,@Name,@Father,@DOB,@Class,@CertificateDate,SYSDATETIME(),@UserId);
 DECLARE @Issue int=CONVERT(int,SCOPE_IDENTITY());
 COMMIT;
 SELECT @CertificateRecordId CertificateRecordId,@Issue IssueId,@AdmissionId AdmissionId,@DOB DateOfBirth,@CertificateDate CertificateDate;
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

-- A leaving certificate's episode details remain historical; personal details refresh from Admission.
CREATE TABLE dbo.LeaveCertificateIssueSnapshots(
 LeaveCertificateIssueId int NOT NULL CONSTRAINT PK_LeaveCertificateIssueSnapshots PRIMARY KEY
  CONSTRAINT FK_LeaveCertificateIssueSnapshots_Issue REFERENCES dbo.LeaveCertificateIssues(LeaveCertificateIssueId),
 PreviousIssueId int NULL CONSTRAINT FK_LeaveCertificateIssueSnapshots_Previous REFERENCES dbo.LeaveCertificateIssues(LeaveCertificateIssueId),
 StudentName nvarchar(500) NOT NULL,FatherName nvarchar(500) NULL,DateOfBirth date NULL,DateOfAdmission date NULL,
 ClassStudied nvarchar(150) NOT NULL,ClassPromotedTo nvarchar(150) NULL,LeaveDate date NOT NULL,
 AcademicRecord nvarchar(200) NULL,AttendanceRecord nvarchar(200) NULL,Remarks nvarchar(500) NULL
);
INSERT dbo.LeaveCertificateIssueSnapshots(LeaveCertificateIssueId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks)
 SELECT I.LeaveCertificateIssueId,C.StudentName,C.FatherName,C.DateOfBirth,C.DateOfAdmission,C.ClassStudied,C.ClassPromotedTo,C.LeaveDate,C.AcademicRecord,C.AttendanceRecord,C.Remarks
 FROM dbo.LeaveCertificateIssues I JOIN dbo.LeaveCertificates C ON C.LeaveRecordId=I.LeaveRecordId;
GO
CREATE TRIGGER dbo.TR_LeaveCertificate_IssueSnapshot ON dbo.LeaveCertificateIssues AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.LeaveCertificateIssueSnapshots(LeaveCertificateIssueId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks)
 SELECT I.LeaveCertificateIssueId,C.StudentName,C.FatherName,C.DateOfBirth,C.DateOfAdmission,C.ClassStudied,C.ClassPromotedTo,C.LeaveDate,C.AcademicRecord,C.AttendanceRecord,C.Remarks
 FROM inserted I JOIN dbo.LeaveCertificates C ON C.LeaveRecordId=I.LeaveRecordId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetLeaveCertificateReissuePreview @UserId int,@LeaveRecordId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Students.Leave';
 SELECT C.AdmissionId,C.CertificateType,C.LeaveDate,I.LeaveCertificateIssueId LatestIssueId,
  S.StudentName CurrentStudentName,S.FatherName CurrentFatherName,S.DateOfBirth CurrentDateOfBirth,S.AdmissionDate CurrentAdmissionDate,
  dbo.CertificateAdmissionFingerprint(S.AdmissionId) SourceHash
 FROM dbo.LeaveCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
 CROSS APPLY(SELECT TOP(1) LeaveCertificateIssueId FROM dbo.LeaveCertificateIssues WHERE LeaveRecordId=C.LeaveRecordId ORDER BY LeaveCertificateIssueId DESC) I
 WHERE C.LeaveRecordId=@LeaveRecordId;
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
CREATE OR ALTER PROCEDURE dbo.GetLeaveCertificateRecords @UserId int,@AdmissionId int=0,@CertificateType nvarchar(20)=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Students.Leave';
 SET @CertificateType=NULLIF(LTRIM(RTRIM(@CertificateType)),N'');
 SELECT C.LeaveRecordId,I.LeaveCertificateIssueId,C.AdmissionId,C.CertificateType,P.StudentName,P.FatherName,P.ClassStudied,P.ClassPromotedTo,P.LeaveDate,
  I.IssuedAt,I.IssuedByUserId,U.Username IssuedBy,I.IssueType,I.CollectedAt,I.CollectedBy,
  CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus,C.EnrollmentId,P.PreviousIssueId
 FROM dbo.LeaveCertificates C JOIN dbo.LeaveCertificateIssues I ON I.LeaveRecordId=C.LeaveRecordId
 JOIN dbo.LeaveCertificateIssueSnapshots P ON P.LeaveCertificateIssueId=I.LeaveCertificateIssueId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId
 WHERE (@AdmissionId=0 OR C.AdmissionId=@AdmissionId) AND (@CertificateType IS NULL OR C.CertificateType=@CertificateType)
 ORDER BY C.LeaveRecordId DESC,I.IssuedAt DESC,I.LeaveCertificateIssueId DESC;
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
 SELECT C.LeaveRecordId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.DateOfBirth,P.DateOfAdmission,
  P.ClassStudied,P.ClassPromotedTo,P.LeaveDate,P.AcademicRecord,P.AttendanceRecord,P.Remarks,I.IssuedAt DateOfIssue,I.IssueType CopyType
 FROM dbo.LeaveCertificates C JOIN dbo.LeaveCertificateIssues I ON I.LeaveRecordId=C.LeaveRecordId
 JOIN dbo.LeaveCertificateIssueSnapshots P ON P.LeaveCertificateIssueId=I.LeaveCertificateIssueId
 WHERE C.LeaveRecordId=@LeaveRecordId AND I.LeaveCertificateIssueId=@LeaveCertificateIssueId;
END;
GO

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'47',SYSDATETIME(),SUSER_SNAME(),N'Save character SSC/HSSC and entered-field corrections; refresh reviewed Admission details on reissue; preserve character/regular/DOB/leaving copies');
GO
