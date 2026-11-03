namespace FMSC_SMS.Core;

/// <summary>Named settings in the existing per-user report store. Applying never runs a query.</summary>
internal sealed partial class ReportSavedViewsBar : FlowLayoutPanel
{
    private readonly string pageKey;
    private readonly int actor=CurrentUser.UserId;
    private readonly Func<Dictionary<string,string>> capture;
    private readonly Action<IReadOnlyDictionary<string,string>> restore;
    private readonly Action<string,bool> status;
    private readonly Func<bool> ready;
    private readonly ModernDatePicker? from,to;
    private readonly ModernComboBox views=new(),period=new();
    private readonly ModernButton apply,save,rename,delete;
    private bool resolving;
    internal bool IsApplying { get; private set; }
    internal bool IsResolvingDates=>resolving;
    internal string DatePeriod => from is null ? ReportDatePeriod.Fixed : period.Text;

    internal ReportSavedViewsBar(string pageKey,Func<Dictionary<string,string>> capture,
        Action<IReadOnlyDictionary<string,string>> restore,Action<string,bool> status,Func<bool>? ready=null,
        ModernDatePicker? from=null,ModernDatePicker? to=null,Func<DataTable?>? academicYears=null,Action<int>? selectAcademicYear=null)
    {
        this.academicYears=academicYears;this.selectAcademicYear=selectAcademicYear;
        this.pageKey=pageKey;this.capture=capture;this.restore=restore;this.status=status;this.ready=ready ?? (()=>true);this.from=from;this.to=to;
        Dock=DockStyle.Top; AutoSize=true; WrapContents=true; Margin=Padding.Empty;
        Padding=new Padding(0,0,0,AppMetrics.SpacingSm); BackColor=AppTheme.PageBackground;
        Controls.Add(ChoiceField("Saved View",views,310));
        apply=Button("Apply View",ApplySelected);save=Button("Save View",SaveCurrent);rename=Button("Rename View",RenameSelected);delete=Button("Delete View",DeleteSelected);
        if (from is not null && to is not null)
        {
            period.Items.AddRange(DateChoices);period.SelectedIndex=0;Controls.Add(ChoiceField("Dates",period,245));
            period.SelectedIndexChanged+=(_,_) => DateChoiceChanged();
            if(academicYears is not null) { changePeriod=Button("Change Period",()=>ChooseAcademicPeriod(false));changePeriod.Visible=false; }
            EventHandler manual=(_,_) => { if (!IsApplying && !resolving) period.SelectedItem=ReportDatePeriod.Fixed; };
            from.ValueChanged+=manual;to.ValueChanged+=manual;
            Disposed+=(_,_) => { from.ValueChanged-=manual;to.ValueChanged-=manual; };
        }
        views.SelectedIndexChanged+=(_,_) => RefreshAvailability();
        RefreshChoices("");
    }
    private static Control ChoiceField(string caption,ModernComboBox combo,int width)
    {
        var field=new Panel {Width=AppScale.Scale(width),Height=AppMetrics.InputControlHeight,Margin=new Padding(0,0,6,4)};
        combo.AccessibleName=caption;combo.Dock=DockStyle.Fill;
        var label=new Label {Text=caption,Dock=DockStyle.Left,Width=AppMetrics.TextWidth(caption,AppTheme.NormalFont,12),
            Font=AppTheme.NormalFont,ForeColor=AppTheme.Text,TextAlign=ContentAlignment.MiddleLeft};
        field.Controls.Add(combo);field.Controls.Add(label);return field;
    }
    private ModernButton Button(string text,Action clicked)
    {
        var button=new ModernButton { Text=text,ThemeRole="Secondary",Height=AppMetrics.ButtonHeight,Margin=new Padding(4,0,0,4) };
        AppMetrics.FitButtonWidth(button,minWidth:90);button.Click+=(_,_)=>clicked();Controls.Add(button);return button;
    }
    internal Control Wrap(Control content)
    {
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(this,0,0);content.Dock=DockStyle.Fill;root.Controls.Add(content,0,1);return root;
    }
    internal void RefreshAvailability()
    {
        bool available=actor>0 && actor==CurrentUser.UserId && ready() && !IsApplying;
        save.Enabled=views.Enabled=available;
        period.Enabled=available && (from?.Enabled ?? true);
        if(changePeriod is not null) { changePeriod.Visible=ReportAcademicPeriod.Months(DatePeriod)>0;changePeriod.Enabled=period.Enabled; }
        apply.Enabled=rename.Enabled=delete.Enabled=available && views.SelectedIndex>0;
    }
    private bool Available()
    {
        if (actor>0 && actor==CurrentUser.UserId && ready() && !IsApplying) return true;
        status("Wait for the report filters to load or the current operation to finish before using saved views.",true);return false;
    }
    private string SelectedName => views.SelectedIndex>0 ? views.Text : "";
    private void RefreshChoices(string select)
    {
        views.Items.Clear();views.Items.Add("Current settings");
        views.Items.AddRange(ReportLayoutStore.GetViews(pageKey).Select(v=>(object)v.Name).ToArray());
        views.SelectedItem=views.Items.Cast<object>().FirstOrDefault(v=>string.Equals(v.ToString(),select,StringComparison.CurrentCultureIgnoreCase)) ?? views.Items[0];
        RefreshAvailability();
    }
    internal Dictionary<string,string> Snapshot()
    {
        var state=capture();state["$version"]="1";state["$dates"]=DatePeriod;SaveAcademicPeriod(state);return state;
    }
    internal void ApplyState(IReadOnlyDictionary<string,string> state)
    {
        if (!Available()) return;
        if (state.GetValueOrDefault("$version")!="1") throw new ReportViewValidationException("This saved view uses an unsupported format.");
        string dates=state.GetValueOrDefault("$dates") ?? ReportDatePeriod.Fixed;
        if (!DateChoices.Contains(dates)) throw new ReportViewValidationException("The saved date period is not supported.");
        var previous=capture();string previousPeriod=DatePeriod;
        int oldYear=academicYearId;DateTime? oldStart=academicStart;
        IsApplying=true;
        try
        {
            RestoreAcademicPeriod(state,dates);
            restore(state);
            if (from is not null) period.SelectedItem=dates;
            ResolveDates();
        }
        catch
        {
            academicYearId=oldYear;academicStart=oldStart;
            restore(previous);
            if (from is not null) period.SelectedItem=previousPeriod;
            throw;
        }
        finally { IsApplying=false;lastDatePeriod=DatePeriod;RefreshAvailability(); }
    }
    internal void ResolveDates()
    {
        if (from is null || to is null || !from.Enabled || DatePeriod==ReportDatePeriod.Fixed || resolving) return;
        var range=ReportAcademicPeriod.Months(DatePeriod)>0?ResolveAcademicPeriod():ReportDatePeriod.Resolve(DatePeriod,DateTime.Today);
        resolving=true;
        try
        {
            if (range.From is { } start) from.Value=start;
            if (range.To is { } end) to.Value=end;
            from.Checked=range.From.HasValue;to.Checked=range.To.HasValue;
        }
        finally { resolving=false; }
    }
    internal void ResetDates()
    {
        if (from is not null) period.SelectedItem=ReportDatePeriod.Fixed;
    }
    internal void ResetSelection()
    {
        if (views.Items.Count > 0) views.SelectedIndex = 0;
        ResetDates();
        RefreshAvailability();
    }
    private void ApplySelected()
    {
        if (!Available() || SelectedName.Length==0) return;
        var view=ReportLayoutStore.GetViews(pageKey).FirstOrDefault(v=>string.Equals(v.Name,SelectedName,StringComparison.CurrentCultureIgnoreCase));
        if (view is null) { RefreshChoices("");status("That saved view is no longer available.",true);return; }
        try { ApplyState(view.State);status($"View \"{view.Name}\" applied. Review the settings, then run the report.",false); }
        catch(Exception ex) { status("The view could not be applied. "+DbMessage.UserMessage(ex),true); }
    }
    private void SaveCurrent()
    {
        if (!Available()) return;
        string? name=ReportNameDialog.Prompt(FindForm(),"Save View","Save these report settings as:",SelectedName);
        if (name is null) return;
        if (ReportLayoutStore.GetViews(pageKey).Any(v=>string.Equals(v.Name,name,StringComparison.CurrentCultureIgnoreCase))
            && !ModernMessageDialog.Confirm(FindForm(),$"Replace saved view \"{name}\" with the current settings?","Save View",defaultYes:false)) return;
        var result=ReportLayoutStore.SaveView(pageKey,new ReportSavedView {Name=name,State=Snapshot()});
        if (!result.Succeeded) { status("The view was not saved. "+DbMessage.UserMessage(result.Error!),true);return; }
        RefreshChoices(name);status($"View \"{name}\" saved."+(result.RecoveryPath is null ? "" : " Damaged previous settings were backed up for recovery."),false);
    }
    private void RenameSelected()
    {
        if (!Available() || SelectedName.Length==0) return;
        string original=SelectedName;
        string? name=ReportNameDialog.Prompt(FindForm(),"Rename View","New name for this saved view:",original);
        if (name is null) return;
        var result=ReportLayoutStore.RenameView(pageKey,original,name);
        if (!result.Succeeded) { status("The view was not renamed. "+DbMessage.UserMessage(result.Error!),true);return; }
        RefreshChoices(name);status(result.Changed ? $"View renamed to \"{name}\"." : "The saved view was unchanged.",false);
    }
    private void DeleteSelected()
    {
        if (!Available() || SelectedName.Length==0) return;
        string name=SelectedName;
        if (!ModernMessageDialog.Confirm(FindForm(),$"Delete saved view \"{name}\"?","Delete View",defaultYes:false)) return;
        var result=ReportLayoutStore.DeleteView(pageKey,name);
        if (!result.Succeeded) { status("The view was not deleted. "+DbMessage.UserMessage(result.Error!),true);return; }
        RefreshChoices("");status(result.Changed ? $"View \"{name}\" deleted." : "That saved view is already absent.",false);
    }
}
