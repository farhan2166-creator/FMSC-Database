namespace FMSC_SMS.Core;
public sealed record NavigationCommand(string Caption, Action Execute)
{
    public override string ToString() => Caption;
}
public sealed class CommandPaletteDialog : ApplicationWindowForm
{
    private readonly TextBox search = new();
    private readonly ListBox results = new();
    private readonly IReadOnlyList<NavigationCommand> source;
    private readonly Func<string,GlobalSearchScope,CancellationToken,Task<IReadOnlyList<GlobalSearchRecord>>>? readRecords;
    private ComboBox? recordType;
    private readonly Label searchStatus=new();
    private CancellationTokenSource? pendingSearch;
    private int generation;
    private bool searching;
    internal GlobalSearchRecord? SelectedRecord { get; private set; }
    public NavigationCommand? SelectedCommand { get; private set; }
    public CommandPaletteDialog(IReadOnlyList<NavigationCommand> source, string? initialQuery = null)
        :this(source,initialQuery,null) { }
    internal CommandPaletteDialog(IReadOnlyList<NavigationCommand> source,string? initialQuery,
        Func<string,GlobalSearchScope,CancellationToken,Task<IReadOnlyList<GlobalSearchRecord>>>? readRecords)
    {
        this.source = source;this.readRecords=readRecords;
        Text = readRecords is null?"Search FMSC":"Find Records";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.S(620), AppScale.S(430));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        KeyPreview = true;
        Build();
        initialQuery = readRecords is null?StripDigits(initialQuery):GlobalSearchInput.Normalize(initialQuery);
        Populate(initialQuery);
        search.Text = initialQuery;
        search.SelectionStart = search.TextLength;
    }
    public static string StripDigits(string? text)=>string.IsNullOrEmpty(text)?string.Empty:new string(text.Where(c=>!char.IsDigit(c)).ToArray());
    private void Build()
    {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(AppScale.S(18))
        };
        var title = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.S(32),
            Text = readRecords is null?"Quick Navigation":"Find Records",
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        search.Dock = DockStyle.Top;
        search.Height = AppScale.S(42);
        search.Font = AppTheme.NormalFont;
        search.BorderStyle = BorderStyle.FixedSingle;
        search.PlaceholderText = readRecords is null?"Search a page or module...":"Student name, admission, bill or receipt number…";
        if(readRecords is not null)search.MaxLength=GlobalSearchInput.MaximumLength;
        search.Margin = new Padding(0, AppScale.S(8), 0, AppScale.S(10));
        if(readRecords is null)search.KeyPress+=(_,e)=>{if(char.IsDigit(e.KeyChar)){e.Handled=true;System.Media.SystemSounds.Beep.Play();}};
        search.TextChanged += async (_,_) => {
            if(readRecords is null)
            {
                string clean=StripDigits(search.Text);
                if(clean!=search.Text){int caret=Math.Max(0,search.SelectionStart-(search.TextLength-clean.Length));search.Text=clean;search.SelectionStart=Math.Min(caret,clean.Length);return;}
            }
            await SearchAsync();
        };
        search.KeyDown += SearchKeyDown;
        results.Dock = DockStyle.Fill;
        results.BorderStyle = BorderStyle.None;
        results.BackColor = AppTheme.Surface;
        results.ForeColor = AppTheme.Text;
        results.Font = AppTheme.NormalFont;
        results.IntegralHeight = false;
        if(readRecords is not null)
        {
            results.DrawMode=DrawMode.OwnerDrawFixed;
            results.ItemHeight=AppMetrics.TextHeight(AppTheme.NormalFont)*2+AppScale.S(10);
            results.DrawItem+=DrawResult;
        }
        searchStatus.Dock=DockStyle.Bottom;searchStatus.Height=AppScale.S(32);
        searchStatus.Font=AppTheme.SmallFont;searchStatus.ForeColor=AppTheme.MutedText;
        searchStatus.AutoEllipsis=true;searchStatus.Visible=readRecords is not null;
        results.DoubleClick += (_, _) => Accept();
        results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); } };
        var hint = new Label {
            Dock = DockStyle.Bottom,
            Height = AppScale.S(26),
            Text = readRecords is null?"Enter to navigate  •  Esc to close":"Ctrl+K to open  •  Enter to open record  •  Esc to close",
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(results);
        card.Controls.Add(searchStatus);
        card.Controls.Add(hint);
        if(readRecords is not null)
        {
            var filter=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(0,AppScale.S(4),0,AppScale.S(8))};
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            filter.Controls.Add(new Label {Text="Search in",AutoSize=true,Anchor=AnchorStyles.Left},0,0);
            recordType=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName="Search in",Font=AppTheme.NormalFont};
            recordType.Items.Add(GlobalSearchScope.All);
            if(GlobalRecordSearch.CanRead("Student"))recordType.Items.Add(GlobalSearchScope.Students);
            if(GlobalRecordSearch.CanRead("Bill")){recordType.Items.Add(GlobalSearchScope.Bills);recordType.Items.Add(GlobalSearchScope.Receipts);}
            recordType.SelectedIndex=0;
            recordType.SelectedIndexChanged+=async(_,_)=>await SearchAsync();
            filter.Controls.Add(recordType,1,0);card.Controls.Add(filter);
        }
        card.Controls.Add(search);
        card.Controls.Add(title);
        Controls.Add(card);
        Shown += async (_, _) => {search.Focus();await SearchAsync();};
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; } };
    }
    private void Populate(string query)
    {
        results.BeginUpdate();
        results.Items.Clear();
        /* Match every search word in any order. */
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var command in source)
        {
            if (string.IsNullOrWhiteSpace(command.Caption))
                continue;
            string caption=command.Caption;
            if (words.Length > 0 && !words.All(w => caption.Contains(w, StringComparison.OrdinalIgnoreCase)))
                continue;
            results.Items.Add(command);
        }
        results.EndUpdate();
        if (results.Items.Count > 0)
            results.SelectedIndex = 0;
    }
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            if (results.Items.Count > 0)
                results.SelectedIndex = Math.Min(results.SelectedIndex + 1, results.Items.Count - 1);
        }
        else if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            if (results.Items.Count > 0)
                results.SelectedIndex = Math.Max(results.SelectedIndex - 1, 0);
        }
        else if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Accept();
        }
    }
    private void Accept()
    {
        if(searching){searchStatus.Text="Searching records…";return;}
        if(results.SelectedItem is GlobalSearchRecord record)
        {
            if(!GlobalRecordSearch.CanRead(record.Kind)){searchStatus.Text="Access changed. Search again.";return;}
            SelectedRecord=record;DialogResult=DialogResult.OK;
        }
        else if(results.SelectedItem is NavigationCommand command)
        {SelectedCommand=command;DialogResult=DialogResult.OK;}
    }
    private async Task SearchAsync()
    {
        pendingSearch?.Cancel();pendingSearch?.Dispose();pendingSearch=new CancellationTokenSource();
        var token=pendingSearch.Token;int request=++generation,actor=CurrentUser.UserId;
        string query=GlobalSearchInput.Normalize(search.Text);
        var scope=recordType?.SelectedItem is GlobalSearchScope selected?selected:GlobalSearchScope.All;
        Populate(query);searching=false;
        if(readRecords is null||!GlobalSearchInput.CanSearch(query)) {searchStatus.Text=readRecords is null?"Enter keywords to find a page.":"Enter a student name, admission, bill or receipt number.";return;}
        searching=true;searchStatus.Text="Searching records…";
        try
        {
            await Task.Delay(300,token);
            var records=await readRecords(query,scope,token);
            if(IsDisposed||request!=generation||token.IsCancellationRequested||actor!=CurrentUser.UserId)return;
            results.BeginUpdate();
            try
            {
                results.Items.Clear();results.Items.AddRange(records.Where(r=>GlobalRecordSearch.CanRead(r.Kind)&&GlobalRecordSearch.MatchesScope(r.Kind,scope)).Take(20).Cast<object>().ToArray());
                if(results.Items.Count>0)results.SelectedIndex=0;
            }
            finally {results.EndUpdate();}
            searchStatus.Text=results.Items.Count>=20?"Showing the first 20 record matches. Enter more detail to narrow the results.":
                results.Items.Count==0?"No matching records. Check the name or number, or choose All.":$"{results.Items.Count} record match(es).";
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception ex)
        {
            if(!IsDisposed&&request==generation&&actor==CurrentUser.UserId)
            {AppExceptionHandler.LogNonFatal(ex,"Global record search");searchStatus.Text="Record search unavailable. Please try again.";}
        }
        finally {if(!IsDisposed&&request==generation)searching=false;}
    }
    private void DrawResult(object? sender,DrawItemEventArgs e)
    {
        if(e.Index<0||e.Index>=results.Items.Count)return;
        bool selected=(e.State&DrawItemState.Selected)!=0;
        var color=selected?SystemColors.HighlightText:AppTheme.Text;
        using(var brush=new SolidBrush(selected?SystemColors.Highlight:AppTheme.Surface))e.Graphics.FillRectangle(brush,e.Bounds);
        object item=results.Items[e.Index];
        string title=item is GlobalSearchRecord record?record.Heading:((NavigationCommand)item).Caption;
        string detail=item is GlobalSearchRecord row?row.Context:"Page navigation";
        var top=new Rectangle(e.Bounds.Left+6,e.Bounds.Top+3,e.Bounds.Width-12,e.Bounds.Height/2);
        var bottom=new Rectangle(top.Left,top.Bottom,top.Width,e.Bounds.Height/2-3);
        TextRenderer.DrawText(e.Graphics,title,AppTheme.NormalFont,top,color,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics,detail,AppTheme.SmallFont,bottom,selected?color:AppTheme.MutedText,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        e.DrawFocusRectangle();
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        generation++;pendingSearch?.Cancel();pendingSearch?.Dispose();pendingSearch=null;base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){generation++;pendingSearch?.Cancel();pendingSearch?.Dispose();pendingSearch=null;}
        base.Dispose(disposing);
    }

}

