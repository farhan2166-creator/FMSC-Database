-- Scope staff-initiated dispatch and validate the preview at snapshot creation.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), 0) <> 3
    THROW 51000, 'Communication send update requires schema 3.', 1;

IF OBJECT_ID(N'comm.CreateCampaign',N'P') IS NOT NULL
EXEC sys.sp_executesql N'ALTER PROCEDURE comm.CreateCampaign @Actor int,@Id uniqueidentifier,@TemplateId uniqueidentifier,@Channel varchar(10),@ContactIds nvarchar(max),@Announcement nvarchar(1000)=N'''',@DueUtc datetime2,@EventKey nvarchar(150)=NULL,@PaymentId int=NULL,@Reviewed nvarchar(max)=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON; EXEC dbo.RequirePermission @Actor,N''Communication.Compose'';
 DECLARE @Hash binary(32)=HASHBYTES(''SHA2_256'',CONCAT(CONVERT(nvarchar(36),@TemplateId),N''|'',@Channel,N''|'',@ContactIds,N''|'',@Announcement,N''|'',CONVERT(nvarchar(40),@DueUtc,126),N''|'',@EventKey,N''|'',@PaymentId));
 IF @Reviewed IS NOT NULL SET @Hash=HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),@Hash)+HASHBYTES(''SHA2_256'',@Reviewed));
 BEGIN TRANSACTION;
 DECLARE @Gate int; SELECT @Gate=Id FROM comm.Settings WITH(UPDLOCK,HOLDLOCK) WHERE Id=1;
 IF EXISTS(SELECT 1 FROM comm.Campaign WITH(UPDLOCK,HOLDLOCK) WHERE CampaignId=@Id)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM comm.Campaign WHERE CampaignId=@Id AND RequestHash=@Hash AND CreatedBy=@Actor) THROW 51002,''Campaign request identity was reused with different data.'',1;
  COMMIT; RETURN;
 END;
 DECLARE @ContentHash binary(32)=HASHBYTES(''SHA2_256'',CONCAT(CONVERT(nvarchar(36),@TemplateId),N''|'',@Channel,N''|'',@ContactIds,N''|'',@Announcement,N''|'',CONVERT(nvarchar(10),@DueUtc,23),N''|'',@EventKey,N''|'',@PaymentId));
 IF EXISTS(SELECT 1 FROM comm.Campaign WITH(UPDLOCK,HOLDLOCK) WHERE CreatedBy=@Actor AND ContentHash=@ContentHash AND Status<>''Cancelled'' AND CreatedUtc>=DATEADD(MINUTE,-15,SYSUTCDATETIME())) THROW 51002,''A matching recent campaign already exists. Review Outbox before preparing it again.'',1;
 CREATE TABLE #P(ContactId uniqueidentifier,ContactName nvarchar(150),Destination varchar(16),AdmissionId int,EnrollmentId int,FamilyId int,Body nvarchar(max),Parameters nvarchar(max),Segments int,EstimatedCost decimal(18,4),Currency char(3),Exclusion nvarchar(200));
 INSERT #P EXEC comm.Preview @Actor,@TemplateId,@Channel,@ContactIds,@Announcement,@PaymentId;
 IF EXISTS(SELECT 1 FROM #P WHERE LEN(Exclusion)>0) OR (SELECT COUNT(*) FROM #P)<>(SELECT COUNT(DISTINCT value) FROM OPENJSON(@ContactIds)) THROW 51001,''Review exclusions or missing contacts before creating the campaign.'',1;
 IF (SELECT COUNT(*) FROM #P)>1 EXEC dbo.RequirePermission @Actor,N''Communication.Bulk'';
 IF (SELECT COUNT(*) FROM #P)>(SELECT MaxBatch FROM comm.Settings) OR NOT EXISTS(SELECT 1 FROM #P) THROW 51001,''Campaign exceeds its configured batch limit or has no recipients.'',1;
 IF @Reviewed IS NOT NULL
 BEGIN
  IF EXISTS (
    SELECT ContactId,Destination,CONVERT(varbinary(max),Body),CONVERT(varbinary(max),Parameters),EstimatedCost,Currency FROM #P
    EXCEPT
    SELECT ContactId,Destination,CONVERT(varbinary(max),Body),CONVERT(varbinary(max),Parameters),EstimatedCost,Currency
    FROM OPENJSON(@Reviewed) WITH (ContactId uniqueidentifier,Destination varchar(16),Body nvarchar(max),Parameters nvarchar(max),EstimatedCost decimal(18,4),Currency char(3))
  ) THROW 51001,''Message details changed. Preview the messages again before sending.'',1;
 END;
 -- Family-level messages deduplicate the shared phone. Student financial messages remain per student.
 IF (SELECT Scope FROM comm.Template WHERE TemplateId=@TemplateId) IN(''Family'',''Manual'')
 BEGIN
  ;WITH D AS(SELECT *,ROW_NUMBER() OVER(PARTITION BY Destination ORDER BY ContactId) N FROM #P) DELETE FROM D WHERE N>1;
 END;
 INSERT comm.Campaign VALUES(@Id,@Hash,@ContentHash,@TemplateId,@Channel,''Draft'',@Actor,NULL,NULL,SYSUTCDATETIME(),@DueUtc,@Announcement,@EventKey,@PaymentId);
 INSERT comm.Message(MessageId,CampaignId,ContactId,AdmissionId,EnrollmentId,Destination,Body,Parameters,Simulation,Segments,EstimatedCost,Currency,State)
 SELECT NEWID(),@Id,ContactId,AdmissionId,EnrollmentId,Destination,Body,Parameters,CONVERT(bit,CASE WHEN (SELECT Mode FROM comm.Settings)<>''Live'' THEN 1 ELSE 0 END),Segments,EstimatedCost,Currency,''Draft'' FROM #P;
 INSERT comm.Audit(Actor,Operation,EntityId,Detail) VALUES(@Actor,''CampaignCreated'',@Id,N''Immutable recipient and message snapshots'');
 COMMIT;
END;';

IF OBJECT_ID(N'comm.Claim',N'P') IS NOT NULL
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

INSERT dbo.DatabaseVersion(VersionNumber, AppliedOn, AppliedBy, Notes)
VALUES (N'4', SYSDATETIME(), SUSER_SNAME(), N'Add campaign-scoped communication sending');
