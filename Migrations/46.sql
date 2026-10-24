-- Per-issuance personal details preserve earlier copies after a DOB correction.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 46 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>45
 THROW 51000,'Migration 46 requires schema 45.',1;
GO
CREATE TABLE dbo.RegularStudentCertificateIssues(
 RegularStudentCertificateIssueId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RegularStudentCertificateIssues PRIMARY KEY,
 RegularStudentCertificateId int NOT NULL CONSTRAINT FK_RegularStudentCertificateIssues_Certificate REFERENCES dbo.RegularStudentCertificates(RegularStudentCertificateId),
 PreviousIssueId int NULL CONSTRAINT FK_RegularStudentCertificateIssues_Previous REFERENCES dbo.RegularStudentCertificateIssues(RegularStudentCertificateIssueId),
 StudentName nvarchar(500) NOT NULL,FatherName nvarchar(500) NULL,DateOfBirth date NOT NULL,
 ClassName nvarchar(100) NOT NULL,CertificateDate date NOT NULL,IssuedAt datetime2 NOT NULL,
 IssuedByUserId int NOT NULL CONSTRAINT FK_RegularStudentCertificateIssues_User REFERENCES dbo.Users(UserId)
);
CREATE INDEX IX_RegularStudentCertificateIssues_Certificate ON dbo.RegularStudentCertificateIssues(RegularStudentCertificateId,RegularStudentCertificateIssueId DESC);
INSERT dbo.RegularStudentCertificateIssues(RegularStudentCertificateId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,IssuedAt,IssuedByUserId)
 SELECT RegularStudentCertificateId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,CreatedAt,CreatedByUserId FROM dbo.RegularStudentCertificates;
CREATE TABLE dbo.DateOfBirthCertificateIssueSnapshots(
 DateOfBirthCertificateIssueId int NOT NULL CONSTRAINT PK_DateOfBirthCertificateIssueSnapshots PRIMARY KEY
  CONSTRAINT FK_DateOfBirthCertificateIssueSnapshots_Issue REFERENCES dbo.DateOfBirthCertificateIssues(DateOfBirthCertificateIssueId),
 PreviousIssueId int NULL CONSTRAINT FK_DateOfBirthCertificateIssueSnapshots_Previous REFERENCES dbo.DateOfBirthCertificateIssues(DateOfBirthCertificateIssueId),
 StudentName nvarchar(500) NOT NULL,FatherName nvarchar(500) NULL,Gender nvarchar(20) NULL,
 DateOfBirth date NOT NULL,CertificateDate date NOT NULL
);
INSERT dbo.DateOfBirthCertificateIssueSnapshots(DateOfBirthCertificateIssueId,StudentName,FatherName,Gender,DateOfBirth,CertificateDate)
 SELECT I.DateOfBirthCertificateIssueId,C.StudentName,C.FatherName,C.Gender,C.DateOfBirth,C.CertificateDate
 FROM dbo.DateOfBirthCertificateIssues I JOIN dbo.DateOfBirthCertificates C ON C.CertificateRecordId=I.CertificateRecordId;
ALTER TABLE dbo.MutationRequests DROP CONSTRAINT CK_MutationRequests_Kind;
ALTER TABLE dbo.MutationRequests WITH CHECK ADD CONSTRAINT CK_MutationRequests_Kind CHECK(
 (Kind=N'FeeAdjustment' AND FeePaymentId IS NOT NULL AND BankCertificateId IS NULL) OR
 (Kind=N'BankCertificateCreate' AND BankCertificateId IS NOT NULL AND FeePaymentId IS NULL) OR
 (Kind IN(N'FeeConcession',N'RegularCertificateReissue',N'BirthCertificateReissue') AND FeePaymentId IS NULL AND BankCertificateId IS NULL));
GO
CREATE TRIGGER dbo.TR_RegularStudentCertificate_InitialIssue ON dbo.RegularStudentCertificates AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.RegularStudentCertificateIssues(RegularStudentCertificateId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,IssuedAt,IssuedByUserId)
 SELECT RegularStudentCertificateId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,CreatedAt,CreatedByUserId FROM inserted;
