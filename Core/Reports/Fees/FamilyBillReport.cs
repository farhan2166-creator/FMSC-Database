namespace FMSC_SMS.Core;

/// <summary>Data and preview entry points for FamilyBill.rdlc.</summary>
internal static partial class FamilyBillReport
{
    internal const string DataSetName="FamilyBillData";
    internal const string TemplateFile="FamilyBill.rdlc";

    internal static async Task ShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,
        string parent,DataTable charges,int primaryAdmission,int[] students)
    {
        using var data=BuildData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
        await FeeBillDocuments.ShowAsync(owner,data,primaryAdmission,students);
    }

    internal static void ShowData(Form? owner,DataTable data)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",TemplateFile);
        RdlcReportPreview.Show(owner,"Family Fee Bill",path,DataSetName,data,
            reportDefinition:FeeDocumentLayout.SizeToContent(path,data));
    }
}
