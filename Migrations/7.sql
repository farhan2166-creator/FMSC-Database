-- Schema 7: one explicit Admission Yes authorizes school SMS/WhatsApp,
-- announcements and financial notices, without a second contact-consent form.
-- Existing WhatsApp-only Yes values remain legacy, never silently broadened.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber)
 FROM dbo.DatabaseVersion ORDER BY VersionId DESC),0)<>6
 THROW 51000,'School communication consent upgrade requires schema 6.',1;

IF COL_LENGTH(N'dbo.StudentGuardianCommunication',N'SchoolCommunicationConsent') IS NOT NULL
 THROW 51000,'School communication consent column already exists; inspect migration history.',1;
ALTER TABLE dbo.StudentGuardianCommunication ADD SchoolCommunicationConsent bit NULL;

IF SCHEMA_ID(N'comm') IS NOT NULL
BEGIN
 IF OBJECT_ID(N'comm.Module',N'U') IS NULL
  OR OBJECT_ID(N'comm.Contact',N'U') IS NULL
  OR OBJECT_ID(N'comm.Preview',N'P') IS NULL
  OR OBJECT_ID(N'comm.Claim',N'P') IS NULL
  OR OBJECT_ID(N'comm.GuardianWhatsAppAllowed',N'FN') IS NULL
   THROW 51000,'Communication module is incomplete; refuse unsafe consent upgrade.',1;

-- SQL definitions below are derived from the matching optional-module reference.
 EXEC sys.sp_executesql N'CREATE FUNCTION comm.NormalizeSchoolPhone(@Raw nvarchar(30)) RETURNS varchar(16) AS
