namespace FMSC_SMS.Core;
public sealed class LoadingState : Panel
{
    private System.Windows.Forms.Timer? _timer;
    private int _phase;
    private bool initialized;
    private readonly Label spinner = new(), heading = new(), detail = new();
    public LoadingState(string title = "Loading", string? subtitle = null)
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Surface;
        DoubleBuffered = true;
        Padding = new Padding(24);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        spinner.Text = "●  ●  ●";
        spinner.Dock = DockStyle.Fill;
        spinner.Font = AppTheme.SharedFont(AppTheme.NormalFont.FontFamily.Name,13f,FontStyle.Bold);
        spinner.ForeColor = AppTheme.Primary;
        spinner.TextAlign = ContentAlignment.BottomCenter;
        heading.Text = title;
        heading.Dock = DockStyle.Fill;
        heading.Font = AppTheme.HeadingFont;
        heading.ForeColor = AppTheme.HeadingText;
        heading.TextAlign = ContentAlignment.MiddleCenter;
        detail.Text = subtitle ?? "Preparing this section...";
        detail.Dock = DockStyle.Fill;
        detail.Font = AppTheme.NormalFont;
        detail.ForeColor = AppTheme.MutedText;
        detail.TextAlign = ContentAlignment.TopCenter;
        layout.Controls.Add(spinner,0,0);layout.Controls.Add(heading,0,1);layout.Controls.Add(detail,0,2);
        Controls.Add(layout);
        AppTheme.ThemeChanged += OnThemeChanged;
        initialized = true;
        UpdateAnimation();
    }
    private void UpdateAnimation()
    {
        if(!initialized)return;
        if(IsDisposed||Disposing||!Visible||!IsHandleCreated||AppTheme.UseSimpleRendering||
            SystemInformation.TerminalServerSession||!SystemInformation.IsMenuAnimationEnabled)
        {
            _timer?.Dispose();_timer=null;
            if(!spinner.IsDisposed)spinner.Text="●  ●  ●";
            return;
        }
        if(_timer is null)
        {
            _timer=new System.Windows.Forms.Timer{Interval=80};
            _timer.Tick+=(_,_)=>{
                _phase=(_phase+1)%4;
                spinner.Text=new string('●',_phase+1)+"  "+new string('·',3-_phase);
            };
        }
        _timer.Start();
    }
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);UpdateAnimation();}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);UpdateAnimation();}
    protected override void OnHandleDestroyed(EventArgs e){_timer?.Dispose();_timer=null;base.OnHandleDestroyed(e);}
    private void OnThemeChanged(object? sender,EventArgs e)
    {
        if(IsDisposed||Disposing)return;
        BackColor=AppTheme.Surface;
        spinner.ForeColor=AppTheme.Primary;heading.ForeColor=AppTheme.HeadingText;detail.ForeColor=AppTheme.MutedText;
        UpdateAnimation();Invalidate(true);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){AppTheme.ThemeChanged-=OnThemeChanged;_timer?.Dispose();_timer=null;}
        base.Dispose(disposing);
    }
}