END;
GO
CREATE TRIGGER dbo.TR_DateOfBirthCertificate_IssueSnapshot ON dbo.DateOfBirthCertificateIssues AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.DateOfBirthCertificateIssueSnapshots(DateOfBirthCertificateIssueId,StudentName,FatherName,Gender,DateOfBirth,CertificateDate)
 SELECT I.DateOfBirthCertificateIssueId,C.StudentName,C.FatherName,C.Gender,C.DateOfBirth,C.CertificateDate
 FROM inserted I JOIN dbo.DateOfBirthCertificates C ON C.CertificateRecordId=I.CertificateRecordId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetCertificateReissuePreview @UserId int,@CertificateKind nvarchar(20),@CertificateRecordId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @CertificateKind=N'Regular'
  SELECT C.AdmissionId,I.RegularStudentCertificateIssueId LatestIssueId,I.DateOfBirth SavedDateOfBirth,S.DateOfBirth CurrentDateOfBirth,I.ClassName
  FROM dbo.RegularStudentCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
  CROSS APPLY(SELECT TOP(1) * FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=C.RegularStudentCertificateId ORDER BY RegularStudentCertificateIssueId DESC) I
  WHERE C.RegularStudentCertificateId=@CertificateRecordId;
 ELSE IF @CertificateKind=N'DOB'
  SELECT C.AdmissionId,I.DateOfBirthCertificateIssueId LatestIssueId,P.DateOfBirth SavedDateOfBirth,S.DateOfBirth CurrentDateOfBirth,CONVERT(nvarchar(100),N'') ClassName
  FROM dbo.DateOfBirthCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
  CROSS APPLY(SELECT TOP(1) * FROM dbo.DateOfBirthCertificateIssues WHERE CertificateRecordId=C.CertificateRecordId ORDER BY IssuedAt DESC,DateOfBirthCertificateIssueId DESC) I
  JOIN dbo.DateOfBirthCertificateIssueSnapshots P ON P.DateOfBirthCertificateIssueId=I.DateOfBirthCertificateIssueId
  WHERE C.CertificateRecordId=@CertificateRecordId;
 ELSE THROW 58530,'Unknown certificate type.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.IssueRegularStudentCertificate
 @UserId int,@CertificateRecordId int,@ExpectedLatestIssueId int,@ExpectedDateOfBirth date,@CertificateDate date
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 IF @CertificateDate IS NULL OR @CertificateDate>CONVERT(date,SYSDATETIME()) THROW 58531,'Choose a certificate date that is not in the future.',1;
 IF @ExpectedDateOfBirth IS NULL THROW 58505,'Record the student date of birth before reissuing.',1;
 BEGIN TRANSACTION;
 DECLARE @AdmissionId int,@DOB date,@Name nvarchar(500),@Father nvarchar(500),@Last int,@Class nvarchar(100);
 SELECT @AdmissionId=AdmissionId FROM dbo.RegularStudentCertificates WITH(UPDLOCK,HOLDLOCK) WHERE RegularStudentCertificateId=@CertificateRecordId;
 IF @AdmissionId IS NULL THROW 58514,'The regular student certificate was not found.',1;
 SELECT @DOB=DateOfBirth FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @DOB IS NULL THROW 58505,'Record the student date of birth before reissuing.',1;
 IF @DOB<>@ExpectedDateOfBirth THROW 58532,'The school birth date changed after review. Reload and review the reissue again.',1;
 SELECT TOP(1) @Last=RegularStudentCertificateIssueId,@Class=ClassName,@Name=StudentName,@Father=FatherName FROM dbo.RegularStudentCertificateIssues WITH(UPDLOCK,HOLDLOCK)
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
 @ExpectedLatestIssueId int=NULL,@ExpectedDateOfBirth date=NULL,@CertificateDate date=NULL
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
 SELECT @DOB=DateOfBirth FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF @DOB IS NULL THROW 58505,'Record the student date of birth before reissuing.',1;
 IF @ExpectedDateOfBirth IS NOT NULL AND @DOB<>@ExpectedDateOfBirth THROW 58532,'The school birth date changed after review. Reload and review the reissue again.',1;
 SELECT TOP(1) @Last=DateOfBirthCertificateIssueId,@Collected=CollectedAt FROM dbo.DateOfBirthCertificateIssues WITH(UPDLOCK,HOLDLOCK)
 WHERE CertificateRecordId=@CertificateRecordId ORDER BY IssuedAt DESC,DateOfBirthCertificateIssueId DESC;
 IF @ExpectedLatestIssueId IS NOT NULL AND ISNULL(@Last,-1)<>@ExpectedLatestIssueId THROW 58533,'A newer certificate copy exists. Reload its history before reissuing.',1;
 IF @Last IS NOT NULL AND @Collected IS NULL THROW 58602,'The currently issued certificate has not been collected yet.',1;
 SELECT @Name=StudentName,@Father=FatherName,@Gender=Gender FROM dbo.DateOfBirthCertificateIssueSnapshots WHERE DateOfBirthCertificateIssueId=@Last;
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
CREATE OR ALTER PROCEDURE dbo.GetRegularStudentCertificateRecords @UserId int,@AdmissionId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 SELECT C.RegularStudentCertificateId,I.RegularStudentCertificateIssueId,C.AdmissionId,I.StudentName,I.FatherName,I.DateOfBirth,I.ClassName,I.CertificateDate,
 I.IssuedAt CreatedAt,CONVERT(datetime2,NULL) UpdatedAt,I.IssuedByUserId,U.Username IssuedBy,I.PreviousIssueId
 FROM dbo.RegularStudentCertificates C JOIN dbo.RegularStudentCertificateIssues I ON I.RegularStudentCertificateId=C.RegularStudentCertificateId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId WHERE @AdmissionId=0 OR C.AdmissionId=@AdmissionId
 ORDER BY I.IssuedAt DESC,I.RegularStudentCertificateIssueId DESC;
