using System.Xml.Linq;

namespace FMSC_SMS.Core;

internal static class FeeReminderPrintLayout
{
    internal const int ThermalBatchLimit = 10;

    internal static bool UseA4(int formatIndex, int selectedCount) =>
        formatIndex == 2 || (formatIndex == 0 && selectedCount > ThermalBatchLimit);

    internal static string A4Definition(string path)
    {
        var document = XDocument.Load(path);
        XNamespace ns = document.Root!.Name.Namespace;
        var footer = document.Descendants(ns + "Tablix").First().Descendants(ns + "Textbox").Last();
        footer.Element(ns + "Style")!.SetElementValue(ns + "PaddingBottom", "8pt");
        AnnualReceiptReportLayout.Apply(document);
        return document.ToString();
    }
}
