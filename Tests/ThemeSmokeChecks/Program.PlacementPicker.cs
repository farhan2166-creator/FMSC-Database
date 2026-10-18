using System.Data;
using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckPlacementPickerNavigation()
    {
        using var form = new Form();
        using var picker = new PlacementPicker();
        form.Controls.Add(picker);
        var rows = new DataTable();
        rows.Columns.Add("ClassSectionId", typeof(int));
        foreach (string column in new[] { "CampusName", "ClassName", "SectionName" }) rows.Columns.Add(column);
        rows.Rows.Add(1, "Main", "Play", "A");
        rows.Rows.Add(2, "Main", "Play", "B");
        picker.Bind(rows);
        form.Show();
        picker.Controls[0].Focus();
        Application.DoEvents();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(StudentPicker).GetMethod(name, flags)!.Invoke(picker, args);
        var list = (ListBox)typeof(StudentPicker).GetField("_list", flags)!.GetValue(picker)!;
        var popup = (Form)typeof(StudentPicker).GetField("_popup", flags)!.GetValue(picker)!;
        int changes = 0;
        picker.SelectedIndexChanged += (_, _) => changes++;
        Call("ApplyResults", rows, "Play");
        Check(popup.Visible && list.Items.Count == 2, "Matching placements remain available in the popup");
        Call("MoveSelection", 1);
        Check(picker.SelectedId == 1 && picker.Text.Contains("Play") && picker.Text.Contains("A"), "Down fills the highlighted placement and identity");
        Check(popup.Visible && changes == 1, "Navigation retains the list and notifies the setup form");
        Call("MoveSelection", 1);
        Check(picker.SelectedId == 2 && picker.Text.Contains("B"), "Down updates the field to the next row");
        Call("MoveSelection", -1);
        Check(picker.SelectedId == 1 && picker.Text.Contains("A"), "Up updates the field to the previous row");
        Call("HandleEnter");
        Check(!popup.Visible && picker.SelectedId == 1, "Enter accepts the displayed placement and closes the popup");
    }
}
