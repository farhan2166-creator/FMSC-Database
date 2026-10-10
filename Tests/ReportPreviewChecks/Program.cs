global using System.Drawing;
using FMSC_SMS.Core;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var fit = ReportPageArrangement.Calculate(3, 220, 430, new Size(1000, 700), 24, 24, 17, 1, false, true);
Check(fit.Pages.Count == 3, "Each receipt must retain its own page.");
Check(fit.Pages[0].Top == fit.Pages[1].Top, "Fit page must show receipts together.");
Check(fit.Pages[0].Bottom + 24 <= 700, "Fit page must fit vertically.");
Check(fit.Pages[1].Right <= 1000, "Fit page must fit horizontally.");
var widthFit = ReportPageArrangement.Calculate(3, 220, 430, new Size(1000, 700), 24, 24, 17, 1, true, false);
Check(widthFit.Pages[0].Top == widthFit.Pages[1].Top && widthFit.Pages[1].Right <= 1000, "Page width must fit two pages, including rounding and scrollbar allowance.");
var zoom = ReportPageArrangement.Calculate(3, 220, 430, new Size(1000, 700), 24, 24, 17, 4, false, false);
Check(zoom.Pages[0].Width == 880 && zoom.Pages[0].Height == 1720, "Zoom must keep the page aspect ratio.");
Check(zoom.Pages[1].Top > zoom.Pages[0].Bottom, "Zoomed pages must wrap without overlap.");
var narrow = ReportPageArrangement.Calculate(2, 220, 430, new Size(120, 200), 24, 24, 17, 1, false, false);
Check(narrow.Extent.Width >= narrow.Pages[0].Right, "Narrow view must support horizontal scrolling.");
var many = ReportPageArrangement.Calculate(100, 220, 430, new Size(1000, 700), 24, 24, 17, 1, false, false);
Check(many.Extent.Height > many.Pages[^1].Bottom, "Scrolling must reach the last page and its label.");
for (int i = 0; i < many.Pages.Count; i++)
    for (int j = i + 1; j < many.Pages.Count; j++) Check(!many.Pages[i].IntersectsWith(many.Pages[j]), "Page previews overlap.");
var empty = ReportPageArrangement.Calculate(0, 220, 430, new Size(1000, 700), 24, 24, 17, 1, false, false);
Check(empty.Pages.Count == 0 && empty.Extent == Size.Empty, "Empty overview must not retain scroll extent.");
Console.WriteLine($"PASS {checks} page arrangement checks");
