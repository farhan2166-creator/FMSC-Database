-- Communication module reference definition for runtime verification and disposable SQL fixtures.
-- Already installed on the current database; not a pending deployment migration.
SET XACT_ABORT ON;
-- Required for filtered indexes, including when run through sqlcmd/SSMS.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF TRY_CONVERT(int,SESSION_CONTEXT(N'FMSC_CommunicationInstall'))<>1 OR SESSION_CONTEXT(N'FMSC_CommunicationInstall') IS NULL
 THROW 51000,'Explicit communication installation context is required.',1;
IF (SELECT MAX(TRY_CONVERT(int,VersionNumber)) FROM dbo.DatabaseVersion)<>6
 THROW 51000,'Communication add-on requires the reviewed schema-6 baseline.',1;
IF SCHEMA_ID(N'comm') IS NOT NULL THROW 51000,'Communication schema already exists. Verify it; do not overwrite it.',1;
BEGIN TRANSACTION;
EXEC(N'CREATE SCHEMA comm AUTHORIZATION dbo');
GO
CREATE TABLE comm.Module(Version int NOT NULL PRIMARY KEY CHECK(Version=1));
CREATE TABLE comm.Settings(
 Id int NOT NULL PRIMARY KEY CHECK(Id=1), Mode varchar(12) NOT NULL CHECK(Mode IN('Disabled','Simulation','Live')),
 Paused bit NOT NULL, MaxBatch int NOT NULL CHECK(MaxBatch BETWEEN 1 AND 500), ApprovalThreshold int NOT NULL CHECK(ApprovalThreshold BETWEEN 1 AND 500),
 HourLimit int NOT NULL CHECK(HourLimit BETWEEN 1 AND 10000), DayLimit int NOT NULL CHECK(DayLimit BETWEEN 1 AND 100000),
 DailyBudget decimal(18,4) NOT NULL CHECK(DailyBudget>=0), MonthlyBudget decimal(18,4) NOT NULL CHECK(MonthlyBudget>=0),
 SmsUnitCost decimal(18,4) NULL CHECK(SmsUnitCost>=0), WhatsAppUnitCost decimal(18,4) NULL CHECK(WhatsAppUnitCost>=0),
 Currency char(3) NOT NULL, QuietStart int NOT NULL CHECK(QuietStart BETWEEN 0 AND 23), QuietEnd int NOT NULL CHECK(QuietEnd BETWEEN 0 AND 23),
 UtcOffsetMinutes int NOT NULL CHECK(UtcOffsetMinutes BETWEEN -720 AND 840), RegistrationConfirmed bit NOT NULL,
 NextSendUtc datetime2 NOT NULL, UpdatedBy int NULL REFERENCES dbo.Users(UserId), UpdatedUtc datetime2 NOT NULL);
INSERT comm.Settings VALUES(1,'Disabled',1,100,25,60,200,10,100,NULL,NULL,'USD',20,8,300,0,SYSUTCDATETIME(),NULL,SYSUTCDATETIME());
CREATE TABLE comm.Contact(
 ContactId uniqueidentifier NOT NULL PRIMARY KEY, OwnerKind varchar(10) NOT NULL CHECK(OwnerKind IN('Student','Family','Staff','Manual')),
 AdmissionId int NULL REFERENCES dbo.Students(AdmissionId), FamilyId int NULL REFERENCES dbo.Families(FamilyId) ON DELETE SET NULL, OriginalFamilyId int NULL, StaffId int NULL REFERENCES dbo.Staff(StaffId),
 ContactName nvarchar(150) NOT NULL, Destination varchar(16) NOT NULL, Language varchar(10) NOT NULL CHECK(Language IN('en','ur','en_US')),
 PreferredChannel varchar(10) NOT NULL CHECK(PreferredChannel IN('Sms','WhatsApp')), Verified bit NOT NULL,
 SmsConsent bit NOT NULL, WhatsAppConsent bit NOT NULL, FinancialConsent bit NOT NULL, AnnouncementConsent bit NOT NULL,
 OptedOut bit NOT NULL, ConsentSource nvarchar(200) NOT NULL, ConsentUtc datetime2 NOT NULL, UpdatedBy int NOT NULL REFERENCES dbo.Users(UserId),
 LastDeliveryUtc datetime2 NULL, Revision rowversion NOT NULL,
 CONSTRAINT CK_CommContact_Owner CHECK(
 (OwnerKind='Student' AND AdmissionId IS NOT NULL AND FamilyId IS NULL AND StaffId IS NULL) OR
 (OwnerKind='Family' AND OriginalFamilyId IS NOT NULL AND AdmissionId IS NULL AND StaffId IS NULL) OR
 (OwnerKind='Staff' AND StaffId IS NOT NULL AND AdmissionId IS NULL AND FamilyId IS NULL) OR
 (OwnerKind='Manual' AND StaffId IS NULL AND AdmissionId IS NULL AND FamilyId IS NULL)),
 CONSTRAINT CK_CommContact_Phone CHECK(LEFT(Destination,1)='+' AND LEN(Destination) BETWEEN 9 AND 16 AND SUBSTRING(Destination,2,16) NOT LIKE '%[^0-9]%'),
 CONSTRAINT CK_CommContact_Source CHECK(LEN(LTRIM(RTRIM(ConsentSource)))>0));
CREATE UNIQUE INDEX UX_CommContact_Identity ON comm.Contact(OwnerKind,AdmissionId,OriginalFamilyId,StaffId,Destination);
CREATE TABLE comm.Template(
 TemplateId uniqueidentifier NOT NULL PRIMARY KEY, Code varchar(80) NOT NULL, Version int NOT NULL CHECK(Version>0),
 Language varchar(10) NOT NULL CHECK(Language IN('en','ur','en_US')), Category varchar(20) NOT NULL CHECK(Category IN('Utility','Financial','Announcement')),
 Scope varchar(10) NOT NULL CHECK(Scope IN('Student','Family','Staff','Manual')), Body nvarchar(2000) NOT NULL,
 ProviderTemplate varchar(512) NOT NULL, ParameterOrder nvarchar(1000) NOT NULL CHECK(ISJSON(ParameterOrder)=1), Approved bit NOT NULL,
 CreatedBy int NULL REFERENCES dbo.Users(UserId), CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT UX_CommTemplate_Version UNIQUE(Code,Language,Version));
CREATE TABLE comm.Campaign(
 CampaignId uniqueidentifier NOT NULL PRIMARY KEY, RequestHash binary(32) NOT NULL, ContentHash binary(32) NOT NULL, TemplateId uniqueidentifier NOT NULL REFERENCES comm.Template(TemplateId),
 Channel varchar(10) NOT NULL CHECK(Channel IN('Sms','WhatsApp')), Status varchar(20) NOT NULL CHECK(Status IN('Draft','AwaitingApproval','Scheduled','Queued','Paused','Cancelled')),
 CreatedBy int NOT NULL REFERENCES dbo.Users(UserId), ApprovedBy int NULL REFERENCES dbo.Users(UserId), DispatchActor int NULL REFERENCES dbo.Users(UserId),
 CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), DueUtc datetime2 NOT NULL, Announcement nvarchar(1000) NOT NULL,
 EventKey nvarchar(150) NULL, PaymentId int NULL REFERENCES dbo.FeePayments(FeePaymentId));
CREATE INDEX IX_CommCampaign_Duplicate ON comm.Campaign(CreatedBy,ContentHash,CreatedUtc) INCLUDE(Status);
CREATE UNIQUE INDEX UX_CommCampaign_Event ON comm.Campaign(EventKey) WHERE EventKey IS NOT NULL;
CREATE TABLE comm.Message(
 MessageId uniqueidentifier NOT NULL PRIMARY KEY, CampaignId uniqueidentifier NOT NULL REFERENCES comm.Campaign(CampaignId),
 ContactId uniqueidentifier NOT NULL REFERENCES comm.Contact(ContactId), AdmissionId int NULL REFERENCES dbo.Students(AdmissionId), EnrollmentId int NULL REFERENCES dbo.Enrollments(EnrollmentId),
 Destination varchar(16) NOT NULL, Body nvarchar(2000) NOT NULL, Parameters nvarchar(4000) NOT NULL CHECK(ISJSON(Parameters)=1),
 Simulation bit NOT NULL, Segments int NOT NULL CHECK(Segments BETWEEN 1 AND 30), EstimatedCost decimal(18,4) NULL CHECK(EstimatedCost>=0), Currency char(3) NOT NULL,
 State varchar(20) NOT NULL CHECK(State IN('Draft','AwaitingApproval','Scheduled','Queued','Submitting','Accepted','Sent','Delivered','Read','Failed','Rejected','DeliveryUnknown','Cancelled')),
 ClaimToken uniqueidentifier NULL, LeaseUntil datetime2 NULL, Attempts int NOT NULL DEFAULT 0 CHECK(Attempts>=0),
 Provider varchar(20) NULL, ProviderReference varchar(200) NULL, SafeToRetry bit NOT NULL DEFAULT 0, LastCode varchar(100) NOT NULL DEFAULT '',
 NextAttemptUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), UpdatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT UX_CommMessage_Recipient UNIQUE(CampaignId,ContactId));
CREATE INDEX IX_CommMessage_Queue ON comm.Message(State,NextAttemptUtc) INCLUDE(CampaignId);
CREATE UNIQUE INDEX UX_CommMessage_Provider ON comm.Message(Provider,ProviderReference) WHERE ProviderReference IS NOT NULL;
CREATE TABLE comm.Attempt(
 AttemptId bigint IDENTITY PRIMARY KEY, MessageId uniqueidentifier NOT NULL REFERENCES comm.Message(MessageId), ClaimToken uniqueidentifier NOT NULL UNIQUE,
 Actor int NOT NULL REFERENCES dbo.Users(UserId), CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), ReservedCost decimal(18,4) NOT NULL CHECK(ReservedCost>=0));
