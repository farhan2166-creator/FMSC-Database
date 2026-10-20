using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckFamilyListSelection()
    {
        using var form = new Form { ClientSize = new Size(450, 400) };
        using var list = new FamilyStudentList { Dock = DockStyle.Fill, CheckOnClick = true, CheckBoxOnly = true, IntegralHeight = false };
        for (int index = 0; index < 15; index++) list.Items.Add($"Child {index + 1}\nPending Registration · Loaded", true);
        form.Controls.Add(list); form.Show(); Application.DoEvents();
        int changes = 0;
        list.ItemCheck += (_, _) => changes++;
        void Click(Point point, uint down = 0x0201)
        {
            var position = new IntPtr((point.Y << 16) | (point.X & 0xffff));
            SearchInputMessage(list.Handle, down, new IntPtr(1), position);
            SearchInputMessage(list.Handle, 0x0202, IntPtr.Zero, position);
        }
        Point Center(Rectangle rectangle) => new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
        var second = list.GetItemRectangle(1);
        Click(new Point(100, second.Top + 15));
        Check(list.SelectedIndex == 1 && list.GetItemChecked(1) && changes == 0,
            "Clicking a child's name selects for review without excluding the child");
        Click(new Point(100, second.Top + 15));
        Click(new Point(100, second.Top + 15), 0x0203);
        Check(list.GetItemChecked(1) && changes == 0, "Repeated and double clicks on a name never toggle inclusion");
        Click(new Point(100, second.Bottom - 8));
        Check(list.GetItemChecked(1) && changes == 0, "Clicking the second detail line also preserves inclusion");
        Click(Center(list.CheckBoxBounds(1)));
        Check(!list.GetItemChecked(1) && changes == 1, "Clicking the checkbox excludes the child exactly once");
        Click(Center(list.CheckBoxBounds(1)));
        Check(list.GetItemChecked(1) && changes == 2, "Clicking the checkbox again includes the child");
        Click(Center(list.CheckBoxBounds(2)));
        Check(list.SelectedIndex == 2 && !list.GetItemChecked(2) && changes == 3,
            "A checkbox on another row selects that child and toggles inclusion once");
        Click(new Point(100, list.GetItemRectangle(2).Top + 15));
        Check(!list.GetItemChecked(2) && changes == 3, "Reviewing an excluded child keeps them excluded");
        list.Focus();
        SearchInputMessage(list.Handle, 0x0102, new IntPtr(' '), new IntPtr(1));
        Check(list.GetItemChecked(2) && changes == 4, "Space remains available to change inclusion from the keyboard");
        list.SetItemChecked(2, false);
        Check(!list.GetItemChecked(2) && changes == 5, "Programmatic inclusion changes remain available");
        list.TopIndex = 8; Application.DoEvents();
        Click(new Point(100, list.GetItemRectangle(9).Top + 15));
        Check(list.SelectedIndex == 9 && list.GetItemChecked(9) && changes == 5,
            "Selecting a scrolled row preserves its checkbox");
        Click(Center(list.CheckBoxBounds(9)));
        Check(!list.GetItemChecked(9) && changes == 6, "Checkbox hit testing follows the scrolled row");
        list.CheckBoxOnly = false;
        Click(new Point(100, list.GetItemRectangle(9).Top + 15));
        Check(list.GetItemChecked(9) && changes == 7,
            "Other shared family lists retain their established whole-row check behavior");
    }
}
