using System.Xml.Linq;

namespace FMSC_SMS.Core;

// Applied only to receipt tables carrying explicit grouping metadata.
internal static class CollectionReceiptRdlc
{
    internal static void Apply(XElement table, int[] indices, IReadOnlyList<bool> numeric, int band)
    {
        XNamespace ns = table.Name.Namespace;
        XElement E(string name, params object[] content) => new(ns + name, content);
        var rows = table.Element(ns + "TablixBody")!.Element(ns + "TablixRows")!;
        XElement heading = rows.Elements().First(), detail = rows.Elements().Last();
        string group = "ReceiptGroup" + band;
        XElement Text(string name, string value, bool bold, string alignment = "Left")
        {
            var box = new XElement(heading.Descendants(ns + "Textbox").First());
            box.SetAttributeValue("Name", name);
            box.Descendants(ns + "Value").First().Value = value;
            box.Descendants(ns + "FontWeight").First().Value = bold ? "Bold" : "Normal";
            box.Descendants(ns + "TextAlign").First().Value = alignment;
            return box;
        }
        var groupHeading = E("TablixRow", E("Height", "0.28in"), E("TablixCells",
            E("TablixCell", E("CellContents", Text("ReceiptHeading" + band, "=Fields!GroupLabel.Value", true), E("ColSpan", indices.Length))),
            Enumerable.Range(1, indices.Length - 1).Select(_ => E("TablixCell"))));
        XElement Total(bool grand)
        {
            var cells = indices.Select((index, position) => {
                string value = position == 0 ? (grand ? "Grand Total" : "Subtotal")
                    : numeric[index] ? $"=Format(Sum(Fields!N{index}.Value{(grand ? "" : ", \"" + group + "\"")}), \"#,##0.##\")" : "";
                return E("TablixCell", E("CellContents", Text($"ReceiptTotal{band}_{index}_{grand}", value, true,
                    numeric[index] ? "Right" : "Left")));
            });
            return E("TablixRow", E("Height", "0.28in"), E("TablixCells", cells));
        }
        rows.RemoveNodes();
        rows.Add(heading, groupHeading, detail, Total(false), Total(true));
        var members = table.Element(ns + "TablixRowHierarchy")!.Element(ns + "TablixMembers")!;
        var columnsHeader = new XElement(members.Elements().First());
        var dynamicDetails = new XElement(members.Elements().Last());
        XElement EmptyGroupVisibility() => E("Visibility", E("Hidden", "=Len(Fields!GroupLabel.Value) = 0"));
        members.RemoveNodes();
        members.Add(columnsHeader,
            E("TablixMember", E("Group", new XAttribute("Name", group), E("GroupExpressions", E("GroupExpression", "=Fields!GroupKey.Value"))),
                E("TablixMembers",
                    E("TablixMember", EmptyGroupVisibility(), E("KeepWithGroup", "After"), E("RepeatOnNewPage", true)),
                    dynamicDetails,
                    E("TablixMember", EmptyGroupVisibility(), E("KeepWithGroup", "Before"))),
                E("KeepTogether", false)),
            E("TablixMember", E("KeepWithGroup", "Before")));
    }
}