CREATE INDEX IX_CommAttempt_Budget ON comm.Attempt(CreatedUtc) INCLUDE(ReservedCost);
CREATE TABLE comm.PendingReceipt(ProviderReference varchar(200) NOT NULL PRIMARY KEY,State varchar(20) NOT NULL CHECK(State IN('Sent','Delivered','Read','Failed')),ReceivedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE TABLE comm.WebhookInbox(PayloadHash binary(32) NOT NULL PRIMARY KEY,ReceivedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE TABLE comm.Audit(
 AuditId bigint IDENTITY PRIMARY KEY, Actor int NOT NULL REFERENCES dbo.Users(UserId), Operation varchar(40) NOT NULL,
 EntityId uniqueidentifier NULL, Detail nvarchar(200) NOT NULL, CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE TABLE comm.[Rule](
 RuleId uniqueidentifier NOT NULL PRIMARY KEY, Name nvarchar(100) NOT NULL, EventType varchar(30) NOT NULL CHECK(EventType IN('PaymentCompleted','AdmissionCompleted','PromotionCompleted','ReturnConfirmed','MonthlyOutstanding')),
 TemplateId uniqueidentifier NOT NULL REFERENCES comm.Template(TemplateId), Channel varchar(10) NOT NULL CHECK(Channel IN('Sms','WhatsApp')),
 Enabled bit NOT NULL DEFAULT 0, EnabledUtc datetime2 NOT NULL, RequiresApproval bit NOT NULL DEFAULT 1,
 DelayMinutes int NOT NULL CHECK(DelayMinutes BETWEEN 0 AND 43200), BatchLimit int NOT NULL CHECK(BatchLimit BETWEEN 1 AND 100),
 Actor int NOT NULL REFERENCES dbo.Users(UserId), StartAfter bigint NOT NULL DEFAULT 0);
-- Completion may occur on an already-created quick registration or enrollment.
CREATE TABLE comm.RuleBaseline(RuleId uniqueidentifier NOT NULL REFERENCES comm.[Rule](RuleId),EventId bigint NOT NULL,CONSTRAINT PK_CommRuleBaseline PRIMARY KEY(RuleId,EventId));
GO
CREATE FUNCTION comm.Balance(@AdmissionId int) RETURNS decimal(18,2) AS
BEGIN
 RETURN (SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled');
END;
GO
CREATE FUNCTION comm.Segments(@Text nvarchar(2000)) RETURNS int AS
BEGIN
 DECLARE @Basic nvarchar(200)=N'@£$¥èéùìòÇ'+NCHAR(10)+N'Øø'+NCHAR(13)+N'ÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !"#¤%&''()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà';
 DECLARE @Ext nvarchar(20)=NCHAR(12)+N'^{}\[~]|€',@i int=1,@units int=0,@unicode bit=0,@len int=DATALENGTH(@Text)/2,@c nchar(1);
 WHILE @i<=@len BEGIN
  SET @c=SUBSTRING(@Text COLLATE Latin1_General_100_BIN2,@i,1);
  IF CHARINDEX(@c COLLATE Latin1_General_100_BIN2,@Basic COLLATE Latin1_General_100_BIN2)>0 SET @units+=1;
  ELSE IF CHARINDEX(@c COLLATE Latin1_General_100_BIN2,@Ext COLLATE Latin1_General_100_BIN2)>0 SET @units+=2;
  ELSE SET @unicode=1;
  SET @i+=1;
 END;
 IF @unicode=1 RETURN CASE WHEN @len<=70 THEN 1 ELSE (@len+66)/67 END;
 RETURN CASE WHEN @units<=160 THEN 1 ELSE (@units+152)/153 END;
END;
GO
CREATE FUNCTION comm.ValuesFor(@ContactId uniqueidentifier,@Announcement nvarchar(1000),@PaymentId int)
RETURNS TABLE AS RETURN
 SELECT C.ContactId,C.AdmissionId,C.FamilyId,C.OwnerKind,C.ContactName,C.Destination,C.Verified,C.OptedOut,C.SmsConsent,C.WhatsAppConsent,C.FinancialConsent,C.AnnouncementConsent,
 ISNULL((SELECT TOP(1) SchoolName FROM dbo.SchoolProfile ORDER BY SchoolProfileId),N'') school,
 ISNULL(S.StudentName,ISNULL(ST.StaffName,C.ContactName)) student,ISNULL(CL.ClassName,N'') class,ISNULL(Y.YearName,N'') year,
 CONVERT(nvarchar(40),CASE WHEN @PaymentId IS NOT NULL THEN P.TotalAmount ELSE comm.Balance(S.AdmissionId) END) amount,
 ISNULL(P.ReceiptNumber,N'') receipt,ISNULL(CONVERT(nvarchar(10),(SELECT MIN(DueDate) FROM dbo.BillingCharges WHERE AdmissionId=S.AdmissionId AND Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BillingChargeId)>0),23),N'') dueDate,
 ISNULL(@Announcement,N'') announcement,E.AcademicYearId,CC.CampusId,CC.ClassId,CC.SectionId,S.Status StudentStatus,
 C.Language,C.PreferredChannel,E.EnrollmentId
 FROM comm.Contact C LEFT JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId LEFT JOIN dbo.Staff ST ON ST.StaffId=C.StaffId
 LEFT JOIN dbo.FeePayments P ON P.FeePaymentId=@PaymentId AND P.AdmissionId=S.AdmissionId AND P.Status=N'Completed'
 OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments X WHERE X.AdmissionId=S.AdmissionId AND (@PaymentId IS NULL OR X.EnrollmentId=P.EnrollmentId) ORDER BY X.EnrollmentDate DESC,X.EnrollmentId DESC) E
 LEFT JOIN dbo.CampusClassSections CC ON CC.ClassSectionId=E.ClassSectionId LEFT JOIN dbo.Classes CL ON CL.ClassId=CC.ClassId
 LEFT JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
 WHERE C.ContactId=@ContactId;
GO
CREATE FUNCTION comm.Render(@Body nvarchar(max),@ContactId uniqueidentifier,@Announcement nvarchar(1000),@PaymentId int) RETURNS nvarchar(max) AS
BEGIN
 -- Parse the original template once. Inserted school/person data is always literal.
 DECLARE @school nvarchar(max),@student nvarchar(max),@class nvarchar(max),@year nvarchar(max),@amount nvarchar(max),@receipt nvarchar(max),@dueDate nvarchar(max),@announcementValue nvarchar(max);
 SELECT @school=school,@student=student,@class=class,@year=year,@amount=amount,@receipt=receipt,@dueDate=dueDate,@announcementValue=announcement FROM comm.ValuesFor(@ContactId,@Announcement,@PaymentId);
 DECLARE @Result nvarchar(max)=N'',@Position int=1,@Open int,@Close int,@Field nvarchar(30),@Value nvarchar(max),@Length int=DATALENGTH(@Body)/2;
 WHILE @Position<=@Length
 BEGIN
  SET @Open=CHARINDEX(N'{{',@Body COLLATE Latin1_General_100_BIN2,@Position);
  IF @Open=0 RETURN @Result+SUBSTRING(@Body COLLATE Latin1_General_100_BIN2,@Position,@Length);
  SET @Result+=SUBSTRING(@Body COLLATE Latin1_General_100_BIN2,@Position,@Open-@Position);
  SET @Close=CHARINDEX(N'}}',@Body COLLATE Latin1_General_100_BIN2,@Open+2);
  IF @Close=0 RETURN @Result+SUBSTRING(@Body COLLATE Latin1_General_100_BIN2,@Open,@Length);
  SET @Field=SUBSTRING(@Body COLLATE Latin1_General_100_BIN2,@Open+2,@Close-@Open-2);
  SET @Value=CASE @Field WHEN N'school' THEN @school WHEN N'student' THEN @student WHEN N'class' THEN @class WHEN N'year' THEN @year WHEN N'amount' THEN @amount WHEN N'receipt' THEN @receipt WHEN N'dueDate' THEN @dueDate WHEN N'announcement' THEN @announcementValue ELSE N'' END;
  SET @Result+=ISNULL(@Value,N'');SET @Position=@Close+2;
 END;
 RETURN @Result;
END;
GO
CREATE PROCEDURE comm.Preview @Actor int,@TemplateId uniqueidentifier,@Channel varchar(10),@ContactIds nvarchar(max),@Announcement nvarchar(1000)=N'',@PaymentId int=NULL AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Compose';
 IF ISJSON(@ContactIds)<>1 OR @Channel NOT IN('Sms','WhatsApp') THROW 51001,'Invalid communication selection.',1;
 IF (SELECT COUNT(*) FROM OPENJSON(@ContactIds)) NOT BETWEEN 1 AND 500 THROW 51001,'Choose 1 to 500 contacts.',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@ContactIds) WHERE TRY_CONVERT(uniqueidentifier,value) IS NULL) THROW 51001,'Invalid contact identity.',1;
 DECLARE @Scope varchar(10)=(SELECT Scope FROM comm.Template WHERE TemplateId=@TemplateId),@Category varchar(20)=(SELECT Category FROM comm.Template WHERE TemplateId=@TemplateId);
 IF @Scope IN('Student','Family') EXEC dbo.RequirePermission @Actor,N'Students.View';
 IF @Scope='Staff' EXEC dbo.RequirePermission @Actor,N'Staff.View';
 IF @Category='Financial' EXEC dbo.RequirePermission @Actor,N'Fees.View';
 SELECT C.ContactId,C.ContactName,C.Destination,C.AdmissionId,V.EnrollmentId,C.FamilyId,
 comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId) Body,
 Rendered.Parameters,
 comm.Segments(comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId)) Segments,
 CASE WHEN S.Mode<>'Live' THEN 0 WHEN @Channel='Sms' THEN S.SmsUnitCost*comm.Segments(comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId)) ELSE S.WhatsAppUnitCost END EstimatedCost,S.Currency,
 CASE WHEN DATALENGTH(Rendered.Parameters)>8000 THEN N'Personalized provider parameters exceed the supported size; shorten the template' WHEN DATALENGTH(comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId))>4000 THEN N'Personalized message exceeds 2000 characters; shorten the template' WHEN C.OwnerKind='Family' AND C.FamilyId IS NULL THEN N'Family link retired; select a current family contact' WHEN T.Approved=0 THEN N'Template requires approval' WHEN C.OwnerKind<>T.Scope THEN N'Contact context does not match template'
 WHEN C.Language<>T.Language THEN N'Template language does not match contact' WHEN C.Verified=0 THEN N'Unverified contact'
 WHEN C.OptedOut=1 THEN N'Opted out' WHEN @Channel='Sms' AND C.SmsConsent=0 THEN N'No SMS consent'
 WHEN @Channel='WhatsApp' AND C.WhatsAppConsent=0 THEN N'No WhatsApp consent'
 WHEN @Category='Financial' AND C.FinancialConsent=0 THEN N'Financial messages not consented'
 WHEN @Category='Announcement' AND C.AnnouncementConsent=0 THEN N'Announcements not consented'
 WHEN @Channel='WhatsApp' AND LEN(T.ProviderTemplate)=0 THEN N'WhatsApp template mapping missing'
 WHEN T.Body LIKE N'%{{receipt}}%' AND @PaymentId IS NULL THEN N'A verified payment event is required for this receipt template'
 WHEN T.Code='admission_completed' AND NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=C.AdmissionId AND Status=N'Active' AND AdmissionNumber IS NOT NULL) THEN N'Admission is not complete'
 WHEN T.Code='placement_confirmed' AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=V.EnrollmentId AND PlacementConfirmedAt IS NOT NULL) THEN N'Placement is not confirmed'
 WHEN T.Code='return_confirmed' AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=V.EnrollmentId AND ReturnConfirmedAt IS NOT NULL) THEN N'Return admission is not confirmed'
 WHEN T.Code='promotion_completed' AND NOT EXISTS(SELECT 1 FROM dbo.PromotionBatchStudents WHERE NewEnrollmentId=V.EnrollmentId AND Decision=N'PROMOTE') THEN N'Promotion is not verified'
 WHEN T.Code='outstanding' AND comm.Balance(C.AdmissionId)<=0 THEN N'No outstanding balance'
 WHEN @PaymentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.FeePayments WHERE FeePaymentId=@PaymentId AND AdmissionId=C.AdmissionId AND Status=N'Completed' AND PaymentMethod NOT IN(N'Refund',N'Correction')) THEN N'Payment is not a verified collection'
 ELSE N'' END Exclusion
 FROM (SELECT DISTINCT TRY_CONVERT(uniqueidentifier,value) Id FROM OPENJSON(@ContactIds)) I JOIN comm.Contact C ON C.ContactId=I.Id
 CROSS APPLY comm.ValuesFor(C.ContactId,@Announcement,@PaymentId) V
 CROSS JOIN comm.Template T CROSS JOIN comm.Settings S
 CROSS APPLY(SELECT ISNULL((SELECT comm.Render(N'{{'+CONVERT(nvarchar(30),J.value)+N'}}',C.ContactId,@Announcement,@PaymentId) text FROM OPENJSON(T.ParameterOrder) J ORDER BY TRY_CONVERT(int,J.[key]) FOR JSON PATH),N'[]') Parameters) Rendered WHERE T.TemplateId=@TemplateId AND S.Id=1 ORDER BY C.ContactName,C.ContactId;
