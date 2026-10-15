using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

internal static class CommunicationSqlChecks
{
    internal static async Task Run(Fixture f,Report report)
    {
        var store=new SqlCommunicationStore(DatabaseHelper.GetConnection,f.Admin);
        int student=0,enrollment=0;Guid contact=Guid.NewGuid(),template=Guid.NewGuid();
        bool installed=await report.Stage("Communication: additive schema, explicit consent and permission isolation",async()=>{
            int grants=Sql.Count("SELECT COUNT(*) FROM dbo.RolePermissions;");
            using var connection=DatabaseHelper.GetConnection();connection.Open();
            new SqlCommand("EXEC sys.sp_set_session_context @key=N'FMSC_CommunicationInstall',@value=1;",connection).ExecuteNonQuery();
            foreach(string batch in Regex.Split(File.ReadAllText("Migrations/Communication/001_CommunicationCenter.sql"),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
                if(!string.IsNullOrWhiteSpace(batch))
                {
                    try { new SqlCommand(batch,connection){CommandTimeout=60}.ExecuteNonQuery(); }
                    catch(SqlException ex){throw new AssertionFailure("Communication migration SQL "+ex.Number+": "+ex.Message);}
                }
            report.Check(await store.IsInstalledAsync(),"Optional communication schema is present without modifying the approved DACPAC.");
            report.Check(Sql.Count("SELECT COUNT(*) FROM dbo.RolePermissions;")==grants,"Installing communication grants no Accountant authority.");
            report.Check(Sql.Count("SELECT MAX(TRY_CONVERT(int,VersionNumber)) FROM dbo.DatabaseVersion;")==6,"Communication does not change base schema version 6.");
            var saved=Sql.Proc("AdmitStudent",f.AdmissionArgs("Communication Example"));student=Convert.ToInt32(saved.Rows[0]["AdmissionId"]);enrollment=Convert.ToInt32(saved.Rows[0]["EnrollmentId"]);
            await Contact(true,false);
            await Template("notice","Utility","{{school}}: {{student}} {{announcement}}");
            var rows=await store.PreviewAsync(template,MessageChannel.Sms,[contact],"Synthetic message");
            report.Check(rows.Rows.Count==1 && Convert.ToString(rows.Rows[0]["Exclusion"])=="","Explicit verified SMS consent permits a scoped student message.");
            await report.Refused("A restricted user cannot read communication history",()=>new SqlCommunicationStore(DatabaseHelper.GetConnection,f.Restricted).ReadAsync("History"),50001,50002,50003);
        });
        if(!installed)return;
        await report.Stage("Communication: durable claims, unknown acknowledgement and restart",async()=>{
            await Settings();
            var id=Guid.NewGuid();await store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Synthetic message",new DateTime(2026,1,1));
            await store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Synthetic message",new DateTime(2026,1,1));
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id",Sql.P("@Id",id))==1,"Durable draft preparation is idempotent before submission.");
        });
        // The same request includes an exact due-time snapshot. Exercise it explicitly.
        await report.Stage("Communication: idempotent submissions, consent recheck and no blind retry",async()=>{
            var due=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);var id=Guid.NewGuid();
            await store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Idempotent fixture",due);
            await store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Idempotent fixture",due);
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id",Sql.P("@Id",id))==1,"Identical campaign retries preserve one durable message.");
            await report.Refused("Changed campaign payload cannot reuse its identity",()=>store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Changed fixture",due),51002);
            await store.TransitionAsync(id,"Submit");
            Ready();
            var claims=await Task.WhenAll(store.ClaimAsync(CancellationToken.None),new SqlCommunicationStore(DatabaseHelper.GetConnection,f.Admin).ClaimAsync(CancellationToken.None));
            report.Check(claims.Count(c=>c is not null)==1,"Concurrent dispatchers obtain one exclusive submission claim.");
            var claim=claims.Single(c=>c is not null)!;
            await store.CompleteAsync(claim,"Fixture",new(DeliveryState.DeliveryUnknown,Code:"LOST_ACK"));Ready();
            report.Check(await new SqlCommunicationStore(DatabaseHelper.GetConnection,f.Admin).ClaimAsync(CancellationToken.None) is null,"Restart does not resubmit an uncertain provider acknowledgement.");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Attempt WHERE MessageId=@Id",Sql.P("@Id",claim.Id))==1,"Unknown submission retains its one attempt.");
            var blocked=Guid.NewGuid();await store.CreateAsync(blocked,template,MessageChannel.Sms,[contact],"Opt out before send",due);await store.TransitionAsync(blocked,"Submit");
            await Contact(true,true);Ready();
            report.Check(await store.ClaimAsync(CancellationToken.None) is null,"Opt-out between preview and dispatch suppresses the message.");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Cancelled'",Sql.P("@Id",blocked))==1,"Suppression is durably recorded with a reason.");
            await Contact(true,false);
        });
        await report.Stage("Communication: delivery monotonicity, pause/cancel and approval",async()=>{
            var id=Guid.NewGuid();await store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Delivery fixture",new DateTime(2026,1,1));await store.TransitionAsync(id,"Submit");Ready();
            var claim=(await store.ClaimAsync(CancellationToken.None))!;
            await store.CompleteAsync(claim,"Fixture",new(DeliveryState.Accepted,"fixture-reference"));
            report.Check(State(claim.Id)=="Accepted","A successful submission is not labelled Delivered.");
            async Task Status(string state)=>await store.CallAsync("StatusUpdate",default,SqlCommunicationStore.P("@Id",claim.Id),SqlCommunicationStore.P("@Provider","Fixture"),SqlCommunicationStore.P("@Reference","fixture-reference"),SqlCommunicationStore.P("@State",state));
            await Status("Delivered");int audit=Sql.Count("SELECT COUNT(*) FROM comm.Audit;");await Status("Delivered");await Status("Sent");
            report.Check(State(claim.Id)=="Delivered" && Sql.Count("SELECT COUNT(*) FROM comm.Audit;")==audit,"Duplicate and older delivery callbacks do not regress state or repeat audit writes.");
            await Status("Read");report.Check(State(claim.Id)=="Read","Supported read receipts advance delivery state.");
            var cancel=Guid.NewGuid();await store.CreateAsync(cancel,template,MessageChannel.Sms,[contact],"Cancel fixture",DateTime.UtcNow.AddDays(1));await store.TransitionAsync(cancel,"Submit");await store.TransitionAsync(cancel,"Pause");await store.TransitionAsync(cancel,"Cancel");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Cancelled'",Sql.P("@Id",cancel))==1,"A paused scheduled campaign can cancel only unsent messages.");
        });
        await report.Stage("Communication: financial reminder revalidation and original ledger",async()=>{
            Guid reminder=Guid.NewGuid();template=Guid.NewGuid();await Template("outstanding","Financial","{{student}}: {{amount}} remaining.");
            var due=new DateTime(2026,1,1);await store.CreateAsync(reminder,template,MessageChannel.Sms,[contact],"",due);await store.TransitionAsync(reminder,"Submit");
            decimal before=Sql.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges WHERE AdmissionId=@Id",Sql.P("@Id",student));
            var charges=Sql.Query("SELECT BillingChargeId,dbo.GetFeeChargeBalance(BillingChargeId) Balance FROM dbo.BillingCharges WHERE AdmissionId=@Id AND Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BillingChargeId)>0;",Sql.P("@Id",student));
            var allocations=charges.AsEnumerable().Select(row=>(Convert.ToInt64(row["BillingChargeId"]),Convert.ToDecimal(row["Balance"]))).ToArray();
            await CollectionDiscountWriter.CollectAsync("ProcessFeeTransaction",Array.Empty<CollectionDiscountWriter.Edit>(),"Synthetic communication fixture",Lifecycle.CollectionArgs(student,enrollment,f.MonthlyDate,allocations.Sum(x=>x.Item2),allocations,"Payment before reminder dispatch"));Ready();
            report.Check(await store.ClaimAsync(CancellationToken.None) is null,"A zero-balance reminder is suppressed before a provider request.");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Cancelled'",Sql.P("@Id",reminder))==1,"Paid/changed-balance suppression survives application restart.");
            report.Check(Sql.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges WHERE AdmissionId=@Id",Sql.P("@Id",student))==before,"Messaging never changes charge amounts or original academic records.");
        });
        await report.Stage("Communication: future-only automation, approval and template revocation",async()=>{
            template=Guid.NewGuid();await Template("automation_fixture","Utility","{{student}}: {{announcement}}");
            var pendingArgs=f.AdmissionArgs("Second Communication Example");pendingArgs.Single(p=>p.ParameterName=="@QuickRegistration").Value=true;
            var pending=Sql.Proc("AdmitStudent",pendingArgs);int pendingStudent=Convert.ToInt32(pending.Rows[0]["AdmissionId"]);
            var rule=Guid.NewGuid();
            await store.CallAsync("SaveRule",default,SqlCommunicationStore.P("@Id",rule),SqlCommunicationStore.P("@Name","Fictional admission rule"),SqlCommunicationStore.P("@Event","AdmissionCompleted"),SqlCommunicationStore.P("@TemplateId",template),SqlCommunicationStore.P("@Channel","Sms"),SqlCommunicationStore.P("@Enabled",true),SqlCommunicationStore.P("@Approval",true));
            await store.CallAsync("Evaluate");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Campaign WHERE EventKey LIKE @Key",Sql.P("@Key",rule+":%"))==0,"Activating a rule never sends historical admissions.");
            Sql.Proc("CompleteQuickAdmission",Sql.P("@UserId",f.Admin),Sql.P("@AdmissionId",pendingStudent));student=pendingStudent;contact=Guid.NewGuid();await Contact(true,false);
            await Task.WhenAll(store.CallAsync("Evaluate"),store.CallAsync("Evaluate"));
            var campaigns=Sql.Query("SELECT CampaignId,Status FROM comm.Campaign WHERE EventKey LIKE @Key",Sql.P("@Key",rule+":%"));
            report.Check(campaigns.Rows.Count==1 && Convert.ToString(campaigns.Rows[0]["Status"])=="AwaitingApproval","Concurrent evaluation detects confirmation of a pre-existing quick registration exactly once and requires approval.");
            Ready();report.Check(await store.ClaimAsync(default) is null,"Automation cannot bypass required approval.");
            var campaign=(Guid)campaigns.Rows[0]["CampaignId"];await store.TransitionAsync(campaign,"Approve");
            var dispatcher=new MessageDispatcher(store,_=>throw new AssertionFailure("Simulation attempted a live transport."));Ready();
            report.Check(await dispatcher.DispatchOneAsync(default),"Production dispatcher resumes the approved persistent simulation without a live provider.");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Accepted' AND LastCode='SIMULATED_NO_NETWORK'",Sql.P("@Id",campaign))==1,"Simulation acceptance is explicitly marked and is never delivery evidence.");
            var revoked=Guid.NewGuid();await store.CreateAsync(revoked,template,MessageChannel.Sms,[contact],"Revocation fixture",new DateTime(2026,1,1));await store.TransitionAsync(revoked,"Submit");
            await store.CallAsync("RevokeTemplate",default,SqlCommunicationStore.P("@Id",template));Ready();
            report.Check(await store.ClaimAsync(default) is null && Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Cancelled'",Sql.P("@Id",revoked))==1,"Revoked templates suppress queued work while retaining its historical text.");
        });
        await report.Stage("Communication: live-mode isolation, budgets, bounded retry and throttling",async()=>{
            await report.Refused("Unauthorized send is rejected at SQL",()=>new SqlCommunicationStore(DatabaseHelper.GetConnection,f.Restricted).ClaimAsync(default),50001,50002,50003);
            template=Guid.NewGuid();await Template("safety_fixture","Utility","{{student}}: {{announcement}}");
            var simulated=Guid.NewGuid();await store.CreateAsync(simulated,template,MessageChannel.Sms,[contact],"Simulation cannot become live",new DateTime(2026,1,1));await store.TransitionAsync(simulated,"Submit");
            await Settings("Live",0);Ready();report.Check(await store.ClaimAsync(default) is null,"A simulation snapshot cannot become a live send when settings change.");await store.TransitionAsync(simulated,"Cancel");
            var live=Guid.NewGuid();await store.CreateAsync(live,template,MessageChannel.Sms,[contact],"Budget fixture",new DateTime(2026,1,1));await store.TransitionAsync(live,"Submit");Ready();
            report.Check(await store.ClaimAsync(default) is null,"A zero live budget blocks a provider attempt before claiming.");
            await Settings("Live",10);Ready();var claimed=(await store.ClaimAsync(default))!;
            report.Check(!claimed.Simulation && Sql.Money("SELECT ReservedCost FROM comm.Attempt WHERE MessageId=@Id",Sql.P("@Id",claimed.Id))==1,"A permitted live claim reserves its current configured estimate without calling a provider.");
            await store.CompleteAsync(claimed,"Fixture",new(DeliveryState.Rejected,Code:"HTTP_429",RetrySafe:true,RetryAfterSeconds:240));Ready();
            report.Check(await store.ClaimAsync(default) is null,"Rate-limited submissions wait for the persisted provider retry interval.");
            Sql.Query("UPDATE comm.Message SET NextAttemptUtc=DATEADD(SECOND,-1,SYSUTCDATETIME()) WHERE MessageId=@Id",Sql.P("@Id",claimed.Id));Ready();
            var retry=(await store.ClaimAsync(default))!;report.Check(retry.Id==claimed.Id && retry.Attempt==2,"A confirmed refusal reuses its message identity for a bounded retry.");
            await store.CompleteAsync(retry,"Fixture",new(DeliveryState.DeliveryUnknown,Code:"LOST_ACK"));
            await Settings();Sql.Query("UPDATE comm.Settings SET HourLimit=1;");var capped=Guid.NewGuid();await store.CreateAsync(capped,template,MessageChannel.Sms,[contact],"Hourly cap fixture",new DateTime(2026,1,1));await store.TransitionAsync(capped,"Submit");Ready();
            report.Check(await store.ClaimAsync(default) is null,"The hourly attempt cap is enforced across previous campaigns.");await store.TransitionAsync(capped,"Cancel");await Settings();
        });
        await report.Stage("Communication: committed promotion, return and payment event context",async()=>{
            async Task<Guid> Rule(string eventName,Guid ruleTemplate){var id=Guid.NewGuid();await store.CallAsync("SaveRule",default,SqlCommunicationStore.P("@Id",id),SqlCommunicationStore.P("@Name","Fictional "+eventName),SqlCommunicationStore.P("@Event",eventName),SqlCommunicationStore.P("@TemplateId",ruleTemplate),SqlCommunicationStore.P("@Channel","Sms"),SqlCommunicationStore.P("@Enabled",true));return id;}
            DataTable Campaigns(Guid rule)=>Sql.Query("SELECT CampaignId FROM comm.Campaign WHERE EventKey LIKE @Key",Sql.P("@Key",rule+":%"));
            template=Guid.NewGuid();await Template("promotion_completed","Utility","{{student}}: {{class}} {{year}}");var promotionRule=await Rule("PromotionCompleted",template);
            var decisions=new DataTable();decisions.Columns.Add("AdmissionId",typeof(int));decisions.Columns.Add("Decision",typeof(string));decisions.Rows.Add(student,"PROMOTE");
            Sql.Proc("BulkPromoteStudents",Sql.P("@UserId",f.Admin),Sql.P("@SourceAcademicYearId",f.SourceYear),Sql.P("@DestinationAcademicYearId",f.NextYear),Sql.P("@SourceCampusId",f.Campus),Sql.P("@SourceClassId",f.Class6),Sql.P("@SourceSectionId",f.Section),Sql.P("@DestinationClassSectionId",f.Placement7),Sql.P("@EffectiveDate",f.PromotionDate),Sql.P("@Remarks","Fictional communication promotion"),Sql.TvpParameter("@Decisions","BulkPromotionDecisionType",decisions));
            int destination=Sql.Count("SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@Id AND AcademicYearId=@Year",Sql.P("@Id",student),Sql.P("@Year",f.NextYear));
            await store.CallAsync("Evaluate");var promotions=Campaigns(promotionRule);report.Check(promotions.Rows.Count==1,"An actual committed bulk promotion produces one communication event.");await store.TransitionAsync((Guid)promotions.Rows[0][0],"Cancel");
            template=Guid.NewGuid();await Template("return_confirmed","Utility","{{student}}: {{class}} {{year}}");var returnRule=await Rule("ReturnConfirmed",template);await store.CallAsync("Evaluate");
            report.Check(Campaigns(returnRule).Rows.Count==0,"An existing unconfirmed destination enrollment does not produce a return message.");
            Sql.Proc("ConfirmPromotedAdmission",Sql.P("@UserId",f.Admin),Sql.P("@EnrollmentId",destination),Sql.P("@ReturnDate",f.PromotionDate),Sql.P("@UsesTransport",false),Sql.TvpParameter("@FeeChoices","ReturnFeeChoiceType",Tvp.ReturnFeeChoices(new[]{(f.Tuition,6000m,0m),(f.AdmissionFee,2000m,0m)})));
            await store.CallAsync("Evaluate");await store.CallAsync("Evaluate");var returns=Campaigns(returnRule);report.Check(returns.Rows.Count==1 && Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND EnrollmentId=@Enrollment",Sql.P("@Id",returns.Rows[0][0]),Sql.P("@Enrollment",destination))==1,"A confirmed return on a pre-existing enrollment is detected once with its correct year context.");await store.TransitionAsync((Guid)returns.Rows[0][0],"Cancel");
            template=Guid.NewGuid();await Template("payment_received","Financial","{{student}}: received {{amount}}; receipt {{receipt}}; {{class}} {{year}}");var paymentRule=await Rule("PaymentCompleted",template);
            long charge=Convert.ToInt64(Sql.Scalar("SELECT TOP(1) BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Id AND dbo.GetFeeChargeBalance(BillingChargeId)>=1000 ORDER BY BillingChargeId",Sql.P("@Id",destination)));
            await CollectionDiscountWriter.CollectAsync("ProcessFeeTransaction",Array.Empty<CollectionDiscountWriter.Edit>(),"Synthetic communication fixture",Lifecycle.CollectionArgs(student,destination,f.NextMonthlyDate,1000,[(charge,1000m)],"Fictional committed event collection"));
            await store.CallAsync("Evaluate");await store.CallAsync("Evaluate");var payments=Campaigns(paymentRule);report.Check(payments.Rows.Count==1,"A genuine committed payment produces one receipt campaign after repeated evaluation.");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message M JOIN comm.Campaign C ON C.CampaignId=M.CampaignId JOIN dbo.FeePayments P ON P.FeePaymentId=C.PaymentId WHERE C.CampaignId=@Id AND M.EnrollmentId=P.EnrollmentId AND M.EnrollmentId=@Enrollment AND CHARINDEX(P.ReceiptNumber,M.Body)>0",Sql.P("@Id",payments.Rows[0][0]),Sql.P("@Enrollment",destination))==1,"The receipt snapshot uses the actual payment identity and destination enrollment.");await store.TransitionAsync((Guid)payments.Rows[0][0],"Cancel");
        });
        await report.Stage("Communication: rendered text limits and Urdu segment parity",async()=>{
            template=Guid.NewGuid();await Template("oversize_fixture","Utility",string.Concat(Enumerable.Repeat("{{student}}",180)));
            var rows=await store.PreviewAsync(template,MessageChannel.Sms,[contact]);report.Check(Convert.ToString(rows.Rows[0]["Exclusion"])!.Contains("2000"),"Oversized personalization is explained instead of silently truncating a message.");
            await report.Refused("Oversized personalization cannot become a durable send",()=>store.CreateAsync(Guid.NewGuid(),template,MessageChannel.Sms,[contact],"",new DateTime(2026,1,1)),51001);
            template=Guid.NewGuid();await Template("literal_fixture","Utility","{{student}}");
            string original=Convert.ToString(Sql.Scalar("SELECT StudentName FROM dbo.Students WHERE AdmissionId=@Id",Sql.P("@Id",student)))!;
            try{Sql.Query("UPDATE dbo.Students SET StudentName=N'Literal {{amount}}' WHERE AdmissionId=@Id",Sql.P("@Id",student));var literal=await store.PreviewAsync(template,MessageChannel.Sms,[contact]);report.Check(Convert.ToString(literal.Rows[0]["Body"])=="Literal {{amount}}","SQL rendering treats substituted person data literally and cannot inject a financial placeholder.");}
            finally{Sql.Query("UPDATE dbo.Students SET StudentName=@Name WHERE AdmissionId=@Id",Sql.P("@Name",original),Sql.P("@Id",student));}
            foreach(var text in new[]{new string('ا',71),new string('^',81),string.Concat(Enumerable.Repeat("😀",36))})report.Check(Sql.Count("SELECT comm.Segments(@Text)",Sql.P("@Text",text))==SmsSegments.Estimate(text).Segments,"SQL and provider estimates agree for Urdu, extension and supplementary Unicode.");
        });
        await report.Stage("Communication: signed Meta receipts, replay isolation and inbound opt-out",async()=>{
            template=Guid.NewGuid();await Template("meta_fixture","Utility","{{student}}: {{announcement}}","approved_fixture");await Contact(true,false,true);
            var id=Guid.NewGuid();await store.CreateAsync(id,template,MessageChannel.WhatsApp,[contact],"Meta fixture",new DateTime(2026,1,1));await store.TransitionAsync(id,"Submit");Ready();
            var claim=(await store.ClaimAsync(default))!;string reference="wamid."+Guid.NewGuid().ToString("N");
            string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var receiver=new MetaWebhookStore(DatabaseHelper.GetConnection,f.Admin,secret,"fixture_account","fixture_phone");
            var staleRevision=(byte[])Sql.Scalar("SELECT Revision FROM comm.Contact WHERE ContactId=@Id",Sql.P("@Id",contact))!;
            var now=DateTimeOffset.UtcNow;
            byte[] Payload(bool stop)=>JsonSerializer.SerializeToUtf8Bytes(new{ @object="whatsapp_business_account",entry=new[]{new{id="fixture_account",changes=new[]{new{value=new{metadata=new{phone_number_id="fixture_phone"},statuses=new[]{new{id=reference,status="delivered",timestamp=now.ToUnixTimeSeconds().ToString()}},messages=stop?new[]{new{from="923001234567",timestamp=now.ToUnixTimeSeconds().ToString(),type="text",text=new{body="STOP"}}}:Array.Empty<object>()}}}}}});
            string Signature(byte[] body)=>"sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),body));
            var body=Payload(false);report.Check(await receiver.ReceiveAsync(body,Signature(body),now,default) && State(claim.Id)=="Submitting","An early signed receipt is held while HTTP acknowledgement is still pending.");
            await store.CompleteAsync(claim,"MetaCloud",new(DeliveryState.Accepted,reference));
            report.Check(State(claim.Id)=="Delivered" && Sql.Count("SELECT COUNT(*) FROM comm.PendingReceipt WHERE ProviderReference=@Ref",Sql.P("@Ref",reference))==0,"Acknowledgement reconciles the early signed receipt with the exact message without losing delivery evidence.");
            int audits=Sql.Count("SELECT COUNT(*) FROM comm.Audit;");await receiver.ReceiveAsync(body,Signature(body),now,default);
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Audit;")==audits,"Replayed signed callback is durably deduplicated.");
            report.Check(!await receiver.ReceiveAsync(body,"sha256=invalid",now,default),"Unsigned input never reaches SQL ingestion.");
            body=Payload(true);report.Check(await receiver.ReceiveAsync(body,Signature(body),now,default) && Sql.Count("SELECT COUNT(*) FROM comm.Contact WHERE ContactId=@Id AND OptedOut=1 AND WhatsAppConsent=0",Sql.P("@Id",contact))==1,"Signed inbound STOP suppresses future WhatsApp messages.");
            await report.Refused("A stale contact editor cannot undo a newer opt-out",()=>Contact(true,false,true,staleRevision),51004);
            report.Check(Convert.ToString(Sql.Scalar("SELECT USER_NAME()"))=="dbo","Restricted callback impersonation is reverted before returning a pooled connection.");
            await report.Refused("Raw webhook ingestion cannot be invoked through the ordinary SQL user",()=>Task.Run(()=>Sql.Proc("comm.IngestMeta",Sql.P("@Actor",f.Admin),Sql.P("@Payload",Encoding.UTF8.GetString(body)))),51003);
        });
        await report.Stage("Communication: interrupted bulk campaign and readable history",async()=>{
            await Contact(true,false);template=Guid.NewGuid();await Template("bulk_fixture","Utility","{{student}}: {{announcement}}");
            var contacts=Sql.Query("SELECT ContactId,AdmissionId FROM comm.Contact WHERE OwnerKind='Student'");
            var savedContact=contact;var savedStudent=student;
            try{foreach(DataRow row in contacts.Rows){contact=(Guid)row["ContactId"];student=Convert.ToInt32(row["AdmissionId"]);await Contact(true,false);}}finally{contact=savedContact;student=savedStudent;}
            var ids=contacts.AsEnumerable().Select(r=>(Guid)r["ContactId"]).ToArray();report.Check(ids.Length==2,"The bulk fixture has two independently scoped fictional student contacts with fresh consent after the shared-phone opt-out test.");
            var eligible=await store.PreviewAsync(template,MessageChannel.Sms,ids,"Bulk fixture");report.Check(eligible.AsEnumerable().All(r=>Convert.ToString(r["Exclusion"])==""),"Both synthetic bulk recipients meet the real consent and template rules.");
            var id=Guid.NewGuid();await store.CreateAsync(id,template,MessageChannel.Sms,ids,"Bulk fixture",new DateTime(2026,1,1));await store.TransitionAsync(id,"Submit");Ready();
            var claim=(await store.ClaimAsync(default))!;await store.CompleteAsync(claim,"Fixture",new(DeliveryState.Accepted,"bulk-"+Guid.NewGuid().ToString("N")));await store.TransitionAsync(id,"Pause");Ready();
            report.Check(await store.ClaimAsync(default) is null,"Pausing an interrupted bulk campaign prevents the next provider claim.");await store.TransitionAsync(id,"Cancel");
            report.Check(Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Accepted'",Sql.P("@Id",id))==1 && Sql.Count("SELECT COUNT(*) FROM comm.Message WHERE CampaignId=@Id AND State='Cancelled'",Sql.P("@Id",id))==1,"Cancellation preserves the submitted message and cancels only the remaining unsent recipient.");
            var rows=await store.ReadAsync("History",search:"Second Communication Example");report.Check(rows.Rows.Count>0 && rows.Columns.Contains("Recipient") && rows.Columns.Contains("Template"),"History supports student search and identifies the contact and template without exposing message bodies in the list.");
        });
        await report.Stage("Communication: contact privacy and financial placeholder authorization",async()=>{
            Sql.Query("INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT U.RoleId,P.PermissionId FROM dbo.Users U CROSS JOIN dbo.Permissions P WHERE U.UserId=@Id AND P.PermissionCode IN(N'Communication.View',N'Communication.Compose');",Sql.P("@Id",f.Restricted));
            var reader=new SqlCommunicationStore(DatabaseHelper.GetConnection,f.Restricted);var rows=await reader.ReadAsync("Recipients");
            report.Check(rows.AsEnumerable().All(r=>r.IsNull("Outstanding")),"A general recipient search does not disclose financial balances.");
            await report.Refused("Compose permission cannot read consent administration",()=>reader.ReadAsync("Contacts"),50003);
            await report.Refused("Student viewing does not grant staff recipient access",()=>reader.ReadAsync("Recipients",filter:"Staff"),50003);
            var previousTemplate=template;template=Guid.NewGuid();try{await report.Refused("A utility template cannot bypass financial-field permissions",()=>Template("misclassified_fixture","Utility","{{amount}}"),51001);}finally{template=previousTemplate;}
        });
        await report.Stage("Communication: expired lease and schema tamper refusal",async()=>{
            await Contact(true,false);var id=Guid.NewGuid();await store.CreateAsync(id,template,MessageChannel.Sms,[contact],"Lease fixture",new DateTime(2026,1,1));await store.TransitionAsync(id,"Submit");Ready();
            var claim=(await store.ClaimAsync(default))!;Sql.Query("UPDATE comm.Message SET LeaseUntil=DATEADD(SECOND,-1,SYSUTCDATETIME()) WHERE MessageId=@Id",Sql.P("@Id",claim.Id));Ready();
            report.Check(await store.ClaimAsync(default) is null && State(claim.Id)=="DeliveryUnknown","An abandoned submission lease becomes uncertain and is never blindly retried.");
            Sql.Query("ALTER PROCEDURE comm.RevokeTemplate @Actor int,@Id uniqueidentifier AS SELECT 1;");
            report.Check(!await store.IsInstalledAsync(),"Changed add-on SQL definitions cannot pass the reviewed module contract.");
        });
        string State(Guid id)=>Convert.ToString(Sql.Scalar("SELECT State FROM comm.Message WHERE MessageId=@Id",Sql.P("@Id",id)))!;
        void Ready()=>Sql.Query("UPDATE comm.Settings SET NextSendUtc=DATEADD(SECOND,-1,SYSUTCDATETIME());");
        Task Settings(string mode="Simulation",decimal daily=10)=>store.CallAsync("SaveSettings",default,SqlCommunicationStore.P("@Mode",mode),SqlCommunicationStore.P("@Paused",false),SqlCommunicationStore.P("@MaxBatch",100),SqlCommunicationStore.P("@ApprovalThreshold",25),SqlCommunicationStore.P("@HourLimit",100),SqlCommunicationStore.P("@DayLimit",200),SqlCommunicationStore.P("@DailyBudget",daily),SqlCommunicationStore.P("@MonthlyBudget",100),SqlCommunicationStore.P("@RegistrationConfirmed",mode=="Live"),SqlCommunicationStore.P("@SmsCost",1),SqlCommunicationStore.P("@WhatsAppCost",1),SqlCommunicationStore.P("@QuietStart",0),SqlCommunicationStore.P("@QuietEnd",0));
        Task Contact(bool consent,bool optout,bool whatsapp=false,byte[]? revision=null)=>store.CallAsync("SaveContact",default,SqlCommunicationStore.P("@Id",contact),SqlCommunicationStore.P("@Kind","Student"),SqlCommunicationStore.P("@AdmissionId",student),SqlCommunicationStore.P("@Name","Fictional guardian"),SqlCommunicationStore.P("@Phone",CommunicationPhone.Normalize("03001234567")),SqlCommunicationStore.P("@Language","en"),SqlCommunicationStore.P("@Channel","Sms"),SqlCommunicationStore.P("@Verified",true),SqlCommunicationStore.P("@Sms",consent),SqlCommunicationStore.P("@WhatsApp",whatsapp),SqlCommunicationStore.P("@Financial",true),SqlCommunicationStore.P("@Announcements",true),SqlCommunicationStore.P("@OptOut",optout),SqlCommunicationStore.P("@Source","Fictional consent evidence "+Guid.NewGuid().ToString("N")),SqlCommunicationStore.Revision(revision??Sql.Scalar("SELECT Revision FROM comm.Contact WHERE ContactId=@Id",Sql.P("@Id",contact))));
        Task Template(string code,string category,string body,string mapping="")=>store.CallAsync("SaveTemplate",default,SqlCommunicationStore.P("@Id",template),SqlCommunicationStore.P("@Code",code),SqlCommunicationStore.P("@Version",2),SqlCommunicationStore.P("@Language","en"),SqlCommunicationStore.P("@Category",category),SqlCommunicationStore.P("@Scope","Student"),SqlCommunicationStore.P("@Body",body),SqlCommunicationStore.P("@ProviderTemplate",mapping),SqlCommunicationStore.P("@Order","[]"),SqlCommunicationStore.P("@Approved",true));
    }
}
