using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal static class TransportServiceStore
{
    internal static Task<DataSet> ReadAsync(int enrollment,bool start)=>DatabaseHelper.QueryUserSetAsync("ManageStudentTransport",
        DatabaseHelper.Int("@EnrollmentId",enrollment),DatabaseHelper.Bit("@IsStart",start));
    internal static Task<DataTable> SaveAsync(int enrollment,bool start,int fee,DateTime month,string reason,string snapshot,Guid request)=>
        DatabaseHelper.QueryUserAsync("ManageStudentTransport",DatabaseHelper.Int("@EnrollmentId",enrollment),DatabaseHelper.Bit("@IsStart",start),
            DatabaseHelper.Int("@FeeTypeId",fee),DatabaseHelper.Date("@FromMonth",month),DatabaseHelper.P("@Reason",reason.Trim()),
            DatabaseHelper.Bit("@Preview",false),DatabaseHelper.P("@ExpectedSnapshot",snapshot),DatabaseHelper.P("@RequestId",request));
    internal static bool IsBusinessRejection(Exception ex)=>ex is SqlException sql&&sql.Number is >=50000 and <60000;
    internal static async Task<bool> WasSavedAsync(int enrollment,Guid request)
    {
        using var result=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
            SET XACT_ABORT ON;BEGIN TRY BEGIN TRAN;
            DECLARE @Lock int;
            EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
            IF @Lock<0 THROW 53140,'Transport is still being saved. Check again shortly.',1;
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.TransportServiceRequests WHERE RequestId=@RequestId AND UserId=@UserId AND EnrollmentId=@EnrollmentId) THEN 1 ELSE 0 END) Saved;
            COMMIT;END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",enrollment),DatabaseHelper.P("@RequestId",request));
        return result.Rows.Count==1&&Convert.ToBoolean(result.Rows[0]["Saved"]);
    }
}
