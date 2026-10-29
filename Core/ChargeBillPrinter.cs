namespace FMSC_SMS.Core;

/// <summary>Prepares posted outstanding bills through the shared reviewed fee-document flow.</summary>
public static class ChargeBillPrinter
{
    public static async Task<string?> PrintAsync(int admissionId,int enrollmentId,string studentName,string classSectionText)
    {
        if(admissionId<=0||enrollmentId<=0)return "Select the student first, then print the bill.";
        await FeeBillPreview.ShowAsync(null,admissionId);
        return null;
    }
}
