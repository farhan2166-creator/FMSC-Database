namespace FMSC_SMS.Core;

/// <summary>Data and preview entry points for FamilyReceipt.rdlc.</summary>
internal static partial class FamilyReceiptReport
{
    internal const string DataSetName="FamilyReceiptData";
    internal const string TemplateFile="FamilyReceipt.rdlc";

    internal static void Show(Form? owner,DataTable source)
    {
        using var data=PrepareData(source);
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",TemplateFile);
        RdlcReportPreview.Show(owner,"Family Payment Receipt",path,DataSetName,data,
            reportDefinition:FeeDocumentLayout.SizeToContent(path,data));
    }
}
