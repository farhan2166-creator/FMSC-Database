namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal sealed record PaymentAdjustmentState(string Status, decimal Remaining, string[] Actions, string Explanation)
{
    internal static PaymentAdjustmentState Create(string status,string method,decimal original,bool adjustmentEntry,decimal refunded,decimal corrected)
    {
        if(adjustmentEntry) return new(method=="Refund" ? "Refund" : "Correction",0m,Array.Empty<string>(),
            "This is an adjustment entry. Select its original receipt to make another adjustment.");
        if(status=="Reversed") return new("Reversed",0m,Array.Empty<string>(),"Already reversed. This payment cannot be adjusted again.");
        decimal remaining=Math.Max(0m,original-refunded-corrected);
        if(remaining<=0m || status!="Completed") return new("Fully Adjusted",0m,Array.Empty<string>(),"No collected amount remains to adjust.");
        if(refunded>0m || corrected>0m) return new(refunded>0m && corrected>0m ? "Refunded / Corrected" : refunded>0m ? "Partially Refunded" : "Corrected",
            remaining,new[]{"Correction","Refund"},"Reversal is unavailable after a refund or correction. Correct to 0 to remove the remaining collection.");
        return new("Collected",remaining,new[]{"Correction","Refund","Reversal"},string.Empty);
    }
}
