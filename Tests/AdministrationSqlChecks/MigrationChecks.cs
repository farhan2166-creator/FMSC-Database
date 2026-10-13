using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

internal static class MigrationChecks
{
    internal static int Run(string connectionText,string migrationPath)
    {
        // The caller already restricts the connection to an explicitly disposable database.
        using var connection=new SqlConnection(connectionText);connection.Open();
        int checks=0;
        object? Scalar(string sql){using var command=new SqlCommand(sql,connection){CommandTimeout=120};return command.ExecuteScalar();}
        void Execute(string sql){using var command=new SqlCommand(sql,connection){CommandTimeout=120};command.ExecuteNonQuery();}
        void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS: "+text);}
        string Snapshot()=>Convert.ToString(Scalar("""
            SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(
              (SELECT RoleId,RoleName,Description FROM dbo.Roles ORDER BY RoleId FOR JSON PATH,INCLUDE_NULL_VALUES),
              (SELECT PermissionId,PermissionCode,PermissionName,Description FROM dbo.Permissions ORDER BY PermissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
              (SELECT RoleId,PermissionId FROM dbo.RolePermissions ORDER BY RoleId,PermissionId FOR JSON PATH),
              (SELECT UserId,Username,RoleId,MustChangePassword,PasswordHash,PasswordSalt,UpdatedAt FROM dbo.Users ORDER BY UserId FOR JSON PATH,INCLUDE_NULL_VALUES),
              (SELECT UserId,RoleId FROM dbo.UserRoles ORDER BY UserId,RoleId FOR JSON PATH),
              (SELECT VersionId,VersionNumber,AppliedOn,AppliedBy,Notes FROM dbo.DatabaseVersion ORDER BY VersionId FOR JSON PATH,INCLUDE_NULL_VALUES),
              (SELECT * FROM dbo.AuditOperations ORDER BY OperationId FOR JSON PATH,INCLUDE_NULL_VALUES),
              (SELECT * FROM dbo.AuditLogs ORDER BY AuditLogId FOR JSON PATH,INCLUDE_NULL_VALUES),
              (SELECT O.name,M.definition FROM sys.sql_modules M JOIN sys.objects O ON O.object_id=M.object_id ORDER BY O.name FOR JSON PATH)
            )),2);
            """))!;
        string migration=File.ReadAllText(migrationPath);
        Check(Convert.ToString(Scalar("SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;"))=="2","migration fixture starts at the supplied version 2");
        string accountantGrants=Convert.ToString(Scalar("SELECT P.PermissionCode FROM dbo.RolePermissions RP JOIN dbo.Roles R ON R.RoleId=RP.RoleId JOIN dbo.Permissions P ON P.PermissionId=RP.PermissionId WHERE R.RoleName=N'Accountant' ORDER BY P.PermissionCode FOR JSON PATH;"))!;
        string credentials=Convert.ToString(Scalar("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT UserId,Username,PasswordHash,PasswordSalt,MustChangePassword FROM dbo.Users ORDER BY UserId FOR JSON PATH,INCLUDE_NULL_VALUES)),2);"))!;
        Execute("CREATE TRIGGER dbo.FMSC_Migration_FailVersion ON dbo.DatabaseVersion AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE VersionNumber=N'3') THROW 59990,'Injected final migration failure',1; END;");
        try
        {
            string before=Snapshot();bool failed=false;
            try{Execute(migration);}catch(SqlException ex) when(ex.Number==59990){failed=true;}
            Check(failed,"an injected final-step migration failure is surfaced");
            Check(Snapshot()==before,"all role, grant, account, audit, version and procedure changes roll back after final-step failure");
        }
        finally{Execute("DROP TRIGGER dbo.FMSC_Migration_FailVersion;");}
        Execute("UPDATE dbo.DatabaseVersion SET VersionNumber=N'1' WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);");
        string wrongVersion=Snapshot();bool refused=false;
        try{Execute(migration);}catch(SqlException ex) when(ex.Number==57400){refused=true;}
        Check(refused&&Snapshot()==wrongVersion,"the migration refuses an older version without partial changes");
        Execute("UPDATE dbo.DatabaseVersion SET VersionNumber=N'2' WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);");
        // Exercise legacy references, zero memberships and Administrator membership on a different primary role.
        Execute("""
            DECLARE @Old int=(SELECT RoleId FROM dbo.Roles WHERE RoleName=N'Front Desk'),@Accountant int=(SELECT RoleId FROM dbo.Roles WHERE RoleName=N'Accountant'),@Admin int=(SELECT RoleId FROM dbo.Roles WHERE RoleName=N'Administrator');
            INSERT dbo.Users(Username,RoleId,MustChangePassword) VALUES(N'migration-old-role-fixture',@Old,1),(N'migration-empty-fixture',@Accountant,1),(N'migration-member-admin-fixture',@Old,1);
            INSERT dbo.UserRoles(UserId,RoleId) SELECT UserId,@Old FROM dbo.Users WHERE Username=N'migration-old-role-fixture';
            INSERT dbo.UserRoles(UserId,RoleId) SELECT UserId,@Admin FROM dbo.Users WHERE Username=N'migration-member-admin-fixture';
            """);
        string schoolCounts=Convert.ToString(Scalar("SELECT (SELECT COUNT(*) FROM dbo.Students) Students,(SELECT COUNT(*) FROM dbo.Enrollments) Enrollments,(SELECT COUNT(*) FROM dbo.FeeConfigurations) Rates,(SELECT COUNT(*) FROM dbo.FeePayments) Payments FOR JSON PATH;"))!;
        Execute(migration);
        Check(Convert.ToString(Scalar("SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;"))=="3","successful migration records version 3");
        Check(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.Roles;"))==2&&Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.Roles WHERE RoleName IN(N'Administrator',N'Accountant');"))==2,"only Administrator and Accountant remain");
        Check(Convert.ToString(Scalar("SELECT P.PermissionCode FROM dbo.RolePermissions RP JOIN dbo.Roles R ON R.RoleId=RP.RoleId JOIN dbo.Permissions P ON P.PermissionId=RP.PermissionId WHERE R.RoleName=N'Accountant' ORDER BY P.PermissionCode FOR JSON PATH;"))==accountantGrants,"Accountant's existing six permission codes are preserved exactly");
        Check(Convert.ToString(Scalar("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT UserId,Username,PasswordHash,PasswordSalt,MustChangePassword FROM dbo.Users WHERE Username NOT LIKE N'migration-%-fixture' ORDER BY UserId FOR JSON PATH,INCLUDE_NULL_VALUES)),2);"))==credentials,"existing account names, credentials and password requirements are unchanged");
        Check(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.UserRoles UR JOIN dbo.Users U ON U.UserId=UR.UserId WHERE U.Username IN(N'migration-old-role-fixture',N'migration-empty-fixture');"))==0,"removed-role and empty-membership accounts receive no automatic replacement access");
        Check(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE U.Username IN(N'migration-old-role-fixture',N'migration-empty-fixture') AND R.RoleName=N'Accountant';"))==2,"legacy primary references remain valid without granting fallback access");
        Check(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE U.Username=N'migration-member-admin-fixture' AND R.RoleName=N'Administrator';"))==1,"legacy Administrator membership retains full authority after normalization");
        Check(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.UserRoles UR JOIN dbo.Users U ON U.UserId=UR.UserId JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' AND UR.RoleId<>U.RoleId;"))==0,"Administrator is no longer assigned unrelated roles");
        string catalog=Convert.ToString(Scalar("SELECT (SELECT PermissionCode Code,PermissionName Name,Description FROM dbo.Permissions ORDER BY PermissionCode FOR JSON PATH);"))!;
        var actual=JsonSerializer.Deserialize<FMSC_SMS.Core.PermissionDefinition[]>(catalog)!;
        Check(actual.Length==28&&actual.All(entry=>FMSC_SMS.Core.PermissionCatalog.All.TryGetValue(entry.Code,out var expected)&&entry==expected),"SQL permission codes and labels match the embedded C# catalog exactly");
        Check(Convert.ToString(Scalar("SELECT (SELECT COUNT(*) FROM dbo.Students) Students,(SELECT COUNT(*) FROM dbo.Enrollments) Enrollments,(SELECT COUNT(*) FROM dbo.FeeConfigurations) Rates,(SELECT COUNT(*) FROM dbo.FeePayments) Payments FOR JSON PATH;"))==schoolCounts,"student, enrollment, fee-rate and payment counts are unchanged");
        string once=Snapshot();Execute(migration);
        Check(Snapshot()==once,"rerunning version 3 is a no-op that preserves later state and audit history");
        return checks;
    }
}
