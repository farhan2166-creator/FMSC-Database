using FMSC_SMS.Core;
using FMSC_SMS.Pages.Administration;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;

// Only an explicitly disposable database is accepted. No school auto-attach path is used.
string connectionText=Environment.GetEnvironmentVariable("FMSC_AUDIT_CONNECTION")??throw new Exception("Set FMSC_AUDIT_CONNECTION to a disposable administration clone.");
var builder=new SqlConnectionStringBuilder(connectionText);
bool loopback=!builder.IntegratedSecurity&&builder.DataSource.StartsWith("127.0.0.1,",StringComparison.Ordinal)&&builder.UserID=="sa";
if(!builder.InitialCatalog.StartsWith("FMSC_AuditChecks_",StringComparison.Ordinal)||(!builder.IntegratedSecurity&&!loopback))
    throw new Exception("Use a disposable FMSC_AuditChecks_<suffix> catalog with integrated security or isolated loopback test SQL authentication.");
DatabaseHelper.ConnectionString=connectionText;
int checks=0;
void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
DataTable Query(string sql,params SqlParameter[] parameters)
{
    using var connection=DatabaseHelper.GetConnection();connection.Open();using var command=new SqlCommand(sql,connection){CommandTimeout=120};command.Parameters.AddRange(parameters);
    var result=new DataTable();using var adapter=new SqlDataAdapter(command);adapter.Fill(result);return result;
}
int Id(string sql)=>Convert.ToInt32(Query(sql).Rows[0][0]);
int admin=Id("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId;");
CurrentUser.UserId=admin;
var users=new AdministrationStore(AccessEditorKind.Users);var roles=new AdministrationStore(AccessEditorKind.Roles);
var userCatalog=await users.LoadCatalogAsync();
int office=userCatalog.Roles.First(role=>!role.Protected).Id;
int finance=userCatalog.Roles.Last(role=>!role.Protected).Id;
int view=userCatalog.Permissions.First(permission=>permission.Code=="Students.View").Id;
int fees=userCatalog.Permissions.First(permission=>permission.Code=="Fees.View").Id;
int adminRole=userCatalog.Roles.Single(role=>role.Protected).Id;
string prefix="admin-audit-"+Guid.NewGuid().ToString("N")[..10];
const string password="Fixture password 12345";

