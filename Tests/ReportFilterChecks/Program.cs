using FMSC_SMS.Core;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
Check(ReportFilterArrangement.For(5, 1200, 150) == new ReportFilterArrangement(5, 1),
    "Wide windows keep the original single row");
Check(ReportFilterArrangement.For(8, 1080, 150) == new ReportFilterArrangement(7, 2),
    "Crowded filters wrap instead of squeezing below a readable width");
Check(ReportFilterArrangement.For(5, 0, 150) == new ReportFilterArrangement(1, 5),
    "Startup before layout does not divide by zero");
Check(ReportFilterArrangement.For(0, 1080, 150) == new ReportFilterArrangement(1, 1),
    "An empty filter bar retains a valid table shape");
foreach (int fieldCount in Enumerable.Range(1, 15))
foreach (int width in new[] { 1, 149, 150, 299, 300, 720, 1080, 1366, 1920 })
{
    var shape = ReportFilterArrangement.For(fieldCount, width, 150);
    var cells = Enumerable.Range(0, fieldCount).Select(index => (index % shape.Columns, index / shape.Columns)).ToArray();
    Check(cells.Distinct().Count() == fieldCount && cells.All(cell => cell.Item2 < shape.Rows),
        "Every filter keeps its own reachable cell at each window width");
    Check(shape.Columns == 1 || width / shape.Columns >= 150,
        "Multiple-column arrangements meet the minimum field width");
}
Console.WriteLine($"Passed {checks} report filter layout checks.");
