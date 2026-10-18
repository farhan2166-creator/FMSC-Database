using System.Data;
using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckPdfFileDurability()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FMSC-PdfDurability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "report.pdf");
            using var rows = new DataTable(); rows.Columns.Add("Student"); rows.Columns.Add("Amount", typeof(decimal)); rows.Rows.Add("Ali", 1000m);
            File.WriteAllText(path, "previous export");
            PdfTableExporter.Export(path, "Fixture report", "SQL-free export", rows, Color.Navy);
            using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                Check(pdf.PageCount > 0, "Actual PDF generation publishes a readable, nonempty report");
            byte[] original = File.ReadAllBytes(path);
            var atomic = typeof(PdfTableExporter).Assembly.GetType("FMSC_SMS.Core.AtomicExportFile", true)!;
            var publish = atomic.GetMethod("Publish", BindingFlags.Static | BindingFlags.NonPublic)!;
            var validate = typeof(PdfTableExporter).GetMethod("ValidatePdf", BindingFlags.Static | BindingFlags.NonPublic)!;
            Action<string> invalid = temporary => File.WriteAllText(temporary, "broken PDF");
            Action<string> checkFormat = temporary => validate.Invoke(null, new object[] { temporary });
            try { publish.Invoke(null, new object[] { path, invalid, checkFormat }); throw new Exception("Invalid PDF replaced a valid report"); }
            catch (TargetInvocationException) { checks++; }
            Check(File.ReadAllBytes(path).SequenceEqual(original), "PDF parser refusal preserves the old valid export byte-for-byte");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { PdfTableExporter.Export(path, "Replacement", "Locked destination", rows, Color.Navy); throw new Exception("Locked PDF was replaced"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { checks++; }
            }
            Check(File.ReadAllBytes(path).SequenceEqual(original), "Windows file-lock refusal preserves the previous PDF");
            using var empty = new DataTable();
            try { PdfTableExporter.Export(path, "Empty", "", empty, Color.Navy); throw new Exception("An empty PDF export was accepted"); }
            catch (InvalidOperationException) { checks++; }
            Check(File.ReadAllBytes(path).SequenceEqual(original) && !Directory.GetFiles(folder, "*.tmp").Any(),
                "PDF failures preserve existing files and clean up temporary exports");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