var created=await users.SaveAsync(new(0,prefix+"-user","",true,new[]{office}),"",userCatalog.Token,password);
Check(created.Outcome==AccessSaveOutcome.Saved&&created.Id>0,"production writer creates a user with a confirmed ID");
var loaded=await users.LoadAsync(created.Id);
Check(loaded.Record.Name==prefix+"-user"&&loaded.Record.Choices.SequenceEqual(new[]{office})&&loaded.Record.ForcePasswordChange,"loaded user fields and assigned roles match the saved draft");
Check(loaded.Record.Snapshot.Length==64&&loaded.Record.CredentialProof.Length==64,"opaque snapshot and credential proof are available without returning stored credential bytes");
using(var credential=Query("SELECT PasswordHash,PasswordSalt FROM dbo.Users WHERE UserId=@Id;",DatabaseHelper.Int("@Id",created.Id)))
{
    byte[] salt=(byte[])credential.Rows[0]["PasswordSalt"],hash=(byte[])credential.Rows[0]["PasswordHash"];
    Check(salt.Length==16&&hash.Length==64&&CryptographicOperations.FixedTimeEquals(hash,Rfc2898DeriveBytes.Pbkdf2(password,salt,100_000,HashAlgorithmName.SHA512,64)),"new-user hashing matches the existing sign-in policy");
}
string credentialBefore=loaded.Record.CredentialProof;
var changed=await users.SaveAsync(new(created.Id,prefix+"-renamed","",false,new[]{finance}),loaded.Record.Snapshot,loaded.Catalog.Token);
Check(changed.Outcome==AccessSaveOutcome.Saved&&changed.Id==created.Id,"account fields and assigned roles save together");
var current=await users.LoadAsync(created.Id);
Check(current.Record.Name==prefix+"-renamed"&&!current.Record.ForcePasswordChange&&current.Record.Choices.SequenceEqual(new[]{finance})&&current.Record.CredentialProof==credentialBefore,"normal Save User changes the explicit role draft and keeps the password");
var stale=await users.SaveAsync(AccessDraft.From(loaded.Record) with { Name=prefix+"-stale" },loaded.Record.Snapshot,loaded.Catalog.Token);
Check(stale.Outcome==AccessSaveOutcome.Rejected&&stale.Error is AdministrativeStaleStateException,"a stale user editor is refused before its write");
Check((await users.LoadAsync(created.Id)).Record.Name==prefix+"-renamed","stale account save does not overwrite the newer account");
var reset=await users.ResetPasswordAsync(current.Record,current.Catalog.Token,password+" reset",true);
Check(reset.Outcome==AccessSaveOutcome.Saved,"password-only reset succeeds for a clean saved user");
var afterReset=await users.LoadAsync(created.Id);
Check(afterReset.Record.Name==current.Record.Name&&afterReset.Record.Choices.SequenceEqual(current.Record.Choices)&&afterReset.Record.ForcePasswordChange&&afterReset.Record.CredentialProof!=credentialBefore,"password reset preserves saved username/roles and changes only credentials/password requirement");
var verified=await users.CheckAsync(reset.Intent!);
Check(verified.Outcome==AccessCheckOutcome.Saved&&verified.Record?.Id==created.Id,"read-only reconciliation verifies the reset's exact credential proof");
var notApplied=await users.CheckAsync(new(AccessEditorKind.Users,AccessDraft.From(afterReset.Record) with { Name=prefix+"-not-applied" },OriginalSnapshot:afterReset.Record.Snapshot));
Check(notApplied.Outcome==AccessCheckOutcome.NotApplied,"read-only recovery recognizes unchanged original values and permits a deliberate retry");
var noAccess=await users.SaveAsync(AccessDraft.From(afterReset.Record) with { Choices=Array.Empty<int>() },afterReset.Record.Snapshot,afterReset.Catalog.Token);
Check(noAccess.Outcome==AccessSaveOutcome.Saved,"empty assigned-role sets remain supported");
Check(Query("EXEC dbo.GetUserPermissions @UserId;",DatabaseHelper.Int("@UserId",created.Id)).Rows.Count==0,"a legacy primary-role reference does not grant fallback access");
var zeroCatalog=await users.LoadCatalogAsync();
var zeroNew=await users.SaveAsync(new(0,prefix+"-zero","",true,Array.Empty<int>()),"",zeroCatalog.Token,password);
Check(zeroNew.Outcome==AccessSaveOutcome.Saved&&Query("EXEC dbo.GetUserPermissions @UserId;",DatabaseHelper.Int("@UserId",zeroNew.Id)).Rows.Count==0,"new accounts can deliberately start with zero effective access");
var protectedUser=await users.LoadAsync(admin);
Check(protectedUser.Record.Protected,"primary Administrator account is protected");
var protectSave=await users.SaveAsync(AccessDraft.From(protectedUser.Record) with { Name=prefix+"-bad-admin" },protectedUser.Record.Snapshot,protectedUser.Catalog.Token);
Check(protectSave.Outcome==AccessSaveOutcome.Rejected,"the normal editor writer refuses Administrator account changes");
Query("INSERT dbo.UserRoles(UserId,RoleId) VALUES(@User,@Role);",DatabaseHelper.Int("@User",zeroNew.Id),DatabaseHelper.Int("@Role",adminRole));
var memberAdmin=await users.LoadAsync(zeroNew.Id);
Check(memberAdmin.Record.Protected&&memberAdmin.Catalog.Items.Single(item=>item.Id==zeroNew.Id).Protected,"Administrator membership is protected even when the legacy primary role differs");
Check((await users.SaveAsync(AccessDraft.From(memberAdmin.Record) with { Choices=Array.Empty<int>() },memberAdmin.Record.Snapshot,memberAdmin.Catalog.Token)).Outcome==AccessSaveOutcome.Rejected,"a protected membership cannot be removed through normal user maintenance");

