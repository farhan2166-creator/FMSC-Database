using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Core;

/// <summary>One existing business procedure and its request marker commit together.</summary>
internal static class RecoverableMutationWriter
{
    internal static string PayloadHash(MutationKind kind,int user,params SqlParameter[] parameters)
    {
        var values=parameters.Where(p=>!p.ParameterName.Equals("@UserId",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.ParameterName,StringComparer.Ordinal)
            .Select(p=>new{Name=p.ParameterName,Value=p.Value is DataTable table?(object)new{Columns=table.Columns.Cast<DataColumn>().Select(c=>c.ColumnName).ToArray(),Rows=table.Rows.Cast<DataRow>().Select(r=>r.ItemArray.Select(v=>v is DBNull?null:v).ToArray()).ToArray()}:p.Value is DBNull?null:p.Value});
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Kind=kind,User=user,Values=values}))));
    }
    private static string Scope(SqlConnection connection)
    {
        using var command=new SqlCommand("SELECT TRY_CONVERT(uniqueidentifier,value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope';",connection);
        if(command.ExecuteScalar() is not Guid scope||scope==Guid.Empty)throw new InvalidDataException("The database recovery identity is missing. Verify its migrations before saving.");
        return Convert.ToHexString(SHA256.HashData(scope.ToByteArray()));
    }
    internal static Task<string> GetScopeAsync()=>Task.Run(()=>{using var c=DatabaseHelper.GetConnection();DatabaseHelper.OpenConnection(c);return Scope(c);});
    private static void Permission(SqlConnection c,SqlTransaction t,int user,MutationKind kind,bool write,bool otherOwner=false)
    {
        using var q=new SqlCommand("EXEC dbo.RequirePermission @UserId,@Permission;"+(otherOwner?" EXEC dbo.RequirePermission @UserId,N'System.Administration';":""),c,t);
        q.Parameters.AddRange(new[]{DatabaseHelper.Int("@UserId",user),DatabaseHelper.P("@Permission",kind==MutationKind.BankCertificateCreate?"Certificates.Create":write?"Fees.Adjust":"Fees.View")});q.ExecuteNonQuery();
    }
    private static void Lock(SqlConnection c,SqlTransaction t,Guid id,bool read)
    {
        using var q=new SqlCommand("DECLARE @Result int; EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode=@Mode,@LockOwner=N'Transaction',@LockTimeout=8000; IF @Result<0 THROW 57610,'The operation is still being processed. Wait, then check its outcome again.',1;",c,t){CommandTimeout=12};
        q.Parameters.AddRange(new[]{DatabaseHelper.P("@Resource","FMSC.Mutation."+id.ToString("D")),DatabaseHelper.P("@Mode",read?"Shared":"Exclusive")});q.ExecuteNonQuery();
    }
    private static MutationResult? ReadMarker(SqlConnection c,SqlTransaction t,MutationIntent intent)
    {
        using var q=new SqlCommand("SELECT UserId,Kind,PayloadHash,ResultJson FROM dbo.MutationRequests WHERE RequestId=@Request;",c,t);q.Parameters.Add(new SqlParameter("@Request",SqlDbType.UniqueIdentifier){Value=intent.Id});using var r=q.ExecuteReader();
        if(!r.Read())return null;
        if(r.GetInt32(0)!=intent.UserId||r.GetString(1)!=intent.Kind.ToString()||!Convert.ToHexString(r.GetFieldValue<byte[]>(2)).Equals(intent.PayloadHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This request belongs to different submitted details. Keep recovery and check the original outcome; do not submit it again.");
        var result=JsonSerializer.Deserialize<MutationResult>(r.GetString(3))??throw new InvalidDataException("The saved outcome is invalid.");result.Validate();if(result.Kind!=intent.Kind)throw new InvalidDataException("The saved operation type is invalid.");return result;
    }
    internal static Task<MutationResult?> CheckAsync(MutationIntent intent)
    {
        MutationRecoveryJournal.Validate(intent);int user=CurrentUser.UserId;
        return Task.Run(()=>{
            using var c=DatabaseHelper.GetConnection();DatabaseHelper.OpenConnection(c);if(Scope(c)!=intent.Scope)throw new InvalidOperationException("This pending operation belongs to a different database. Reconnect to its original database before checking it.");
            using var t=c.BeginTransaction(IsolationLevel.Serializable);Permission(c,t,user,intent.Kind,false,intent.UserId!=user);Lock(c,t,intent.Id,true);var result=ReadMarker(c,t,intent);t.Commit();return result;
        });
    }
    internal static Task<MutationWriteResult> SaveAsync(MutationIntent intent,SqlParameter[] parameters,Action<SqlTransaction>? commit=null)
    {
        MutationRecoveryJournal.Validate(intent);int user=CurrentUser.UserId;
        if(intent.UserId!=user||!intent.PayloadHash.Equals(PayloadHash(intent.Kind,user,parameters),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The submitted request or signed-in operator changed. Check its outcome before submitting anything again.");
        return Task.Run(()=>{
            try
            {
                using var c=DatabaseHelper.GetConnection();DatabaseHelper.OpenConnection(c);if(Scope(c)!=intent.Scope)throw new InvalidOperationException("The operation database changed before submission.");
                using var t=c.BeginTransaction(IsolationLevel.Serializable);var state=new Transaction(t,commit);
                return MutationSaveCoordinator.Execute(state,()=>{
                    Permission(c,t,user,intent.Kind,true);Lock(c,t,intent.Id,false);if(ReadMarker(c,t,intent) is {} prior)return prior;
                    string procedure=intent.Kind switch {
                        MutationKind.FeeAdjustment=>"ProcessFeeTransaction",
                        MutationKind.FeeConcession=>"SaveStudentFeeDiscount",
                        MutationKind.BankCertificateCreate=>"CreateBankAccountOpeningCertificate",
                        _=>throw new InvalidOperationException("Unsupported operation.") };
                    using var command=new SqlCommand(procedure,c,t){CommandType=CommandType.StoredProcedure,CommandTimeout=120};
                    command.Parameters.AddRange(parameters.Where(p=>!p.ParameterName.Equals("@UserId",StringComparison.OrdinalIgnoreCase)).Select(p=>(SqlParameter)((ICloneable)p).Clone()).ToArray());command.Parameters.Add(DatabaseHelper.Int("@UserId",user));
                    state.SubmissionStarted=true;var data=new DataTable();using(var adapter=new SqlDataAdapter(command))adapter.Fill(data);
                    var saved=Response(intent.Kind,parameters,data);saved.Validate();
                    using var marker=new SqlCommand("INSERT dbo.MutationRequests(RequestId,UserId,Kind,PayloadHash,FeePaymentId,BankCertificateId,ResultJson) VALUES(@Request,@User,@Kind,@Hash,@Payment,@Certificate,@Json);",c,t);
                    marker.Parameters.AddRange(new[]{new SqlParameter("@Request",SqlDbType.UniqueIdentifier){Value=intent.Id},DatabaseHelper.Int("@User",user),DatabaseHelper.P("@Kind",intent.Kind.ToString()),new SqlParameter("@Hash",SqlDbType.Binary,32){Value=Convert.FromHexString(intent.PayloadHash)},DatabaseHelper.P("@Payment",intent.Kind==MutationKind.FeeAdjustment?saved.RecordId:null),DatabaseHelper.P("@Certificate",intent.Kind==MutationKind.BankCertificateCreate?saved.RecordId:null),DatabaseHelper.P("@Json",JsonSerializer.Serialize(saved))});marker.ExecuteNonQuery();return saved;
                });
            }
            catch(Exception ex){return new MutationWriteResult(MutationOutcome.Rejected,Error:ex);}
        });
    }
    private static MutationResult Response(MutationKind kind,SqlParameter[] args,DataTable data)
    {
        if(data.Rows.Count!=1)throw new InvalidDataException("The operation did not return one authoritative saved result.");var row=data.Rows[0];
        object? Arg(string name)=>args.First(p=>p.ParameterName.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
        int Key(string column)=>row.Table.Columns.Contains(column)&&row[column] is int id&&id>0?id:throw new InvalidDataException("The saved operation identity is invalid.");
        if(kind==MutationKind.FeeConcession)
        {
            int assignment=Convert.ToInt32(Arg("@StudentFeeAssignmentId"));
            if(Key("StudentFeeAssignmentId")!=assignment||Convert.ToDateTime(row["EffectiveFrom"])!=Convert.ToDateTime(Arg("@EffectiveFrom"))||
                Convert.ToDateTime(row["EffectiveThrough"])!=Convert.ToDateTime(Arg("@EffectiveThrough")))
                throw new InvalidDataException("The saved concession does not match the reviewed assignment and period.");
            return new(kind,Key("AdmissionId"),assignment,assignment,"","FeeConcession",Convert.ToDecimal(row["Difference"]),null) {
                EffectiveFrom=Convert.ToDateTime(row["EffectiveFrom"]),EffectiveThrough=Convert.ToDateTime(row["EffectiveThrough"]),
                ReleasedPaidAmount=Convert.ToDecimal(row["ReleasedPaidAmount"]) };
        }
        if(kind==MutationKind.BankCertificateCreate){int staff=Convert.ToInt32(Arg("@StaffId"));if(Key("StaffId")!=staff)throw new InvalidDataException("The saved certificate belongs to a different staff member.");return new(kind,staff,0,Key("BankAccountOpeningCertificateId"),"","BankCertificateCreate",Arg("@MonthlyIncome") is null or DBNull?0m:Convert.ToDecimal(Arg("@MonthlyIncome")),null);}
        string type=Convert.ToString(Arg("@TransactionType"))??"";decimal amount=Convert.ToDecimal(Arg("@Amount"));if(Convert.ToString(row["TransactionType"])!=type||Convert.ToDecimal(row["Amount"])!=amount)throw new InvalidDataException("The saved adjustment does not match the submitted operation.");
        int origin=Convert.ToInt32(Arg("@FeePaymentId")),resultId=type=="Reversal"?origin:Key("FeePaymentId");
        return new(kind,Convert.ToInt32(Arg("@AdmissionId")),origin,resultId,Convert.ToString(row["ReceiptNumber"])??"",type,amount,row.IsNull("Balance")?null:Convert.ToDecimal(row["Balance"]));
    }
    private sealed class Transaction(SqlTransaction transaction,Action<SqlTransaction>? commit):IMutationTransaction
    {
        public bool SubmissionStarted{get;set;}
        public void Commit(){if(commit is null)transaction.Commit();else commit(transaction);}
        public void Rollback()=>transaction.Rollback();
    }
}
