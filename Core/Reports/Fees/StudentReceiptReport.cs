namespace FMSC_SMS.Core;

/// <summary>Data and preview entry points for StudentReceipt.rdlc, including annual sheets.</summary>
internal static partial class StudentReceiptReport
{
    internal const string DataSetName="StudentReceiptData";
    internal const string TemplateFile="StudentReceipt.rdlc";

    internal static void Show(Form? owner,DataTable source,bool annualSheet=false)
    {
        using var data=source.Copy();
        FeeDocumentData.GroupPeriods(data);
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",TemplateFile);
        RdlcReportPreview.Show(owner,annualSheet?"Student Annual Fee Receipts":"Student Payment Receipt",
            path,DataSetName,data,fitPageWidth:annualSheet,
            reportDefinition:FeeDocumentLayout.SizeToContent(path,data,annualSheet));
    }
}