END;
GO
CREATE PROCEDURE comm.SaveContact @Actor int,@Id uniqueidentifier,@Kind varchar(10),@AdmissionId int=NULL,@FamilyId int=NULL,@StaffId int=NULL,
 @Name nvarchar(150),@Phone varchar(16),@Language varchar(10),@Channel varchar(10),@Verified bit,@Sms bit,@WhatsApp bit,@Financial bit,@Announcements bit,@OptOut bit,@Source nvarchar(200),@ExpectedRevision binary(8)=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 IF @Kind='Family' AND (@FamilyId IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.Families WHERE FamilyId=@FamilyId)) AND NOT EXISTS(SELECT 1 FROM comm.Contact WHERE ContactId=@Id) THROW 51001,'Choose an existing family for a new family contact.',1;
 IF @Phone NOT LIKE '+%' OR LEN(@Name)=0 OR LEN(@Source)=0 THROW 51001,'A named, attributable contact and consent source are required.',1;
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM comm.Contact WITH(UPDLOCK,HOLDLOCK) WHERE ContactId=@Id)
 BEGIN
  IF @ExpectedRevision IS NULL OR NOT EXISTS(SELECT 1 FROM comm.Contact WHERE ContactId=@Id AND Revision=@ExpectedRevision) THROW 51004,'Contact or consent changed. Refresh before editing again.',1;
  IF EXISTS(SELECT 1 FROM comm.Contact WHERE ContactId=@Id AND (OwnerKind<>@Kind OR ISNULL(AdmissionId,0)<>ISNULL(@AdmissionId,0) OR ISNULL(FamilyId,0)<>ISNULL(@FamilyId,0) OR ISNULL(StaffId,0)<>ISNULL(@StaffId,0))) THROW 51001,'A contact cannot be reassigned to another person or family.',1;
  IF EXISTS(SELECT 1 FROM comm.Contact WHERE ContactId=@Id AND (Destination<>@Phone OR OptedOut=1 AND @OptOut=0) AND ConsentSource=@Source) THROW 51001,'A changed number or renewed opt-in requires fresh consent evidence.',1;
  UPDATE comm.Contact SET ContactName=@Name,Destination=@Phone,Language=@Language,PreferredChannel=@Channel,Verified=@Verified,SmsConsent=@Sms,WhatsAppConsent=@WhatsApp,FinancialConsent=@Financial,AnnouncementConsent=@Announcements,OptedOut=@OptOut,ConsentSource=@Source,ConsentUtc=SYSUTCDATETIME(),UpdatedBy=@Actor WHERE ContactId=@Id;
 END
 ELSE INSERT comm.Contact(ContactId,OwnerKind,AdmissionId,FamilyId,OriginalFamilyId,StaffId,ContactName,Destination,Language,PreferredChannel,Verified,SmsConsent,WhatsAppConsent,FinancialConsent,AnnouncementConsent,OptedOut,ConsentSource,ConsentUtc,UpdatedBy)
 VALUES(@Id,@Kind,@AdmissionId,@FamilyId,@FamilyId,@StaffId,@Name,@Phone,@Language,@Channel,@Verified,@Sms,@WhatsApp,@Financial,@Announcements,@OptOut,@Source,SYSUTCDATETIME(),@Actor);
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'ContactConsentSaved',@Id,CASE WHEN @OptOut=1 THEN N'Opted out' ELSE N'Contact or consent revised; previous message destinations preserved' END);
 COMMIT;
END;
GO
CREATE PROCEDURE comm.SaveTemplate @Actor int,@Id uniqueidentifier,@Code varchar(80),@Version int,@Language varchar(10),@Category varchar(20),@Scope varchar(10),@Body nvarchar(2000),@ProviderTemplate varchar(512),@Order nvarchar(1000),@Approved bit AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 DECLARE @R nvarchar(2000)=@Body;
 SELECT @R=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@R,N'{{school}}',N''),N'{{student}}',N''),N'{{class}}',N''),N'{{year}}',N''),N'{{amount}}',N''),N'{{receipt}}',N''),N'{{dueDate}}',N''),N'{{announcement}}',N'');
 IF LEN(@Body)=0 OR @R LIKE N'%{{%' OR @R LIKE N'%}}%' OR ISJSON(@Order)<>1 OR LEFT(LTRIM(@Order),1)<>'[' THROW 51001,'Invalid template text or parameter order.',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@Order) WHERE [type]<>1 OR value NOT IN('school','student','class','year','amount','receipt','dueDate','announcement')) THROW 51001,'Unknown template parameter.',1;
 IF @Scope IN('Family','Manual') AND (EXISTS(SELECT 1 FROM OPENJSON(@Order) WHERE value IN('student','amount','receipt','class','year','dueDate')) OR @Body LIKE N'%{{student}}%' OR @Body LIKE N'%{{amount}}%' OR @Body LIKE N'%{{receipt}}%' OR @Body LIKE N'%{{class}}%' OR @Body LIKE N'%{{year}}%' OR @Body LIKE N'%{{dueDate}}%') THROW 51001,'Personal student fields cannot be used in a family or manual template.',1;
 IF @Category<>'Financial' AND (@Body LIKE N'%{{amount}}%' OR @Body LIKE N'%{{receipt}}%' OR @Body LIKE N'%{{dueDate}}%' OR EXISTS(SELECT 1 FROM OPENJSON(@Order) WHERE value IN('amount','receipt','dueDate'))) THROW 51001,'Financial placeholders require the Financial category and Fees.View authority.',1;
 IF @Category='Financial' AND @Scope<>'Student' THROW 51001,'Financial templates require an explicitly linked student contact.',1;
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM comm.Template WITH(UPDLOCK,HOLDLOCK) WHERE TemplateId=@Id)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM comm.Template WHERE TemplateId=@Id AND Code=@Code AND Version=@Version AND Language=@Language AND Category=@Category AND Scope=@Scope AND Body=@Body AND ProviderTemplate=@ProviderTemplate AND ParameterOrder=@Order AND Approved=@Approved AND CreatedBy=@Actor) THROW 51002,'Template request identity was reused with changed content.',1;
  COMMIT;RETURN;
 END;
 INSERT comm.Template(TemplateId,Code,Version,Language,Category,Scope,Body,ProviderTemplate,ParameterOrder,Approved,CreatedBy) VALUES(@Id,@Code,@Version,@Language,@Category,@Scope,@Body,@ProviderTemplate,@Order,@Approved,@Actor);
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'TemplateVersionCreated',@Id,N'Immutable template version; approval explicitly recorded');
 COMMIT;
END;
GO
CREATE PROCEDURE comm.RevokeTemplate @Actor int,@Id uniqueidentifier AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 BEGIN TRANSACTION;
 UPDATE comm.Template SET Approved=0 WHERE TemplateId=@Id;
 IF @@ROWCOUNT<>1 THROW 51001,'Template not found.',1;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'TemplateRevoked',@Id,N'Unsubmitted messages will be suppressed; historical text is preserved');
 COMMIT;
