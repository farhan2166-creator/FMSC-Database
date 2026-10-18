using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FMSC_SMS.Core;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);

    private static void CheckLevel3Components()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FMSC-Level3-components-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var measurements = new Dictionary<string, double>();
        var watch = Stopwatch.StartNew();
        try
        {
            void Cycle()
            {
                using var dialog = ModernMessageDialog.CreateDialog(null, new MessageDialogContent("Fictional operation", Facts: new[] { ("Records", "500"), ("Amount", "500,000") }), "Level 3 fixture", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                _ = dialog.Handle; dialog.PerformLayout();
                dialog.Dispose(); Check(dialog.IsDisposed, "Repeated production dialog disposal releases the complete control tree");
            }
            for (int i = 0; i < 10; i++) Cycle();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var process = Process.GetCurrentProcess();
            long before = GC.GetTotalMemory(true); int handles = process.HandleCount, gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1);
            for (int batch = 0; batch < 6; batch++)
            {
                for (int i = 0; i < 40; i++) Cycle();
                Application.DoEvents(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh();
                measurements[$"Batch{batch + 1}ManagedBytes"] = GC.GetTotalMemory(true);
                measurements[$"Batch{batch + 1}Handles"] = process.HandleCount;
                measurements[$"Batch{batch + 1}Gdi"] = GetGuiResources(process.Handle, 0);
                measurements[$"Batch{batch + 1}User"] = GetGuiResources(process.Handle, 1);
            }
            process.Refresh(); measurements["DialogCycles"] = 240; measurements["DialogCycleSeconds"] = watch.Elapsed.TotalSeconds;
            measurements["ManagedBaselineBytes"] = before; measurements["HandleBaseline"] = handles;
            measurements["GdiBaseline"] = gdi; measurements["UserBaseline"] = user;
            measurements["WorkingSetBytes"] = process.WorkingSet64;
            // Bounded component smoke check, not a claim about a full-day operator session.
            Check(measurements["Batch6ManagedBytes"] - before < 16 * 1024 * 1024, "Disposed dialog cycles retain less than the bounded 16-MiB smoke-test budget");
            Check(measurements["Batch6Gdi"] <= gdi + 16 && measurements["Batch6User"] <= user + 16, "Disposed dialog cycles do not retain an unbounded native GUI resource per cycle");
            using var data = new DataTable(); data.Columns.Add("Reference"); data.Columns.Add("Description"); data.Columns.Add("Amount", typeof(decimal));
            for (int i = 0; i < 500; i++) data.Rows.Add("SYNTHETIC-" + i, "Fictional report text with comma, quotes \"example\" and wrapping. " + new string('x', 100), 1000m);
            string pdf = Path.Combine(folder, "synthetic.pdf"), csv = Path.Combine(folder, "synthetic.csv");
            watch.Restart(); PdfTableExporter.Export(pdf, "Fictional large report", "500 synthetic entries", data, Color.Navy);
            measurements["Pdf500RowsSeconds"] = watch.Elapsed.TotalSeconds;
            using (var parsed = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                measurements["PdfPages"] = parsed.PageCount;
                Check(parsed.PageCount > 1, "Production PDF exporter generates a parseable multi-page report for long synthetic rows");
            }
            var csvType = typeof(AppTheme).Assembly.GetType("FMSC_SMS.Core.CsvFileExporter", true)!;
            csvType.GetMethod("Write", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { data, csv });
            var lines = File.ReadAllLines(csv);
            Check(lines.Length == 501 && lines.Skip(1).All(line => line.EndsWith(",\"1000\"", StringComparison.Ordinal)), "CSV exports every synthetic amount exactly without losing quoted long-text rows");
            measurements["CsvRows"] = lines.Length - 1;
            measurements["SyntheticSourceTotal"] = (double)data.AsEnumerable().Sum(row => row.Field<decimal>("Amount"));
            string? output = Environment.GetEnvironmentVariable("FMSC_LEVEL3_WINDOWS_EVIDENCE");
            if (output is not null)
            {
                string root = Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new InvalidOperationException("Evidence export requires the disposable runner temporary root.");
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || !Path.GetFullPath(output).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only the disposable runner evidence root is accepted.");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new {
                    commit = Environment.GetEnvironmentVariable("GITHUB_SHA"), result = "PASS", kind = "Noninteractive production dialog and export components",
                    measurements, limitations = new[] { "No actual operator workflow, DB-backed screen cycling, prolonged session, physical DPI change or printer interaction.", "PDF parse/page checks do not prove visual layout or extracted monetary totals; CSV uses independent synthetic source values." }
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"PASS: {checks} Level 3 Windows component assertions; {measurements["DialogCycles"]} cycles; PDF {measurements["PdfPages"]} pages.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
