using Microsoft.Data.SqlClient;
using System.Text.Json;

internal static class FeeMatrixChecks
{
    private sealed record Rate(int Id,int Campus,int Class,int Fee,decimal Amount,string Frequency,string Remarks);
    internal static void Run(string master)
    {
        string connection=new SqlConnectionStringBuilder(master){InitialCatalog="CommunicationFixture",Pooling=false}.ConnectionString;
        using var c=new SqlConnection(connection);c.Open();int checks=0;
        SqlTransaction? transaction=null;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        object? Scalar(string sql){using var q=new SqlCommand(sql,c,transaction);return q.ExecuteScalar();}
        void Execute(string sql){using var q=new SqlCommand(sql,c,transaction);q.ExecuteNonQuery();}
        int actor=Convert.ToInt32(Scalar("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';"));
        var rates=new List<Rate>();
        using(var q=new SqlCommand("SELECT TOP(3) FeeConfigurationId,CampusId,ClassId,FeeTypeId,Amount,ISNULL(Frequency,N''),ISNULL(Remarks,N'') FROM dbo.FeeConfigurations ORDER BY FeeConfigurationId;",c))
        using(var reader=q.ExecuteReader())while(reader.Read())rates.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetString(5),reader.GetString(6)));
        if(rates.Count<3)throw new Exception("Fixture needs three existing class rates.");
        Rate first=rates[0],second=rates[1];
        string Json(params Rate[] rows)=>JsonSerializer.Serialize(rows.Select(row=>new {FeeConfigurationId=row.Id,CampusId=row.Campus,ClassId=row.Class,FeeTypeId=row.Fee,
            ExpectedAmount=row.Amount,ExpectedFrequency=row.Frequency,ExpectedRemarks=row.Remarks}));
        DataTable Plan(params Rate[] rows)
        {
            var table=new DataTable();
            foreach(string field in new[]{"FeeConfigurationId","CampusId","ClassId","FeeTypeId"})table.Columns.Add(field,typeof(int));
            table.Columns.Add("Amount",typeof(decimal));table.Columns.Add("Frequency");table.Columns.Add("Remarks");
            foreach(var row in rows)table.Rows.Add(row.Id,row.Campus,row.Class,row.Fee,row.Amount,row.Frequency,row.Remarks);
            return table;
        }
        void Apply(DataTable plan,string? cleared=null,int? user=null)
        {
            using var q=new SqlCommand("dbo.ApplyFeeConfigurationPlan",c,transaction){CommandType=CommandType.StoredProcedure};
            q.Parameters.AddWithValue("@UserId",user??actor);
            q.Parameters.Add(new SqlParameter("@Plan",SqlDbType.Structured){TypeName="dbo.FeeConfigurationPlanType",Value=plan});
            if(cleared is not null)q.Parameters.AddWithValue("@ClearedConfigurations",cleared);
            q.ExecuteNonQuery();
        }
        void Within(Action test)
        {
            using var owned=c.BeginTransaction();transaction=owned;
            try{test();}
            finally{try{owned.Rollback();}catch(InvalidOperationException){}transaction=null;}
        }
        decimal Amount(Rate row)=>Convert.ToDecimal(Scalar($"SELECT Amount FROM dbo.FeeConfigurations WHERE FeeConfigurationId={row.Id};"));
        bool Exists(Rate row)=>Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM dbo.FeeConfigurations WHERE FeeConfigurationId={row.Id};"))==1;
        void Refused(DataTable plan,string cleared,int error,string label,int? user=null)
        {
            try{Within(()=>Apply(plan,cleared,user));throw new Exception("Expected refusal: "+label);}
            catch(SqlException ex){Check(error==0||ex.Number==error,label);}
            Check(Exists(first)&&Amount(first)==first.Amount&&Amount(second)==second.Amount,"Rejected plans retain every original rate");
        }
        string[] records={"Students","Enrollments","StudentFeeAssignments","BillingCharges","FeePayments","FeePaymentAllocations"};
        var counts=records.ToDictionary(name=>name,name=>Convert.ToInt64(Scalar($"SELECT COUNT_BIG(*) FROM dbo.[{name}];")));
        Within(()=>{
            Apply(Plan(),Json(first));Check(!Exists(first),"Clearing the only edited cell removes its saved class-rate template");
            using var q=new SqlCommand("dbo.GetFeeConfigurations",c,transaction){CommandType=CommandType.StoredProcedure};
            q.Parameters.AddWithValue("@UserId",actor);q.Parameters.AddWithValue("@CampusId",first.Campus);q.Parameters.AddWithValue("@AllClasses",true);
            using var reader=q.ExecuteReader();bool found=false;while(reader.Read())found|=reader.GetInt32(reader.GetOrdinal("FeeConfigurationId"))==first.Id;
            Check(!found,"Reloading the matrix no longer returns the cleared rate");
        });
        Within(()=>{
            Apply(Plan(second with {Amount=second.Amount+1}),Json(first));
            Check(!Exists(first)&&Amount(second)==second.Amount+1,"Clearing and changing amounts commit as one plan");
            foreach(string name in records)Check(Convert.ToInt64(Scalar($"SELECT COUNT_BIG(*) FROM dbo.[{name}];"))==counts[name],"Class-rate edits retain "+name);
        });
        Within(()=>{
            Apply(Plan(first with {Amount=0}));Check(Exists(first)&&Amount(first)==0,"Legacy callers retain explicit zero as a saved rate");
            Apply(Plan(),Json(first with {Amount=0}));Check(!Exists(first),"An explicit zero rate can also be cleared");
            Apply(Plan(first with {Id=0,Amount=123}));
            Check(Convert.ToDecimal(Scalar($"SELECT Amount FROM dbo.FeeConfigurations WHERE CampusId={first.Campus} AND ClassId={first.Class} AND FeeTypeId={first.Fee};"))==123,
                "A cleared rate can be configured again");
        });
        Refused(Plan(second with {Amount=second.Amount+1}),Json(first with {Amount=first.Amount+1}),53173,"A stale amount refuses the whole plan");
        Refused(Plan(),Json(first with {Frequency="Stale"}),53173,"A stale frequency refuses clearing");
        Refused(Plan(),Json(first with {Remarks=first.Remarks+"stale"}),53173,"Stale remarks refuse clearing");
        Refused(Plan(),Json(first with {Class=-1}),53171,"Invalid class identity refuses clearing");
        Refused(Plan(),Json(first with {Fee=second.Fee==first.Fee?int.MaxValue:second.Fee}),53173,"Mismatched fee identity refuses clearing");
        Refused(Plan(),Json(first,first),53172,"Duplicate clear requests are refused");
        Refused(Plan(first with {Amount=0}),Json(first),53172,"The same rate cannot be updated and cleared together");
        Refused(Plan(second with {Amount=1}),"[null]",53171,"Malformed clear rows refuse all edits");
        Refused(Plan(second with {Amount=1}),Json(first),0,"An unauthorized actor cannot clear class rates",int.MaxValue);
        Within(()=>{
            Execute("CREATE TRIGGER dbo.FMSC_MatrixRejectChange ON dbo.FeeConfigurations AFTER UPDATE AS THROW 51000,'Fixture refusal after clearing.',1;");
            try{Apply(Plan(second with {Amount=second.Amount+1}),Json(first));throw new Exception("Expected fixture update refusal.");}
            catch(SqlException ex){Check(ex.Number==53170,"A write failure rolls back the whole matrix plan");}
            transaction=null;
            Check(Exists(first)&&Amount(second)==second.Amount,"A later update failure restores the earlier cleared rate");
        });
        Check(Exists(first)&&Amount(first)==first.Amount,"SQL cases leave original rate templates intact");
        Console.WriteLine($"PASS: {checks} real SQL matrix clear, zero-rate, stale-plan, permission and rollback checks.");
    }
}