var roleCatalog=await roles.LoadCatalogAsync();
var createdRole=await roles.SaveAsync(new(0,prefix+"-role","Role fixture",false,new[]{view}),"",roleCatalog.Token);
Check(createdRole.Outcome==AccessSaveOutcome.Saved&&createdRole.Id>0,"role definition and permission selection create together");
var roleFrame=await roles.LoadAsync(createdRole.Id);
Check(roleFrame.Record.Description=="Role fixture"&&roleFrame.Record.Choices.SequenceEqual(new[]{view}),"saved role description and permissions round-trip");
var newRole=await roles.SaveAsync(AccessDraft.From(roleFrame.Record) with { Name=prefix+"-role-edit",Description="Edited description",Choices=new[]{fees} },roleFrame.Record.Snapshot,roleFrame.Catalog.Token);
Check(newRole.Outcome==AccessSaveOutcome.Saved,"one Save Role changes name, description and permissions");
var roleEdited=await roles.LoadAsync(createdRole.Id);
Check(roleEdited.Record.Name==prefix+"-role-edit"&&roleEdited.Record.Description=="Edited description"&&roleEdited.Record.Choices.SequenceEqual(new[]{fees}),"all role values reflect the one committed draft");
Check((await roles.SaveAsync(AccessDraft.From(roleFrame.Record) with { Description="Stale" },roleFrame.Record.Snapshot,roleFrame.Catalog.Token)).Error is AdministrativeStaleStateException,"stale role definitions/permissions are refused");
var catalogDriftUser=await users.LoadAsync(created.Id);
Query("UPDATE dbo.Roles SET Description=CONCAT(ISNULL(Description,N''),N' changed') WHERE RoleId=@Id;",DatabaseHelper.Int("@Id",office));
Check((await users.SaveAsync(AccessDraft.From(catalogDriftUser.Record) with { ForcePasswordChange=!catalogDriftUser.Record.ForcePasswordChange },catalogDriftUser.Record.Snapshot,catalogDriftUser.Catalog.Token)).Error is AdministrativeStaleStateException,"changed access definitions invalidate a user's old permission preview");

string failureNew=prefix+"-fail-new",failureEdit=prefix+"-fail-edit";
Query($"CREATE TRIGGER dbo.FMSC_Admin_FailPermissionSave ON dbo.RolePermissions AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Roles R ON R.RoleId=I.RoleId WHERE R.RoleName IN(N'{failureNew}',N'{failureEdit}')) THROW 59961,'Injected second-step failure',1; END;");
try
{
    var before=await roles.LoadAsync(createdRole.Id);
    int auditBefore=Id("SELECT COUNT(*) FROM dbo.AuditLogs;");
    var failed=await roles.SaveAsync(AccessDraft.From(before.Record) with { Name=failureEdit,Description="Must roll back",Choices=new[]{view} },before.Record.Snapshot,before.Catalog.Token);
    Check(failed.Outcome!=AccessSaveOutcome.Saved,"an injected permission-step failure never claims success");
    var after=await roles.LoadAsync(createdRole.Id);
    Check(after.Record.Snapshot==before.Record.Snapshot,"the role name, description, permissions and membership state all roll back after second-step failure");
    Check(Id("SELECT COUNT(*) FROM dbo.AuditLogs;")==auditBefore,"audit entries from the earlier role-definition step also roll back");
    var failedCreate=await roles.SaveAsync(new(0,failureNew,"Must not exist",false,new[]{view}),"",after.Catalog.Token);
    Check(failedCreate.Outcome!=AccessSaveOutcome.Saved&&Query("SELECT RoleId FROM dbo.Roles WHERE RoleName=@Name;",DatabaseHelper.P("@Name",failureNew)).Rows.Count==0,"second-step failure cannot leave a partly created role");
    var checkedFailure=await roles.CheckAsync(failedCreate.Intent??new(AccessEditorKind.Roles,new(0,failureNew,"Must not exist",false,new[]{view})));
    Check(checkedFailure.Outcome==AccessCheckOutcome.NotApplied,"read-only reconciliation recognizes a rolled-back new role");
}
finally{Query("DROP TRIGGER dbo.FMSC_Admin_FailPermissionSave;");}

