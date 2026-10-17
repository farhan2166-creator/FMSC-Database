using System.Globalization;
using System.Xml.Linq;

namespace FMSC_SMS.Core;

internal static class ProvisionalSubjectLayout
{
    internal static void Apply(XDocument document, string subjectLine)
        => Apply(document, subjectLine.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    internal static void Apply(XDocument document, IReadOnlyList<string> subjectNames)
    {
        XNamespace ns = document.Root!.Name.Namespace;
        var body = document.Root.Element(ns + "Body")!;
        var items = body.Element(ns + "ReportItems")!;
        var prototype = items.Elements(ns + "Textbox").Single(item => (string?)item.Attribute("Name") == "txtSubjectsPassed");
        double top = Inches(prototype.Element(ns + "Top"));
        double left = Inches(prototype.Element(ns + "Left"));
        double width = Inches(prototype.Element(ns + "Width"));
        double originalHeight = Inches(prototype.Element(ns + "Height"));
        string[] subjects = subjectNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToArray();
        const double gutter = 0.18;
        using var font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Point);
        using var measurementSurface = new Bitmap(1, 1);
        measurementSurface.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(measurementSurface);
        const int columns = 4;
        double columnWidth = (width - (columns - 1) * gutter) / columns;
        double rowTop = top;
        for (int start = 0; start < subjects.Length; start += columns)
        {
            int count = Math.Min(columns, subjects.Length - start);
            double rowHeight = 0.3;
            for (int column = 0; column < count; column++)
            {
                var measured = graphics.MeasureString(subjects[start + column], font,
                    new SizeF((float)(columnWidth * 96) - 8, 10000), StringFormat.GenericTypographic);
                rowHeight = Math.Max(rowHeight, (measured.Height + 8) / 96.0);
            }
            for (int column = 0; column < count; column++)
            {
                var box = new XElement(prototype);
                box.SetAttributeValue("Name", "txtSubject" + (start + column + 1));
                string subject = subjects[start + column];
                box.Descendants(ns + "Value").Single().Value = subject.StartsWith('=')
                    ? "=\"" + subject.Replace("\"", "\"\"") + "\"" : subject;
                Set(box, ns, "Top", rowTop);
                Set(box, ns, "Left", left + column * (columnWidth + gutter));
                Set(box, ns, "Width", columnWidth);
                Set(box, ns, "Height", rowHeight);
                foreach (var alignment in box.Descendants(ns + "TextAlign")) alignment.Value = "Left";
                var style = box.Element(ns + "Style")!;
                foreach (string padding in new[] { "PaddingLeft", "PaddingRight", "PaddingTop", "PaddingBottom" })
                    style.SetElementValue(ns + padding, "0pt");
                style.SetElementValue(ns + "VerticalAlign", "Middle");
                items.Add(box);
            }
            rowTop += rowHeight + 0.08;
        }
        prototype.Remove();
        // Keep the date/signature clear when the subject list needs more than its original space.
        var following = items.Elements().Where(item =>
            !((string?)item.Attribute("Name") ?? string.Empty).StartsWith("txtSubject", StringComparison.Ordinal) &&
            Inches(item.Element(ns + "Top")) >= top + originalHeight).ToArray();
        double nextTop = following.Select(item => Inches(item.Element(ns + "Top"))).DefaultIfEmpty(top + originalHeight).Min();
        double shift = Math.Max(0, rowTop - nextTop + 0.12);
        if (shift > 0)
        {
            foreach (var item in following)
                Set(item, ns, "Top", Inches(item.Element(ns + "Top")) + shift);
            double bottom = items.Elements().Max(item => Inches(item.Element(ns + "Top")) + Inches(item.Element(ns + "Height")));
            Set(body, ns, "Height", Math.Max(Inches(body.Element(ns + "Height")), bottom + 0.05));
        }
    }

    private static double Inches(XElement? element) => element is null ? 0 :
        double.Parse(element.Value.Replace("in", string.Empty), CultureInfo.InvariantCulture);

    private static void Set(XElement item, XNamespace ns, string name, double value) =>
        item.SetElementValue(ns + name, value.ToString("0.#####", CultureInfo.InvariantCulture) + "in");
}
