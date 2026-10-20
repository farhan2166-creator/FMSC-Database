using System.Runtime.InteropServices;
using FMSC_SMS.Core;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SearchInputMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    private static void CheckSearchTextSelection()
    {
        using var form = new Form();
        using var search = new ModernTextBox { SelectAllOnFocus = true, Text = "6589", Bounds = new Rectangle(10, 10, 250, 40) };
        using var other = new TextBox { Bounds = new Rectangle(10, 60, 250, 30) };
        using var picker = new StudentPicker { Dock = DockStyle.None, Bounds = new Rectangle(10, 100, 250, 40) };
        picker.SetSource(new System.Data.DataTable());
        form.Controls.AddRange(new Control[] { search, other, picker });
        form.Show(); Application.DoEvents();
        var input = search.Controls.OfType<TextBox>().Single();
        var pickerInput = picker.Controls.OfType<TextBox>().Single();
        void Click(TextBox box)
        {
            var point = new IntPtr((8 << 16) | 8);
            SearchInputMessage(box.Handle, 0x0201, new IntPtr(1), point);
            SearchInputMessage(box.Handle, 0x0202, IntPtr.Zero, point);
        }
        other.Focus();
        Click(input);
        Check(input.Focused && input.SelectedText == "6589", "First mouse click selects the complete previous search");
        Click(input);
        Check(input.SelectionLength == 0, "A second click can place the caret for editing");
        other.Focus(); search.Focus();
        Check(input.SelectedText == "6589", "Keyboard focus also selects the previous search");
        input.SelectedText = "6509";
        Check(search.Text == "6509", "Typing replaces the previous search instead of appending");
        Click(input);
        Check(input.SelectionLength == 0, "Editing consumes the pending selection gesture");
        search.SelectAllOnFocus = false;
        other.Focus(); Click(input);
        Check(input.SelectionLength == 0, "Normal text fields retain ordinary caret placement");

        int changed = 0, cleared = 0;
        picker.UserSelectionChanged += (_, _) => changed++;
        picker.SelectionCleared += (_, _) => cleared++;
        picker.Focus();
        picker.SetStudent(41, "6589 - Synthetic student");
        Click(pickerInput);
        Check(pickerInput.SelectedText == picker.Text, "A committed student is selected on the next click even when focus was retained");
        Check(picker.SelectedId == 41 && changed == 0 && cleared == 0, "Highlighting preserves the chosen student and raises no selection events");
        Click(pickerInput);
        Check(pickerInput.SelectionLength == 0, "Subsequent student finder clicks permit caret placement");
        other.Focus(); Click(pickerInput);
        Check(pickerInput.SelectedText == picker.Text, "Returning to the student finder selects its existing text again");
        Check(picker.SelectedId == 41 && cleared == 0, "Returning to search does not clear the admission record");
    }
}