BEGIN
 IF @Raw IS NULL OR LEN(LTRIM(RTRIM(@Raw)))=0 RETURN NULL;
 DECLARE @Text nvarchar(30)=LTRIM(RTRIM(@Raw));
 IF @Text LIKE N''%[^+0-9 ()-]%'' OR CHARINDEX(N''+'',@Text)>1
  OR LEN(@Text)-LEN(REPLACE(@Text,N''+'',N''''))>1 RETURN NULL;
 DECLARE @Digits nvarchar(30)=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
  @Text,N''+'',N''''),N'' '',N''''),N''-'',N''''),N''('',N''''),N'')'',N'''');
 IF @Digits LIKE N''%[^0-9]%'' RETURN NULL;
 IF LEFT(@Text,1)=N''+'' AND LEFT(@Digits,2)<>N''92''
 BEGIN
  IF LEN(@Digits) NOT BETWEEN 8 AND 15 OR @Digits NOT LIKE N''[1-9]%'' RETURN NULL;
  RETURN CONVERT(varchar(16),N''+''+@Digits);
 END;
 IF LEFT(@Digits,4)=N''0092'' AND LEN(@Digits)=14 SET @Digits=SUBSTRING(@Digits,5,10);
 ELSE IF LEFT(@Digits,2)=N''92'' AND LEN(@Digits)=12 SET @Digits=SUBSTRING(@Digits,3,10);
 ELSE IF LEFT(@Digits,2)=N''03'' AND LEN(@Digits)=11 SET @Digits=SUBSTRING(@Digits,2,10);
 ELSE RETURN NULL;
 IF LEN(@Digits)<>10 OR @Digits NOT LIKE N''3[0-6]%''
  OR @Digits LIKE N''%[^0-9]%'' RETURN NULL;
 RETURN CONVERT(varchar(16),N''+92''+@Digits);
END;';

 EXEC sys.sp_executesql N'CREATE FUNCTION comm.SchoolCommunicationState(@AdmissionId int,@Destination varchar(16),@Channel varchar(10)) RETURNS tinyint AS
BEGIN
 DECLARE @School bit,@RecordedPhone nvarchar(30),@CurrentPhone nvarchar(30);
 SELECT @School=G.SchoolCommunicationConsent,@RecordedPhone=G.Phone,
  @CurrentPhone=S.ContactNumber
 FROM dbo.StudentGuardianCommunication G JOIN dbo.Students S ON S.AdmissionId=G.AdmissionId
 WHERE G.AdmissionId=@AdmissionId;
 -- A later phone edit outside Admission must also stop messages to the old number.
 IF @School IS NOT NULL AND
  (comm.NormalizeSchoolPhone(@RecordedPhone) IS NULL OR comm.NormalizeSchoolPhone(@CurrentPhone) IS NULL
   OR comm.NormalizeSchoolPhone(@RecordedPhone)<>comm.NormalizeSchoolPhone(@CurrentPhone))
  RETURN 0;
 IF @School IS NULL
  RETURN CASE WHEN @Channel=''WhatsApp'' AND comm.GuardianWhatsAppAllowed(@AdmissionId,@Destination)=0 THEN 0 ELSE 1 END;
 IF @School=0 RETURN 0;
 RETURN CASE WHEN comm.GuardianWhatsAppAllowed(@AdmissionId,@Destination)=1 THEN 2 ELSE 0 END;
END;';

 EXEC sys.sp_executesql N'CREATE PROCEDURE comm.SyncAdmissionContacts @Actor int AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @Actor,N''Communication.Compose'';
 EXEC dbo.RequirePermission @Actor,N''Students.View'';
 DECLARE @Eligible TABLE(AdmissionId int NOT NULL PRIMARY KEY,Destination varchar(16) NOT NULL,
  ContactName nvarchar(150) NOT NULL,RecordedUtc datetime2 NOT NULL);
 INSERT @Eligible(AdmissionId,Destination,ContactName,RecordedUtc)
 SELECT G.AdmissionId,P.Destination,
  LEFT(COALESCE(NULLIF(LTRIM(RTRIM(S.GuardianName)),N''''),LEFT(S.StudentName,135)+N'' guardian''),150),
  G.RecordedUtc
 FROM dbo.StudentGuardianCommunication G
 JOIN dbo.Students S ON S.AdmissionId=G.AdmissionId
 CROSS APPLY(SELECT comm.NormalizeSchoolPhone(G.Phone) Destination) P
 WHERE G.SchoolCommunicationConsent=1 AND P.Destination IS NOT NULL
 AND P.Destination=comm.NormalizeSchoolPhone(S.ContactNumber);
 BEGIN TRANSACTION;
 DECLARE @Linked TABLE(ContactId uniqueidentifier PRIMARY KEY);
 UPDATE C SET Verified=1,SmsConsent=1,WhatsAppConsent=1,FinancialConsent=1,
  AnnouncementConsent=1,ConsentSource=N''Admission school communication agreement'',
  ConsentUtc=E.RecordedUtc,UpdatedBy=@Actor
 OUTPUT inserted.ContactId INTO @Linked(ContactId)
 FROM comm.Contact C JOIN @Eligible E ON C.OwnerKind=''Student''
   AND C.AdmissionId=E.AdmissionId AND C.Destination=E.Destination
 WHERE C.OptedOut=0 AND (C.Verified=0 OR C.SmsConsent=0 OR C.WhatsAppConsent=0
   OR C.FinancialConsent=0 OR C.AnnouncementConsent=0);
 INSERT comm.Contact(ContactId,OwnerKind,AdmissionId,FamilyId,OriginalFamilyId,StaffId,
  ContactName,Destination,Language,PreferredChannel,Verified,SmsConsent,WhatsAppConsent,
  FinancialConsent,AnnouncementConsent,OptedOut,ConsentSource,ConsentUtc,UpdatedBy)
 OUTPUT inserted.ContactId INTO @Linked(ContactId)
 SELECT NEWID(),''Student'',E.AdmissionId,NULL,NULL,NULL,E.ContactName,E.Destination,
  ''en'',''WhatsApp'',1,1,1,1,1,0,N''Admission school communication agreement'',
  E.RecordedUtc,@Actor
 FROM @Eligible E
 WHERE NOT EXISTS(SELECT 1 FROM comm.Contact C WITH(UPDLOCK,HOLDLOCK)
    WHERE C.OwnerKind=''Student'' AND C.AdmissionId=E.AdmissionId AND C.Destination=E.Destination)
 AND NOT EXISTS(SELECT 1 FROM comm.Contact C WITH(UPDLOCK,HOLDLOCK)
    WHERE C.OwnerKind=''Student'' AND C.AdmissionId=E.AdmissionId AND C.OptedOut=1);
 INSERT comm.Audit(Actor,Operation,EntityId,Detail)
 SELECT @Actor,''AdmissionConsentLinked'',L.ContactId,N''Contact eligibility linked from saved Admission Yes''
 FROM @Linked L;
 COMMIT;
END;';

 EXEC sys.sp_executesql N'ALTER PROCEDURE comm.Preview @Actor int,@TemplateId uniqueidentifier,@Channel varchar(10),@ContactIds nvarchar(max),@Announcement nvarchar(1000)=N'''',@PaymentId int=NULL AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @Actor,N''Communication.Compose'';
 IF ISJSON(@ContactIds)<>1 OR @Channel NOT IN(''Sms'',''WhatsApp'') THROW 51001,''Invalid communication selection.'',1;
 IF (SELECT COUNT(*) FROM OPENJSON(@ContactIds)) NOT BETWEEN 1 AND 500 THROW 51001,''Choose 1 to 500 contacts.'',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@ContactIds) WHERE TRY_CONVERT(uniqueidentifier,value) IS NULL) THROW 51001,''Invalid contact identity.'',1;
 DECLARE @Scope varchar(10)=(SELECT Scope FROM comm.Template WHERE TemplateId=@TemplateId),@Category varchar(20)=(SELECT Category FROM comm.Template WHERE TemplateId=@TemplateId);
 IF @Scope IN(''Student'',''Family'') EXEC dbo.RequirePermission @Actor,N''Students.View'';
 IF @Scope=''Staff'' EXEC dbo.RequirePermission @Actor,N''Staff.View'';
 IF @Category=''Financial'' EXEC dbo.RequirePermission @Actor,N''Fees.View'';
 SELECT C.ContactId,C.ContactName,C.Destination,C.AdmissionId,V.EnrollmentId,C.FamilyId,
 comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId) Body,
 Rendered.Parameters,
 comm.Segments(comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId)) Segments,
 CASE WHEN S.Mode<>''Live'' THEN 0 WHEN @Channel=''Sms'' THEN S.SmsUnitCost*comm.Segments(comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId)) ELSE S.WhatsAppUnitCost END EstimatedCost,S.Currency,
 CASE WHEN DATALENGTH(Rendered.Parameters)>8000 THEN N''Personalized provider parameters exceed the supported size; shorten the template'' WHEN DATALENGTH(comm.Render(T.Body,C.ContactId,@Announcement,@PaymentId))>4000 THEN N''Personalized message exceeds 2000 characters; shorten the template'' WHEN C.OwnerKind=''Family'' AND C.FamilyId IS NULL THEN N''Family link retired; select a current family contact'' WHEN T.Approved=0 THEN N''Template requires approval'' WHEN C.OwnerKind<>T.Scope THEN N''Contact context does not match template''
 WHEN C.Language<>T.Language THEN N''Template language does not match contact'' WHEN C.Verified=0 AND NOT (C.OwnerKind=''Student''
 AND comm.SchoolCommunicationState(C.AdmissionId,C.Destination,@Channel)=2) THEN N''Unverified contact''
 WHEN C.OptedOut=1 THEN N''Opted out'' WHEN @Channel=''Sms'' AND C.SmsConsent=0 AND NOT (C.OwnerKind=''Student''
 AND comm.SchoolCommunicationState(C.AdmissionId,C.Destination,@Channel)=2) THEN N''No SMS consent''
 WHEN C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,C.Destination,@Channel)=0
 THEN N''School messages declined or guardian number changed''
 WHEN @Channel=''WhatsApp'' AND C.WhatsAppConsent=0 AND NOT (C.OwnerKind=''Student''
  AND comm.SchoolCommunicationState(C.AdmissionId,C.Destination,@Channel)=2) THEN N''No WhatsApp consent''
 WHEN @Category=''Financial'' AND C.FinancialConsent=0 AND NOT (C.OwnerKind=''Student''
 AND comm.SchoolCommunicationState(C.AdmissionId,C.Destination,@Channel)=2) THEN N''Financial messages not consented''
 WHEN @Category=''Announcement'' AND C.AnnouncementConsent=0 AND NOT (C.OwnerKind=''Student''
 AND comm.SchoolCommunicationState(C.AdmissionId,C.Destination,@Channel)=2) THEN N''Announcements not consented''
 WHEN @Channel=''WhatsApp'' AND LEN(T.ProviderTemplate)=0 THEN N''WhatsApp template mapping missing''
 WHEN T.Body LIKE N''%{{receipt}}%'' AND @PaymentId IS NULL THEN N''A verified payment event is required for this receipt template''
 WHEN T.Code=''admission_completed'' AND NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=C.AdmissionId AND Status=N''Active'' AND AdmissionNumber IS NOT NULL) THEN N''Admission is not complete''
 WHEN T.Code=''placement_confirmed'' AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=V.EnrollmentId AND PlacementConfirmedAt IS NOT NULL) THEN N''Placement is not confirmed''
 WHEN T.Code=''return_confirmed'' AND NOT EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=V.EnrollmentId AND ReturnConfirmedAt IS NOT NULL) THEN N''Return admission is not confirmed''
 WHEN T.Code=''promotion_completed'' AND NOT EXISTS(SELECT 1 FROM dbo.PromotionBatchStudents WHERE NewEnrollmentId=V.EnrollmentId AND Decision=N''PROMOTE'') THEN N''Promotion is not verified''
 WHEN T.Code=''outstanding'' AND comm.Balance(C.AdmissionId)<=0 THEN N''No outstanding balance''
 WHEN @PaymentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.FeePayments WHERE FeePaymentId=@PaymentId AND AdmissionId=C.AdmissionId AND Status=N''Completed'' AND PaymentMethod NOT IN(N''Refund'',N''Correction'')) THEN N''Payment is not a verified collection''
 ELSE N'''' END Exclusion
 FROM (SELECT DISTINCT TRY_CONVERT(uniqueidentifier,value) Id FROM OPENJSON(@ContactIds)) I JOIN comm.Contact C ON C.ContactId=I.Id
 CROSS APPLY comm.ValuesFor(C.ContactId,@Announcement,@PaymentId) V
 CROSS JOIN comm.Template T CROSS JOIN comm.Settings S
 CROSS APPLY(SELECT ISNULL((SELECT comm.Render(N''{{''+CONVERT(nvarchar(30),J.value)+N''}}'',C.ContactId,@Announcement,@PaymentId) text FROM OPENJSON(T.ParameterOrder) J ORDER BY TRY_CONVERT(int,J.[key]) FOR JSON PATH),N''[]'') Parameters) Rendered WHERE T.TemplateId=@TemplateId AND S.Id=1 ORDER BY C.ContactName,C.ContactId;
END;';

 EXEC sys.sp_executesql N'ALTER PROCEDURE comm.Claim @Actor int,@OnlyCampaign uniqueidentifier=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL READ COMMITTED; EXEC dbo.RequirePermission @Actor,N''Communication.Send'';
 BEGIN TRANSACTION;
 DECLARE @Now datetime2=SYSUTCDATETIME(),@Mode varchar(12),@MaxHour int,@MaxDay int,@DayBudget decimal(18,4),@MonthBudget decimal(18,4),@Hour int,@QS int,@QE int;
 SELECT @Mode=Mode,@MaxHour=HourLimit,@MaxDay=DayLimit,@DayBudget=DailyBudget,@MonthBudget=MonthlyBudget,@QS=QuietStart,@QE=QuietEnd,@Hour=DATEPART(HOUR,DATEADD(MINUTE,UtcOffsetMinutes,@Now))
 FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
 UPDATE comm.Message SET State=''DeliveryUnknown'',LastCode=''LEASE_EXPIRED_RECONCILIATION_REQUIRED'',UpdatedUtc=@Now WHERE State=''Submitting'' AND LeaseUntil<@Now AND (@OnlyCampaign IS NULL OR CampaignId=@OnlyCampaign);
 IF @Mode=''Disabled'' OR EXISTS(SELECT 1 FROM comm.Settings WHERE Paused=1 OR NextSendUtc>@Now OR (Mode=''Live'' AND RegistrationConfirmed=0))
 OR (@QS<>@QE AND ((@QS<@QE AND @Hour>=@QS AND @Hour<@QE) OR (@QS>@QE AND (@Hour>=@QS OR @Hour<@QE))))
 BEGIN COMMIT; RETURN; END;
 IF (SELECT COUNT(*) FROM comm.Attempt WHERE CreatedUtc>=DATEADD(HOUR,-1,@Now))>=@MaxHour OR
 (SELECT COUNT(*) FROM comm.Attempt WHERE CreatedUtc>=CONVERT(date,@Now))>=@MaxDay BEGIN COMMIT; RETURN; END;
 -- Revalidate mutable contact ownership/consent and paid reminders before any provider request.
 UPDATE M SET State=''Cancelled'',LastCode=''CONTACT_OR_BALANCE_CHANGED'',UpdatedUtc=@Now
 FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId JOIN comm.Contact C ON C.ContactId=M.ContactId JOIN comm.Template T ON T.TemplateId=P.TemplateId
 WHERE P.DispatchActor=@Actor AND (@OnlyCampaign IS NULL OR P.CampaignId=@OnlyCampaign) AND M.State IN(''Queued'',''Scheduled'') AND (
 (C.OwnerKind=''Family'' AND C.FamilyId IS NULL) OR T.Approved=0 OR C.OptedOut=1 OR C.Destination<>M.Destination OR
 (C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,M.Destination,P.Channel)=0) OR
 (C.Verified=0 AND NOT (C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,M.Destination,P.Channel)=2)) OR
 (P.Channel=''Sms'' AND C.SmsConsent=0 AND NOT (C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,M.Destination,P.Channel)=2)) OR
 (P.Channel=''WhatsApp'' AND C.WhatsAppConsent=0 AND NOT (C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,M.Destination,P.Channel)=2)) OR
 (T.Category=''Financial'' AND C.FinancialConsent=0 AND NOT (C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,M.Destination,P.Channel)=2)) OR
 (T.Category=''Announcement'' AND C.AnnouncementConsent=0 AND NOT (C.OwnerKind=''Student'' AND comm.SchoolCommunicationState(C.AdmissionId,M.Destination,P.Channel)=2)) OR
 (T.Category=''Financial'' AND P.PaymentId IS NULL AND (comm.Balance(M.AdmissionId)<=0 OR comm.Render(T.Body,C.ContactId,P.Announcement,P.PaymentId)<>M.Body)) OR
 (P.PaymentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.FeePayments F WHERE F.FeePaymentId=P.PaymentId AND F.AdmissionId=M.AdmissionId AND F.Status=N''Completed'' AND F.PaymentMethod NOT IN(N''Refund'',N''Correction''))));
 DECLARE @Id uniqueidentifier,@Campaign uniqueidentifier,@Cost decimal(18,4),@Token uniqueidentifier=NEWID();
 SELECT TOP(1) @Id=M.MessageId,@Campaign=M.CampaignId,@Cost=CASE WHEN @Mode=''Simulation'' THEN 0 ELSE M.EstimatedCost END
 FROM comm.Message M WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK) JOIN comm.Campaign P WITH(UPDLOCK) ON P.CampaignId=M.CampaignId
 WHERE M.State IN(''Queued'',''Scheduled'') AND M.NextAttemptUtc<=@Now AND P.DueUtc<=@Now AND P.Status IN(''Queued'',''Scheduled'') AND P.DispatchActor=@Actor
 AND (@OnlyCampaign IS NULL OR P.CampaignId=@OnlyCampaign)
 AND M.Simulation=CASE WHEN @Mode=''Simulation'' THEN 1 ELSE 0 END
 AND (@Mode=''Simulation'' OR M.Currency=(SELECT Currency FROM comm.Settings) AND CASE WHEN P.Channel=''Sms'' THEN (SELECT SmsUnitCost FROM comm.Settings) ELSE (SELECT WhatsAppUnitCost FROM comm.Settings) END IS NOT NULL)
 AND ((SELECT COUNT(*) FROM comm.Message X WHERE X.CampaignId=P.CampaignId)<(SELECT ApprovalThreshold FROM comm.Settings) OR P.ApprovedBy IS NOT NULL) ORDER BY P.DueUtc,M.MessageId;
 IF @Id IS NULL BEGIN COMMIT; RETURN; END;
 IF @Mode=''Live'' SELECT @Cost=CASE WHEN P.Channel=''Sms'' THEN S.SmsUnitCost*M.Segments ELSE S.WhatsAppUnitCost END FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId CROSS JOIN comm.Settings S WHERE M.MessageId=@Id;
 IF @OnlyCampaign IS NOT NULL AND @Mode=''Live'' AND EXISTS(SELECT 1 FROM comm.Message WHERE MessageId=@Id AND (EstimatedCost IS NULL OR EstimatedCost<>@Cost))
 BEGIN COMMIT; RETURN; END;
 IF (SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Campaign)>1 EXEC dbo.RequirePermission @Actor,N''Communication.Bulk'';
 IF @Mode=''Live'' AND ((SELECT ISNULL(SUM(ReservedCost),0) FROM comm.Attempt WHERE CreatedUtc>=CONVERT(date,@Now))+@Cost>@DayBudget OR
 (SELECT ISNULL(SUM(ReservedCost),0) FROM comm.Attempt WHERE CreatedUtc>=DATEFROMPARTS(YEAR(@Now),MONTH(@Now),1))+@Cost>@MonthBudget) BEGIN COMMIT; RETURN; END;
 UPDATE comm.Message SET EstimatedCost=@Cost,State=''Submitting'',ClaimToken=@Token,LeaseUntil=DATEADD(MINUTE,2,@Now),Attempts=Attempts+1,UpdatedUtc=@Now WHERE MessageId=@Id;
 INSERT comm.Attempt(MessageId,ClaimToken,Actor,ReservedCost) VALUES(@Id,@Token,@Actor,@Cost);
 UPDATE comm.Settings SET NextSendUtc=DATEADD(SECOND,1,@Now) WHERE Id=1;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,''SubmissionClaimed'',@Id,N''One provider attempt reserved'');
 SELECT M.MessageId,M.ClaimToken,P.Channel,M.Destination,M.Body,T.ProviderTemplate,T.Language,M.Parameters,M.Attempts,CONVERT(bit,CASE WHEN @Mode=''Simulation'' THEN 1 ELSE 0 END) Simulation
 FROM comm.Message M JOIN comm.Campaign P ON P.CampaignId=M.CampaignId JOIN comm.Template T ON T.TemplateId=P.TemplateId WHERE M.MessageId=@Id;
 COMMIT;
END;';
END;

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'7',SYSDATETIME(),SUSER_SNAME(),
 N'One Admission school-messaging choice with optional contact synchronization');
