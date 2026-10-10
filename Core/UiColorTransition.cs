using System.Diagnostics;

namespace FMSC_SMS.Core;

/// <summary>A short, idle-free transition for decorative control colors.</summary>
internal sealed class UiColorTransition : IDisposable
{
    private readonly Control owner;
    private readonly Action<Color> apply;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private Color start;
    private Color target;
    private long started;
    private bool disposed;
    internal Color Value { get; private set; }

    internal UiColorTransition(Control owner, Color initial, Action<Color> apply)
    {
        this.owner = owner;
        this.apply = apply;
        Value = start = target = initial;
        timer.Tick += (_, _) => Tick();
        owner.VisibleChanged += OnVisibilityChanged;
    }

    internal void MoveTo(Color color, bool animate = true)
    {
        if (disposed) return;
        if (!animate || !owner.Visible || !owner.Enabled || !owner.IsHandleCreated ||
            SystemInformation.TerminalServerSession || !SystemInformation.IsMenuAnimationEnabled)
        {
            timer.Stop();
            Value = start = target = color;
            apply(color);
            return;
        }
        if (target == color) return;
        start = Value;
        target = color;
        started = Stopwatch.GetTimestamp();
        timer.Start();
    }

    private void Tick()
    {
        if (owner.IsDisposed || !owner.Visible || !owner.Enabled)
        {
            timer.Stop();
            return;
        }
        double progress = Math.Min(1, Stopwatch.GetElapsedTime(started).TotalMilliseconds / 140);
        double eased = 1 - Math.Pow(1 - progress, 3);
        int Blend(int from, int to) => (int)Math.Round(from + (to - from) * eased);
        Value = Color.FromArgb(Blend(start.A, target.A), Blend(start.R, target.R),
            Blend(start.G, target.G), Blend(start.B, target.B));
        apply(Value);
        if (progress >= 1) timer.Stop();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (!owner.Visible) MoveTo(target, animate: false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.VisibleChanged -= OnVisibilityChanged;
        timer.Dispose();
    }
}
