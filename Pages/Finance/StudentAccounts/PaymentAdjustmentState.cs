namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal sealed record PaymentAdjustmentState(string Status, decimal Remaining, string[] Actions, string Explanation)
{
    internal static PaymentAdjustmentState Create(string status,string method,decimal original,bool adjustmentEntry,decimal refunded,decimal corrected,bool creditEntry=false,decimal releasedToCredit=0m)
    {
        if(creditEntry) return new("Student Credit",0m,Array.Empty<string>(),
            "This entry belongs to Student Credit. Use the credit account or bill correction workflow; it is not a new cash collection.");
        if(adjustmentEntry) return new(method=="Refund" ? "Refund" : "Correction",0m,Array.Empty<string>(),
            "This is an adjustment entry. Select its original receipt to make another adjustment.");
        if(status=="Reversed") return new("Reversed",0m,Array.Empty<string>(),"Already reversed. This payment cannot be adjusted again.");
        decimal remaining=Math.Max(0m,original-refunded-corrected-releasedToCredit);
        if(remaining<=0m || status!="Completed") return new("Fully Adjusted",0m,Array.Empty<string>(),"No collected amount remains to adjust.");
        if(refunded>0m || corrected>0m || releasedToCredit>0m) return new(releasedToCredit>0m ? "Released to Credit" : refunded>0m && corrected>0m ? "Refunded / Corrected" : refunded>0m ? "Partially Refunded" : "Corrected",
            remaining,new[]{"Correction","Refund"},"Reversal is unavailable after a refund or payment reduction. Use Reduce Recorded Payment with 0 to remove the remaining collection and restore it to outstanding dues.");
        return new("Collected",remaining,new[]{"Correction","Refund","Reversal"},string.Empty);
    }
}
