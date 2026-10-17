namespace FMSC_SMS.Core;
/// <summary>Prints the "PAYMENT BILL" slip - the student's posted charges that still carry a balance - on the 58 mm thermal roll, in a print preview first.</summary>
public static class ChargeBillPrinter
{
    private sealed class Line
    {
        public string FeeName { get; init; } = string.Empty;
        public string BillingMonth { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal DiscountAmount { get; init; }
    }
    /// <summary>Reads the student's outstanding charges on <paramref name="enrollmentId"/> and opens the bill in a print preview. Database errors are thrown for the caller's page to report.</summary>
    public static async Task<string?> PrintAsync(
        int admissionId,
        int enrollmentId,
        string studentName,
        string classSectionText)
    {
        if (admissionId <= 0 || enrollmentId <= 0)
            return "Select the student first, then print the bill.";
        DataTable raw = await DatabaseHelper.QueryUserAsync(
            "GetOutstandingCharges",
            DatabaseHelper.Int("@AdmissionId", admissionId),
            DatabaseHelper.Int("@EnrollmentId", enrollmentId),
            DatabaseHelper.Date("@AsOfDate", DateTime.Today));
        var lines = new List<Line>();
        foreach (DataRow row in raw.Rows)
        {
            decimal outstanding = ReadDecimal(row, "Balance");
            if (outstanding <= 0m)
                continue;
            decimal gross = ReadDecimal(row, "GrossAmount");
            decimal net = ReadDecimal(row, "NetAmount");
            lines.Add(new Line {
                FeeName = ReadText(row, "FeeTypeName"),
                BillingMonth = row.Table.Columns.Contains("BillingMonth")
                    ? FormatMonth(row["BillingMonth"])
                    : string.Empty,
                Amount = outstanding,
                DiscountAmount = Math.Max(0m, gross - net)
            });
        }
        if (lines.Count == 0)
            return "There is nothing outstanding to bill for this student.";
        /* The institution name comes from School Profile; an official slip with a wrong or blank name is worse than none, so nothing is printed when it cannot be read. */
        string institutionName = await InstitutionProfile.GetNameAsync();
        if (string.IsNullOrWhiteSpace(institutionName))
        {
            return "The school profile could not be read, so the bill was not printed. " +
                   "Check School Profile, then print again.";
        }
        Print(institutionName, studentName, admissionId, lines, classSectionText);
        return null;
    }
    private static void Print(
        string institutionName,
        string studentName,
        int admissionId,
        List<Line> rows,
        string classSectionText)
    {
        const int paperWidthHundredthsInch = 228;
        const int margin = 5;
        decimal totalDiscount = rows.Sum(r => r.DiscountAmount);
        bool showDiscountLine = totalDiscount > 0m;
        bool showClass = !string.IsNullOrWhiteSpace(classSectionText);
        int extraLines = (showClass ? 1 : 0) + (showDiscountLine ? 1 : 0);
        int lineCount = 6 + rows.Count + 3 + extraLines;
        int paperHeightHundredthsInch = Math.Max(170, 30 + (lineCount * 13));
        using var document = new System.Drawing.Printing.PrintDocument();
        document.DefaultPageSettings.PaperSize =
            new System.Drawing.Printing.PaperSize("Thermal 58mm", paperWidthHundredthsInch, paperHeightHundredthsInch);
        document.DefaultPageSettings.Margins =
            new System.Drawing.Printing.Margins(margin, margin, margin, margin);
        document.PrintPage += (_, e) => {
            using var titleFont = new Font("Arial", 9, FontStyle.Bold);
            using var normalFont = new Font("Arial", 7.5f, FontStyle.Regular);
            using var boldFont = new Font("Arial", 7.5f, FontStyle.Bold);
            // WinForms supplies Graphics on every PrintPage event; it is never null here.
            Graphics graphics = e.Graphics!;
            float left = e.MarginBounds.Left;
            float width = e.MarginBounds.Width;
            float y = e.MarginBounds.Top;
            const float lineHeight = 12f;
            DrawCentered(graphics, institutionName, boldFont, left, width, ref y);
            DrawCentered(graphics, "PAYMENT BILL", titleFont, left, width, ref y);
            DrawCentered(
                graphics,
                $"Admission Number: {AdmissionNumbers.ForId(admissionId)}   " + DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),
                normalFont, left, width, ref y);
            DrawSeparator(graphics, normalFont, left, width, ref y);
            DrawText(graphics, string.IsNullOrWhiteSpace(studentName) ? "Student" : studentName.Trim(),
                boldFont, left, width, ref y, lineHeight);
            if (showClass)
                DrawLine(graphics, "Class / Section", classSectionText, normalFont, left, width, ref y, lineHeight);
            decimal total = 0m;
            foreach (Line row in rows)
            {
                string feeText = row.FeeName;
                if (!string.IsNullOrWhiteSpace(row.BillingMonth))
                    feeText += " " + row.BillingMonth;
                DrawLine(graphics, feeText, row.Amount.ToString("N0"), normalFont, left, width, ref y, lineHeight);
                total += row.Amount;
            }
            DrawSeparator(graphics, normalFont, left, width, ref y);
            if (showDiscountLine)
                DrawLine(graphics, "Discount Applied", totalDiscount.ToString("N0"), normalFont, left, width, ref y, lineHeight);
            DrawLine(graphics, "TOTAL PAYABLE", total.ToString("N0"), boldFont, left, width, ref y, lineHeight);
            y += 3;
            DrawCentered(graphics, "Please pay at the Fee Counter", normalFont, left, width, ref y);
            e.HasMorePages = false;
        };
        using var preview = new PrintPreviewDialog {
            Document = document,
            Width = AppScale.Scale(600),
            Height = AppScale.Scale(700),
            StartPosition = FormStartPosition.CenterScreen
        };
        preview.ShowDialog();
    }
    private static void DrawLine(
        Graphics graphics, string leftText, string rightText, Font font,
        float x, float width, ref float y, float lineHeight)
    {
        float rightWidth = graphics.MeasureString(rightText, font).Width;
        float rightX = x + width - rightWidth;
        float availableWidth = Math.Max(30, rightX - x - 4);
        graphics.DrawString(TrimToWidth(graphics, leftText, font, availableWidth), font, Brushes.Black, x, y);
        graphics.DrawString(rightText, font, Brushes.Black, rightX, y);
        y += lineHeight;
    }
    private static void DrawCentered(Graphics graphics, string text, Font font, float x, float width, ref float y)
    {
        SizeF size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, Brushes.Black, x + Math.Max(0, (width - size.Width) / 2), y);
        y += size.Height + 2;
    }
    private static void DrawSeparator(Graphics graphics, Font font, float x, float width, ref float y)
    {
        string separator = TrimToWidth(graphics, "--------------------------------", font, width);
        graphics.DrawString(separator, font, Brushes.Black, x, y);
        y += 13;
    }
    private static void DrawText(Graphics graphics, string text, Font font, float x, float width, ref float y, float lineHeight)
    {
        graphics.DrawString(TrimToWidth(graphics, text, font, width), font, Brushes.Black, x, y);
        y += lineHeight;
    }
    private static string TrimToWidth(Graphics graphics, string text, Font font, float width)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (graphics.MeasureString(text, font).Width <= width)
            return text;
        const string suffix = "...";
        string trimmed = text;
        while (trimmed.Length > 1 && graphics.MeasureString(trimmed + suffix, font).Width > width)
            trimmed = trimmed[..^1];
        return trimmed + suffix;
    }
    private static string FormatMonth(object value)
    {
        if (value is null || value == DBNull.Value)
            return string.Empty;
        return DateTime.TryParse(Convert.ToString(value), out DateTime date)
            ? date.ToString("MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : Convert.ToString(value) ?? string.Empty;
    }
    private static string ReadText(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToString(row[column]) ?? string.Empty
            : string.Empty;
    private static decimal ReadDecimal(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToDecimal(row[column])
            : 0m;
}
