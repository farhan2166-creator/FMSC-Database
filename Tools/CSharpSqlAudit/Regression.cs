using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.Dac.Model;

internal static class Regression
{
    internal static void Run()
    {
        using var schema = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        schema.AddObjects("CREATE TABLE dbo.Existing (Id int NOT NULL, Title nvarchar(60) NULL);");
        schema.AddObjects("CREATE TYPE dbo.RowT AS TABLE (Id int NOT NULL);");
        schema.AddObjects("CREATE PROCEDURE dbo.P @Id int, @Optional int = NULL AS SELECT @Id AS Id;");
        schema.AddObjects("CREATE PROCEDURE dbo.UserP @UserId int AS SELECT @UserId AS Id;");
        schema.AddObjects("CREATE PROCEDURE dbo.TvpP @Rows dbo.RowT READONLY AS SELECT Id FROM @Rows;");
        Require(new AuditExtensions.Schema(schema).Procedures["dbo.P"].Known,
            "DacFx-generated procedure DDL must supply parameters.");
        var root = Path.Combine(Path.GetTempPath(), "fmsc-audit-fictional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Synthetic only. No live FMSC database or application files are modified.
            var code = """
                using DB = FMSC_SMS.DatabaseHelper;
                namespace FMSC_SMS {
                    public static class DatabaseHelper {
                        public static object Query(string procedure, params object[] parameters) => new();
                        public static object QueryUserAsync(string procedure, params object[] parameters) => new();
                        public static object QueryUser(string procedure, params object[] parameters) => new();
                        public static object QueryUserSet(string procedure, params object[] parameters) => new();
                        public static object ExecuteUser(string procedure, params object[] parameters) => new();
                        public static object ExecuteUserAsync(string procedure, params object[] parameters) => new();
                        public static object ScalarUser(string procedure, params object[] parameters) => new();
                        public static object ScalarUserAsync(string procedure, params object[] parameters) => new();
                        public static object QueryUserSetAsync(string procedure, params object[] parameters) => new();
                        public static object Execute(string procedure, params object[] parameters) => new();
                        public static object Scalar(string procedure, params object[] parameters) => new();
                        public static object QuerySql(string sql, params object[] parameters) => new();
                        public static object QuerySqlAsync(string sql, params object[] parameters) => new();
                        public static object Int(string name, int? number) => new();
                        public static object Bit(string name, bool value) => new();
                    }
                    public enum SqlDbType { Structured }
                    public sealed class SqlParameter {
                        public SqlParameter(string name, SqlDbType sqlDbType) {}
                        public string TypeName { get; set; } = "";
                    }
                    public enum CommandType { Text, StoredProcedure }
                    public sealed class SqlCommand {
                        public SqlCommand(string sql) {}
                        public CommandType CommandType { get; set; }
                    }
                    public sealed class Demo {
                        private const string Procedure = "dbo.P";
                        private string Choose() => "dbo.P";
                        public void Run() {
                            DatabaseHelper.Query(Procedure, DatabaseHelper.Int("@Id", 1)); // CONST_GOOD
                            DatabaseHelper.Query("dbo." + "P", DatabaseHelper.Int("@Id", 1)); // CONCAT_GOOD
                            DatabaseHelper.Query("dbo.Gone", DatabaseHelper.Int("@Id", 1)); // PROC_MISSING
                            DatabaseHelper.Query("dbo.P", DatabaseHelper.Int("@Bad", 1)); // PARAM_MISSING
                            DatabaseHelper.Query("dbo.P", DatabaseHelper.Bit("@Id", true)); // PARAM_TYPE
                            DatabaseHelper.Query("dbo.P", DatabaseHelper.Int("@Id", 1)); // OPTIONAL_GOOD
                            DatabaseHelper.Query(procedure: "dbo.P", parameters: new[] { DatabaseHelper.Int("@Id", 1) }); // NAMED_GOOD
                            DB.Query("dbo.P", DatabaseHelper.Int("@Id", 1)); // ALIAS_GOOD
                            const string stable = "dbo.P";
                            DatabaseHelper.Query(stable, DatabaseHelper.Int("@Id", 1)); // LOCAL_CONST
                            DatabaseHelper.QueryUser("dbo.UserP"); // USER_SYNC
                            DatabaseHelper.QueryUserSet("dbo.UserP"); // USER_SET_SYNC
                            DatabaseHelper.Execute("dbo.P", DatabaseHelper.Int("@Id", 1)); // EXEC_GOOD
                            DatabaseHelper.ExecuteUser("dbo.UserP"); // EXEC_USER
                            DatabaseHelper.ExecuteUserAsync("dbo.UserP"); // EXEC_USER_ASYNC
                            DatabaseHelper.Scalar("dbo.P", DatabaseHelper.Int("@Id", 1)); // SCALAR_GOOD
                            DatabaseHelper.ScalarUser("dbo.UserP"); // SCALAR_USER
                            DatabaseHelper.ScalarUserAsync("dbo.UserP"); // SCALAR_USER_ASYNC
                            DatabaseHelper.QuerySqlAsync("SELECT Id FROM dbo.Existing"); // RAW_ASYNC
                            DatabaseHelper.QuerySql("SELECT Id FROM dbo.Missing"); // RAW_UNQUALIFIED_CASE
                            DatabaseHelper.QuerySql("SELECT Id FROM Missing"); // RAW_MISSING_REVIEW
                            DatabaseHelper.QueryUserAsync("dbo.UserP"); // USER_INJECT
                            DatabaseHelper.QueryUserSetAsync("dbo.UserP"); // USER_SET
                            var p = new[] { DatabaseHelper.Int("@Id", 1) };
                            DatabaseHelper.Query("dbo.P", p); // ARRAY_GOOD
                            var target = Choose();
                            DatabaseHelper.Query(target); // DYNAMIC_REVIEW
                            var mutable = "dbo.P";
                            mutable = Choose();
                            DatabaseHelper.Query(mutable); // MUTABLE_REVIEW
                            DatabaseHelper.QuerySql("SELECT e.Id FROM dbo.Existing e"); // SQL_GOOD
                            DatabaseHelper.QuerySql("SELECT e.Id FROM dbo.Absent e"); // TABLE_MISSING
                            DatabaseHelper.QuerySql("SELECT e.NoSuch FROM dbo.Existing e"); // COLUMN_REVIEW
                            DatabaseHelper.QuerySql("WITH Events AS (SELECT e.Id FROM dbo.Existing e) SELECT e.EventDate FROM Events e"); // CTE_ALIAS_GOOD
                            DatabaseHelper.QuerySql("SELECT " + Choose()); // DYNAMIC_SQL
                            DatabaseHelper.Query("dbo.TvpP", new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.RowT" }); // TVP_GOOD
                            DatabaseHelper.Query("dbo.TvpP", new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.Wrong" }); // TVP_MISSING
                            var procedure = new SqlCommand("dbo.P") { CommandType = CommandType.StoredProcedure }; // CMD_PROC
                            var absent = new SqlCommand("dbo.Gone") { CommandType = CommandType.StoredProcedure }; // CMD_ABSENT
                            var dynamicCommand = new SqlCommand(Choose()) { CommandType = CommandType.StoredProcedure }; // CMD_DYNAMIC
                            var defaultText = new SqlCommand("dbo.Gone"); // CMD_DEFAULT_TEXT
                            var text = new SqlCommand("dbo.Gone") { CommandType = CommandType.Text }; // CMD_TEXT
                            var later = new SqlCommand("dbo.Gone"); // CMD_LATER
                            later.CommandType = CommandType.StoredProcedure;
                        }
                    }
                }
                namespace Other {
                    public static class DatabaseHelper {
                        public static object Query(string value) => new();
                    }
                    public class OtherDemo {
                        public void Run() { DatabaseHelper.Query("dbo.Gone"); } // DECOY
                    }
                }
                """;
            var path = Path.Combine(root, "Cases.cs");
            File.WriteAllText(path, code);
            var findings = new List<Finding>();
            var audited = AuditExtensions.Scan(schema, root, new[] { path }, findings);
            Require(audited == 36, $"Caller count expected 36, got {audited}.");
            int Line(string marker)
            {
                var offset = code.IndexOf("// " + marker, StringComparison.Ordinal);
                Require(offset >= 0, "Missing fixture marker " + marker);
                return code[..offset].Count(c => c == '\n') + 1;
            }
            void Expect(string marker, string status, string fragment)
            {
                var location = Line(marker);
                Require(findings.Any(f => f.Line == location && f.Status == status &&
                       (f.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
                        f.Object.Contains(fragment, StringComparison.OrdinalIgnoreCase))),
                    $"Expected {status} at {marker} with {fragment}. Actual: " +
                    string.Join("; ", findings.Where(f => f.Line == location)));
            }
            void Clean(string marker)
            {
                Require(!findings.Any(f => f.Line == Line(marker)),
                    $"Unexpected finding for {marker}: " +
                    string.Join("; ", findings.Where(f => f.Line == Line(marker))));
            }
            foreach (var marker in new[] { "CONST_GOOD", "CONCAT_GOOD", "OPTIONAL_GOOD",
                "USER_INJECT", "USER_SET", "ARRAY_GOOD", "SQL_GOOD", "TVP_GOOD", "CMD_TEXT", "CMD_DEFAULT_TEXT", "DECOY",
                "NAMED_GOOD", "ALIAS_GOOD", "LOCAL_CONST", "USER_SYNC", "USER_SET_SYNC", "EXEC_GOOD",
                "EXEC_USER", "EXEC_USER_ASYNC", "SCALAR_GOOD", "SCALAR_USER", "SCALAR_USER_ASYNC", "RAW_ASYNC", "CTE_ALIAS_GOOD" })
                Clean(marker);
            Expect("PROC_MISSING", "FAIL", "dbo.Gone");
            Expect("PARAM_MISSING", "FAIL", "@Bad");
            Expect("PARAM_TYPE", "REVIEW_REQUIRED", "SQL type differs");
            Expect("DYNAMIC_REVIEW", "REVIEW_REQUIRED", "Dynamic");
            Expect("MUTABLE_REVIEW", "REVIEW_REQUIRED", "Dynamic");
            Expect("TABLE_MISSING", "FAIL", "dbo.Absent");
            Expect("RAW_UNQUALIFIED_CASE", "FAIL", "dbo.Missing");
            Expect("RAW_MISSING_REVIEW", "REVIEW_REQUIRED", "dbo.Missing");
            Expect("COLUMN_REVIEW", "REVIEW_REQUIRED", "NoSuch");
            Expect("DYNAMIC_SQL", "REVIEW_REQUIRED", "Dynamic");
            Expect("TVP_MISSING", "FAIL", "TVP type differs");
            Expect("CMD_PROC", "REVIEW_REQUIRED", "SqlCommand.Parameters");
            Expect("CMD_LATER", "REVIEW_REQUIRED", "after construction");
            Expect("CMD_ABSENT", "FAIL", "dbo.Gone");
            Expect("CMD_DYNAMIC", "REVIEW_REQUIRED", "Dynamic");
            Require(findings.All(f => f.File == "Cases.cs" && f.Line > 0), "Invalid reported file/line.");
            var expression = SyntaxFactory.ParseExpression("(@\"dbo.\" + \"P\")");
            Require(AuditExtensions.Constant(expression, null) == "dbo.P", "Literal concatenation.");
            Require(AuditExtensions.Normalize("[dbo].[P]") == "dbo.P", "Qualified identifiers.");
            Require(AuditExtensions.Normalize("P") == "dbo.P", "Default dbo schema.");
            Console.WriteLine("PASS: fictional Roslyn/DacFx/ScriptDom regressions (36 caller cases; positives, negatives, parameters, TVPs, SQL, locations).");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
