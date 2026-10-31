using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Setup.Academics;

internal static partial class Program
{
    private static void CheckAssignmentLayout()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new CampusClassSectionAssignmentsPage(false);
        using var host = new Form { ClientSize = new Size(1600, 850) };
        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        var grid = (DataGridView)typeof(CampusClassSectionAssignmentsPage).GetField("matrix", flags)!.GetValue(page)!;
        foreach (string name in new[] { "F", "M", "S1" })
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = name, HeaderText = name, MinimumWidth = 80 });
        grid.Rows.Add("Play Group", false, true, false);
        grid.Rows.Add("Nursery", true, false, true);
        host.Show();
        Application.DoEvents();
        var tools = page.Controls.Find("AssignmentTools", true).Single();
        foreach (int width in new[] { 1600, 1200 })
        {
            host.ClientSize = new Size(width, 850);
            Application.DoEvents();
            UiHelper.FitColumnsToWidth(grid);
            var widths = grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width).ToArray();
            Check(widths.Max() - widths.Min() <= 1, "Shared fitter gives Class and section columns equal widths after resize");
            Check(tools.Controls.Cast<Control>().Select(control => control.Top).Distinct().Count() == 1,
                "Campus selector and all buttons stay on one line");
            Check(Math.Abs(tools.Left - Math.Max(0, (tools.Parent!.ClientSize.Width - tools.Width) / 2)) <= 1,
                "The entire control row stays centered");
            Check((bool)grid.Rows[0].Cells[2].Value! && !(bool)grid.Rows[0].Cells[1].Value!, "Sizing preserves assignment values");
        }
        host.ClientSize = new Size(1600, 850);
        Application.DoEvents();
        Directory.CreateDirectory("artifacts/assignment-layout");
        using var picture = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        picture.Save("artifacts/assignment-layout/assignment-layout.png");
        using var years = new AcademicYearsPage(false);
        var yearGrid = (DataGridView)typeof(V2Page).GetField("Grid", flags | BindingFlags.Public)!.GetValue(years)!;
        yearGrid.Columns.Add("YearName", "Year Name");
        yearGrid.Columns.Add("Remarks", "Remarks");
        yearGrid.Columns.Add("DatesLocked", "DatesLocked");
        typeof(AcademicYearsPage).GetMethod("LayoutRecordColumns", flags)!.Invoke(years, null);
        Check(!yearGrid.Columns["Remarks"].Visible && !yearGrid.Columns["DatesLocked"].Visible, "Academic-year grid hides Remarks and the internal lock flag");
    }
}
