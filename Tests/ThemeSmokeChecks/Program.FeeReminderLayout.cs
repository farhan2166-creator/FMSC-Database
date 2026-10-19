using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    private static void CheckFeeReminderLayout()
    {
        using var balances=new DataTable();
        balances.Columns.Add("AdmissionId",typeof(int));
        foreach(string name in new[]{"AdmissionNumber","StudentName","FatherName"})balances.Columns.Add(name);
        balances.Columns.Add("Outstanding",typeof(decimal));
        balances.Rows.Add(1,"101","Fictional Ali","Fictional Parent",800m);
        balances.Rows.Add(2,"QR-102","Fictional Bilal","Fictional Parent",6900m);
        balances.Rows.Add(3,"103","Fictional Sara","Fictional Parent",6900m);
        using var dialog=new FeeReminderSelectionDialog(balances,"Campus: All; Academic year: All; Class: All; Section: All");
        dialog.Show();Application.DoEvents();
        IEnumerable<Control> Walk(Control root) => root.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Walk(child)));
        var controls=Walk(dialog).ToArray();
        ModernButton Button(string text)=>controls.OfType<ModernButton>().Single(button=>button.Text==text);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var grid=(DataGridView)typeof(FeeReminderSelectionDialog).GetField("students",flags)!.GetValue(dialog)!;
        var paper=(ModernComboBox)typeof(FeeReminderSelectionDialog).GetField("paper",flags)!.GetValue(dialog)!;
        var search=(ModernTextBox)typeof(FeeReminderSelectionDialog).GetField("search",flags)!.GetValue(dialog)!;
        foreach(var size in new[]{dialog.Size,dialog.MinimumSize})
        {
            dialog.Size=size;dialog.PerformLayout();Application.DoEvents();
            string folder=Path.GetFullPath("artifacts/reminder-layout");Directory.CreateDirectory(folder);
            using var image=new Bitmap(dialog.Width,dialog.Height);
            dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
            image.Save(Path.Combine(folder,$"reminder-{dialog.Width}.png"));
            foreach(string caption in new[]{"Select All","Select Shown","Clear Selection","Print Preview",UiText.Actions.Cancel})
            {
                var button=Button(caption);
                Check(button.Visible,$"Reminder action is visible: {caption}");
                var bounds=button.RectangleToScreen(button.ClientRectangle);
                for(Control? parent=button.Parent;parent is not null;parent=parent.Parent)
                    Check(parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds),$"Reminder action is not clipped: {caption}, {bounds}, parent {parent.GetType().Name} {parent.RectangleToScreen(parent.ClientRectangle)}");
            }
            Check(grid.Height>=AppScale.Scale(110),"Reminder grid retains usable height at minimum size");
        }
        Check(!Button("Print Preview").Enabled,"Paper format must still be chosen explicitly");
        paper.SelectedIndex=1;
        Check(Button("Print Preview").Enabled,"Chosen paper format enables preview with selected students");
        search.Text="Bilal";Application.DoEvents();
        Check(grid.Rows.Count==1&&dialog.SelectedStudents.Count==3,"Search preserves checked students outside the visible matches");
        Button("Clear Selection").PerformClick();
        Check(dialog.SelectedStudents.Count==0&&!Button("Print Preview").Enabled,"Clear Selection clears hidden and visible selections");
        Button("Select Shown").PerformClick();
        Check(dialog.SelectedStudents.SetEquals(new[]{2}),"Select Shown selects only search matches");
        Button("Select All").PerformClick();
        Check(dialog.SelectedStudents.Count==3,"Select All includes students hidden by search");
        Check(grid.Columns["Outstanding"].DefaultCellStyle.Format=="N0","Reminder amounts use whole-rupee formatting");
        dialog.Close();
    }
}
