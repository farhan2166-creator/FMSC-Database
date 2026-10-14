namespace FMSC_SMS.Core;

/// <summary>Preserves readable dialog content when the window must fit a smaller work area.</summary>
internal sealed class ResponsiveViewport : Panel
{
    private readonly Control content;
    private Size contentMinimum;
    private bool fitting;

    internal ResponsiveViewport(Control content, Size minimum)
    {
        this.content = content;
        contentMinimum = minimum;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Margin = Padding.Empty;
        content.Dock = DockStyle.None;
        content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        content.Location = Point.Empty;
        Controls.Add(content);
    }

    internal void ChangeDpi(int from, int to) => contentMinimum = DpiLayout.ChangeDpi(contentMinimum, from, to);

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (!fitting && content is not null)
        {
            fitting = true;
            try
            {
                content.Size = new Size(Math.Max(contentMinimum.Width, ClientSize.Width),
                    Math.Max(contentMinimum.Height, ClientSize.Height));
            }
            finally { fitting = false; }
        }
        base.OnLayout(e);
    }
}
