namespace FMSC_SMS.Core;

internal static class FeeReceiptCopyDialog
{
    // null cancels, zero preserves the original family document.
    internal static int? Choose(Form? owner,DataTable students)
    {
        using var dialog=new ApplicationWindowForm {Text="Choose Receipt Copy",StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(AppScale.Scale(620),AppScale.Scale(235)),ShowInTaskbar=false};
        var choice=HumanUi.Combo();choice.Items.Add("Original family receipt — all original members");
        foreach(DataRow child in students.Rows)choice.Items.Add($"Child copy — {child["AdmissionNumber"]} · {child["StudentName"]}");
        choice.SelectedIndex=0;
        var print=new ModernButton {Text="Preview Receipt",ThemeRole="Primary",DialogResult=DialogResult.OK};
        var cancel=new ModernButton {Text="Cancel",DialogResult=DialogResult.Cancel};dialog.CancelButton=cancel;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(HumanUi.Field("Receipt copy",choice),0,0);
        root.Controls.Add(new Label {Text="Child copies retain the original receipt number and date and show only that child's recorded amounts. No new payment or receipt is created.",Dock=DockStyle.Fill,AutoSize=true,MaximumSize=new Size(AppScale.Scale(570),0)},0,1);
        var actions=HumanUi.ButtonRow(print,cancel);actions.Dock=DockStyle.Fill;
        root.Controls.Add(actions,0,2);dialog.Controls.Add(root);
        return dialog.ShowDialog(owner)!=DialogResult.OK?null:choice.SelectedIndex==0?0:Convert.ToInt32(students.Rows[choice.SelectedIndex-1]["AdmissionId"]);
    }
}
