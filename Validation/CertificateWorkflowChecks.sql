IF OBJECT_ID(N'dbo.VoidCertificateCopy',N'P') IS NOT NULL THROW 51000,'Certificate cancellation must be removed.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE name IN(N'VoidedAt',N'VoidReason'))
 THROW 51000,'Retired cancellation columns still exist.',1;
IF EXISTS(SELECT 1 FROM sys.sql_modules WHERE definition LIKE N'%VoidedAt%' OR definition LIKE N'%VoidReason%')
 THROW 51000,'An active SQL module still references retired cancellation columns.',1;
SELECT N'PASS: certificate cancellation command is absent';
SELECT N'PASS: cancellation columns and active SQL references are absent';
GO
SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.SportsCertificates(AdmissionId,StudentName,FatherName,SportName,Performance,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',N'Cricket',N'Good',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
-- The same copy may be printed and corrected before handover.
EXEC dbo.GetSportsCertificatePrintData @UserId=@User,@SportsCertificateId=@Copy;
UPDATE dbo.SportsCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE SportsCertificateId=@Copy;
SELECT @Version=ContentVersion FROM dbo.SportsCertificates WHERE SportsCertificateId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'Sports',@Copy,N'Fixture collector',@Version;
DECLARE @Result table(CopyId int);
INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Sports',@Copy,@Request;
SELECT @New=CopyId FROM @Result;
IF NOT EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE SportsCertificateId=@New AND PreviousCertificateId=@Copy AND CollectedAt IS NULL) THROW 51000,'New copy not linked and editable.',1;
DELETE @Result;INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Sports',@Copy,@Request;
IF (SELECT CopyId FROM @Result)<>@New THROW 51000,'A retried issue created another copy.',1;
UPDATE dbo.SportsCertificates SET CertificateDate=DATEADD(day,-2,CONVERT(date,GETDATE())) WHERE SportsCertificateId=@New;
BEGIN TRY
 UPDATE dbo.SportsCertificates SET CertificateDate=CONVERT(date,GETDATE()) WHERE SportsCertificateId=@Copy;
 THROW 51000,'Collected copy was editable.',1;
END TRY BEGIN CATCH
 IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;
 IF @@TRANCOUNT>0 ROLLBACK;
