using System.Xml.Linq;

namespace FMSC_SMS.Core;

/// <summary>Flows complete receipt blocks down the three columns of an A4 sheet.</summary>
internal static class FeeDocumentSheetLayout
{
    internal static void Apply(XDocument document)
    {
        XNamespace ns = document.Root!.Name.Namespace;
        var body = document.Root.Element(ns + "Body")!;
        var table = body.Element(ns + "ReportItems")!.Element(ns + "Tablix")!;
        var rowHierarchy = table.Element(ns + "TablixRowHierarchy")!;
        var receiptMember = rowHierarchy.Element(ns + "TablixMembers")!.Element(ns + "TablixMember")!;
        var outerMember = new XElement(receiptMember);
        outerMember.Element(ns + "TablixMembers")!.Remove();
        outerMember.Descendants(ns + "PageBreak").Remove();

        // The outer group selects a receipt; the inner table renders its rows in that scope.
        // One rectangle per receipt lets the print renderer move the entire block to the
        // next column when it fits there, while oversized receipts can still continue.
        rowHierarchy.ReplaceNodes(new XElement(receiptMember.Element(ns + "TablixMembers")!));
        string dataSetName=table.Element(ns+"DataSetName")!.Value;
        table.Element(ns + "DataSetName")!.Remove();
        table.SetAttributeValue("Name", "AnnualReceiptContents");
        table.SetElementValue(ns + "KeepTogether", true);
        table.SetElementValue(ns + "Top", "0in");
        table.SetElementValue(ns + "Left", "0in");
        string height = table.Element(ns + "Height")!.Value;
        var rectangle = new XElement(ns + "Rectangle", new XAttribute("Name", "AnnualReceiptBlock"),
            new XElement(ns + "ReportItems", new XElement(table)),
            new XElement(ns + "KeepTogether", true), new XElement(ns + "Style"));
        var outer = new XElement(ns + "Tablix", new XAttribute("Name", "AnnualReceiptPages"),
            new XElement(ns + "TablixBody",
                new XElement(ns + "TablixColumns", new XElement(ns + "TablixColumn", new XElement(ns + "Width", "2.35in"))),
                new XElement(ns + "TablixRows", new XElement(ns + "TablixRow",
                    new XElement(ns + "Height", height),
                    new XElement(ns + "TablixCells", new XElement(ns + "TablixCell", new XElement(ns + "CellContents", rectangle)))))),
            new XElement(ns + "TablixColumnHierarchy", new XElement(ns + "TablixMembers", new XElement(ns + "TablixMember"))),
            new XElement(ns + "TablixRowHierarchy", new XElement(ns + "TablixMembers", outerMember)),
            new XElement(ns + "KeepTogether", false),
            new XElement(ns + "DataSetName", dataSetName),
            new XElement(ns + "Height", height), new XElement(ns + "Width", "2.35in"), new XElement(ns + "Style"));
        table.ReplaceWith(outer);
        document.Root.Element(ns + "Width")!.Value = "2.35in";
        var page = document.Root.Element(ns + "Page")!;
        page.SetElementValue(ns + "PageWidth", "8.27in");
        page.SetElementValue(ns + "PageHeight", "11.69in");
        foreach (string margin in new[] { "LeftMargin", "RightMargin", "TopMargin", "BottomMargin" })
            page.SetElementValue(ns + margin, "0.4in");
        page.Elements(ns + "Columns").Remove();
        page.Elements(ns + "ColumnSpacing").Remove();
        page.Element(ns + "Style")!.AddBeforeSelf(new XElement(ns + "Columns", 3), new XElement(ns + "ColumnSpacing", "0.2in"));
    }
}