END;
GO
CREATE PROCEDURE comm.CreateCampaign @Actor int,@Id uniqueidentifier,@TemplateId uniqueidentifier,@Channel varchar(10),@ContactIds nvarchar(max),@Announcement nvarchar(1000)=N'',@DueUtc datetime2,@EventKey nvarchar(150)=NULL,@PaymentId int=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Compose';
 DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),@TemplateId),N'|',@Channel,N'|',@ContactIds,N'|',@Announcement,N'|',CONVERT(nvarchar(40),@DueUtc,126),N'|',@EventKey,N'|',@PaymentId));
 BEGIN TRANSACTION;
 DECLARE @Gate int; SELECT @Gate=Id FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
 IF EXISTS(SELECT 1 FROM comm.Campaign WITH(UPDLOCK,HOLDLOCK) WHERE CampaignId=@Id)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM comm.Campaign WHERE CampaignId=@Id AND RequestHash=@Hash AND CreatedBy=@Actor) THROW 51002,'Campaign request identity was reused with different data.',1;
  COMMIT; RETURN;
 END;
 DECLARE @ContentHash binary(32)=HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),@TemplateId),N'|',@Channel,N'|',@ContactIds,N'|',@Announcement,N'|',CONVERT(nvarchar(10),@DueUtc,23),N'|',@EventKey,N'|',@PaymentId));
 IF EXISTS(SELECT 1 FROM comm.Campaign WITH(UPDLOCK,HOLDLOCK) WHERE CreatedBy=@Actor AND ContentHash=@ContentHash AND Status<>'Cancelled' AND CreatedUtc>=DATEADD(MINUTE,-15,SYSUTCDATETIME())) THROW 51002,'A matching recent campaign already exists. Review Outbox before preparing it again.',1;
 CREATE TABLE #P(ContactId uniqueidentifier,ContactName nvarchar(150),Destination varchar(16),AdmissionId int,EnrollmentId int,FamilyId int,Body nvarchar(max),Parameters nvarchar(max),Segments int,EstimatedCost decimal(18,4),Currency char(3),Exclusion nvarchar(200));
 INSERT #P EXEC comm.Preview @Actor,@TemplateId,@Channel,@ContactIds,@Announcement,@PaymentId;
 IF EXISTS(SELECT 1 FROM #P WHERE LEN(Exclusion)>0) OR (SELECT COUNT(*) FROM #P)<>(SELECT COUNT(DISTINCT value) FROM OPENJSON(@ContactIds)) THROW 51001,'Review exclusions or missing contacts before creating the campaign.',1;
 IF (SELECT COUNT(*) FROM #P)>1 EXEC dbo.RequirePermission @Actor,N'Communication.Bulk';
 IF (SELECT COUNT(*) FROM #P)>(SELECT MaxBatch FROM comm.Settings) OR NOT EXISTS(SELECT 1 FROM #P) THROW 51001,'Campaign exceeds its configured batch limit or has no recipients.',1;
 -- Family-level messages deduplicate the shared phone. Student financial messages remain per student.
 IF (SELECT Scope FROM comm.Template WHERE TemplateId=@TemplateId) IN('Family','Manual')
 BEGIN
  ;WITH D AS(SELECT *,ROW_NUMBER() OVER(PARTITION BY Destination ORDER BY ContactId) N FROM #P) DELETE FROM D WHERE N>1;
 END;
 INSERT comm.Campaign VALUES(@Id,@Hash,@ContentHash,@TemplateId,@Channel,'Draft',@Actor,NULL,NULL,SYSUTCDATETIME(),@DueUtc,@Announcement,@EventKey,@PaymentId);
 INSERT comm.Message(MessageId,CampaignId,ContactId,AdmissionId,EnrollmentId,Destination,Body,Parameters,Simulation,Segments,EstimatedCost,Currency,State)
 SELECT NEWID(),@Id,ContactId,AdmissionId,EnrollmentId,Destination,Body,Parameters,CONVERT(bit,CASE WHEN (SELECT Mode FROM comm.Settings)<>'Live' THEN 1 ELSE 0 END),Segments,EstimatedCost,Currency,'Draft' FROM #P;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'CampaignCreated',@Id,N'Immutable recipient and message snapshots');
 COMMIT;
END;
GO
CREATE PROCEDURE comm.Transition @Actor int,@Id uniqueidentifier,@Action varchar(20) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @Actor,N'Communication.View';
 IF @Action='Approve' EXEC dbo.RequirePermission @Actor,N'Communication.Approve'; ELSE EXEC dbo.RequirePermission @Actor,N'Communication.Compose';
 IF @Action IN('Submit','Resume') EXEC dbo.RequirePermission @Actor,N'Communication.Send';
 BEGIN TRANSACTION;
 DECLARE @Gate int;SELECT @Gate=Id FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
 DECLARE @Owner int,@State varchar(20),@Count int,@Due datetime2,@New varchar(20);
 SELECT @Owner=CreatedBy,@State=Status,@Due=DueUtc FROM comm.Campaign WITH(UPDLOCK,HOLDLOCK) WHERE CampaignId=@Id;
 IF @Owner IS NULL THROW 51001,'Campaign not found.',1;
 IF @Owner<>@Actor AND @Action<>'Approve' EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 SELECT @Count=COUNT(*) FROM comm.Message WHERE CampaignId=@Id;
 IF @Count>1 AND @Action IN('Submit','Resume') EXEC dbo.RequirePermission @Actor,N'Communication.Bulk';
 IF @Action='Submit' AND @State='Draft'
 BEGIN
  SET @New=CASE WHEN (SELECT ApprovedBy FROM comm.Campaign WHERE CampaignId=@Id) IS NULL AND (@Count>=(SELECT ApprovalThreshold FROM comm.Settings) OR EXISTS(SELECT 1 FROM comm.Campaign WHERE CampaignId=@Id AND EventKey IS NOT NULL)) THEN 'AwaitingApproval' WHEN @Due>SYSUTCDATETIME() THEN 'Scheduled' ELSE 'Queued' END;
 END
 ELSE IF @Action='Approve' AND @State='AwaitingApproval'
 BEGIN
  IF @Owner=@Actor AND NOT EXISTS(SELECT 1 FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE U.UserId=@Actor AND R.RoleName=N'Administrator') THROW 51001,'Another authorized approver must approve this campaign.',1;
  SET @New=CASE WHEN @Due>SYSUTCDATETIME() THEN 'Scheduled' ELSE 'Queued' END;
  UPDATE comm.Campaign SET ApprovedBy=@Actor WHERE CampaignId=@Id;
 END
 ELSE IF @Action='Pause' AND @State IN('Queued','Scheduled') SET @New='Paused';
 ELSE IF @Action='Resume' AND @State='Paused' SET @New=CASE WHEN @Due>SYSUTCDATETIME() THEN 'Scheduled' ELSE 'Queued' END;
 ELSE IF @Action='Cancel' AND @State IN('Draft','AwaitingApproval','Queued','Scheduled','Paused') SET @New='Cancelled';
 ELSE THROW 51001,'This campaign transition is not permitted.',1;
 UPDATE comm.Campaign SET Status=@New,DispatchActor=CASE WHEN @Action IN('Submit','Resume') THEN @Actor ELSE DispatchActor END WHERE CampaignId=@Id;
 IF @New<>'Paused' UPDATE comm.Message SET State=@New,UpdatedUtc=SYSUTCDATETIME() WHERE CampaignId=@Id AND State IN('Draft','AwaitingApproval','Queued','Scheduled');
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,@Action,@Id,N'Only unsubmitted messages are affected');
 COMMIT;
END;
GO
CREATE PROCEDURE comm.Claim @Actor int AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL READ COMMITTED; EXEC dbo.RequirePermission @Actor,N'Communication.Send';
 BEGIN TRANSACTION;
 DECLARE @Now datetime2=SYSUTCDATETIME(),@Mode varchar(12),@MaxHour int,@MaxDay int,@DayBudget decimal(18,4),@MonthBudget decimal(18,4),@Hour int,@QS int,@QE int;
 SELECT @Mode=Mode,@MaxHour=HourLimit,@MaxDay=DayLimit,@DayBudget=DailyBudget,@MonthBudget=MonthlyBudget,@QS=QuietStart,@QE=QuietEnd,@Hour=DATEPART(HOUR,DATEADD(MINUTE,UtcOffsetMinutes,@Now))
 FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
 UPDATE comm.Message SET State='DeliveryUnknown',LastCode='LEASE_EXPIRED_RECONCILIATION_REQUIRED',UpdatedUtc=@Now WHERE State='Submitting' AND LeaseUntil<@Now;
 IF @Mode='Disabled' OR EXISTS(SELECT 1 FROM comm.Settings WHERE Paused=1 OR NextSendUtc>@Now OR (Mode='Live' AND RegistrationConfirmed=0))
 OR (@QS<>@QE AND ((@QS<@QE AND @Hour>=@QS AND @Hour<@QE) OR (@QS>@QE AND (@Hour>=@QS OR @Hour<@QE))))
 BEGIN COMMIT; RETURN; END;
 IF (SELECT COUNT(*) FROM comm.Attempt WHERE CreatedUtc>=DATEADD(HOUR,-1,@Now))>=@MaxHour OR
 (SELECT COUNT(*) FROM comm.Attempt WHERE CreatedUtc>=CONVERT(date,@Now))>=@MaxDay BEGIN COMMIT; RETURN; END;
 -- Revalidate mutable contact ownership/consent and paid reminders before any provider request.
 UPDATE M SET State='Cancelled',LastCode='CONTACT_OR_BALANCE_CHANGED',UpdatedUtc=@Now
 FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId JOIN comm.Contact C ON C.ContactId=M.ContactId JOIN comm.Template T ON T.TemplateId=P.TemplateId
 WHERE P.DispatchActor=@Actor AND M.State IN('Queued','Scheduled') AND (
 (C.OwnerKind='Family' AND C.FamilyId IS NULL) OR T.Approved=0 OR C.Verified=0 OR C.OptedOut=1 OR C.Destination<>M.Destination OR (P.Channel='Sms' AND C.SmsConsent=0) OR (P.Channel='WhatsApp' AND C.WhatsAppConsent=0) OR
 (T.Category='Financial' AND C.FinancialConsent=0) OR (T.Category='Announcement' AND C.AnnouncementConsent=0) OR
 (T.Category='Financial' AND P.PaymentId IS NULL AND (comm.Balance(M.AdmissionId)<=0 OR comm.Render(T.Body,C.ContactId,P.Announcement,P.PaymentId)<>M.Body)) OR
 (P.PaymentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.FeePayments F WHERE F.FeePaymentId=P.PaymentId AND F.AdmissionId=M.AdmissionId AND F.Status=N'Completed' AND F.PaymentMethod NOT IN(N'Refund',N'Correction'))));
 DECLARE @Id uniqueidentifier,@Campaign uniqueidentifier,@Cost decimal(18,4),@Token uniqueidentifier=NEWID();
 SELECT TOP(1) @Id=M.MessageId,@Campaign=M.CampaignId,@Cost=CASE WHEN @Mode='Simulation' THEN 0 ELSE M.EstimatedCost END
 FROM comm.Message M WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK) JOIN comm.Campaign P WITH(UPDLOCK) ON P.CampaignId=M.CampaignId
 WHERE M.State IN('Queued','Scheduled') AND M.NextAttemptUtc<=@Now AND P.DueUtc<=@Now AND P.Status IN('Queued','Scheduled') AND P.DispatchActor=@Actor
 AND M.Simulation=CASE WHEN @Mode='Simulation' THEN 1 ELSE 0 END
 AND (@Mode='Simulation' OR M.Currency=(SELECT Currency FROM comm.Settings) AND CASE WHEN P.Channel='Sms' THEN (SELECT SmsUnitCost FROM comm.Settings) ELSE (SELECT WhatsAppUnitCost FROM comm.Settings) END IS NOT NULL)
 AND ((SELECT COUNT(*) FROM comm.Message X WHERE X.CampaignId=P.CampaignId)<(SELECT ApprovalThreshold FROM comm.Settings) OR P.ApprovedBy IS NOT NULL) ORDER BY P.DueUtc,M.MessageId;
 IF @Id IS NULL BEGIN COMMIT; RETURN; END;
 IF @Mode='Live' SELECT @Cost=CASE WHEN P.Channel='Sms' THEN S.SmsUnitCost*M.Segments ELSE S.WhatsAppUnitCost END FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId CROSS JOIN comm.Settings S WHERE M.MessageId=@Id;
 IF (SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Campaign)>1 EXEC dbo.RequirePermission @Actor,N'Communication.Bulk';
 IF @Mode='Live' AND ((SELECT ISNULL(SUM(ReservedCost),0) FROM comm.Attempt WHERE CreatedUtc>=CONVERT(date,@Now))+@Cost>@DayBudget OR
 (SELECT ISNULL(SUM(ReservedCost),0) FROM comm.Attempt WHERE CreatedUtc>=DATEFROMPARTS(YEAR(@Now),MONTH(@Now),1))+@Cost>@MonthBudget) BEGIN COMMIT; RETURN; END;
 UPDATE comm.Message SET EstimatedCost=@Cost,State='Submitting',ClaimToken=@Token,LeaseUntil=DATEADD(MINUTE,2,@Now),Attempts=Attempts+1,UpdatedUtc=@Now WHERE MessageId=@Id;
 INSERT comm.Attempt(MessageId,ClaimToken,Actor,ReservedCost) VALUES(@Id,@Token,@Actor,@Cost);
 UPDATE comm.Settings SET NextSendUtc=DATEADD(SECOND,1,@Now) WHERE Id=1;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'SubmissionClaimed',@Id,N'One provider attempt reserved');
 SELECT M.MessageId,M.ClaimToken,P.Channel,M.Destination,M.Body,T.ProviderTemplate,T.Language,M.Parameters,M.Attempts,CONVERT(bit,CASE WHEN @Mode='Simulation' THEN 1 ELSE 0 END) Simulation
 FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId JOIN comm.Template T ON T.TemplateId=P.TemplateId WHERE M.MessageId=@Id;
 COMMIT;
