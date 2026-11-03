using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckSavedOrderAliases()
    {
        foreach(string view in new[]{"Receipts","Summary"})
        foreach(bool descending in new[]{true,false})
        {
            var choices=ReportRowOrder.Choices("GetCollectionsAndReceiptsReport",view);
            string old="Money received: "+(descending?"highest first":"lowest first");
            var restored=ReportRowOrder.FindSavedChoice(choices,old);
            Check(restored is not null&&restored.Column=="Money Received"&&restored.Descending==descending
                &&restored.Label.StartsWith("Payment received:"),view+": old saved captions retain the actual amount column and direction");
            Check(ReferenceEquals(restored,ReportRowOrder.FindSavedChoice(choices,restored!.Label)),view+": current captions resolve to the same choice");
        }
        Check(ReportRowOrder.FindSavedChoice(ReportRowOrder.Choices("GetCollectionsAndReceiptsReport","Detailed Transactions"),
            "Money received: highest first") is null,"Legacy captions cannot select a different financial column in another view");
        Check(ReportRowOrder.FindSavedChoice(ReportRowOrder.Choices("GetCollectionsAndReceiptsReport","Receipts"),
            "Unavailable: highest first") is null,"Unrecognised saved orderings remain rejected");
    }
}
