using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Administration;

public abstract partial class AccessEditorPage
{
    internal readonly ModernTextBox NameInput = HumanUi.TextBox(false,100);
    internal readonly ModernTextBox DescriptionInput = HumanUi.TextBox(false,250);
    internal readonly ModernCheckBox ForceChange = new() { Text="Require password change at next sign-in",AutoSize=true };
    internal readonly DataGridView Assignments = HumanUi.Grid(false);
    internal ModernButton SaveAction = null!, NewAction = null!, ReloadAction = null!, CheckAction = null!;
    private protected readonly TableLayoutPanel Fields = new() { AutoSize=true,Dock=DockStyle.Top,ColumnCount=1,Margin=Padding.Empty };
    private readonly ModernTextBox search = HumanUi.TextBox(false,100), choiceSearch = HumanUi.TextBox(false,100);
    private readonly Label editorTitle = new() { AutoSize=false,AutoEllipsis=true,Dock=DockStyle.Top,Font=AppTheme.BodyBoldFont,
        Height=AppMetrics.TextHeight(AppTheme.BodyBoldFont)+16,Padding=new Padding(0,4,0,8),UseMnemonic=false };
    private readonly Label listCount = new() { Dock=DockStyle.Fill,AutoSize=true,ForeColor=AppTheme.MutedText,UseMnemonic=false };
    private readonly TextBox summary = new() { ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Top,
        Height=AppScale.Scale(115),BorderStyle=BorderStyle.None,TabStop=false,Font=AppTheme.NormalFont,BackColor=AppTheme.Surface,ForeColor=AppTheme.Text };
    private readonly Panel editorViewport = new() { Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(12) };
    private readonly TableLayoutPanel editorStack = new() { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty,Padding=Padding.Empty };
    private readonly TableLayoutPanel workspace = new() { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
    private Control listPanel = null!;
    private System.Windows.Forms.Timer searchTimer = null!, choiceTimer = null!;
    private DataTable? recordsData, choicesData;
    private bool bindingList, narrow;
    internal ModernTextBox AssignmentSearch => choiceSearch;
    internal ModernTextBox RecordSearch => search;
    internal DataGridView RecordGrid => Grid;
    internal string EditorContext => editorTitle.Text;

    private void BuildLayout()
    {
        NameInput.MaxLength=Kind==AccessEditorKind.Users ? 100 : 50;
        NameInput.TextChanged+=(_,_)=>DraftChanged(); DescriptionInput.TextChanged+=(_,_)=>DraftChanged();ForceChange.CheckedChanged+=(_,_)=>DraftChanged();
        NewAction=AddAction(Kind==AccessEditorKind.Users ? "New User" : "New Role",(_,_)=>NewRecord(),"Secondary");
        SaveAction=AddAction(Kind==AccessEditorKind.Users ? "Save User" : "Save Role",async(_,_)=>await SaveAsync(),"Success");
        ReloadAction=AddAction("Reload",async(_,_)=>await ReloadAsync(),"Secondary");
        CheckAction=AddAction("Check Saved Changes",async(_,_)=>await CheckAsync(),"Secondary");CheckAction.Visible=false;
        search.PlaceholderText=Kind==AccessEditorKind.Users ? "Search users…" : "Search roles…";
        choiceSearch.PlaceholderText=Kind==AccessEditorKind.Users ? "Filter roles…" : "Filter permissions by task or area…";
        searchTimer=HumanUi.CreateSearchDebounceTimer(ApplyListFilter,150);
        choiceTimer=HumanUi.CreateSearchDebounceTimer(ApplyChoiceFilter,150);
        search.TextChanged+=(_,_)=>{searchTimer.Stop();searchTimer.Start();};choiceSearch.TextChanged+=(_,_)=>{choiceTimer.Stop();choiceTimer.Start();};
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){search.Clear();e.SuppressKeyPress=true;}};
        choiceSearch.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){choiceSearch.Clear();e.SuppressKeyPress=true;}};
        WorkspaceState.Remember(search,Kind+":Search");
        Grid.AutoGenerateColumns=false;
        Grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",DataPropertyName="Name",HeaderText=Kind==AccessEditorKind.Users?"User":"Role",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=65});
        Grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Summary",DataPropertyName="Summary",HeaderText="Access",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=35});
        Grid.SelectionChanged+=async(_,_)=>{
            if(bindingList||applying||IsBusy||Grid.CurrentRow?.DataBoundItem is not DataRowView row)return;
            await SelectAsync((int)row["Id"]);
        };
        listPanel=HumanUi.Card(Kind==AccessEditorKind.Users?"User accounts":"Access roles","Select a row to open its details.");
        var listContent=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        listContent.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.InputControlHeight+8));
        listContent.RowStyles.Add(new RowStyle(SizeType.Percent,100));listContent.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.TextHeight(AppTheme.NormalFont)+8));
        listContent.Controls.Add(search,0,0);listContent.Controls.Add(Grid,0,1);listContent.Controls.Add(listCount,0,2);
        ((HumanCard)listPanel).Content.Controls.Add(listContent);
        Assignments.AutoGenerateColumns=false;
        Assignments.Columns.Add(new DataGridViewCheckBoxColumn{Name="Selected",DataPropertyName="Selected",HeaderText=Kind==AccessEditorKind.Users?"Assign":"Allow",Width=AppScale.Scale(55),AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        Assignments.Columns.Add(new DataGridViewTextBoxColumn{Name="Label",DataPropertyName="Label",HeaderText=Kind==AccessEditorKind.Users?"Role":"Task",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=70});
        Assignments.Columns.Add(new DataGridViewTextBoxColumn{Name="Group",DataPropertyName="Group",HeaderText="Area",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=30});
        Assignments.Height=AppScale.Scale(235);Assignments.Dock=DockStyle.Fill;
        Assignments.CurrentCellDirtyStateChanged+=(_,_)=>{if(!applying&&Assignments.IsCurrentCellDirty)Assignments.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        Assignments.CellValueChanged+=(_,_)=>DraftChanged();
        Assignments.CellToolTipTextNeeded+=(_,e)=>{
            if(e.RowIndex>=0&&Assignments.Rows[e.RowIndex].DataBoundItem is DataRowView row)
                e.ToolTipText=Convert.ToString(row["Description"])+"\n"+Convert.ToString(row["Code"]);
        };
        editorStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        editorStack.Controls.Add(editorTitle,0,0);editorStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorStack.Controls.Add(Fields,0,1);editorStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorStack.Controls.Add(choiceSearch,0,2);editorStack.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.InputControlHeight+10));
        editorStack.Controls.Add(Assignments,0,3);editorStack.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(235)));
        editorStack.Controls.Add(summary,0,4);editorStack.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(115)));
        editorStack.RowCount=5;editorViewport.Controls.Add(editorStack);
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,AppScale.Scale(300)));workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));workspace.Controls.Add(listPanel,0,0);workspace.Controls.Add(editorViewport,1,0);
        SetContent(workspace);
        Resize+=(_,_)=>FitLayout();editorViewport.Resize+=(_,_)=>FitEditorWidth();FitLayout();
    }
    private protected RowStyle AddField(Control control, int? height=null)
    {
        int row=Fields.RowCount;Fields.RowCount=row+1;
        var style=new RowStyle(SizeType.Absolute,height??AppMetrics.FieldRowHeight);Fields.RowStyles.Add(style);Fields.Controls.Add(control,0,row);return style;
    }
    private void FitLayout()
    {
        if(listPanel is null)return;
        bool next=ClientSize.Width<AppScale.Scale(850);
        if(next!=narrow)
        {
            narrow=next;workspace.SuspendLayout();
            workspace.Controls.Remove(listPanel);workspace.Controls.Remove(editorViewport);workspace.ColumnStyles.Clear();workspace.RowStyles.Clear();
            workspace.ColumnCount=next?1:2;workspace.RowCount=next?2:1;
            workspace.ColumnStyles.Add(new ColumnStyle(next?SizeType.Percent:SizeType.Absolute,next?100:AppScale.Scale(300)));
            if(!next)workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            if(next)workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(190)));
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            workspace.Controls.Add(listPanel,0,0);workspace.Controls.Add(editorViewport,next?0:1,next?1:0);workspace.ResumeLayout(true);
        }
        FitEditorWidth();
    }
    private void FitEditorWidth() => editorStack.Width=Math.Max(AppScale.Scale(250),editorViewport.ClientSize.Width-editorViewport.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);

    private DataTable ListTable(AccessCatalog catalog)
    {
        var table=new DataTable();table.Columns.Add("Id",typeof(int));table.Columns.Add("Name");table.Columns.Add("Summary");
        foreach(var item in catalog.Items.OrderByDescending(item=>item.Protected).ThenBy(item=>item.Name,StringComparer.OrdinalIgnoreCase))table.Rows.Add(item.Id,item.Name,item.Summary);
        return table;
    }
    private DataTable ChoiceTable(AccessCatalog catalog, AccessRecord? record)
    {
        var table=new DataTable();table.Columns.Add("ChoiceId",typeof(int));table.Columns.Add("Selected",typeof(bool));
        foreach(string column in new[]{"Label","Group","Code","Description"})table.Columns.Add(column);
        if(Kind==AccessEditorKind.Users)
            foreach(var role in catalog.Roles.Where(role=>record?.Protected==true||!role.Protected).OrderBy(role=>role.Name,StringComparer.OrdinalIgnoreCase))
                table.Rows.Add(role.Id,record?.Protected==true||record?.Choices.Contains(role.Id)==true,role.Name,"Roles",role.Name,role.Description);
        else
            foreach(var permission in catalog.Permissions.OrderBy(permission=>permission.Code,StringComparer.OrdinalIgnoreCase))
                table.Rows.Add(permission.Id,record?.Protected==true||record?.Choices.Contains(permission.Id)==true,
                    string.IsNullOrWhiteSpace(permission.Name)?permission.Code:permission.Name,permission.Code.Split('.')[0],permission.Code,permission.Description);
        return table;
    }
    private void ApplyFrame(AccessCatalog catalog, AccessRecord? record, bool create)
    {
        if(string.IsNullOrWhiteSpace(catalog.Token)||record is not null&&(record.Id<=0||string.IsNullOrWhiteSpace(record.Snapshot)))
            throw new InvalidDataException("The loaded access state cannot be verified.");
        if(record is not null&&(record.MemberCount<0||!catalog.Items.Any(item=>item.Id==record.Id&&item.Name==record.Name&&item.Protected==record.Protected)))
            throw new InvalidDataException("The loaded record does not match its access catalog.");
        var known=Kind==AccessEditorKind.Users?catalog.Roles.Select(role=>role.Id).ToHashSet():catalog.Permissions.Select(permission=>permission.Id).ToHashSet();
        if(record is not null&&record.Choices.Any(id=>!known.Contains(id)))throw new InvalidDataException("The record references access that is missing from its catalog.");
        var nextList=ListTable(catalog);var nextChoices=ChoiceTable(catalog,record);
        var oldCatalog=Catalog;var oldOriginal=Original;var oldBaseline=baseline;var oldUnconfirmed=Unconfirmed;
        bool oldCreate=Creating,oldReload=NeedsReload,oldCommitted=committedWaitingForReload;
        var oldList=recordsData;var oldChoices=choicesData;var oldDraft=CaptureDraft();var extra=CaptureExtra();
        applying=true;bindingList=true;SuspendLayout();
        try
        {
            Catalog=catalog;Original=record;Creating=create;NeedsReload=false;Unconfirmed=null;committedWaitingForReload=false;
            recordsData=nextList;choicesData=nextChoices;
            NameInput.Text=record?.Name??"";DescriptionInput.Text=record?.Description??"";ForceChange.Checked=record?.ForcePasswordChange??true;
            Grid.DataSource=nextList.DefaultView;Assignments.DataSource=nextChoices.DefaultView;
            baseline=CaptureDraft();ClearSecrets();ModeChanged();ApplyListFilter();ApplyChoiceFilter();UpdateSummary();
            ResumeLayout(true);applying=false;bindingList=false;UpdateActions();
        }
        catch
        {
            Catalog=oldCatalog;Original=oldOriginal;baseline=oldBaseline;Unconfirmed=oldUnconfirmed;
            Creating=oldCreate;NeedsReload=oldReload;committedWaitingForReload=oldCommitted;recordsData=oldList;choicesData=oldChoices;
            try
            {
                Grid.DataSource=oldList?.DefaultView;Assignments.DataSource=oldChoices?.DefaultView;
                NameInput.Text=oldDraft.Name;DescriptionInput.Text=oldDraft.Description;ForceChange.Checked=oldDraft.ForcePasswordChange;
                RestoreExtra(extra);ModeChanged();UpdateSummary();RestoreListSelection();
            }
            catch(Exception rollbackError){NeedsReload=true;AppExceptionHandler.LogNonFatal(rollbackError,"Restoring the administration editor after binding failure");}
            finally{ResumeLayout(false);applying=false;bindingList=false;nextList.Dispose();nextChoices.Dispose();}
            throw;
        }
        oldList?.Dispose();oldChoices?.Dispose();
    }
    private void ApplyListFilter()
    {
        if(recordsData is null)return;
        bool was=bindingList;bindingList=true;
        try{recordsData.DefaultView.RowFilter=AdministrativeAccess.Filter(search.Text,"Name","Summary");RestoreListSelection();}
        finally{bindingList=was;}
        bool hidden=Original is not null&&!recordsData.DefaultView.Cast<DataRowView>().Any(row=>(int)row["Id"]==Original.Id);
        listCount.Text=$"{recordsData.DefaultView.Count} of {recordsData.Rows.Count}"+(hidden?" · open record hidden by filter":"");
    }
    private void ApplyChoiceFilter()
    {
        if(choicesData is null)return;
        Assignments.EndEdit();choicesData.DefaultView.RowFilter=AdministrativeAccess.Filter(choiceSearch.Text,"Label","Group","Code","Description");
        UpdateSummary();
    }
    private void RestoreListSelection()
    {
        bool was=bindingList;bindingList=true;
        try
        {
            Grid.ClearSelection();Grid.CurrentCell=null;
            if(Original is null)return;
            foreach(DataGridViewRow row in Grid.Rows)
                if(row.DataBoundItem is DataRowView value&&(int)value["Id"]==Original.Id){row.Selected=true;Grid.CurrentCell=row.Cells["Name"];break;}
        }
        finally{bindingList=was;}
    }
    private void UpdateSummary()
    {
        if(Catalog is null||!HasEditor){summary.Text="Choose a record to review its access.";return;}
        var draft=CaptureDraft();var permissions=Original?.Protected==true?Catalog.Permissions.Select(permission=>permission.Id).ToHashSet()
            :Kind==AccessEditorKind.Users?AdministrativeAccess.EffectivePermissions(Catalog,draft.Choices):draft.Choices.ToHashSet();
        var previous=Kind==AccessEditorKind.Users?AdministrativeAccess.EffectivePermissions(Catalog,Original?.Choices??Array.Empty<int>())
            :(Original?.Choices??Array.Empty<int>()).ToHashSet();
        string text=Original?.Protected==true?"Protected Administrator: full access is managed by the system."
            :$"{draft.Choices.Length} {(Kind==AccessEditorKind.Users?"role(s)":"permission(s)")} selected · {permissions.Count} effective permission(s).";
        if(permissions.Count==0&&Original?.Protected!=true)text+=" No access will be granted.";
        if(HasUnsavedChanges&&Original?.Protected!=true)text+=$"\r\nProposed changes: {permissions.Except(previous).Count()} permission(s) added; {previous.Except(permissions).Count()} removed.";
        if(Kind==AccessEditorKind.Roles&&Original?.MemberCount>0)text+=$"\r\nAssigned to {Original.MemberCount} account(s); access changes affect those assignments.";
        var hints=AdministrativeAccess.Hints(Catalog,permissions);if(hints.Length>0)text+="\r\n"+string.Join(" ",hints);
        text+="\r\n"+string.Join(", ",Catalog.Permissions.Where(permission=>permissions.Contains(permission.Id)).Select(permission=>permission.Name.Length>0?permission.Name:permission.Code));
        summary.Text=text;
    }
    private void UpdateActions()
    {
        bool editable=canManage&&HasEditor&&Original?.Protected!=true&&!IsBusy&&!NeedsReload&&Unconfirmed is null;
        SaveAction.Enabled=editable&&(Creating||HasUnsavedChanges);
        NewAction.Enabled=canManage&&canRead&&Catalog is not null&&!IsBusy&&!NeedsReload&&Unconfirmed is null&&
            (Kind==AccessEditorKind.Roles||Catalog.Roles.Any(role=>!role.Protected));
        ReloadAction.Enabled=canRead&&!IsBusy;CheckAction.Visible=Unconfirmed is not null;CheckAction.Enabled=canRead&&!IsBusy;
        Grid.Enabled=canRead&&!IsBusy;search.Enabled=canRead&&!IsBusy;
        NameInput.ReadOnly=!editable;DescriptionInput.ReadOnly=!editable;ForceChange.Enabled=editable;
        Assignments.ReadOnly=!editable;Assignments.Enabled=HasEditor&&!IsBusy;choiceSearch.Enabled=HasEditor&&!IsBusy;
        ExtraEnablement(editable);
        string state=Unconfirmed is not null?"check save outcome":NeedsReload?"reload required":Original?.Protected==true?"protected":!canManage?"read-only":HasUnsavedChanges?"unsaved changes":"saved details";
        editorTitle.Text=!HasEditor?"Choose a record to begin":Creating?(Kind==AccessEditorKind.Users?"New user · not saved":"New role · not saved"):$"{Original!.Name} · {state}";
    }
}