END;
GO
CREATE OR ALTER PROCEDURE dbo.UpdateRegularStudentCertificate
 @UserId int,@RegularStudentCertificateId int,@CertificateDate date,@ClassName nvarchar(100)
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 THROW 58534,'Saved certificate copies cannot be edited. Use Issue Again to create a reviewed new copy.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetDateOfBirthCertificateRecords @UserId int,@AdmissionId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
 SELECT C.CertificateRecordId,I.DateOfBirthCertificateIssueId,C.AdmissionId,P.StudentName,P.FatherName,P.DateOfBirth,P.CertificateDate,
 I.IssuedAt,I.IssuedByUserId,U.Username IssuedBy,I.CollectedAt,I.CollectedBy,
 CASE WHEN I.CollectedAt IS NULL THEN N'Not Collected' ELSE N'Collected' END CollectionStatus,P.PreviousIssueId
 FROM dbo.DateOfBirthCertificates C JOIN dbo.DateOfBirthCertificateIssues I ON I.CertificateRecordId=C.CertificateRecordId
 JOIN dbo.DateOfBirthCertificateIssueSnapshots P ON P.DateOfBirthCertificateIssueId=I.DateOfBirthCertificateIssueId
 LEFT JOIN dbo.Users U ON U.UserId=I.IssuedByUserId WHERE @AdmissionId=0 OR C.AdmissionId=@AdmissionId
 ORDER BY I.IssuedAt DESC,I.DateOfBirthCertificateIssueId DESC;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetRegularStudentCertificatePrintData @UserId int,@RegularStudentCertificateId int,@RegularStudentCertificateIssueId int=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Print';
 IF @RegularStudentCertificateId<=0 THROW 58520,'A valid regular student certificate is required.',1;
 SELECT C.RegularStudentCertificateId,I.RegularStudentCertificateIssueId,C.AdmissionId,C.CertificateNumber,
 I.StudentName,I.FatherName,I.DateOfBirth,I.ClassName,I.CertificateDate,I.IssuedAt DateOfIssue,
 LTRIM(RTRIM(CONCAT(SP.SchoolName,N' ',CP.CampusName,N' ',COALESCE(NULLIF(LTRIM(RTRIM(CP.City)),N''),SP.City)))) InstitutionLine
 FROM dbo.RegularStudentCertificates C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
 JOIN dbo.Campuses CP ON CP.CampusId=S.AdmissionCampusId CROSS JOIN dbo.SchoolProfile SP
 CROSS APPLY(SELECT TOP(1) * FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=C.RegularStudentCertificateId
 AND (@RegularStudentCertificateIssueId IS NULL OR RegularStudentCertificateIssueId=@RegularStudentCertificateIssueId)
 ORDER BY RegularStudentCertificateIssueId) I
 WHERE C.RegularStudentCertificateId=@RegularStudentCertificateId AND SP.SchoolProfileId=1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetDateOfBirthCertificatePrintData @UserId int,@CertificateRecordId int,@DateOfBirthCertificateIssueId int=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Certificates.Print';
 IF @CertificateRecordId<=0 THROW 58800,'A valid date of birth certificate is required.',1;
 SELECT C.CertificateRecordId,I.DateOfBirthCertificateIssueId,C.AdmissionId,C.CertificateNumber,P.StudentName,P.FatherName,P.Gender,P.DateOfBirth,
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
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'46',SYSDATETIME(),SUSER_SNAME(),N'Preserve regular and DOB certificate copies per issuance; reviewed corrected-DOB reissue with recovery');
GO
