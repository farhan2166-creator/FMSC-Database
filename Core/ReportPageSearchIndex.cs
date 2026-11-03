using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FMSC_SMS.Core;

/// <summary>Search the actual printed pages, including variable-height slips and multi-column A4 sheets.</summary>
internal sealed class ReportPageSearchIndex
{
    private readonly string[] pages;
    internal ReportPageSearchIndex(IEnumerable<string> pageText) => pages=pageText.Select(Normalize).ToArray();
    internal static ReportPageSearchIndex FromPdf(byte[] bytes)
    {
        using var pdf=PdfDocument.Open(bytes);
        return new ReportPageSearchIndex(pdf.GetPages().Select(page=>ContentOrderTextExtractor.GetText(page)));
    }
    internal int[] FindPages(string query)
    {
        query=Normalize(query);
        return query.Length==0 ? Array.Empty<int>() : pages.Select((text,index)=>(text,index))
            .Where(page=>page.text.Contains(query,StringComparison.OrdinalIgnoreCase)).Select(page=>page.index+1).ToArray();
    }
    private static string Normalize(string text) => Regex.Replace(text,@"\s+"," ").Trim();
}
