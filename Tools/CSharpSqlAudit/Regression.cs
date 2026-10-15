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
        var fixture = schema.GetObjects(DacQueryScopes.UserDefined)
            .First(p => p.ObjectType.Name == "Procedure" && p.Name.ToString().Contains("P", StringComparison.Ordinal));
        Console.WriteLine("FIXTURE_AST: " + string.Join(", ",
            fixture.GetAst().Batches.SelectMany(b => b.Statements).Select(x => x.GetType().FullName)));
        Console.WriteLine("FIXTURE_SIGNATURE: " + new AuditExtensions.Schema(schema).Procedures["dbo.P"].Known);
        var root = Path.Combine(Path.GetTempPath(), "fmsc-audit-fictional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Synthetic only. No live FMSC database or application files are modified.
            var code = """
                namespace FMSC_SMS {
                    public static class DatabaseHelper {
                        public static object Query(string procedure, params object[] parameters) => new();
                        public static object QueryUserAsync(string procedure, params object[] parameters) => new();
                        public static object QueryUserSetAsync(string procedure, params object[] parameters) => new();
                        public static object Execute(string procedure, params object[] parameters) => new();
                        public static object Scalar(string procedure, params object[] parameters) => new();
                        public static object QuerySql(string sql, params object[] parameters) => new();
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
                            DatabaseHelper.QuerySql("SELECT " + Choose()); // DYNAMIC_SQL
                            DatabaseHelper.Query("dbo.TvpP", new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.RowT" }); // TVP_GOOD
                            DatabaseHelper.Query("dbo.TvpP", new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.Wrong" }); // TVP_MISSING
                            var procedure = new SqlCommand("dbo.P") { CommandType = CommandType.StoredProcedure }; // CMD_PROC
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
            Require(audited == 19, $"Caller count expected 19, got {audited}.");
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
                "USER_INJECT", "USER_SET", "ARRAY_GOOD", "SQL_GOOD", "TVP_GOOD", "CMD_TEXT", "DECOY" })
                Clean(marker);
            Expect("PROC_MISSING", "FAIL", "dbo.Gone");
            Expect("PARAM_MISSING", "FAIL", "@Bad");
            Expect("PARAM_TYPE", "REVIEW_REQUIRED", "SQL type differs");
            Expect("DYNAMIC_REVIEW", "REVIEW_REQUIRED", "Dynamic");
            Expect("MUTABLE_REVIEW", "REVIEW_REQUIRED", "Dynamic");
            Expect("TABLE_MISSING", "FAIL", "dbo.Absent");
            Expect("COLUMN_REVIEW", "REVIEW_REQUIRED", "NoSuch");
            Expect("DYNAMIC_SQL", "REVIEW_REQUIRED", "Dynamic");
            Expect("TVP_MISSING", "FAIL", "TVP type differs");
            Expect("CMD_PROC", "REVIEW_REQUIRED", "SqlCommand.Parameters");
            Expect("CMD_LATER", "FAIL", "dbo.Gone");
            Require(findings.All(f => f.File == "Cases.cs" && f.Line > 0), "Invalid reported file/line.");
            var expression = SyntaxFactory.ParseExpression("(@\"dbo.\" + \"P\")");
            Require(AuditExtensions.Constant(expression, null) == "dbo.P", "Literal concatenation.");
            Require(AuditExtensions.Normalize("[dbo].[P]") == "dbo.P", "Qualified identifiers.");
            Require(AuditExtensions.Normalize("P") == "dbo.P", "Default dbo schema.");
            Console.WriteLine("PASS: fictional Roslyn/DacFx/ScriptDom regressions (19 caller cases; positives, negatives, parameters, TVPs, SQL, locations).");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
