using System.Xml.Linq;

namespace FMSC_SMS.Core;

internal static class FeeReminderPrintLayout
{
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