END CATCH;
SELECT N'PASS: Sports print before collection, uncollected edit, final lock, linked new copy and replay recovery';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.SportsCertificates(AdmissionId,StudentName,FatherName,SportName,Performance,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',N'Cricket',N'Good',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.SportsCertificates WHERE SportsCertificateId=@Copy;
UPDATE dbo.SportsCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE SportsCertificateId=@Copy;
BEGIN TRY
 EXEC dbo.CollectCertificateCopy @User,N'Sports',@Copy,N'Fixture collector',@Version;
 THROW 51000,'Stale collection was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58903 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: Sports changed copy cannot be collected from stale review';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.NoObjectionCertificates(AdmissionId,StudentName,FatherName,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
-- The same copy may be printed and corrected before handover.
EXEC dbo.GetNoObjectionCertificatePrintData @UserId=@User,@NoObjectionCertificateId=@Copy;
UPDATE dbo.NoObjectionCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE NoObjectionCertificateId=@Copy;
SELECT @Version=ContentVersion FROM dbo.NoObjectionCertificates WHERE NoObjectionCertificateId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'NOC',@Copy,N'Fixture collector',@Version;
DECLARE @Result table(CopyId int);
INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'NOC',@Copy,@Request;
SELECT @New=CopyId FROM @Result;
IF NOT EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE NoObjectionCertificateId=@New AND PreviousCertificateId=@Copy AND CollectedAt IS NULL) THROW 51000,'New copy not linked and editable.',1;
DELETE @Result;INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'NOC',@Copy,@Request;
IF (SELECT CopyId FROM @Result)<>@New THROW 51000,'A retried issue created another copy.',1;
UPDATE dbo.NoObjectionCertificates SET CertificateDate=DATEADD(day,-2,CONVERT(date,GETDATE())) WHERE NoObjectionCertificateId=@New;
BEGIN TRY
 UPDATE dbo.NoObjectionCertificates SET CertificateDate=CONVERT(date,GETDATE()) WHERE NoObjectionCertificateId=@Copy;
 THROW 51000,'Collected copy was editable.',1;
END TRY BEGIN CATCH
 IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;
 IF @@TRANCOUNT>0 ROLLBACK;
END CATCH;
SELECT N'PASS: NOC print before collection, uncollected edit, final lock, linked new copy and replay recovery';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.NoObjectionCertificates(AdmissionId,StudentName,FatherName,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.NoObjectionCertificates WHERE NoObjectionCertificateId=@Copy;
UPDATE dbo.NoObjectionCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE NoObjectionCertificateId=@Copy;
BEGIN TRY
 EXEC dbo.CollectCertificateCopy @User,N'NOC',@Copy,N'Fixture collector',@Version;
 THROW 51000,'Stale collection was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58903 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: NOC changed copy cannot be collected from stale review';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.ProvisionalCertificates(AdmissionId,StudentName,FatherName,Gender,DateOfBirth,ExamType,ExamYear,BoardName,RollNumber,MarksObtained,MarksOutOf,Grade,OtherSubjects,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',N'Male','20100101',N'SSC',2025,N'BISE',N'123',800,1000,N'A',N'English',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
-- The same copy may be printed and corrected before handover.
EXEC dbo.GetProvisionalCertificatePrintData @UserId=@User,@ProvisionalCertificateId=@Copy;
UPDATE dbo.ProvisionalCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE ProvisionalCertificateId=@Copy;
SELECT @Version=ContentVersion FROM dbo.ProvisionalCertificates WHERE ProvisionalCertificateId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'Provisional',@Copy,N'Fixture collector',@Version;
DECLARE @Result table(CopyId int);
INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Provisional',@Copy,@Request;
SELECT @New=CopyId FROM @Result;
IF NOT EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE ProvisionalCertificateId=@New AND PreviousCertificateId=@Copy AND CollectedAt IS NULL) THROW 51000,'New copy not linked and editable.',1;
DELETE @Result;INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Provisional',@Copy,@Request;
IF (SELECT CopyId FROM @Result)<>@New THROW 51000,'A retried issue created another copy.',1;
UPDATE dbo.ProvisionalCertificates SET CertificateDate=DATEADD(day,-2,CONVERT(date,GETDATE())) WHERE ProvisionalCertificateId=@New;
BEGIN TRY
 UPDATE dbo.ProvisionalCertificates SET CertificateDate=CONVERT(date,GETDATE()) WHERE ProvisionalCertificateId=@Copy;
 THROW 51000,'Collected copy was editable.',1;
END TRY BEGIN CATCH
 IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;
 IF @@TRANCOUNT>0 ROLLBACK;
END CATCH;
SELECT N'PASS: Provisional print before collection, uncollected edit, final lock, linked new copy and replay recovery';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.ProvisionalCertificates(AdmissionId,StudentName,FatherName,Gender,DateOfBirth,ExamType,ExamYear,BoardName,RollNumber,MarksObtained,MarksOutOf,Grade,OtherSubjects,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',N'Male','20100101',N'SSC',2025,N'BISE',N'123',800,1000,N'A',N'English',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.ProvisionalCertificates WHERE ProvisionalCertificateId=@Copy;
UPDATE dbo.ProvisionalCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE ProvisionalCertificateId=@Copy;
BEGIN TRY
 EXEC dbo.CollectCertificateCopy @User,N'Provisional',@Copy,N'Fixture collector',@Version;
 THROW 51000,'Stale collection was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58903 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: Provisional changed copy cannot be collected from stale review';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.RegularLecturerCertificates(StaffId,StaffName,FatherName,Gender,RoleTitle,SubjectTaught,CertificateDate,CreatedByUserId) VALUES(@Staff,N'Workflow Staff',N'Parent',N'Male',N'Teacher',N'English',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
-- The same copy may be printed and corrected before handover.
EXEC dbo.GetRegularLecturerCertificatePrintData @UserId=@User,@RegularLecturerCertificateId=@Copy;
UPDATE dbo.RegularLecturerCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE RegularLecturerCertificateId=@Copy;
SELECT @Version=ContentVersion FROM dbo.RegularLecturerCertificates WHERE RegularLecturerCertificateId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'Lecturer',@Copy,N'Fixture collector',@Version;
DECLARE @Result table(CopyId int);
INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Lecturer',@Copy,@Request;
SELECT @New=CopyId FROM @Result;
IF NOT EXISTS(SELECT 1 FROM dbo.RegularLecturerCertificates WHERE RegularLecturerCertificateId=@New AND PreviousCertificateId=@Copy AND CollectedAt IS NULL) THROW 51000,'New copy not linked and editable.',1;
DELETE @Result;INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Lecturer',@Copy,@Request;
IF (SELECT CopyId FROM @Result)<>@New THROW 51000,'A retried issue created another copy.',1;
UPDATE dbo.RegularLecturerCertificates SET CertificateDate=DATEADD(day,-2,CONVERT(date,GETDATE())) WHERE RegularLecturerCertificateId=@New;
BEGIN TRY
 UPDATE dbo.RegularLecturerCertificates SET CertificateDate=CONVERT(date,GETDATE()) WHERE RegularLecturerCertificateId=@Copy;
 THROW 51000,'Collected copy was editable.',1;
END TRY BEGIN CATCH
 IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;
 IF @@TRANCOUNT>0 ROLLBACK;
END CATCH;
SELECT N'PASS: Lecturer print before collection, uncollected edit, final lock, linked new copy and replay recovery';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.RegularLecturerCertificates(StaffId,StaffName,FatherName,Gender,RoleTitle,SubjectTaught,CertificateDate,CreatedByUserId) VALUES(@Staff,N'Workflow Staff',N'Parent',N'Male',N'Teacher',N'English',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.RegularLecturerCertificates WHERE RegularLecturerCertificateId=@Copy;
UPDATE dbo.RegularLecturerCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE RegularLecturerCertificateId=@Copy;
BEGIN TRY
 EXEC dbo.CollectCertificateCopy @User,N'Lecturer',@Copy,N'Fixture collector',@Version;
 THROW 51000,'Stale collection was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58903 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: Lecturer changed copy cannot be collected from stale review';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.BankAccountOpeningCertificates(StaffId,StaffName,FatherName,Gender,Designation,BankName,BranchName,Purpose,CertificateDate,CreatedByUserId) VALUES(@Staff,N'Workflow Staff',N'Parent',N'Male',N'Teacher',N'Bank',N'Branch',N'Account',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
-- The same copy may be printed and corrected before handover.
EXEC dbo.GetBankAccountOpeningCertificatePrintData @UserId=@User,@BankAccountOpeningCertificateId=@Copy;
UPDATE dbo.BankAccountOpeningCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE BankAccountOpeningCertificateId=@Copy;
SELECT @Version=ContentVersion FROM dbo.BankAccountOpeningCertificates WHERE BankAccountOpeningCertificateId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'Bank',@Copy,N'Fixture collector',@Version;
DECLARE @Result table(CopyId int);
INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Bank',@Copy,@Request;
SELECT @New=CopyId FROM @Result;
IF NOT EXISTS(SELECT 1 FROM dbo.BankAccountOpeningCertificates WHERE BankAccountOpeningCertificateId=@New AND PreviousCertificateId=@Copy AND CollectedAt IS NULL) THROW 51000,'New copy not linked and editable.',1;
DELETE @Result;INSERT @Result EXEC dbo.IssueCertificateCopy @User,N'Bank',@Copy,@Request;
IF (SELECT CopyId FROM @Result)<>@New THROW 51000,'A retried issue created another copy.',1;
UPDATE dbo.BankAccountOpeningCertificates SET CertificateDate=DATEADD(day,-2,CONVERT(date,GETDATE())) WHERE BankAccountOpeningCertificateId=@New;
BEGIN TRY
 UPDATE dbo.BankAccountOpeningCertificates SET CertificateDate=CONVERT(date,GETDATE()) WHERE BankAccountOpeningCertificateId=@Copy;
 THROW 51000,'Collected copy was editable.',1;
END TRY BEGIN CATCH
 IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;
 IF @@TRANCOUNT>0 ROLLBACK;
END CATCH;
SELECT N'PASS: Bank print before collection, uncollected edit, final lock, linked new copy and replay recovery';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.BankAccountOpeningCertificates(StaffId,StaffName,FatherName,Gender,Designation,BankName,BranchName,Purpose,CertificateDate,CreatedByUserId) VALUES(@Staff,N'Workflow Staff',N'Parent',N'Male',N'Teacher',N'Bank',N'Branch',N'Account',CONVERT(date,GETDATE()),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.BankAccountOpeningCertificates WHERE BankAccountOpeningCertificateId=@Copy;
UPDATE dbo.BankAccountOpeningCertificates SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE BankAccountOpeningCertificateId=@Copy;
BEGIN TRY
 EXEC dbo.CollectCertificateCopy @User,N'Bank',@Copy,N'Fixture collector',@Version;
 THROW 51000,'Stale collection was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58903 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: Bank changed copy cannot be collected from stale review';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.CharacterCertificates(AdmissionId,StudentName,FatherName,Gender,LastExamYear,RollNumber,Remarks,CertificateDate,CreatedByUserId,ExamType) VALUES(@Student,N'Workflow Student',N'Parent',N'Male',2025,N'123',N'Good',CONVERT(date,GETDATE()),@User,N'SSC');SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.CharacterCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId) VALUES(@Record,SYSDATETIME(),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
EXEC dbo.GetCharacterCertificatePrintData @User,@Record,@Copy;
UPDATE dbo.CharacterCertificateIssueSnapshots SET Remarks=N'Corrected before handover' WHERE CharacterCertificateIssueId=@Copy;
SELECT @Version=ContentVersion FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Copy;
EXEC dbo.MarkCharacterCertificateCollected @User,@Copy,N'Fixture collector',@Version;
EXEC dbo.GetCharacterCertificatePrintData @User,@Record,@Copy;
BEGIN TRY UPDATE dbo.CharacterCertificateIssueSnapshots SET Remarks=N'Corrected before handover' WHERE CharacterCertificateIssueId=@Copy;THROW 51000,'Collected snapshot was editable.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: Character print before handover, correction before collection and immutable collected snapshot';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.CharacterCertificates(AdmissionId,StudentName,FatherName,Gender,LastExamYear,RollNumber,Remarks,CertificateDate,CreatedByUserId,ExamType) VALUES(@Student,N'Workflow Student',N'Parent',N'Male',2025,N'123',N'Good',CONVERT(date,GETDATE()),@User,N'SSC');SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.CharacterCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId) VALUES(@Record,SYSDATETIME(),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Copy;
EXEC dbo.MarkCharacterCertificateCollected @User,@Copy,N'Fixture collector',@Version;
DECLARE @OriginalName nvarchar(500)=(SELECT StudentName FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Copy);
UPDATE dbo.Students SET StudentName=N'Corrected Workflow Student',Gender=N'Female' WHERE AdmissionId=@Student;
SET @Hash=dbo.CertificateAdmissionFingerprint(@Student);
DECLARE @Lifecycle nvarchar(max)=(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES);
EXEC dbo.IssueCharacterCertificate @UserId=@User,@CertificateRecordId=@Record,@ExpectedLatestIssueId=@Copy,@ExpectedSourceHash=@Hash;
SELECT @New=MAX(CharacterCertificateIssueId) FROM dbo.CharacterCertificateIssues WHERE CertificateRecordId=@Record;
IF @New=@Copy OR NOT EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@New AND StudentName=N'Corrected Workflow Student') THROW 51000,'New copy did not use current personal details.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId=@Copy AND StudentName=@OriginalName) THROW 51000,'Earlier copy changed during reissue.',1;
IF @Lifecycle<>(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES) THROW 51000,'Reissue changed student lifecycle data.',1;
ROLLBACK;SELECT N'PASS: Character collected copy can be replaced with fresh details while preserving the earlier copy and student lifecycle';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.DateOfBirthCertificates(AdmissionId,StudentName,FatherName,Gender,DateOfBirth,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',N'Male','20100101',CONVERT(date,GETDATE()),@User);SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.DateOfBirthCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId) VALUES(@Record,SYSDATETIME(),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
EXEC dbo.GetDateOfBirthCertificatePrintData @User,@Record,@Copy;
UPDATE dbo.DateOfBirthCertificateIssueSnapshots SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE DateOfBirthCertificateIssueId=@Copy;
SELECT @Version=ContentVersion FROM dbo.DateOfBirthCertificateIssueSnapshots WHERE DateOfBirthCertificateIssueId=@Copy;
EXEC dbo.MarkDateOfBirthCertificateCollected @User,@Copy,N'Fixture collector',@Version;
EXEC dbo.GetDateOfBirthCertificatePrintData @User,@Record,@Copy;
BEGIN TRY UPDATE dbo.DateOfBirthCertificateIssueSnapshots SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE DateOfBirthCertificateIssueId=@Copy;THROW 51000,'Collected snapshot was editable.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: DOB print before handover, correction before collection and immutable collected snapshot';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.DateOfBirthCertificates(AdmissionId,StudentName,FatherName,Gender,DateOfBirth,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent',N'Male','20100101',CONVERT(date,GETDATE()),@User);SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.DateOfBirthCertificateIssues(CertificateRecordId,IssuedAt,IssuedByUserId) VALUES(@Record,SYSDATETIME(),@User);SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.DateOfBirthCertificateIssueSnapshots WHERE DateOfBirthCertificateIssueId=@Copy;
EXEC dbo.MarkDateOfBirthCertificateCollected @User,@Copy,N'Fixture collector',@Version;
DECLARE @OriginalName nvarchar(500)=(SELECT StudentName FROM dbo.DateOfBirthCertificateIssueSnapshots WHERE DateOfBirthCertificateIssueId=@Copy);
UPDATE dbo.Students SET StudentName=N'Corrected Workflow Student',Gender=N'Female' WHERE AdmissionId=@Student;
SET @Hash=dbo.CertificateAdmissionFingerprint(@Student);
DECLARE @Lifecycle nvarchar(max)=(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES);
EXEC dbo.IssueDateOfBirthCertificate @UserId=@User,@CertificateRecordId=@Record,@ExpectedLatestIssueId=@Copy,@ExpectedDateOfBirth='20100101',@CertificateDate='20261025',@ExpectedSourceHash=@Hash;
SELECT @New=MAX(DateOfBirthCertificateIssueId) FROM dbo.DateOfBirthCertificateIssues WHERE CertificateRecordId=@Record;
IF @New=@Copy OR NOT EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificateIssueSnapshots WHERE DateOfBirthCertificateIssueId=@New AND StudentName=N'Corrected Workflow Student') THROW 51000,'New copy did not use current personal details.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificateIssueSnapshots WHERE DateOfBirthCertificateIssueId=@Copy AND StudentName=@OriginalName) THROW 51000,'Earlier copy changed during reissue.',1;
IF @Lifecycle<>(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES) THROW 51000,'Reissue changed student lifecycle data.',1;
ROLLBACK;SELECT N'PASS: DOB collected copy can be replaced with fresh details while preserving the earlier copy and student lifecycle';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.RegularStudentCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent','20100101',N'Ten',CONVERT(date,GETDATE()),@User);SET @Record=CONVERT(int,SCOPE_IDENTITY());
SELECT @Copy=MAX(RegularStudentCertificateIssueId) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=@Record;
EXEC dbo.GetRegularStudentCertificatePrintData @User,@Record,@Copy;
UPDATE dbo.RegularStudentCertificateIssues SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE RegularStudentCertificateIssueId=@Copy;
SELECT @Version=ContentVersion FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateIssueId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'Regular',@Copy,N'Fixture collector',@Version;
EXEC dbo.GetRegularStudentCertificatePrintData @User,@Record,@Copy;
BEGIN TRY UPDATE dbo.RegularStudentCertificateIssues SET CertificateDate=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE RegularStudentCertificateIssueId=@Copy;THROW 51000,'Collected snapshot was editable.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: Regular print before handover, correction before collection and immutable collected snapshot';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.RegularStudentCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,ClassName,CertificateDate,CreatedByUserId) VALUES(@Student,N'Workflow Student',N'Parent','20100101',N'Ten',CONVERT(date,GETDATE()),@User);SET @Record=CONVERT(int,SCOPE_IDENTITY());
SELECT @Copy=MAX(RegularStudentCertificateIssueId) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=@Record;
SELECT @Version=ContentVersion FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateIssueId=@Copy;
EXEC dbo.CollectCertificateCopy @User,N'Regular',@Copy,N'Fixture collector',@Version;
DECLARE @OriginalName nvarchar(500)=(SELECT StudentName FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateIssueId=@Copy);
UPDATE dbo.Students SET StudentName=N'Corrected Workflow Student',Gender=N'Female' WHERE AdmissionId=@Student;
SET @Hash=dbo.CertificateAdmissionFingerprint(@Student);
DECLARE @Lifecycle nvarchar(max)=(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES);
EXEC dbo.IssueRegularStudentCertificate @UserId=@User,@CertificateRecordId=@Record,@ExpectedLatestIssueId=@Copy,@ExpectedDateOfBirth='20100101',@CertificateDate='20261025',@ExpectedSourceHash=@Hash;
SELECT @New=MAX(RegularStudentCertificateIssueId) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId=@Record;
IF @New=@Copy OR NOT EXISTS(SELECT 1 FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateIssueId=@New AND StudentName=N'Corrected Workflow Student') THROW 51000,'New copy did not use current personal details.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateIssueId=@Copy AND StudentName=@OriginalName) THROW 51000,'Earlier copy changed during reissue.',1;
IF @Lifecycle<>(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES) THROW 51000,'Reissue changed student lifecycle data.',1;
ROLLBACK;SELECT N'PASS: Regular collected copy can be replaced with fresh details while preserving the earlier copy and student lifecycle';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks,CreatedByUserId,CertificateType) VALUES(@Student,N'Workflow Student',N'Parent','20100101','20200101',N'Ten',N'Eleven',CONVERT(date,GETDATE()),N'Good',N'Regular',N'Personal',@User,N'School');SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificateIssues(LeaveRecordId,IssuedAt,IssuedByUserId,IssueType) VALUES(@Record,SYSDATETIME(),@User,N'Original');SET @Copy=CONVERT(int,SCOPE_IDENTITY());
EXEC dbo.GetLeaveCertificatePrintData @User,@Record,@Copy;
UPDATE dbo.LeaveCertificateIssueSnapshots SET Remarks=N'Corrected before handover' WHERE LeaveCertificateIssueId=@Copy;
SELECT @Version=ContentVersion FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy;
EXEC dbo.MarkLeaveCertificateCollected @User,@Copy,N'Fixture collector',@Version;
EXEC dbo.GetLeaveCertificatePrintData @User,@Record,@Copy;
BEGIN TRY UPDATE dbo.LeaveCertificateIssueSnapshots SET Remarks=N'Corrected before handover' WHERE LeaveCertificateIssueId=@Copy;THROW 51000,'Collected snapshot was editable.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: School Leaving print before handover, correction before collection and immutable collected snapshot';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks,CreatedByUserId,CertificateType) VALUES(@Student,N'Workflow Student',N'Parent','20100101','20200101',N'Ten',N'Eleven',CONVERT(date,GETDATE()),N'Good',N'Regular',N'Personal',@User,N'School');SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificateIssues(LeaveRecordId,IssuedAt,IssuedByUserId,IssueType) VALUES(@Record,SYSDATETIME(),@User,N'Original');SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy;
EXEC dbo.MarkLeaveCertificateCollected @User,@Copy,N'Fixture collector',@Version;
DECLARE @OriginalName nvarchar(500)=(SELECT StudentName FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy);
UPDATE dbo.Students SET StudentName=N'Corrected Workflow Student',Gender=N'Female' WHERE AdmissionId=@Student;
SET @Hash=dbo.CertificateAdmissionFingerprint(@Student);
DECLARE @Lifecycle nvarchar(max)=(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES);
EXEC dbo.IssueLeaveCertificate @UserId=@User,@LeaveRecordId=@Record,@ExpectedLatestIssueId=@Copy,@ExpectedSourceHash=@Hash;
SELECT @New=MAX(LeaveCertificateIssueId) FROM dbo.LeaveCertificateIssues WHERE LeaveRecordId=@Record;
IF @New=@Copy OR NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@New AND StudentName=N'Corrected Workflow Student') THROW 51000,'New copy did not use current personal details.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy AND StudentName=@OriginalName) THROW 51000,'Earlier copy changed during reissue.',1;
IF @Lifecycle<>(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES) THROW 51000,'Reissue changed student lifecycle data.',1;
ROLLBACK;SELECT N'PASS: School Leaving collected copy can be replaced with fresh details while preserving the earlier copy and student lifecycle';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks,CreatedByUserId,CertificateType) VALUES(@Student,N'Workflow Student',N'Parent','20100101','20200101',N'Ten',N'Eleven',CONVERT(date,GETDATE()),N'Good',N'Regular',N'Personal',@User,N'College');SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificateIssues(LeaveRecordId,IssuedAt,IssuedByUserId,IssueType) VALUES(@Record,SYSDATETIME(),@User,N'Original');SET @Copy=CONVERT(int,SCOPE_IDENTITY());
EXEC dbo.GetLeaveCertificatePrintData @User,@Record,@Copy;
UPDATE dbo.LeaveCertificateIssueSnapshots SET Remarks=N'Corrected before handover' WHERE LeaveCertificateIssueId=@Copy;
SELECT @Version=ContentVersion FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy;
EXEC dbo.MarkLeaveCertificateCollected @User,@Copy,N'Fixture collector',@Version;
EXEC dbo.GetLeaveCertificatePrintData @User,@Record,@Copy;
BEGIN TRY UPDATE dbo.LeaveCertificateIssueSnapshots SET Remarks=N'Corrected before handover' WHERE LeaveCertificateIssueId=@Copy;THROW 51000,'Collected snapshot was editable.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>58900 BEGIN IF @@TRANCOUNT>0 ROLLBACK;THROW;END;IF @@TRANCOUNT>0 ROLLBACK;END CATCH;
SELECT N'PASS: College Leaving print before handover, correction before collection and immutable collected snapshot';

GO

SET XACT_ABORT ON;
DECLARE @User int=(SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId),@Student int,@Staff int=(SELECT TOP(1) StaffId FROM dbo.Staff ORDER BY StaffId),@Record int,@Copy int,@New int,@Gender nvarchar(20),@Version binary(8),@Hash varbinary(32),@Request uniqueidentifier=NEWID();
BEGIN TRANSACTION;
IF @Staff IS NULL BEGIN INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status) VALUES(CONCAT(N'WF-',LEFT(CONVERT(nvarchar(36),NEWID()),12)),N'Workflow Staff',N'Male',N'Active');SET @Staff=CONVERT(int,SCOPE_IDENTITY());END;
INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,Gender,DateOfBirth,Status)
VALUES(CONCAT(N'8',DATEDIFF_BIG(microsecond,CONVERT(datetime2,'20200101'),SYSDATETIME())),CONCAT(N'WF-',LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20)),'20200101',(SELECT TOP(1) CampusId FROM dbo.Campuses),N'Workflow Student',N'Workflow Parent',N'Male','20100101',N'Active');
SET @Student=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks,CreatedByUserId,CertificateType) VALUES(@Student,N'Workflow Student',N'Parent','20100101','20200101',N'Ten',N'Eleven',CONVERT(date,GETDATE()),N'Good',N'Regular',N'Personal',@User,N'College');SET @Record=CONVERT(int,SCOPE_IDENTITY());
INSERT dbo.LeaveCertificateIssues(LeaveRecordId,IssuedAt,IssuedByUserId,IssueType) VALUES(@Record,SYSDATETIME(),@User,N'Original');SET @Copy=CONVERT(int,SCOPE_IDENTITY());
SELECT @Version=ContentVersion FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy;
EXEC dbo.MarkLeaveCertificateCollected @User,@Copy,N'Fixture collector',@Version;
DECLARE @OriginalName nvarchar(500)=(SELECT StudentName FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy);
UPDATE dbo.Students SET StudentName=N'Corrected Workflow Student',Gender=N'Female' WHERE AdmissionId=@Student;
SET @Hash=dbo.CertificateAdmissionFingerprint(@Student);
DECLARE @Lifecycle nvarchar(max)=(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES);
EXEC dbo.IssueLeaveCertificate @UserId=@User,@LeaveRecordId=@Record,@ExpectedLatestIssueId=@Copy,@ExpectedSourceHash=@Hash;
SELECT @New=MAX(LeaveCertificateIssueId) FROM dbo.LeaveCertificateIssues WHERE LeaveRecordId=@Record;
IF @New=@Copy OR NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@New AND StudentName=N'Corrected Workflow Student') THROW 51000,'New copy did not use current personal details.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificateIssueSnapshots WHERE LeaveCertificateIssueId=@Copy AND StudentName=@OriginalName) THROW 51000,'Earlier copy changed during reissue.',1;
IF @Lifecycle<>(SELECT * FROM dbo.Students WHERE AdmissionId=@Student FOR JSON PATH,INCLUDE_NULL_VALUES) THROW 51000,'Reissue changed student lifecycle data.',1;
ROLLBACK;SELECT N'PASS: College Leaving collected copy can be replaced with fresh details while preserving the earlier copy and student lifecycle';

GO