END;
GO
CREATE PROCEDURE comm.Complete @Actor int,@Id uniqueidentifier,@Token uniqueidentifier,@State varchar(20),@Provider varchar(20),@Reference varchar(200)=NULL,@Code varchar(100)='',@RetrySafe bit=0,@RetryDelay int=0 AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Send';
 IF @RetryDelay NOT BETWEEN 0 AND 86400 THROW 51001,'Invalid provider retry interval.',1;
 IF @State NOT IN('Accepted','Rejected','DeliveryUnknown') THROW 51001,'A submission response cannot establish delivery.',1;
 BEGIN TRANSACTION;
 IF NOT EXISTS(SELECT 1 FROM comm.Attempt WITH(UPDLOCK,HOLDLOCK) WHERE MessageId=@Id AND ClaimToken=@Token AND Actor=@Actor) THROW 51001,'Submission claim does not belong to this actor.',1;
 UPDATE comm.Message SET State=CASE WHEN @State='Rejected' AND @RetrySafe=1 AND Attempts<3 THEN 'Queued' ELSE @State END,
 Provider=@Provider,ProviderReference=@Reference,SafeToRetry=CASE WHEN @State='Rejected' THEN @RetrySafe ELSE 0 END,LastCode=@Code,
 NextAttemptUtc=DATEADD(SECOND,CASE WHEN @RetryDelay>CASE WHEN Attempts=1 THEN 30 ELSE 120 END THEN @RetryDelay WHEN Attempts=1 THEN 30 ELSE 120 END,SYSUTCDATETIME()),UpdatedUtc=SYSUTCDATETIME()
 WHERE MessageId=@Id AND ClaimToken=@Token AND State IN('Submitting','DeliveryUnknown');
 -- A signed receipt can arrive before the submitting HTTP request returns.
 IF @Provider='MetaCloud' AND @Reference IS NOT NULL
 BEGIN
  UPDATE M SET State=R.State,UpdatedUtc=SYSUTCDATETIME()
  FROM comm.Message M JOIN comm.PendingReceipt R ON R.ProviderReference=M.ProviderReference
  WHERE M.MessageId=@Id AND M.ClaimToken=@Token AND M.State IN('Accepted','Sent','DeliveryUnknown');
  IF @@ROWCOUNT>0 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'EarlyReceiptReconciled',@Id,N'Previously signed provider receipt matched after acknowledgement');
  UPDATE C SET LastDeliveryUtc=SYSUTCDATETIME() FROM comm.Contact C JOIN comm.Message M ON M.ContactId=C.ContactId WHERE M.MessageId=@Id AND M.State IN('Delivered','Read');
  DELETE FROM comm.PendingReceipt WHERE ProviderReference=@Reference AND EXISTS(SELECT 1 FROM comm.Message WHERE MessageId=@Id AND ProviderReference=@Reference);
 END;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'SubmissionResult',@Id,@State+N': '+@Code);
 COMMIT;
END;
GO
CREATE PROCEDURE comm.StatusUpdate @Actor int,@Id uniqueidentifier,@Provider varchar(20),@Reference varchar(200),@State varchar(20) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Send';
 IF ISNULL(IS_ROLEMEMBER(N'FMSC_CommunicationWebhook'),0)<>1 EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 DECLARE @Old varchar(20);
 BEGIN TRANSACTION;
 SELECT @Old=State FROM comm.Message WITH(UPDLOCK,HOLDLOCK) WHERE MessageId=@Id AND Provider=@Provider AND ProviderReference=@Reference;
 IF @Old IS NULL THROW 51001,'Provider reference is not associated with this message.',1;
 IF @State NOT IN('Accepted','Sent','Delivered','Read','Failed','Rejected','DeliveryUnknown') THROW 51001,'Unsupported delivery state.',1;
 IF @Old<>@State AND ((@Old IN('Accepted','Sent','DeliveryUnknown') AND @State IN('Sent','Delivered','Read','Failed','Rejected')) OR (@Old='Delivered' AND @State='Read'))
 BEGIN
  UPDATE comm.Message SET State=@State,UpdatedUtc=SYSUTCDATETIME() WHERE MessageId=@Id;
  IF @State IN('Delivered','Read') UPDATE C SET LastDeliveryUtc=SYSUTCDATETIME() FROM comm.Contact C JOIN comm.Message M ON M.ContactId=C.ContactId WHERE M.MessageId=@Id;
  INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'DeliveryStatus',@Id,@State);
 END;
 COMMIT;
END;
GO
CREATE PROCEDURE comm.[Read] @Actor int,@View varchar(20),@Page int=0,@Search nvarchar(100)=N'',@Campus int=0,@Year int=0,@Class int=0,@Section int=0,@Filter varchar(20)='' AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @Actor,N'Communication.View';
 IF @Page NOT BETWEEN 0 AND 100000 THROW 51001,'Invalid result page.',1;
 IF @View='Contacts'
 BEGIN
  EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
  SELECT ContactId,OwnerKind,AdmissionId,FamilyId,StaffId,ContactName,Destination,Language,PreferredChannel,Verified,SmsConsent,WhatsAppConsent,FinancialConsent,AnnouncementConsent,OptedOut,ConsentSource,ConsentUtc,Revision,CASE WHEN OwnerKind='Family' AND FamilyId IS NULL THEN 'Retired family' ELSE 'Current' END LinkStatus FROM comm.Contact
  WHERE ContactName LIKE N'%'+@Search+N'%' OR Destination LIKE N'%'+@Search+N'%' ORDER BY ContactName,ContactId OFFSET @Page*100 ROWS FETCH NEXT 100 ROWS ONLY;
 END
 ELSE IF @View='Recipients'
 BEGIN
  EXEC dbo.RequirePermission @Actor,N'Communication.Compose';
  IF @Filter='Staff' EXEC dbo.RequirePermission @Actor,N'Staff.View'; ELSE IF @Filter<>'Manual' EXEC dbo.RequirePermission @Actor,N'Students.View';
  IF @Filter='Outstanding' EXEC dbo.RequirePermission @Actor,N'Fees.View';
  SELECT C.ContactId,C.OwnerKind,C.ContactName,C.Destination,V.student Student,V.class Class,V.year AcademicYear,C.Verified,C.OptedOut,C.Language,C.SmsConsent,C.WhatsAppConsent,
   V.AdmissionId,V.FamilyId,CASE WHEN @Filter='Outstanding' AND V.AdmissionId IS NOT NULL THEN comm.Balance(V.AdmissionId) END Outstanding
  FROM comm.Contact C CROSS APPLY comm.ValuesFor(C.ContactId,N'',NULL) V
  WHERE (C.OwnerKind<>'Family' OR C.FamilyId IS NOT NULL) AND (@Campus=0 OR V.CampusId=@Campus) AND (@Year=0 OR V.AcademicYearId=@Year) AND (@Class=0 OR V.ClassId=@Class) AND (@Section=0 OR V.SectionId=@Section)
   AND (C.ContactName LIKE N'%'+@Search+N'%' OR V.student LIKE N'%'+@Search+N'%')
   AND (@Filter='' AND C.OwnerKind<>'Staff' OR @Filter='Outstanding' AND comm.Balance(V.AdmissionId)>0 OR @Filter='Staff' AND C.OwnerKind='Staff' OR @Filter='Family' AND C.OwnerKind='Family' OR @Filter='Manual' AND C.OwnerKind='Manual' OR @Filter='Pending' AND V.StudentStatus=N'Pending')
  ORDER BY C.ContactName,C.ContactId OFFSET @Page*100 ROWS FETCH NEXT 100 ROWS ONLY;
 END
 ELSE IF @View='Operational' SELECT Mode,Paused,Currency,MaxBatch,HourLimit,DayLimit FROM comm.Settings;
 ELSE IF @View='Campuses' SELECT CampusId,CampusName FROM dbo.Campuses ORDER BY CampusName;
 ELSE IF @View='Years' SELECT AcademicYearId,YearName FROM dbo.AcademicYears ORDER BY StartDate DESC;
 ELSE IF @View='Classes' SELECT ClassId,ClassName FROM dbo.Classes ORDER BY DisplayOrder;
 ELSE IF @View='Sections' SELECT SectionId,SectionName FROM dbo.Sections ORDER BY DisplayOrder;
 ELSE IF @View='Templates' SELECT * FROM comm.Template ORDER BY Code,Language,Version DESC OFFSET @Page*100 ROWS FETCH NEXT 100 ROWS ONLY;
 ELSE IF @View IN('History','Outbox')
 BEGIN
  IF @View='History' EXEC dbo.RequirePermission @Actor,N'Communication.History';
  SELECT M.MessageId,M.CampaignId,S.StudentName Student,C.ContactName Recipient,T.Code Template,T.Language,P.CreatedBy,P.ApprovedBy,P.DueUtc,P.Status CampaignStatus,M.State,M.Simulation,M.Provider,M.ProviderReference,M.Attempts,M.LastCode,
   CONCAT(LEFT(M.Destination,3),'******',RIGHT(M.Destination,3)) Destination,M.EstimatedCost,M.Currency,M.UpdatedUtc
  FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId JOIN comm.Contact C ON C.ContactId=M.ContactId JOIN comm.Template T ON T.TemplateId=P.TemplateId LEFT JOIN dbo.Students S ON S.AdmissionId=M.AdmissionId WHERE (@View='History' OR P.CreatedBy=@Actor) AND (@Filter='' OR M.State=@Filter) AND (CONVERT(nvarchar(36),M.CampaignId) LIKE N'%'+@Search+N'%' OR M.LastCode LIKE N'%'+@Search+N'%' OR S.StudentName LIKE N'%'+@Search+N'%' OR C.ContactName LIKE N'%'+@Search+N'%')
  ORDER BY M.UpdatedUtc DESC,M.MessageId OFFSET @Page*100 ROWS FETCH NEXT 100 ROWS ONLY;
 END
 ELSE IF @View='Settings' BEGIN EXEC dbo.RequirePermission @Actor,N'Communication.Configure'; SELECT * FROM comm.Settings; END
 ELSE IF @View='Rules' BEGIN EXEC dbo.RequirePermission @Actor,N'Communication.Configure'; SELECT * FROM comm.[Rule] ORDER BY Name; END
 ELSE IF @View='Candidates'
 BEGIN
  EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
  SELECT * FROM (
   SELECT 'Student' OwnerKind,S.AdmissionId OwnerId,S.StudentName Person,S.GuardianName Guardian,S.ContactNumber,S.AlternateContactNumber FROM dbo.Students S
   UNION ALL SELECT 'Staff',StaffId,StaffName,StaffName,ContactNumber,AlternateContactNumber FROM dbo.Staff
   UNION ALL SELECT 'Family',F.FamilyId,F.FamilyCode,H.FatherName,H.ContactNumber,H.AlternateContactNumber FROM dbo.Families F CROSS APPLY(SELECT TOP(1) S.FatherName,S.ContactNumber,S.AlternateContactNumber FROM dbo.FamilyMembers FM JOIN dbo.Students S ON S.AdmissionId=FM.AdmissionId WHERE FM.FamilyId=F.FamilyId ORDER BY FM.IsPrimaryStudent DESC,FM.AdmissionId) H) X
  WHERE Person LIKE N'%'+@Search+N'%' ORDER BY OwnerKind,OwnerId OFFSET @Page*100 ROWS FETCH NEXT 100 ROWS ONLY;
 END
 ELSE THROW 51001,'Unknown communication view.',1;
