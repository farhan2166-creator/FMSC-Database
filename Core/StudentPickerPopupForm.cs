namespace FMSC_SMS.Core;

internal sealed class StudentPickerPopupForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    internal StudentPickerPopupForm(ListBox list, Control header)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Padding = new Padding(1);
        BackColor = AppTheme.InputBorder;
        list.Dock = DockStyle.Fill;
        header.Dock = DockStyle.Top;
        Controls.Add(list);
        Controls.Add(header);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }
}
