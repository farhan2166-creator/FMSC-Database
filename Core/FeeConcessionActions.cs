using FMSC_SMS.Pages.Finance.StudentAccounts;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

internal static class FeeConcessionActions
{
    internal const string WaiverReason="One-time fee waiver from Fee Collection";
    internal static SqlParameter[] WaiverArgs(DataRow context,bool preview,string? snapshot=null)
    {
        var month=Convert.ToDateTime(context["BillingMonth"]);
        month=new DateTime(month.Year,month.Month,1);
        return new[]{DatabaseHelper.Int("@StudentFeeAssignmentId",Convert.ToInt32(context["StudentFeeAssignmentId"])),
            DatabaseHelper.P("@DiscountAmount",0m),DatabaseHelper.P("@ConcessionLabel","Waived"),
            DatabaseHelper.Date("@EffectiveFrom",month),DatabaseHelper.Date("@EffectiveThrough",month),
            DatabaseHelper.P("@NewAmount",null),DatabaseHelper.Bit("@ApplyAvailableCredit",false),
            DatabaseHelper.Bit("@UpdateAgreement",false),DatabaseHelper.P("@BillingChargeId",Convert.ToInt64(context["BillingChargeId"])),
            DatabaseHelper.Bit("@FullFeeWaiver",true),DatabaseHelper.Bit("@ApplyToBills",true),
            DatabaseHelper.P("@PaidReductionAction","Credit"),DatabaseHelper.P("@Reason",WaiverReason),
            DatabaseHelper.Bit("@Preview",preview),DatabaseHelper.P("@ExpectedSnapshot",snapshot)};
    }
    internal static async Task WaiveOnceAsync(long chargeId)
    {
        if(MutationRecoveryJournal.ReadPending(MutationKind.FeeConcession) is not null)
            throw new InvalidOperationException("An earlier fee change needs checking. Open Review bill terms and use Check Save Outcome first.");
        int actor=CurrentUser.UserId;
        var context=await Task.Run(()=>FeeDiscountPeriod.BillContext(chargeId));
        using var review=await DatabaseHelper.QueryUserSetAsync("SaveStudentFeeDiscount",WaiverArgs(context,true));
        if(review.Tables.Count<3 || review.Tables[0].Rows.Count!=1 || review.Tables[1].Rows.Count!=1)
            throw new InvalidOperationException("This charge could not be verified. Reload the fees.");
        var result=review.Tables[0].Rows[0];
        if(Convert.ToDecimal(result["ReleasedPaidAmount"])!=0m || Convert.ToInt32(result["IssuedBills"])!=1)
            throw new InvalidOperationException("The waiver must affect only this charge's unpaid dues. Reload before trying again.");
        if(Convert.ToDecimal(result["Difference"])>=0m)return;
        var args=WaiverArgs(context,false,Convert.ToString(result["Snapshot"]));
        string scope=await RecoverableMutationWriter.GetScopeAsync();
        if(CurrentUser.UserId!=actor)throw new InvalidOperationException("The operator changed. Reload the fees before saving.");
        using var journal=MutationRecoveryJournal.Begin(MutationKind.FeeConcession,actor,scope,
            RecoverableMutationWriter.PayloadHash(MutationKind.FeeConcession,actor,args),$"One-time waiver for charge {chargeId}");
        var saved=await RecoverableMutationWriter.SaveAsync(journal.Intent,args);
        if(saved.Outcome==MutationOutcome.Unconfirmed)
            throw new InvalidOperationException("The waiver save needs checking. Open Review bill terms and use Check Save Outcome before repeating it.");
        journal.Dispose();
        MutationRecoveryJournal.ClearVerified(journal.Intent);
        if(saved.Outcome==MutationOutcome.Rejected)throw saved.Error??new InvalidOperationException("The waiver was not saved.");
        LookupDataCache.OperationCompleted("SaveStudentFeeDiscount");
    }
}
