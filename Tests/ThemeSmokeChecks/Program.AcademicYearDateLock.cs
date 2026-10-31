using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Setup.Academics;

internal static partial class Program
{
    private static void CheckAcademicYearDateLock()
    {
        // Never show the page or pump messages: its deferred database load must not run.
        using var page = new AcademicYearsPage();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var controls = (Dictionary<string, Control>)typeof(MasterPage).GetField("_controls", flags)!.GetValue(page)!;
        var start = (ModernDatePicker)controls["StartDate"];
        var end = (ModernDatePicker)controls["EndDate"];
        start.Value = new DateTime(2021, 4, 1);
        end.Value = new DateTime(2022, 3, 31);
        using var grid = new DataGridView();
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "DatesLocked" });
        int row = grid.Rows.Add(true);
        var load = typeof(AcademicYearsPage).GetMethod("RecordLoaded", flags)!;
        load.Invoke(page, new object[] { grid.Rows[row] });
        Check(!start.Enabled && !end.Enabled, "Used academic-year start and end controls are disabled");
        foreach (string key in new[] { "YearName", "Remarks", "IsCurrent" })
            Check(controls[key].Enabled, "Used academic-year " + key + " remains editable");
        controls["YearName"].Text = "2020-2021";
        Check(start.Value.Date == new DateTime(2021, 4, 1) && end.Value.Date == new DateTime(2022, 3, 31),
            "Correcting a used-year label preserves its dates");
        grid.Rows[row].Cells["DatesLocked"].Value = false;
        load.Invoke(page, new object[] { grid.Rows[row] });
        Check(start.Enabled && end.Enabled, "Switching to an unused year unlocks both dates");
        grid.Rows[row].Cells["DatesLocked"].Value = true;
        load.Invoke(page, new object[] { grid.Rows[row] });
        typeof(AcademicYearsPage).GetMethod("EditorCleared", flags)!.Invoke(page, new object[] { false });
        Check(start.Enabled && end.Enabled, "New Record unlocks dates after viewing a used year");
        grid.Rows[row].Cells["DatesLocked"].Value = DBNull.Value;
        load.Invoke(page, new object[] { grid.Rows[row] });
        Check(!start.Enabled && !end.Enabled, "Unknown usage status fails closed");
    }
}