END;
GO
CREATE PROCEDURE comm.SaveSettings @Actor int,@Mode varchar(12),@Paused bit,@MaxBatch int,@ApprovalThreshold int,@HourLimit int,@DayLimit int,
 @DailyBudget decimal(18,4),@MonthlyBudget decimal(18,4),@SmsCost decimal(18,4)=NULL,@WhatsAppCost decimal(18,4)=NULL,@Currency char(3)='USD',@QuietStart int=20,@QuietEnd int=8,@Offset int=300,@RegistrationConfirmed bit=0 AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 IF @Mode='Live' AND (@RegistrationConfirmed=0 OR (@SmsCost IS NULL AND @WhatsAppCost IS NULL)) THROW 51001,'Live sending requires provider registration confirmation and an explicit cost estimate.',1;
 BEGIN TRANSACTION;
 DECLARE @Gate int;SELECT @Gate=Id FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
 IF @Currency<>(SELECT Currency FROM comm.Settings) AND EXISTS(SELECT 1 FROM comm.Attempt WHERE CreatedUtc>=DATEFROMPARTS(YEAR(SYSUTCDATETIME()),MONTH(SYSUTCDATETIME()),1) AND ReservedCost>0) THROW 51001,'Currency cannot change while this month has reserved provider costs.',1;
 UPDATE comm.Settings SET Mode=@Mode,Paused=@Paused,MaxBatch=@MaxBatch,ApprovalThreshold=@ApprovalThreshold,HourLimit=@HourLimit,DayLimit=@DayLimit,DailyBudget=@DailyBudget,MonthlyBudget=@MonthlyBudget,SmsUnitCost=@SmsCost,WhatsAppUnitCost=@WhatsAppCost,Currency=@Currency,QuietStart=@QuietStart,QuietEnd=@QuietEnd,UtcOffsetMinutes=@Offset,RegistrationConfirmed=@RegistrationConfirmed,UpdatedBy=@Actor,UpdatedUtc=SYSUTCDATETIME() WHERE Id=1;
 INSERT comm.Audit(Actor,Operation,Detail) VALUES(@Actor,'SettingsSaved',N'Sending mode and limits revised; no credentials stored in SQL');
 COMMIT;
END;
GO
CREATE PROCEDURE comm.Retry @Actor int,@Id uniqueidentifier AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Send';
 BEGIN TRANSACTION;
 IF NOT EXISTS(SELECT 1 FROM comm.Message M WITH(UPDLOCK,HOLDLOCK) JOIN comm.Campaign P ON P.CampaignId=M.CampaignId WHERE M.MessageId=@Id AND P.CreatedBy=@Actor AND M.State='Rejected' AND M.SafeToRetry=1 AND M.ProviderReference IS NULL) THROW 51001,'Only a confirmed unaccepted, retry-safe submission can be retried.',1;
 UPDATE comm.Message SET State='Queued',Attempts=0,NextAttemptUtc=SYSUTCDATETIME(),UpdatedUtc=SYSUTCDATETIME() WHERE MessageId=@Id;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'ExplicitSafeRetry',@Id,N'No provider acceptance exists; existing budgets still count every attempt');
 COMMIT;
END;
GO
CREATE PROCEDURE comm.CampaignSummary @Actor int,@Id uniqueidentifier AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @Actor,N'Communication.View';
 IF NOT EXISTS(SELECT 1 FROM comm.Campaign WHERE CampaignId=@Id AND CreatedBy=@Actor) EXEC dbo.RequirePermission @Actor,N'Communication.History';
 SELECT P.CampaignId,P.Channel,P.Status,T.Code,T.Language,T.Version,COUNT(*) Recipients,SUM(M.EstimatedCost) EstimatedCost,MAX(M.Currency) Currency,
  SUM(CASE WHEN M.EstimatedCost IS NULL THEN 1 ELSE 0 END) UnknownCosts,MAX(CONVERT(int,M.Simulation)) Simulation
 FROM comm.Campaign P JOIN comm.Message M ON M.CampaignId=P.CampaignId JOIN comm.Template T ON T.TemplateId=P.TemplateId
 WHERE P.CampaignId=@Id GROUP BY P.CampaignId,P.Channel,P.Status,T.Code,T.Language,T.Version;
END;
GO
CREATE PROCEDURE comm.Details @Actor int,@Id uniqueidentifier AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @Actor,N'Communication.View';
 IF NOT EXISTS(SELECT 1 FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId WHERE M.MessageId=@Id AND P.CreatedBy=@Actor) EXEC dbo.RequirePermission @Actor,N'Communication.History';
 SELECT M.MessageId,M.CampaignId,M.ContactId,M.AdmissionId,M.Body,M.Parameters,M.State,M.Simulation,M.Provider,M.ProviderReference,M.LastCode,M.Attempts,M.EstimatedCost,M.Currency,M.UpdatedUtc,
 CONCAT(LEFT(M.Destination,3),'******',RIGHT(M.Destination,3)) Destination FROM comm.Message M WHERE M.MessageId=@Id;
END;
GO
CREATE PROCEDURE comm.SaveRolePermissions @UserId int,@RoleId int,@PermissionIdsJson nvarchar(max) AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'System.Roles.Manage',N'System.Administration';
 IF ISNULL(ISJSON(@PermissionIdsJson),0)<>1 OR LEFT(LTRIM(@PermissionIdsJson),1)<>N'[' THROW 51001,'Select a valid permission list.',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@PermissionIdsJson) WHERE [type]<>2 OR TRY_CONVERT(int,value) IS NULL OR TRY_CONVERT(int,value)<=0) THROW 51001,'Permission IDs must be positive integers.',1;
 DECLARE @Selected TABLE(PermissionId int NOT NULL PRIMARY KEY);
 INSERT @Selected SELECT DISTINCT CONVERT(int,value) FROM OPENJSON(@PermissionIdsJson);
 IF EXISTS(SELECT 1 FROM @Selected S LEFT JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionId IS NULL) THROW 51001,'Unknown permission selection.',1;
 DECLARE @Optional TABLE(PermissionId int NOT NULL PRIMARY KEY,Code nvarchar(100));
 INSERT @Optional SELECT P.PermissionId,P.PermissionCode FROM @Selected S JOIN dbo.Permissions P ON P.PermissionId=S.PermissionId WHERE P.PermissionCode IN(N'Communication.View',N'Communication.Compose',N'Communication.Send',N'Communication.Bulk',N'Communication.Approve',N'Communication.Configure',N'Communication.History');
 IF EXISTS(SELECT 1 FROM @Optional WHERE Code<>N'Communication.View') AND NOT EXISTS(SELECT 1 FROM @Optional WHERE Code=N'Communication.View') THROW 51001,'Communication actions require Communication.View.',1;
 IF EXISTS(SELECT 1 FROM @Optional WHERE Code IN(N'Communication.Send',N'Communication.Bulk')) AND NOT EXISTS(SELECT 1 FROM @Optional WHERE Code=N'Communication.Compose') THROW 51001,'Sending and bulk composition require Communication.Compose.',1;
 IF EXISTS(SELECT 1 FROM @Optional WHERE Code=N'Communication.Approve') AND NOT EXISTS(SELECT 1 FROM @Optional WHERE Code=N'Communication.History') THROW 51001,'Approvers need Communication.History to review campaigns.',1;
 DECLARE @Base nvarchar(max)=N'['+ISNULL((SELECT STRING_AGG(CONVERT(nvarchar(max),S.PermissionId),N',') WITHIN GROUP(ORDER BY S.PermissionId) FROM @Selected S WHERE NOT EXISTS(SELECT 1 FROM @Optional O WHERE O.PermissionId=S.PermissionId)),N'')+N']';
 BEGIN TRANSACTION;
 DECLARE @RoleName nvarchar(50);SELECT @RoleName=RoleName FROM dbo.Roles WITH(UPDLOCK,HOLDLOCK) WHERE RoleId=@RoleId;
 IF ISNULL(@RoleName,N'')<>N'Accountant' THROW 51001,'Only Accountant permissions are editable; Administrator retains full authority.',1;
 DECLARE @Old nvarchar(max)=N'['+ISNULL((SELECT STRING_AGG(CONVERT(nvarchar(max),PermissionId),N',') WITHIN GROUP(ORDER BY PermissionId) FROM dbo.RolePermissions WHERE RoleId=@RoleId),N'')+N']';
 -- Preserve every baseline permission validation, protected-role rule and audit.
 DECLARE @Saved TABLE(RoleId int);INSERT @Saved EXEC dbo.SaveRolePermissions @UserId,@RoleId,@Base;
 INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @RoleId,PermissionId FROM @Optional;
 DECLARE @New nvarchar(max)=N'['+ISNULL((SELECT STRING_AGG(CONVERT(nvarchar(max),PermissionId),N',') WITHIN GROUP(ORDER BY PermissionId) FROM dbo.RolePermissions WHERE RoleId=@RoleId),N'')+N']',@Operation uniqueidentifier=NEWID(),@Record nvarchar(200)=CONVERT(nvarchar(200),@RoleId);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'COMMUNICATION_ROLE_PERMISSIONS_SAVE',@Record);
 EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@UserId,@TableName=N'RolePermissions',@RecordId=@Record,@Action=N'UPDATE',@OldValues=@Old,@NewValues=@New;
 DELETE dbo.AuditOperations WHERE OperationId=@Operation AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@Operation);
 INSERT comm.Audit(Actor,Operation,Detail) VALUES(@UserId,'CommunicationGrantsSaved',N'Explicit role edit; original base permission procedure and protected-role checks retained');
 COMMIT;
 SELECT @RoleId RoleId;
