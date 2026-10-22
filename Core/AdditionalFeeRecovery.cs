using Microsoft.Data.SqlClient;
using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Core;

// Both additional-fee entry points share one durable, database-scoped intent.
// An uncertain request is checked read-only, never resubmitted automatically.
internal sealed class AdditionalFeeRecovery
{
    private readonly string? path;
    private readonly Func<Task<string>> scope;
    private readonly Func<MutationIntent,SqlParameter[],Task<int>> save;
    private readonly Func<MutationIntent,Task<int?>> check;
    internal MutationIntent? Pending { get; private set; }
    internal bool Blocked { get; private set; }
    internal AdditionalFeeRecovery(string? path=null,Func<Task<string>>? scope=null,
        Func<MutationIntent,SqlParameter[],Task<int>>? save=null,Func<MutationIntent,Task<int?>>? check=null)
    {
        this.path=path;this.scope=scope??RecoverableMutationWriter.GetScopeAsync;
        this.save=save??((intent,args)=>AdditionalFeePostingWriter.SaveAsync(intent,args));
        this.check=check??AdditionalFeePostingWriter.CheckAsync;
    }
    internal void Load()
    {
        Blocked=true;
        Pending=MutationRecoveryJournal.ReadPending(MutationKind.AdditionalFeePosting,path);
        Blocked=Pending is not null;
    }
    internal async Task<int> SaveAsync(SqlParameter[] args,string summary)
    {
        Load();
        if(Blocked)throw new InvalidOperationException("Check the earlier additional-fee save outcome before posting again.");
        int user=CurrentUser.UserId;
        string database=await scope();
        int count;
        Blocked=true;
        using(var journal=MutationRecoveryJournal.Begin(MutationKind.AdditionalFeePosting,user,database,
            RecoverableMutationWriter.PayloadHash(MutationKind.AdditionalFeePosting,user,args),summary,path))
        {
            Pending=journal.Intent;Blocked=true;
            count=await save(Pending,args);
        }
        MutationRecoveryJournal.ClearVerified(Pending!,path);Pending=null;Blocked=false;
        return count;
    }
    internal async Task<int?> CheckAsync()
    {
        Load();if(Pending is null)return null;
        int count=await check(Pending)??0;
        MutationRecoveryJournal.ClearVerified(Pending,path);Pending=null;Blocked=false;
        return count;
    }
}

