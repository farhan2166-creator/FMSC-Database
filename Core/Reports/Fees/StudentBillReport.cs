namespace FMSC_SMS.Core;

/// <summary>Data and preview entry points for StudentBill.rdlc.</summary>
internal static partial class StudentBillReport
{
    internal const string DataSetName="StudentBillData";
    internal const string TemplateFile="StudentBill.rdlc";

    internal static async Task ShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,
        string studentName,int admissionId,string classSection,IReadOnlyList<Line> lines,bool showDiscount,
        string campusName="",string fatherName="",string sourceKey="")
    {
        using var data=BuildData(institution.Name,institution.ContactNumber,institution.Address,studentName,AdmissionNumbers.ForId(admissionId),classSection,
            lines,showDiscount,campusName,fatherName,sourceKey);
        await FeeBillDocuments.ShowAsync(owner,data,admissionId,new[]{admissionId});
    }

    internal static async Task ShowChargesAsync(Form? owner,InstitutionProfile.PrintIdentity institution,
        string parent,DataTable charges,int admissionId)
    {
        using var data=BuildData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
        await FeeBillDocuments.ShowAsync(owner,data,admissionId,new[]{admissionId});
    }

    internal static void ShowData(Form? owner,DataTable data,bool a4=false)
    {
        using var display=FeeBillPresentation.Prepare(data);
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",TemplateFile);
        if(!a4){FeeBillRollPreview.Show(owner,"Student Fee Bill",path,DataSetName,display);return;}
        RdlcReportPreview.Show(owner,"Student Fee Bill",path,DataSetName,display,
            fitPageWidth:a4,reportDefinition:FeeDocumentLayout.SizeToContent(path,display,a4));
    }
}