END;
GO
CREATE PROCEDURE comm.SaveRule @Actor int,@Id uniqueidentifier,@Name nvarchar(100),@Event varchar(30),@TemplateId uniqueidentifier,@Channel varchar(10),@Enabled bit,@Approval bit=1,@Delay int=0,@Limit int=25 AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 IF @Enabled=1 BEGIN EXEC dbo.RequirePermission @Actor,N'Communication.Send'; EXEC dbo.RequirePermission @Actor,N'Communication.Approve'; END;
 DECLARE @Start bigint=0;
 IF @Event='PaymentCompleted' SELECT @Start=ISNULL(MAX(FeePaymentId),0) FROM dbo.FeePayments;
 IF @Event='AdmissionCompleted' SELECT @Start=ISNULL(MAX(AdmissionId),0) FROM dbo.Students;
 IF @Event='PromotionCompleted' SELECT @Start=ISNULL(MAX(PromotionBatchStudentId),0) FROM dbo.PromotionBatchStudents;
 IF @Event='ReturnConfirmed' SELECT @Start=ISNULL(MAX(EnrollmentId),0) FROM dbo.Enrollments;
 IF @Event<>'PaymentCompleted' AND EXISTS(SELECT 1 FROM comm.Template WHERE TemplateId=@TemplateId AND Body LIKE N'%{{receipt}}%') THROW 51001,'Receipt templates require a verified payment event.',1;
 IF @Enabled=1 AND NOT EXISTS(SELECT 1 FROM comm.Template WHERE TemplateId=@TemplateId AND Approved=1 AND Scope='Student') THROW 51001,'Enable only a reviewed student-context template.',1;
 BEGIN TRANSACTION;
 DECLARE @Snapshot bit=1;
 IF EXISTS(SELECT 1 FROM comm.[Rule] WITH(UPDLOCK,HOLDLOCK) WHERE RuleId=@Id AND Enabled=1 AND @Enabled=1 AND EventType=@Event) SET @Snapshot=0;
 IF EXISTS(SELECT 1 FROM comm.[Rule] WITH(UPDLOCK,HOLDLOCK) WHERE RuleId=@Id)
  UPDATE comm.[Rule] SET Name=@Name,EventType=@Event,TemplateId=@TemplateId,Channel=@Channel,RequiresApproval=@Approval,DelayMinutes=@Delay,BatchLimit=@Limit,
   StartAfter=CASE WHEN Enabled=1 AND @Enabled=1 AND EventType=@Event THEN StartAfter ELSE @Start END,
   EnabledUtc=CASE WHEN Enabled=1 AND @Enabled=1 THEN EnabledUtc ELSE SYSUTCDATETIME() END,Enabled=@Enabled,Actor=@Actor WHERE RuleId=@Id;
 ELSE INSERT comm.[Rule] VALUES(@Id,@Name,@Event,@TemplateId,@Channel,@Enabled,SYSUTCDATETIME(),@Approval,@Delay,@Limit,@Actor,@Start);
 IF @Enabled=1 AND @Snapshot=1
 BEGIN
  DELETE FROM comm.RuleBaseline WHERE RuleId=@Id;
  IF @Event='AdmissionCompleted' INSERT comm.RuleBaseline SELECT @Id,AdmissionId FROM dbo.Students WHERE AdmissionNumber IS NOT NULL;
  IF @Event='ReturnConfirmed' INSERT comm.RuleBaseline SELECT @Id,EnrollmentId FROM dbo.Enrollments WHERE ReturnConfirmedAt IS NOT NULL;
 END;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,'AutomationRuleSaved',@Id,CASE WHEN @Enabled=1 THEN N'Explicit activation; existing events are excluded' ELSE N'Disabled' END);
 COMMIT;
END;
GO
CREATE PROCEDURE comm.Evaluate @Actor int AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N'Communication.Configure';
 EXEC dbo.RequirePermission @Actor,N'Communication.Send'; EXEC dbo.RequirePermission @Actor,N'Communication.Approve';
 DECLARE @Rule uniqueidentifier,@Template uniqueidentifier,@Event varchar(30),@Channel varchar(10),@Approval bit,@Delay int,@Limit int,@Start bigint;
 DECLARE R CURSOR LOCAL FAST_FORWARD FOR SELECT RuleId,TemplateId,EventType,Channel,RequiresApproval,DelayMinutes,BatchLimit,StartAfter FROM comm.[Rule] WHERE Enabled=1 AND Actor=@Actor;
 OPEN R; FETCH NEXT FROM R INTO @Rule,@Template,@Event,@Channel,@Approval,@Delay,@Limit,@Start;
 WHILE @@FETCH_STATUS=0
 BEGIN
  DECLARE @Candidates TABLE(ContactId uniqueidentifier,EventId bigint,PaymentId int);
  DELETE FROM @Candidates;
  IF @Event='PaymentCompleted' INSERT @Candidates SELECT C.ContactId,P.FeePaymentId,P.FeePaymentId FROM dbo.FeePayments P JOIN comm.Contact C ON C.AdmissionId=P.AdmissionId WHERE P.FeePaymentId>@Start AND P.Status=N'Completed' AND P.PaymentMethod NOT IN(N'Refund',N'Correction');
  IF @Event='AdmissionCompleted' INSERT @Candidates SELECT C.ContactId,S.AdmissionId,NULL FROM dbo.Students S JOIN comm.Contact C ON C.AdmissionId=S.AdmissionId WHERE S.Status=N'Active' AND S.AdmissionNumber IS NOT NULL AND NOT EXISTS(SELECT 1 FROM comm.RuleBaseline B WHERE B.RuleId=@Rule AND B.EventId=S.AdmissionId);
  IF @Event='PromotionCompleted' INSERT @Candidates SELECT C.ContactId,P.PromotionBatchStudentId,NULL FROM dbo.PromotionBatchStudents P JOIN comm.Contact C ON C.AdmissionId=P.AdmissionId WHERE P.PromotionBatchStudentId>@Start AND P.Decision=N'PROMOTE' AND P.NewEnrollmentId=(SELECT TOP(1) EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=P.AdmissionId ORDER BY EnrollmentDate DESC,EnrollmentId DESC);
  IF @Event='ReturnConfirmed' INSERT @Candidates SELECT C.ContactId,E.EnrollmentId,NULL FROM dbo.Enrollments E JOIN comm.Contact C ON C.AdmissionId=E.AdmissionId WHERE E.ReturnConfirmedAt IS NOT NULL AND E.EnrollmentId=(SELECT TOP(1) EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=E.AdmissionId ORDER BY EnrollmentDate DESC,EnrollmentId DESC) AND NOT EXISTS(SELECT 1 FROM comm.RuleBaseline B WHERE B.RuleId=@Rule AND B.EventId=E.EnrollmentId);
  IF @Event='MonthlyOutstanding' INSERT @Candidates SELECT C.ContactId,CONVERT(bigint,FORMAT(SYSUTCDATETIME(),'yyyyMM')),NULL FROM comm.Contact C WHERE C.AdmissionId IS NOT NULL AND comm.Balance(C.AdmissionId)>0;
  DECLARE @Contact uniqueidentifier,@EventId bigint,@Payment int,@Key nvarchar(150),@Campaign uniqueidentifier,@Ids nvarchar(100),@Due datetime2;
  DECLARE E CURSOR LOCAL FAST_FORWARD FOR SELECT TOP(@Limit) X.ContactId,X.EventId,X.PaymentId FROM @Candidates X JOIN comm.Contact C ON C.ContactId=X.ContactId JOIN comm.Template T ON T.TemplateId=@Template
   WHERE C.Verified=1 AND C.OptedOut=0 AND C.Language=T.Language AND T.Approved=1 AND C.OwnerKind=T.Scope
    AND (@Channel='Sms' AND C.SmsConsent=1 OR @Channel='WhatsApp' AND C.WhatsAppConsent=1 AND LEN(T.ProviderTemplate)>0)
    AND (T.Category<>'Financial' OR C.FinancialConsent=1) AND (T.Category<>'Announcement' OR C.AnnouncementConsent=1)
    AND NOT EXISTS(SELECT 1 FROM comm.Campaign P WHERE P.EventKey=CONCAT(CONVERT(nvarchar(36),@Rule),':',X.EventId,':',CONVERT(nvarchar(36),X.ContactId))) ORDER BY X.EventId,X.ContactId;
  OPEN E; FETCH NEXT FROM E INTO @Contact,@EventId,@Payment;
  WHILE @@FETCH_STATUS=0
  BEGIN
   SET @Key=CONCAT(CONVERT(nvarchar(36),@Rule),':',@EventId,':',CONVERT(nvarchar(36),@Contact));
   SET @Campaign=NEWID();SET @Ids=N'["'+CONVERT(nvarchar(36),@Contact)+N'"]';SET @Due=DATEADD(MINUTE,@Delay,SYSUTCDATETIME());
   BEGIN TRANSACTION;
   DECLARE @Gate int;SELECT @Gate=Id FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
   -- Serialize evaluation across operators/processes without changing a business transaction.
   IF NOT EXISTS(SELECT 1 FROM comm.Campaign WITH(UPDLOCK,HOLDLOCK) WHERE EventKey=@Key)
   BEGIN
    EXEC comm.CreateCampaign @Actor,@Campaign,@Template,@Channel,@Ids,N'',@Due,@Key,@Payment;
    IF @Approval=0 UPDATE comm.Campaign SET ApprovedBy=@Actor WHERE CampaignId=@Campaign;
    EXEC comm.Transition @Actor,@Campaign,'Submit';
   END;
   COMMIT;
   FETCH NEXT FROM E INTO @Contact,@EventId,@Payment;
  END;
  CLOSE E;DEALLOCATE E;
  FETCH NEXT FROM R INTO @Rule,@Template,@Event,@Channel,@Approval,@Delay,@Limit,@Start;
 END;
 CLOSE R;DEALLOCATE R;
