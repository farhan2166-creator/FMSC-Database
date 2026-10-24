using System.Runtime.CompilerServices;

namespace FMSC_SMS.Core;

internal static class StatusAttention
{
    private static readonly ConditionalWeakTable<Label, Attention> Active = new();
    internal static void Show(Control owner,Label status,ToolTip tip,string message,Action restore)
    {
        if(Active.TryGetValue(status,out var previous))previous.Stop(false);
        if(owner.IsDisposed||status.IsDisposed||!status.IsHandleCreated||!status.Visible)return;
        try{tip.Show(message,status,0,status.Height+2,5000);}
        catch(Exception ex){AppExceptionHandler.LogNonFatal(ex,"StatusAttention: Show fallback");}
        if(AppTheme.UseSimpleRendering||SystemInformation.TerminalServerSession||!SystemInformation.IsMenuAnimationEnabled)
        {restore();return;}
        var attention=new Attention(owner,status,restore);
        Active.Add(status,attention);attention.Start();
    }
    private sealed class Attention
    {
        private readonly Control owner;
        private readonly Label status;
        private readonly Action restore;
        private readonly System.Windows.Forms.Timer timer=new(){Interval=170};
        private int ticks;
        private bool stopped;
        internal Attention(Control owner,Label status,Action restore)
        {
            this.owner=owner;this.status=status;this.restore=restore;
            timer.Tick+=Tick;owner.Disposed+=OnDisposed;status.Disposed+=OnDisposed;
            AppTheme.ThemeChanged+=OnThemeChanged;
        }
        internal void Start()=>timer.Start();
        private void Tick(object? sender,EventArgs e)
        {
            if(owner.IsDisposed||status.IsDisposed){Stop(false);return;}
            if(!status.Visible||AppTheme.UseSimpleRendering||++ticks>4){Stop(true);return;}
            status.BackColor=AppTheme.Tint(AppTheme.Danger,ticks%2==1?0.55f:0.90f);
        }
        private void OnDisposed(object? sender,EventArgs e)=>Stop(false);
        private void OnThemeChanged(object? sender,EventArgs e)=>Stop(true);
        internal void Stop(bool restoreVisual)
        {
            if(stopped)return;
            stopped=true;timer.Dispose();owner.Disposed-=OnDisposed;status.Disposed-=OnDisposed;
            AppTheme.ThemeChanged-=OnThemeChanged;Active.Remove(status);
            if(restoreVisual&&!status.IsDisposed&&!owner.IsDisposed)restore();
        }
    }
}
