using System.Drawing.Printing;

namespace FMSC_SMS.Core;

/// <summary>The Windows print dialog shared by report and variable-length bill previews.</summary>
internal static class ReportPrintDialog
{
    internal static PrintDialog Create(RdlcPrintDocument document, int currentPage, bool printCurrentPage)
    {
        if (currentPage < 1 || currentPage > document.PageCount)
            throw new InvalidOperationException($"Select a page between 1 and {document.PageCount} before printing.");
        document.PrinterSettings.MinimumPage = 1;
        document.PrinterSettings.MaximumPage = document.PageCount;
        // All remains the normal default. Selecting Pages then targets the
        // preview's current page immediately, without typing the number twice.
        document.PrinterSettings.FromPage = currentPage;
        document.PrinterSettings.ToPage = currentPage;
        document.PrinterSettings.PrintRange = printCurrentPage ? PrintRange.SomePages : PrintRange.AllPages;
        return new PrintDialog {
            Document = document,
            AllowSomePages = true,
            AllowSelection = false,
            // Windows can replace the extended dialog with its modern print UI,
            // which ignores CurrentPage. Keep the established range-based dialog.
            UseEXDialog = false
        };
    }
}