END;
GO
CREATE PROCEDURE comm.IngestMeta @Actor int,@Payload nvarchar(max) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF ISNULL(IS_ROLEMEMBER(N'FMSC_CommunicationWebhook'),0)<>1 THROW 51003,'Only the restricted webhook principal may ingest callbacks.',1;
 EXEC dbo.RequirePermission @Actor,N'Communication.Send';
 IF (SELECT MAX(TRY_CONVERT(int,VersionNumber)) FROM dbo.DatabaseVersion)<>6 THROW 51000,'Incompatible base database version.',1;
 IF ISJSON(@Payload)<>1 OR DATALENGTH(@Payload)>131072 THROW 51001,'Invalid webhook envelope.',1;
 -- Only the independently signature-validated gateway may call this operation.
 -- Do not expose SQL credentials or SQL Server to the public internet.
 DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',@Payload);
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM comm.WebhookInbox WITH(UPDLOCK,HOLDLOCK) WHERE PayloadHash=@Hash) BEGIN COMMIT;RETURN;END;
 INSERT comm.WebhookInbox(PayloadHash) VALUES(@Hash);
 DELETE FROM comm.PendingReceipt WHERE ReceivedUtc<DATEADD(DAY,-7,SYSUTCDATETIME());
 DECLARE @Reference varchar(200),@State varchar(20),@Id uniqueidentifier;
 DECLARE S CURSOR LOCAL FAST_FORWARD FOR
  SELECT JSON_VALUE(S.value,'$.id'),CASE JSON_VALUE(S.value,'$.status') WHEN 'sent' THEN 'Sent' WHEN 'delivered' THEN 'Delivered' WHEN 'read' THEN 'Read' WHEN 'failed' THEN 'Failed' END
  FROM OPENJSON(@Payload,'$.entry') E CROSS APPLY OPENJSON(E.value,'$.changes') C CROSS APPLY OPENJSON(C.value,'$.value.statuses') S;
 OPEN S;FETCH NEXT FROM S INTO @Reference,@State;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @Id=NULL;SELECT @Id=MessageId FROM comm.Message WHERE Provider='MetaCloud' AND ProviderReference=@Reference;
  IF @State IS NOT NULL AND LEN(@Reference)>0
  BEGIN
   IF @Id IS NOT NULL EXEC comm.StatusUpdate @Actor,@Id,'MetaCloud',@Reference,@State;
   ELSE IF NOT EXISTS(SELECT 1 FROM comm.PendingReceipt WITH(UPDLOCK,HOLDLOCK) WHERE ProviderReference=@Reference) INSERT comm.PendingReceipt(ProviderReference,State) VALUES(@Reference,@State);
   ELSE UPDATE comm.PendingReceipt SET State=@State WHERE ProviderReference=@Reference AND (State='Sent' AND @State IN('Delivered','Read','Failed') OR State='Delivered' AND @State='Read');
  END;
  FETCH NEXT FROM S INTO @Reference,@State;
 END;
 CLOSE S;DEALLOCATE S;
 UPDATE CT SET OptedOut=1,WhatsAppConsent=0,ConsentSource=N'Signed inbound WhatsApp opt-out',ConsentUtc=SYSUTCDATETIME(),UpdatedBy=@Actor
 FROM comm.Contact CT JOIN (
  SELECT '+'+JSON_VALUE(M.value,'$.from') Destination,TRY_CONVERT(bigint,JSON_VALUE(M.value,'$.timestamp')) Stamp
  FROM OPENJSON(@Payload,'$.entry') E CROSS APPLY OPENJSON(E.value,'$.changes') C CROSS APPLY OPENJSON(C.value,'$.value.messages') M
  WHERE JSON_VALUE(M.value,'$.type')='text' AND UPPER(LTRIM(RTRIM(JSON_VALUE(M.value,'$.text.body')))) IN(N'STOP',N'UNSUBSCRIBE',N'بند')
 ) X ON X.Destination=CT.Destination
 WHERE CT.OptedOut=0 AND X.Stamp BETWEEN 1 AND 2147483647 AND CT.ConsentUtc<=DATEADD(SECOND,CONVERT(int,X.Stamp)+1,CONVERT(datetime2,'19700101'));
 INSERT comm.Audit(Actor,Operation,Detail) VALUES(@Actor,'SignedMetaCallback',N'Validated external gateway callback persisted; duplicates are idempotent');
 COMMIT;
END;
GO
IF DATABASE_PRINCIPAL_ID(N'FMSC_CommunicationWebhook') IS NOT NULL OR DATABASE_PRINCIPAL_ID(N'FMSCCommunicationIngress') IS NOT NULL
 THROW 51000,'Communication webhook principals already exist; review the deployment instead of replacing them.',1;
CREATE ROLE FMSC_CommunicationWebhook AUTHORIZATION dbo;
CREATE USER FMSCCommunicationIngress WITHOUT LOGIN;
ALTER ROLE FMSC_CommunicationWebhook ADD MEMBER FMSCCommunicationIngress;
GRANT EXECUTE ON OBJECT::comm.IngestMeta TO FMSC_CommunicationWebhook;
GRANT SELECT ON OBJECT::comm.Module TO FMSC_CommunicationWebhook;
GRANT VIEW DEFINITION ON SCHEMA::comm TO FMSC_CommunicationWebhook;
INSERT dbo.Permissions(PermissionCode,PermissionName,Description)
SELECT Code,Name,Name FROM (VALUES
 (N'Communication.View',N'Open Communication Center'),(N'Communication.Compose',N'Compose external messages'),(N'Communication.Send',N'Submit external messages'),
 (N'Communication.Bulk',N'Compose and send bulk messages'),(N'Communication.Approve',N'Approve communication campaigns'),(N'Communication.Configure',N'Manage communication contacts, templates and gateways'),(N'Communication.History',N'View communication delivery history')) P(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions X WHERE X.PermissionCode=P.Code);
-- No RolePermissions rows are added: Accountant access is unchanged.
-- School review is required; stock text does not claim Meta approval. No exam or attendance events are invented.
INSERT comm.Template(TemplateId,Code,Version,Language,Category,Scope,Body,ProviderTemplate,ParameterOrder,Approved) VALUES
(NEWID(),N'admission_completed',1,N'en',N'Utility',N'Student',N'{{school}}: Admission completed for {{student}}, {{class}}, {{year}}.','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'admission_completed',1,N'ur',N'Utility',N'Student',N'{{school}}: {{student}} کا داخلہ {{class}}، {{year}} میں مکمل ہو گیا ہے۔','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'registration_pending',1,N'en',N'Utility',N'Student',N'{{school}}: Registration for {{student}} needs information. {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'registration_pending',1,N'ur',N'Utility',N'Student',N'{{school}}: {{student}} کی رجسٹریشن کے لیے معلومات درکار ہیں۔ {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'placement_confirmed',1,N'en',N'Utility',N'Student',N'{{school}}: Placement confirmed for {{student}} in {{class}}, {{year}}.','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'placement_confirmed',1,N'ur',N'Utility',N'Student',N'{{school}}: {{student}} کی جماعت {{class}}، {{year}} کی تصدیق ہو گئی ہے۔','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'documents_requested',1,N'en',N'Utility',N'Student',N'{{school}}: Please provide the requested admission documents for {{student}}. {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'documents_requested',1,N'ur',N'Utility',N'Student',N'{{school}}: {{student}} کے داخلے کی مطلوبہ دستاویزات فراہم کریں۔ {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'fees_posted',1,N'en',N'Financial',N'Student',N'{{school}}: Fees are available for {{student}}. Outstanding amount: {{amount}}.','',N'["school", "student", "amount"]',0),
(NEWID(),N'fees_posted',1,N'ur',N'Financial',N'Student',N'{{school}}: {{student}} کی فیس درج ہو گئی ہے۔ بقایا رقم: {{amount}}۔','',N'["school", "student", "amount"]',0),
(NEWID(),N'outstanding',1,N'en',N'Financial',N'Student',N'{{school}}: Fee reminder for {{student}}. Outstanding: {{amount}}. Please contact the school for details.','',N'["school", "student", "amount"]',0),
(NEWID(),N'outstanding',1,N'ur',N'Financial',N'Student',N'{{school}}: {{student}} کی فیس کی یاد دہانی۔ بقایا رقم: {{amount}}۔ تفصیل کے لیے سکول سے رابطہ کریں۔','',N'["school", "student", "amount"]',0),
(NEWID(),N'due_date',1,N'en',N'Financial',N'Student',N'{{school}}: {{student}} has an outstanding amount of {{amount}}. Earliest unpaid due date: {{dueDate}}.','',N'["school", "student", "amount", "dueDate"]',0),
(NEWID(),N'due_date',1,N'ur',N'Financial',N'Student',N'{{school}}: {{student}} کی بقایا رقم {{amount}} ہے۔ قریب ترین واجب الادا تاریخ: {{dueDate}}۔','',N'["school", "student", "amount", "dueDate"]',0),
(NEWID(),N'payment_completed',1,N'en',N'Financial',N'Student',N'{{school}}: Payment of {{amount}} received for {{student}}. Receipt: {{receipt}}.','',N'["school", "amount", "student", "receipt"]',0),
(NEWID(),N'payment_completed',1,N'ur',N'Financial',N'Student',N'{{school}}: {{student}} کی {{amount}} رقم وصول ہو گئی ہے۔ رسید: {{receipt}}۔','',N'["school", "student", "amount", "receipt"]',0),
(NEWID(),N'promotion_completed',1,N'en',N'Utility',N'Student',N'{{school}}: Promotion recorded for {{student}}. Placement: {{class}}, {{year}}. Please complete return confirmation where required.','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'promotion_completed',1,N'ur',N'Utility',N'Student',N'{{school}}: {{student}} کی ترقی درج ہو گئی ہے۔ جماعت {{class}}، {{year}}۔ جہاں ضروری ہو واپسی کے داخلے کی تصدیق مکمل کریں۔','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'return_confirmed',1,N'en',N'Utility',N'Student',N'{{school}}: Return admission confirmed for {{student}}, {{class}}, {{year}}.','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'return_confirmed',1,N'ur',N'Utility',N'Student',N'{{school}}: {{student}} کے واپسی داخلے کی {{class}}، {{year}} میں تصدیق ہو گئی ہے۔','',N'["school", "student", "class", "year"]',0),
(NEWID(),N'year_commencement',1,N'en',N'Announcement',N'Student',N'{{school}}: Academic year announcement for {{student}}. {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'year_commencement',1,N'ur',N'Announcement',N'Student',N'{{school}}: {{student}} کے لیے تعلیمی سال کا اعلان۔ {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'parent_meeting',1,N'en',N'Announcement',N'Student',N'{{school}}: Parent meeting notice for {{student}}. {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'parent_meeting',1,N'ur',N'Announcement',N'Student',N'{{school}}: {{student}} کے والدین کی ملاقات کا نوٹس۔ {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'school_closure',1,N'en',N'Announcement',N'Family',N'{{school}}: School closure announcement. {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'school_closure',1,N'ur',N'Announcement',N'Family',N'{{school}}: سکول کی تعطیل کا اعلان۔ {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'family_announcement',1,N'en',N'Announcement',N'Family',N'{{school}}: {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'family_announcement',1,N'ur',N'Announcement',N'Family',N'{{school}}: {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'staff_announcement',1,N'en',N'Announcement',N'Staff',N'{{school}}: Staff announcement. {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'staff_announcement',1,N'ur',N'Announcement',N'Staff',N'{{school}}: عملے کے لیے اعلان۔ {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'personal_announcement',1,N'en',N'Announcement',N'Student',N'{{school}}: Notice for {{student}}. {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'personal_announcement',1,N'ur',N'Announcement',N'Student',N'{{school}}: {{student}} کے لیے نوٹس۔ {{announcement}}','',N'["school", "student", "announcement"]',0),
(NEWID(),N'general_announcement',1,N'en',N'Announcement',N'Manual',N'{{school}}: {{announcement}}','',N'["school", "announcement"]',0),
(NEWID(),N'general_announcement',1,N'ur',N'Announcement',N'Manual',N'{{school}}: {{announcement}}','',N'["school", "announcement"]',0);
INSERT comm.Module VALUES(1);
COMMIT;
