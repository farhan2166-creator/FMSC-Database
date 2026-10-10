using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Reporting.WinForms;

namespace FMSC_SMS.Core;

internal static class RdlcReportPrinter
{
    internal static void Print(Form owner, ReportViewer viewer)
    {
        try { PrintReport(owner, viewer); }
        finally
        {
            RestoreFocus(owner, viewer);
        }
    }

    private static void RestoreFocus(Form owner, ReportViewer viewer)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated || !owner.Visible || !owner.Enabled) return;
        owner.Activate();
        if (!viewer.IsDisposed) viewer.Focus();
    }

    private static void PrintReport(Form owner, ReportViewer viewer)
    {
        var pageSettings = viewer.GetPageSettings();
        using var document = CreateDocument(viewer.LocalReport, pageSettings, owner.Text);
        document.PrinterSettings = (PrinterSettings)viewer.PrinterSettings.Clone();
        document.PrinterSettings.MinimumPage = 1;
        document.PrinterSettings.MaximumPage = document.PageCount;
        document.PrinterSettings.FromPage = 1;
        document.PrinterSettings.ToPage = document.PageCount;
        document.PrinterSettings.PrintRange = PrintRange.AllPages;
        using var dialog = new PrintDialog {
            Document = document,
            AllowSomePages = true,
            AllowSelection = false,
            UseEXDialog = false
        };
        DialogResult result = dialog.ShowDialog(owner);
        // Restore the report before spooling, rather than after the entire job finishes.
        RestoreFocus(owner, viewer);
        if (result != DialogResult.OK) return;
        if (!document.PrinterSettings.IsValid)
            throw new InvalidOperationException("The selected printer is unavailable. Select an installed printer and try again.");

        // Switching printers in the dialog can replace the document's page settings.
        // Keep the report geometry and use the selected printer, copies and duplex settings.
        pageSettings.PrinterSettings = document.PrinterSettings;
        document.DefaultPageSettings = pageSettings;
        document.PrintController = new StandardPrintController();
        document.Print();
        if (document.PagesPrinted == 0)
            throw new InvalidOperationException("The print job did not produce any pages.");
        viewer.PrinterSettings = (PrinterSettings)document.PrinterSettings.Clone();
    }

    internal static RdlcPrintDocument CreateDocument(LocalReport report, PageSettings settings, string title)
    {
        string Inches(int value) => (value / 100d).ToString(CultureInfo.InvariantCulture) + "in";
        int width = settings.Landscape ? settings.PaperSize.Height : settings.PaperSize.Width;
        int height = settings.Landscape ? settings.PaperSize.Width : settings.PaperSize.Height;
        var deviceInfo = new XElement("DeviceInfo",
            new XElement("OutputFormat", "EMF"),
            new XElement("StartPage", 0), new XElement("EndPage", 0),
            new XElement("PageWidth", Inches(width)), new XElement("PageHeight", Inches(height)),
            new XElement("MarginLeft", Inches(settings.Margins.Left)),
            new XElement("MarginRight", Inches(settings.Margins.Right)),
            new XElement("MarginTop", Inches(settings.Margins.Top)),
            new XElement("MarginBottom", Inches(settings.Margins.Bottom)));
        var pages = new List<MemoryStream>();
        try
        {
            report.Render("IMAGE", deviceInfo.ToString(), (name, extension, encoding, mimeType, willSeek) => {
                var stream = new MemoryStream();
                pages.Add(stream);
                return stream;
            }, out _);
            if (pages.Count == 0 || pages.Any(page => page.Length == 0))
                throw new InvalidOperationException("The report could not generate printable pages.");
            foreach (var page in pages) page.Position = 0;
            return new RdlcPrintDocument(pages) {
                DocumentName = title,
                DefaultPageSettings = (PageSettings)settings.Clone(),
                OriginAtMargins = false
            };
        }
        catch
        {
            foreach (var page in pages) page.Dispose();
            throw;
        }
    }
}

internal sealed class RdlcPrintDocument(List<MemoryStream> pages) : PrintDocument
{
    private int pageIndex;
    private int lastPage;
    internal int PageCount => pages.Count;
    internal int PagesPrinted { get; private set; }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        PagesPrinted = 0;
        pageIndex = PrinterSettings.PrintRange == PrintRange.SomePages ? PrinterSettings.FromPage - 1 : 0;
        lastPage = PrinterSettings.PrintRange == PrintRange.SomePages ? PrinterSettings.ToPage : PageCount;
        if (pageIndex < 0 || lastPage > PageCount || pageIndex >= lastPage)
            throw new InvalidOperationException($"Select a page range between 1 and {PageCount}.");
        base.OnBeginPrint(e);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        var graphics = e.Graphics ?? throw new InvalidOperationException("The printer did not provide a drawing surface.");
        var stream = pages[pageIndex];
        stream.Position = 0;
        using var image = new Metafile(stream);
        // EMF pages already contain report margins; compensate only for the printer's physical origin.
        var bounds = new RectangleF(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY,
            e.PageBounds.Width, e.PageBounds.Height);
        graphics.DrawImage(image, bounds);
        PagesPrinted++;
        pageIndex++;
        e.HasMorePages = pageIndex < lastPage;
        base.OnPrintPage(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var page in pages) page.Dispose();
        base.Dispose(disposing);
    }
}
