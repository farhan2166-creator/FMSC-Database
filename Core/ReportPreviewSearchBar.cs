namespace FMSC_SMS.Core;

internal sealed class ReportPreviewSearchBar : FlowLayoutPanel
{
    private readonly ModernTextBox query=HumanUi.TextBox(false,120);
    private readonly ModernButton next=new(){Text="Find Next",ThemeRole="Secondary"};
    private readonly Label status=new(){AutoSize=true,ForeColor=AppTheme.MutedText,Font=AppTheme.NormalFont};
    private readonly Func<Task<ReportPageSearchIndex>> buildIndex;
    private readonly Action<int> navigate;
    private ReportPageSearchIndex? index;
    private string previousQuery="";
    private int[] matches=Array.Empty<int>();
    private int position=-1,version;
    private bool busy;

    internal ReportPreviewSearchBar(ApplicationWindowForm owner,Func<Task<ReportPageSearchIndex>> buildIndex,Action<int> navigate)
    {
        this.buildIndex=buildIndex;this.navigate=navigate;
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;WrapContents=false;Margin=Padding.Empty;
        query.Width=AppScale.Scale(230);query.Height=AppMetrics.InputControlHeight;
        query.PlaceholderText="Admission no., name or text…";query.AccessibleName="Find in report";
        query.Margin=new Padding(0,0,AppScale.Scale(8),0);
        next.Width=AppScale.Scale(104);next.Height=AppMetrics.ButtonHeight;
        next.Margin=new Padding(0,0,AppScale.Scale(8),0);
        status.Margin=new Padding(0,AppScale.Scale(8),AppScale.Scale(8),0);
        Controls.Add(query);Controls.Add(next);Controls.Add(status);
        next.Click+=async(_,_)=>await FindAsync(1);
        query.KeyDown+=async(_,e)=>{
            if(e.KeyCode!=Keys.Enter)return;
            e.SuppressKeyPress=true;await FindAsync(e.Shift?-1:1);
        };
        query.TextChanged+=(_,_)=>{previousQuery="";position=-1;status.Text="";};
        owner.ShortcutController.RegisterShortcut(query,Keys.Control|Keys.F,"Ctrl+F",()=>{query.Focus();query.SelectAll();});
        owner.ShortcutController.RegisterShortcut(next,Keys.F3,"F3",()=>_ = FindAsync(1));
        owner.ShortcutController.RegisterShortcut(next,Keys.Shift|Keys.F3,"Shift+F3",()=>_ = FindAsync(-1));
        UiAccessibility.Describe(query,"Find in report","Search the printed pages without changing what will print or export. Enter or F3 finds the next matching page; Shift+F3 finds the previous one.");
    }
    internal void ResetIndex()
    {
        version++;index=null;previousQuery="";matches=Array.Empty<int>();position=-1;status.Text="";
    }
    private async Task FindAsync(int direction)
    {
        if(busy||!Enabled||IsDisposed)return;
        string text=query.Text.Trim();
        if(text.Length==0){status.Text="Enter an admission number, name or text.";query.Focus();return;}
        busy=true;next.Enabled=false;int request=version;
        try
        {
            if(index is null)
            {
                status.Text="Preparing page search…";await Task.Yield();
                if(IsDisposed||request!=version)return;
                var prepared=await buildIndex();
                if(IsDisposed||request!=version)return;
                index=prepared;
            }
            if(query.Text.Trim()!=text){status.Text="Press Find Next to search.";return;}
            if(previousQuery!=text)
            {
                matches=index.FindPages(text);position=direction>0?0:matches.Length-1;previousQuery=text;
            }
            else if(matches.Length>0)position=(position+direction+matches.Length)%matches.Length;
            if(matches.Length==0){status.Text="No matching pages.";return;}
            int page=matches[position];
            navigate(page);
            status.Text=$"Page {page} · {position+1} of {matches.Length} matching pages";
        }
        catch(Exception ex)
        {
            if(!IsDisposed){status.Text="Page search unavailable. Try again.";AppExceptionHandler.LogNonFatal(ex,"Report page search");}
        }
        finally {busy=false;if(!IsDisposed)next.Enabled=true;}
    }
}
