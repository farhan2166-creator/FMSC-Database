using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using Microsoft.SqlServer.TransactSql.ScriptDom;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    private static async Task Main(string[] args)
    {
        foreach(string value in new[]{"6508","B-2026-00015","FR-2026-00012","R-2026-00010","QR-6508","Ali Khan","1","Urdu اردو"})
            Check(GlobalSearchInput.CanSearch(value),"Names and document references retain their original digits/text");
        foreach(string value in new[]{"","A",new string('a',121)})Check(!GlobalSearchInput.CanSearch(value),"Empty, broad one-letter and oversized lookups do not query records");
        Check(GlobalSearchInput.EscapeLike("a%_[~") == "a~%~_~[~~","LIKE metacharacters are literal search text");
        Check(GlobalSearchInput.Words("one two three four five six seven").Length==7,"No part of a long person name is silently discarded");
        foreach(string query in new[]{GlobalRecordSearch.Query,GlobalRecordSearch.ReadBill,GlobalRecordSearch.ReadReceiptIdentity,GlobalRecordSearch.ReadPayment})
        {
            var fragment=new TSql160Parser(true).Parse(new StringReader(query),out var errors);
            Check(errors.Count==0,"Search/document query parses: "+string.Join("; ",errors.Select(e=>e.Message)));
            var visitor=new ReadOnlyVisitor(query);fragment.Accept(visitor);Check(visitor.Safe,"Lookup writes only its local result table and executes only permission checks");
        }
        int reads=0;
        DatabaseHelper.Read=(_,token,parameters)=>{
            token.ThrowIfCancellationRequested();reads++;
            Check((int)parameters.Single(p=>p.ParameterName=="@UserId").Value==CurrentUser.UserId,"Search passes the authenticated actor");
            Check(((string)parameters.Single(p=>p.ParameterName=="@Prefix").Value).EndsWith('%'),"Prefix matching is parameterized");
            return Task.FromResult(Rows());
        };
        CurrentUser.Grants.Clear();Check((await GlobalRecordSearch.ReadAsync("Ali",default)).Count==0&&reads==0,"No record access means no database query");
        CurrentUser.Grants.Add("Students.View");
        var students=await GlobalRecordSearch.ReadAsync("Ali",default);
        Check(students.Count==1&&students[0].Kind=="Student","Student-only access cannot receive financial results");
        CurrentUser.Grants.Clear();CurrentUser.Grants.Add("Fees.View");
        var documents=await GlobalRecordSearch.ReadAsync("R-2026",default);
        Check(documents.Count==3&&documents.All(r=>r.Kind!="Student"),"Fees-only access returns documents and payment references without student navigation");
        CurrentUser.Grants.Add("Students.View");
        Check((await GlobalRecordSearch.ReadAsync("1",default)).Count==4,"Ambiguous input preserves labelled alternatives");
        int before=reads;await GlobalRecordSearch.ReadAsync("A",default);Check(reads==before,"Single-letter searches do not hit the database");
        DatabaseHelper.Read=(_,_,_)=>{var data=Rows();CurrentUser.UserId=2;return Task.FromResult(data);};
        Check((await GlobalRecordSearch.ReadAsync("Ali",default)).Count==0,"Responses from an earlier signed-in actor are discarded");
        DatabaseHelper.Read=(_,_,_)=>{var data=Rows();DatabaseHelper.DatabaseEndpoint="different-endpoint";return Task.FromResult(data);};
        Check((await GlobalRecordSearch.ReadAsync("Ali",default)).Count==0,"Responses from a different database endpoint are discarded");
        DatabaseHelper.Read=(_,_,_)=>{var data=Rows();CurrentUser.Grants.Clear();return Task.FromResult(data);};
        Check((await GlobalRecordSearch.ReadAsync("Ali",default)).Count==0,"Revoked permissions cannot reveal a completed result");
        if(args.Contains("--sql"))await CheckSqlAsync();
        Console.WriteLine($"PASS: {checks} global search checks"+(args.Contains("--sql")?" including disposable SQL.":"; SQL execution not requested."));
    }
    private static DataTable Rows()
    {
        var data=new DataTable();foreach(string field in new[]{"Kind","RecordKey","Reference","Name","Context"})data.Columns.Add(field);
        data.Columns.Add("AdmissionId",typeof(int));data.Columns.Add("Rank",typeof(int));
        foreach(string kind in new[]{"Student","Bill","Receipt","PaymentReference"})data.Rows.Add(kind,kind is "Student" or "PaymentReference"?"6508":Guid.NewGuid().ToString(),"R-2026-0001","Ali Example","Fictional context",6508,0);
        return data;
    }
    private sealed class ReadOnlyVisitor(string sql) : TSqlFragmentVisitor
    {
        internal bool Safe=true;
        public override void ExplicitVisit(InsertStatement node){Safe&=node.InsertSpecification.Target is VariableTableReference; base.ExplicitVisit(node);}
        public override void ExplicitVisit(UpdateStatement node){Safe=false;}
        public override void ExplicitVisit(DeleteStatement node){Safe=false;}
        public override void ExplicitVisit(MergeStatement node){Safe=false;}
        public override void ExplicitVisit(CreateTableStatement node){Safe=false;}
        public override void ExplicitVisit(ExecuteStatement node)
        {
            var source=sql.Substring(node.StartOffset,node.FragmentLength);
            Safe&=source.StartsWith("EXEC dbo.RequireAuthenticatedUser ")||source.StartsWith("EXEC dbo.RequirePermission ");
        }
    }
}