internal static class AdditionalFeePostingWriter
{
    private static decimal Money(decimal value)=>decimal.Parse(value.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),System.Globalization.CultureInfo.InvariantCulture);
    internal static SqlParameter[] Parameters(int fee,DateTime date,decimal amount,decimal discount,string reason,int[] enrollments)=>
        new[]{DatabaseHelper.Int("@FeeTypeId",fee),DatabaseHelper.Date("@ChargeDate",DateTime.SpecifyKind(date.Date,DateTimeKind.Unspecified)),DatabaseHelper.P("@Amount",Money(amount)),
            DatabaseHelper.P("@DiscountAmount",Money(discount)),DatabaseHelper.P("@Reason",reason),
            new SqlParameter("@Selection",SqlDbType.Structured){TypeName="dbo.ManualChargeSelectionType",Value=Tvp.ManualChargeSelection(enrollments.OrderBy(id=>id))}};

    private static void Validate(MutationIntent intent)
    {
        MutationRecoveryJournal.Validate(intent);
        if(intent.Kind!=MutationKind.AdditionalFeePosting)throw new InvalidDataException("This recovery record is not an additional-fee posting.");
    }
    private static async Task CheckScopeAsync(MutationIntent intent)
    {
        Validate(intent);
        if(await RecoverableMutationWriter.GetScopeAsync()!=intent.Scope)
            throw new InvalidOperationException("This pending posting belongs to a different database. Reconnect to its original database before checking it.");
    }
    private static void Lock(SqlConnection c,SqlTransaction t,MutationIntent intent,int user,bool read)
    {
        using var q=new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,@Permission;
            IF @OtherOwner=1 EXEC dbo.RequirePermission @UserId,N'System.Administration';
            IF TRY_CONVERT(uniqueidentifier,(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope')) IS NULL
                THROW 57610,'The database recovery identity is unavailable.',1;
            DECLARE @Resource nvarchar(255)=N'FMSC.AdditionalFee.'+CONVERT(nvarchar(36),@Request),@Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode=@Mode,@LockOwner=N'Transaction',@LockTimeout=8000;
            IF @Result<0 THROW 57610,'This posting is still being processed. Wait, then check its outcome again.',1;
            """,c,t){CommandTimeout=15};
        q.Parameters.AddRange(new[]{DatabaseHelper.Int("@UserId",user),DatabaseHelper.P("@Permission",read?"Fees.View":"Fees.Collect"),
            DatabaseHelper.Bit("@OtherOwner",intent.UserId!=user),new SqlParameter("@Request",SqlDbType.UniqueIdentifier){Value=intent.Id},DatabaseHelper.P("@Mode",read?"Shared":"Exclusive")});
        q.ExecuteNonQuery();
        using var identity=new SqlCommand("SELECT TRY_CONVERT(uniqueidentifier,value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope';",c,t);
        if(identity.ExecuteScalar() is not Guid id || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(id.ToByteArray()))!=intent.Scope)
            throw new InvalidOperationException("The posting database changed. Reconnect to the original database.");
    }
    internal static async Task<int> SaveAsync(MutationIntent intent,SqlParameter[] args,Action<SqlTransaction>? commit=null)
    {
        Validate(intent);int user=CurrentUser.UserId;
        if(user!=intent.UserId || RecoverableMutationWriter.PayloadHash(intent.Kind,user,args)!=intent.PayloadHash)
            throw new InvalidOperationException("The operator or submitted details changed. Check the original save outcome.");
        await CheckScopeAsync(intent);
        return await Task.Run(()=>{
            using var c=DatabaseHelper.GetConnection();DatabaseHelper.OpenConnection(c);
            using var t=c.BeginTransaction();Lock(c,t,intent,user,false);
            using var q=new SqlCommand("dbo.PostAdditionalFee",c,t){CommandType=CommandType.StoredProcedure,CommandTimeout=120};
            q.Parameters.AddRange(args.Select(p=>(SqlParameter)((ICloneable)p).Clone()).ToArray());
            q.Parameters.Add(DatabaseHelper.Int("@UserId",user));q.Parameters.Add(new SqlParameter("@RequestId",SqlDbType.UniqueIdentifier){Value=intent.Id});
            using var rows=new DataTable();using(var adapter=new SqlDataAdapter(q))adapter.Fill(rows);
            var expected=((DataTable)args.Single(p=>p.ParameterName=="@Selection").Value).AsEnumerable().Select(r=>Convert.ToInt32(r["EnrollmentId"])).OrderBy(id=>id);
            if(!rows.AsEnumerable().Select(r=>Convert.ToInt32(r["EnrollmentId"])).OrderBy(id=>id).SequenceEqual(expected))
                throw new InvalidDataException("The saved posting did not identify every selected enrollment.");
            if(commit is null)t.Commit();else commit(t);
            return rows.Rows.Count;
        });
    }
    internal static async Task<int?> CheckAsync(MutationIntent intent)
    {
        await CheckScopeAsync(intent);int user=CurrentUser.UserId;
        return await Task.Run<int?>(()=>{
            using var c=DatabaseHelper.GetConnection();DatabaseHelper.OpenConnection(c);
            using var t=c.BeginTransaction();Lock(c,t,intent,user,true);
            using var q=new SqlCommand("""
                SELECT FeeTypeId,ChargeDate,Amount,DiscountAmount,Reason FROM dbo.ManualFeePostingRequests WHERE RequestId=@Request;
                SELECT EnrollmentId FROM dbo.BillingCharges WHERE ManualRequestId=@Request ORDER BY EnrollmentId;
                """,c,t);
            q.Parameters.Add(new SqlParameter("@Request",SqlDbType.UniqueIdentifier){Value=intent.Id});
            using var data=new DataSet();using(var adapter=new SqlDataAdapter(q))adapter.Fill(data);
            if(data.Tables[0].Rows.Count==0)
            {
                if(data.Tables[1].Rows.Count!=0)throw new InvalidDataException("Posted charges have no matching request record. Retain recovery for administrator review.");
                t.Commit();return null;
            }
            var row=data.Tables[0].Rows[0];
            int[] ids=data.Tables[1].AsEnumerable().Select(r=>Convert.ToInt32(r["EnrollmentId"])).ToArray();
            var args=Parameters(Convert.ToInt32(row["FeeTypeId"]),Convert.ToDateTime(row["ChargeDate"]),Convert.ToDecimal(row["Amount"]),Convert.ToDecimal(row["DiscountAmount"]),Convert.ToString(row["Reason"])!,ids);
            if(ids.Length==0 || RecoverableMutationWriter.PayloadHash(intent.Kind,intent.UserId,args)!=intent.PayloadHash)
                throw new InvalidDataException("The saved posting does not match the submitted details. Retain recovery for administrator review.");
            t.Commit();return ids.Length;
        });
    }
}
