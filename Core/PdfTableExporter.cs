using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using MigraColor = MigraDoc.DocumentObjectModel.Color;
using MigraOrientation = MigraDoc.DocumentObjectModel.Orientation;
namespace FMSC_SMS.Core;
public static class PdfTableExporter
{
    public static void Export(string filePath, string reportTitle, string subtitle, DataTable table, System.Drawing.Color accent)
    {
        DataColumn[] columns = table.Columns
            .Cast<DataColumn>()
            .ToArray();
        if (columns.Length == 0)
            throw new InvalidOperationException("There is no report data to export.");
        var document = new Document();
        document.Info.Title = reportTitle;
        document.Info.Author = "FMSC SMS";
        Section section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.2);
        double rawWidthPt = Unit.FromCentimeter(21.0).Point;
        double rawHeightPt = Unit.FromCentimeter(29.7).Point;
        double marginsPt = section.PageSetup.LeftMargin.Point + section.PageSetup.RightMargin.Point;
        if (!(marginsPt > 0) || marginsPt >= rawWidthPt)
            marginsPt = Unit.FromCentimeter(2.4).Point;
        double portraitUsableWidthPt = rawWidthPt - marginsPt;
        double landscapeUsableWidthPt = rawHeightPt - marginsPt;
        double[] rawNatural = MeasureRawNaturalColumnWidths(table, columns);
        double rawNaturalTotal = rawNatural.Sum();
        bool landscape = rawNaturalTotal > portraitUsableWidthPt * 1.1;
        section.PageSetup.Orientation = landscape
            ? MigraOrientation.Landscape
            : MigraOrientation.Portrait;
        Paragraph title = section.AddParagraph(reportTitle);
        title.Format.Font.Size = Unit.FromPoint(16);
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(2);
        Paragraph meta = section.AddParagraph(string.IsNullOrWhiteSpace(subtitle)
                ? $"Generated {DateTime.Now:dd-MM-yyyy HH:mm} - {table.Rows.Count:N0} record(s)"
                : $"{subtitle}  •  Generated {DateTime.Now:dd-MM-yyyy HH:mm}  •  {table.Rows.Count:N0} record(s)");
        meta.Format.Font.Size = Unit.FromPoint(9);
        meta.Format.Font.Color = new MigraColor(107, 114, 128);
        meta.Format.SpaceAfter = Unit.FromPoint(12);
        double usableWidthPt = landscape ? landscapeUsableWidthPt : portraitUsableWidthPt;
        double[] widths = ScaleColumnWidths(columns, rawNatural, usableWidthPt);
        MigraDoc.DocumentObjectModel.Tables.Table pdfTable = section.AddTable();
        pdfTable.Borders.Width = 0.5;
        pdfTable.Borders.Color = new MigraColor(226, 232, 240);
        pdfTable.Format.Font.Size = Unit.FromPoint(9);
        foreach (double width in widths)
            pdfTable.AddColumn(Unit.FromPoint(width));
        MigraColor headerColor = new MigraColor(accent.R, accent.G, accent.B);
        MigraColor stripeColor = new MigraColor(248, 250, 252);
        MigraDoc.DocumentObjectModel.Tables.Row headerRow = pdfTable.AddRow();
        headerRow.HeadingFormat = true;
        headerRow.Shading.Color = headerColor;
        headerRow.Format.Font.Bold = true;
        headerRow.Format.Font.Color = Colors.White;
        headerRow.VerticalAlignment = VerticalAlignment.Center;
        for (int i = 0; i < columns.Length; i++)
        {
            Paragraph cell = headerRow[i].AddParagraph(columns[i].ColumnName);
            cell.Format.SpaceBefore = Unit.FromPoint(3);
            cell.Format.SpaceAfter = Unit.FromPoint(3);
        }
        int rowIndex = 0;
        foreach (DataRow dataRow in table.Rows)
        {
            MigraDoc.DocumentObjectModel.Tables.Row row = pdfTable.AddRow();
            row.VerticalAlignment = VerticalAlignment.Top;
            if (rowIndex % 2 == 1)
                row.Shading.Color = stripeColor;
            for (int i = 0; i < columns.Length; i++)
            {
                object value = dataRow[columns[i]];
                string text = value == null || value == DBNull.Value
                    ? string.Empty
                    : value is DateTime date ? date.ToString(date.TimeOfDay==TimeSpan.Zero?"dd-MM-yyyy":"dd-MM-yyyy HH:mm:ss", CultureInfo.CurrentCulture)
                    : RecordTextPolicy.DisplayText(columns[i].ColumnName,Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty);
                Paragraph cell = row[i].AddParagraph(text);
                cell.Format.SpaceBefore = Unit.FromPoint(3);
                cell.Format.SpaceAfter = Unit.FromPoint(3);
            }
            rowIndex++;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = renderer.PdfDocument;
        AtomicExportFile.Publish(filePath, temporary => output.Save(temporary), ValidatePdf);
    }
    internal static void ValidatePdf(string path)
    {
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        if (pdf.PageCount <= 0) throw new InvalidDataException("The PDF contains no pages. The previous file was not replaced.");
    }
    private const double MinColumnWidthPt = 45;
    private const double MaxColumnWidthPt = 260;
    private const double ColumnPaddingPt = 14;
    private const double CompactThresholdPt = 70;
    private static double[] MeasureRawNaturalColumnWidths(DataTable table, DataColumn[] columns)
    {
        double[] natural = new double[columns.Length];
        using var bitmap = new Bitmap(1, 1);
        using Graphics g = Graphics.FromImage(bitmap);
        using var headerFont = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        using var bodyFont = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        for (int i = 0; i < columns.Length; i++)
        {
            double widest = MeasureWidthPoints(g, columns[i].ColumnName, headerFont);
            foreach (DataRow row in table.Rows)
            {
                object value = row[columns[i]];
                string text = value == null || value == DBNull.Value
                    ? string.Empty
                    : value is DateTime date ? date.ToString(date.TimeOfDay==TimeSpan.Zero?"dd-MM-yyyy":"dd-MM-yyyy HH:mm:ss", CultureInfo.CurrentCulture)
                    : RecordTextPolicy.DisplayText(columns[i].ColumnName,Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty);
                double cellWidth = MeasureWidthPoints(g, text, bodyFont);
                if (cellWidth > widest)
                    widest = cellWidth;
            }
            natural[i] = Math.Clamp(widest + ColumnPaddingPt, MinColumnWidthPt, MaxColumnWidthPt);
        }
        return natural;
    }
    private static double[] ScaleColumnWidths(DataColumn[] columns, double[] rawNatural, double usableWidthPt)
    {
        int n = columns.Length;
        double[] natural = (double[])rawNatural.Clone();
        bool[] isCompact = new bool[n];
        for (int i = 0; i < n; i++)
        {
            isCompact[i] = natural[i] <= CompactThresholdPt
                && LooksLikeIdentifierColumn(columns[i].ColumnName);
        }
        double compactTotal = 0;
        for (int i = 0; i < n; i++)
            if (isCompact[i]) compactTotal += natural[i];
        var flexible = new List<int>();
        for (int i = 0; i < n; i++)
            if (!isCompact[i]) flexible.Add(i);
        double remaining = usableWidthPt - compactTotal;
        double flexibleTotal = flexible.Sum(i => natural[i]);
        if (flexible.Count > 0 && flexibleTotal > 0 && remaining > 0)
        {
            double scale = remaining / flexibleTotal;
            foreach (int i in flexible)
                natural[i] *= scale;
        }
        return EnforceMinimumWidth(natural, MinColumnWidthPt, usableWidthPt);
    }
    private static double[] EnforceMinimumWidth(double[] widths, double minPt, double usableWidthPt)
    {
        int n = widths.Length;
        if (n == 0)
            return widths;
        bool anyInvalid = widths.Any(w => !(w > 0) || double.IsNaN(w) || double.IsInfinity(w));
        if (!(usableWidthPt > 0) || usableWidthPt < minPt * n || anyInvalid)
        {
            double even = usableWidthPt > 0 ? usableWidthPt / n : minPt;
            return Enumerable.Repeat(even, n).ToArray();
        }
        double[] result = (double[])widths.Clone();
        double deficit = 0;
        for (int i = 0; i < n; i++)
        {
            if (result[i] < minPt)
            {
                deficit += minPt - result[i];
                result[i] = minPt;
            }
        }
        if (deficit > 0)
        {
            var donors = Enumerable.Range(0, n).Where(i => result[i] > minPt).ToList();
            double donorSurplus = donors.Sum(i => result[i] - minPt);
            if (donorSurplus > deficit)
            {
                foreach (int i in donors)
                    result[i] -= deficit * ((result[i] - minPt) / donorSurplus);
            }
        }
        double resultTotal = result.Sum();
        if (resultTotal > 0)
        {
            double finalScale = usableWidthPt / resultTotal;
            for (int i = 0; i < n; i++)
                result[i] *= finalScale;
        }
        return result;
    }
    private static bool LooksLikeIdentifierColumn(string header)
    {
        string compact = header.Replace(" ", string.Empty);
        return compact.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
            || compact.EndsWith("No", StringComparison.OrdinalIgnoreCase)
            || compact.EndsWith("Code", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("Sr", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("SNo", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("Serial", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("SerialNo", StringComparison.OrdinalIgnoreCase);
    }
    private static double MeasureWidthPoints(Graphics g, string text, System.Drawing.Font font)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        SizeF size = g.MeasureString(text, font);
        return size.Width * 72.0 / g.DpiX;
    }
}
