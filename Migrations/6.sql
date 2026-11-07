-- Schema 6: enforce admission WhatsApp withdrawals during preview and provider claims.
-- The existing optional communication module is updated in place when present.
-- Never install or recreate the module as a side effect of this migration.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF ISNULL((SELECT TOP (1) TRY_CONVERT(int,VersionNumber)
  FROM dbo.DatabaseVersion ORDER BY VersionId DESC),0)<>5
 THROW 51000,'Communication consent guard requires schema 5.',1;

IF SCHEMA_ID(N'comm') IS NOT NULL
BEGIN
 IF OBJECT_ID(N'comm.Module',N'U') IS NULL OR OBJECT_ID(N'comm.Contact',N'U') IS NULL
    OR OBJECT_ID(N'comm.Preview',N'P') IS NULL OR OBJECT_ID(N'comm.Claim',N'P') IS NULL
    OR OBJECT_ID(N'dbo.StudentGuardianCommunication',N'U') IS NULL
  THROW 51000,'Communication module is incomplete; refuse an unsafe consent migration.',1;

 -- New installations use the matching reference definition in Services/Communication/Schema.sql.
 EXEC sys.sp_executesql N'CREATE OR ALTER FUNCTION comm.GuardianWhatsAppAllowed(@AdmissionId int,@Destination varchar(16)) RETURNS bit AS
BEGIN
 DECLARE @Recorded nvarchar(30),@Agreed bit;
 SELECT @Recorded=Phone,@Agreed=WhatsAppConsent
 FROM dbo.StudentGuardianCommunication WHERE AdmissionId=@AdmissionId;
 IF @Agreed IS NULL RETURN 1;
 IF @Agreed=0 OR @Recorded IS NULL RETURN 0;
 DECLARE @Digits nvarchar(30)=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
  LTRIM(RTRIM(@Recorded)),N''+'',N''''),N'' '',N''''),N''-'',N''''),N''('',N''''),N'')'',N'''');
 IF LEFT(@Digits,4)=N''0092'' AND LEN(@Digits)=14 SET @Digits=SUBSTRING(@Digits,5,10);
 ELSE IF LEFT(@Digits,2)=N''92'' AND LEN(@Digits)=12 SET @Digits=SUBSTRING(@Digits,3,10);
 ELSE IF LEFT(@Digits,1)=N''0'' AND LEN(@Digits)=11 SET @Digits=SUBSTRING(@Digits,2,10);
 ELSE RETURN 0;
 IF LEN(@Digits)<>10 OR @Digits NOT LIKE N''3[0-6]%'' OR @Digits LIKE N''%[^0-9]%'' RETURN 0;
 RETURN CASE WHEN @Destination=CONCAT(N''+92'',@Digits) THEN 1 ELSE 0 END;
END;';

 -- Keep all existing preview exclusions, provider safeguards and claim leasing semantics.
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
 WHEN C.Language<>T.Language THEN N''Template language does not match contact'' WHEN C.Verified=0 THEN N''Unverified contact''
 WHEN C.OptedOut=1 THEN N''Opted out'' WHEN @Channel=''Sms'' AND C.SmsConsent=0 THEN N''No SMS consent''
 WHEN @Channel=''WhatsApp'' AND C.WhatsAppConsent=0 THEN N''No WhatsApp consent''
 WHEN @Channel=''WhatsApp'' AND C.OwnerKind=''Student'' AND comm.GuardianWhatsAppAllowed(C.AdmissionId,C.Destination)=0
 THEN N''Admission WhatsApp consent withdrawn or guardian number changed''
 WHEN @Category=''Financial'' AND C.FinancialConsent=0 THEN N''Financial messages not consented''
 WHEN @Category=''Announcement'' AND C.AnnouncementConsent=0 THEN N''Announcements not consented''
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
 (C.OwnerKind=''Family'' AND C.FamilyId IS NULL) OR T.Approved=0 OR C.Verified=0 OR C.OptedOut=1 OR C.Destination<>M.Destination OR (P.Channel=''Sms'' AND C.SmsConsent=0) OR (P.Channel=''WhatsApp'' AND C.WhatsAppConsent=0) OR
  (P.Channel=''WhatsApp'' AND C.OwnerKind=''Student'' AND comm.GuardianWhatsAppAllowed(C.AdmissionId,M.Destination)=0) OR
 (T.Category=''Financial'' AND C.FinancialConsent=0) OR (T.Category=''Announcement'' AND C.AnnouncementConsent=0) OR
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
VALUES(N'6',SYSDATETIME(),SUSER_SNAME(),
 N'Guard WhatsApp messaging against admission consent withdrawal and number changes');
