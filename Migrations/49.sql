-- Character certificate: choose the academic year and conduct; edit the current copy in place.
-- Passing year is the chosen academic year's end year minus one. Issue Again retains saved exam details.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 49 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>48
 THROW 51000,'Migration 49 requires schema 48.',1;
GO
CREATE OR ALTER PROCEDURE dbo.CreateCharacterCertificate
 @UserId int,@AdmissionId int,@CertificateDate date,@RollNumber nvarchar(50)=NULL,@Remarks nvarchar(1000)=NULL,@ExamType nvarchar(10)=NULL,@AcademicYearId int=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @ExamType IS NULL OR @ExamType NOT IN(N'SSC',N'HSSC') THROW 58009,'Choose SSC or HSSC. This type is saved with the certificate.',1;
 IF @CertificateDate IS NULL OR @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58002,'Choose a certificate date that is not in the future.',1;
 IF NULLIF(LTRIM(RTRIM(@RollNumber)),N'') IS NULL THROW 58003,'BISE Roll Number is required.',1;
 SET @Remarks=CASE LTRIM(RTRIM(@Remarks)) WHEN N'Good' THEN N'Good' WHEN N'Very Good' THEN N'Very Good' WHEN N'Excellent' THEN N'Excellent' END;
 IF @Remarks IS NULL THROW 58004,'Choose Good, Very Good or Excellent for conduct.',1;
 BEGIN TRANSACTION;
 DECLARE @Name nvarchar(500),@Father nvarchar(500),@Gender nvarchar(20),@Year int,@Certificate int,@Issue int;
 SELECT @Name=StudentName,@Father=FatherName,@Gender=Gender FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @Name IS NULL THROW 58006,'The student was not found.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND ExamType IS NULL)
  THROW 58010,'Review the existing certificate and save its SSC/HSSC type before creating another certificate.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId AND ExamType=@ExamType)
  THROW 58005,'This student already has this exam-type certificate. Select it to issue again or correct its details.',1;
 SELECT @Year=YEAR(EndDate)-1 FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@AcademicYearId;
 IF @Year IS NULL OR @Year<=0 THROW 58015,'Choose an academic year for the passing year.',1;
 IF @Year>YEAR(@CertificateDate) THROW 58016,'The passing year cannot be later than the certificate date.',1;
 INSERT dbo.CharacterCertificates(AdmissionId,StudentName,FatherName,Gender,LastExamYear,RollNumber,Remarks,CertificateDate,CreatedByUserId,ExamType)
 VALUES(@AdmissionId,@Name,@Father,@Gender,@Year,LTRIM(RTRIM(@RollNumber)),LTRIM(RTRIM(@Remarks)),@CertificateDate,@UserId,@ExamType);
 SET @Certificate=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.CharacterCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId) VALUES(@Certificate,SYSDATETIME(),@UserId);
 SET @Issue=CONVERT(int,SCOPE_IDENTITY()); COMMIT;
 SELECT @Certificate CertificateRecordId,@Issue CharacterCertificateIssueId,@Issue IssueId,@AdmissionId AdmissionId,@CertificateDate CertificateDate;
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
CREATE OR ALTER PROCEDURE dbo.SaveCharacterCertificateOperation
 @UserId int,@Action nvarchar(20),@CertificateRecordId int=0,@AdmissionId int=0,@ExpectedLatestIssueId int=0,
 @ExamType nvarchar(10)=NULL,@CertificateDate date=NULL,@RollNumber nvarchar(50)=NULL,@Remarks nvarchar(1000)=NULL,
 @Reason nvarchar(500)=NULL,@ExpectedSourceHash varbinary(32)=NULL,@AcademicYearId int=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @Action=N'Create'
 BEGIN
  EXEC dbo.CreateCharacterCertificate @UserId,@AdmissionId,@CertificateDate,@RollNumber,@Remarks,@ExamType,@AcademicYearId; RETURN;
 END;
 IF @Action=N'Issue'
 BEGIN
  EXEC dbo.IssueCharacterCertificate @UserId,@CertificateRecordId,@Remarks,@ExpectedLatestIssueId,@ExpectedSourceHash; RETURN;
 END;
 IF @Action IS NULL OR @Action<>N'Update' THROW 58011,'Unknown certificate action.',1;
 IF @ExamType IS NULL OR @ExamType NOT IN(N'SSC',N'HSSC') THROW 58009,'Choose SSC or HSSC.',1;
 IF @CertificateDate IS NULL OR @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58002,'Choose a certificate date that is not in the future.',1;
 IF NULLIF(LTRIM(RTRIM(@RollNumber)),N'') IS NULL THROW 58014,'BISE roll number is required.',1;
 SET @Remarks=CASE LTRIM(RTRIM(@Remarks)) WHEN N'Good' THEN N'Good' WHEN N'Very Good' THEN N'Very Good' WHEN N'Excellent' THEN N'Excellent' END;
 IF @Remarks IS NULL THROW 58004,'Choose Good, Very Good or Excellent for conduct.',1;
 BEGIN TRANSACTION;
 DECLARE @Owner int,@Last int,@Year int;
 SELECT @Owner=AdmissionId FROM dbo.CharacterCertificates WHERE CertificateRecordId=@CertificateRecordId;
 SELECT @Owner=AdmissionId FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Owner;
 SELECT @AdmissionId=AdmissionId FROM dbo.CharacterCertificates WITH(UPDLOCK,HOLDLOCK) WHERE CertificateRecordId=@CertificateRecordId;
 IF @Owner IS NULL OR @AdmissionId<=0 THROW 58101,'The character certificate was not found.',1;
 SELECT @Last=MAX(CharacterCertificateIssueId) FROM dbo.CharacterCertificateIssues WITH(UPDLOCK,HOLDLOCK) WHERE CertificateRecordId=@CertificateRecordId;
 IF @ExpectedLatestIssueId IS NULL OR ISNULL(@Last,0)<>@ExpectedLatestIssueId THROW 58533,'A newer copy exists. Reload before saving.',1;
 IF EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId AND ExamType=@ExamType AND CertificateRecordId<>@CertificateRecordId)
  THROW 58005,'This student already has a certificate for that exam type.',1;
 SELECT @Year=YEAR(EndDate)-1 FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@AcademicYearId;
 IF @Year IS NULL OR @Year<=0 THROW 58015,'Choose an academic year for the passing year.',1;
 IF @Year>YEAR(@CertificateDate) THROW 58016,'The passing year cannot be later than the certificate date.',1;
 UPDATE dbo.CharacterCertificates SET ExamType=@ExamType,LastExamYear=@Year,RollNumber=LTRIM(RTRIM(@RollNumber)),Remarks=@Remarks,CertificateDate=@CertificateDate
  WHERE CertificateRecordId=@CertificateRecordId;
 UPDATE dbo.CharacterCertificateIssueSnapshots SET ExamType=@ExamType,LastExamYear=@Year,RollNumber=LTRIM(RTRIM(@RollNumber)),Remarks=@Remarks,CertificateDate=@CertificateDate
  WHERE CharacterCertificateIssueId=@Last;
 COMMIT;
 SELECT @CertificateRecordId CertificateRecordId,@Last CharacterCertificateIssueId,@Last IssueId,@AdmissionId AdmissionId,CertificateDate
 FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Last;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'49',SYSDATETIME(),SUSER_SNAME(),N'Character certificate academic-year choice, passing year, conduct options and direct Save Changes');
GO