roleCatalog=await roles.LoadCatalogAsync();
var blankRole=await roles.SaveAsync(new(0,prefix+"-empty-role","No access",false,Array.Empty<int>()),"",roleCatalog.Token);
Check(blankRole.Outcome==AccessSaveOutcome.Saved&&(await roles.LoadAsync(blankRole.Id)).Record.Choices.Length==0,"empty-permission roles retain the established zero-access policy");
var readonlyCatalog=await roles.LoadCatalogAsync();
int[] reads=readonlyCatalog.Permissions.Where(permission=>permission.Code is "System.Users" or "System.Roles").Select(permission=>permission.Id).ToArray();
var readerRole=await roles.SaveAsync(new(0,prefix+"-reader","Read-only",false,reads),"",readonlyCatalog.Token);
var readerCatalog=await users.LoadCatalogAsync();
var reader=await users.SaveAsync(new(0,prefix+"-reader-user","",true,new[]{readerRole.Id}),"",readerCatalog.Token,password);
CurrentUser.UserId=reader.Id;
var readonlyRole=await roles.LoadAsync(createdRole.Id);
Check((await roles.SaveAsync(AccessDraft.From(readonlyRole.Record) with { Description="Denied" },readonlyRole.Record.Snapshot,readonlyRole.Catalog.Token)).Outcome==AccessSaveOutcome.Rejected,"read-only role viewers cannot submit management writes");
var readonlyUser=await users.LoadAsync(created.Id);
Check((await users.SaveAsync(AccessDraft.From(readonlyUser.Record) with { Name=prefix+"-denied" },readonlyUser.Record.Snapshot,readonlyUser.Catalog.Token)).Outcome==AccessSaveOutcome.Rejected,"read-only user viewers cannot submit management writes");
CurrentUser.UserId=admin;
var capturedCatalog=await roles.LoadCatalogAsync();
var queued=roles.SaveAsync(new(0,prefix+"-captured-actor","Captured actor",false,Array.Empty<int>()),"",capturedCatalog.Token);
CurrentUser.UserId=reader.Id;
var capturedResult=await queued;
Check(capturedResult.Outcome==AccessSaveOutcome.Saved,"queued save captures its acting user before background execution");
Check(Convert.ToInt32(Query("SELECT TOP(1) UserId FROM dbo.AuditLogs WHERE TableName=N'Roles' AND RecordId=@Id ORDER BY AuditLogId DESC;",DatabaseHelper.P("@Id",capturedResult.Id.ToString())).Rows[0][0])==admin,"SQL audit records the captured actor rather than a later session value");
CurrentUser.UserId=admin;
var currentAdminRole=await roles.LoadAsync(adminRole);
Check(currentAdminRole.Record.Protected&&(await roles.SaveAsync(AccessDraft.From(currentAdminRole.Record) with { Description="Forbidden" },currentAdminRole.Record.Snapshot,currentAdminRole.Catalog.Token)).Outcome==AccessSaveOutcome.Rejected,"Administrator role remains protected in the unified role writer");
var concurrentUser=await users.LoadAsync(created.Id);
var competing=await Task.WhenAll(
    users.SaveAsync(AccessDraft.From(concurrentUser.Record) with { Name=prefix+"-clerk-a" },concurrentUser.Record.Snapshot,concurrentUser.Catalog.Token),
    users.SaveAsync(AccessDraft.From(concurrentUser.Record) with { Name=prefix+"-clerk-b" },concurrentUser.Record.Snapshot,concurrentUser.Catalog.Token));
Check(competing.Count(result=>result.Outcome==AccessSaveOutcome.Saved)==1&&competing.Count(result=>result.Error is AdministrativeStaleStateException)==1,
    "two simultaneous editors cannot silently overwrite the same user");
var winner=await users.LoadAsync(created.Id);
Check(winner.Record.Name is var finalName&&(finalName==prefix+"-clerk-a"||finalName==prefix+"-clerk-b"),"the winning concurrent draft is stored completely");
Query("UPDATE dbo.Users SET FailedLoginCount=FailedLoginCount+1 WHERE UserId=@Id;",DatabaseHelper.Int("@Id",created.Id));
Check((await users.LoadAsync(created.Id)).Record.Snapshot==winner.Record.Snapshot,"unrelated login counters do not create false stale account edits");
Console.WriteLine($"PASS: {checks} native production administration SQL checks on disposable fixture.");
