using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed class OutstandingBillsPage : V2Page,IBusyPage
{
    private readonly LookupBox campus=new(),year=new(),cls=new(),section=new();
    private readonly ModernComboBox comparison=HumanUi.Combo(),paper=HumanUi.Combo();
    private readonly ModernNumericTextBox amount=HumanUi.Number(),through=HumanUi.Number();
    private readonly ModernTextBox search=HumanUi.TextBox(false,100);
    private readonly Label selectedSummary=new(){Dock=DockStyle.Fill,AutoSize=true,ForeColor=AppTheme.MutedText,Padding=new Padding(4,6,0,4)};
    private readonly ModernButton preview;
    private readonly BindingSource selectionSource=new();
    private readonly Func<OutstandingBillScope,Task<DataTable>> readBalances;
    private DataTable? roster;
    private OutstandingBillScope? loadedScope;
    private bool changingSelection;
    public bool IsBusy=>BusyOperationRunning;
    public string BusyMessage=>"Outstanding bills are being prepared. Wait for the preview before leaving.";
    public OutstandingBillsPage():this(true,null){ }
    internal OutstandingBillsPage(bool initialize,Func<OutstandingBillScope,Task<DataTable>>? readBalances)
        :base(PageTitles.OutstandingBills,"Choose students by placement and balance, then preview their bills in the standard format.")
    {
        this.readBalances=readBalances??ReadBalancesAsync;
        comparison.Items.AddRange(new object[]{"Any outstanding balance","At least (≥)","More than (>)","At most (≤)","Less than (<)","Exactly (=)","Between (inclusive)"});
        comparison.SelectedIndex=0;
        paper.Items.AddRange(new object[]{"Thermal","A4 sheets"});paper.SelectedIndex=0;
        foreach(var number in new[]{amount,through}){number.DecimalPlaces=0;number.WholeRupeesOnly=true;}
        search.PlaceholderText="Admission / QR number, student or father name";
        var load=AddBusyAction("Load Students",LoadStudentsAsync,"Primary");
        preview=AddBusyAction("Preview Selected Bills",PreviewAsync,"Secondary");preview.Enabled=false;
        FinanceHelp.Attach(this,preview,"Prepare bills for the selected students using their posted unpaid fees. This does not receive payment.","A bill showing Rs. 1,000 due is a request for payment; the student still owes it until money or advance is applied.");
        FinanceHelp.Attach(this,comparison,"Choose which students to include by the size of their outstanding balance. This only filters the list.","At least Rs. 1,000 includes a student owing Rs. 1,000 or Rs. 1,500, but excludes one owing Rs. 500.");
        preview.EnabledChanged+=(_,_)=>{
            if(preview.Enabled && (Selected().Count==0 || loadedScope!=Scope() || !CurrentUser.HasPermission("Fees.View")))preview.Enabled=false;
        };
        var all=AddAction("Select All",(_,_)=>SetSelection(true,false),"Secondary");
        var shown=AddAction("Select Shown",(_,_)=>SetSelection(true,true),"Secondary");
        var none=AddAction("Clear Selection",(_,_)=>SetSelection(false,false),"Secondary");
        FinanceHelp.Attach(this,all,"Check every student in the loaded list, including students hidden by the search.","Search shows one child from six loaded students. Select All checks all six.");
        FinanceHelp.Attach(this,shown,"Check only the students shown by the current search. Clear the checks for students hidden by the search.","Search shows two children from six loaded students. Select Shown checks those two and unchecks the other four.");
        FinanceHelp.Attach(this,none,"Uncheck every student in the loaded list, including students hidden by the search.","Clear Selection leaves no students selected for bill preview.");
        RelocateFromActions(load,preview,all,shown,none);
        Grid.AutoGenerateColumns=false;Grid.ReadOnly=false;Grid.AllowUserToAddRows=false;
        Grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Include",DataPropertyName="Include",HeaderText="Print",Width=65});
        foreach(var (field,label,width) in new[]{("AdmissionNumber","Adm. / QR No.",115),("StudentName","Student",170),
            ("FatherName","Father",170),("CampusName","Campus",120),("ClassSection","Class – Section",140),
            ("AcademicYear","Academic Year",115),("Outstanding","Outstanding (Rs.)",140)})
            Grid.Columns.Add(new DataGridViewTextBoxColumn{Name=field,DataPropertyName=field,HeaderText=label,ReadOnly=true,MinimumWidth=width,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        Grid.Columns["Outstanding"].DefaultCellStyle.Format="N0";
        Grid.Columns["Outstanding"].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
        Grid.DataSource=selectionSource;
        Grid.CurrentCellDirtyStateChanged+=(_,_)=>{if(Grid.IsCurrentCellDirty)Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        Grid.CellValueChanged+=(_,_)=>{if(!changingSelection)UpdateSummary();};
        search.TextChanged+=(_,_)=>{if(roster is not null){OutstandingBillSelection.Search(roster,search.Text);UpdateSummary();}};
        foreach(var lookup in new[]{campus,year,cls,section})lookup.SelectedIndexChanged+=(_,_)=>FiltersChanged();
        comparison.SelectedIndexChanged+=(_,_)=>{UpdateAmountFields();FiltersChanged();};
        amount.TextChanged+=(_,_)=>FiltersChanged();through.TextChanged+=(_,_)=>FiltersChanged();

        var fields=ReportUi.Bar(("Campus",campus),("Academic Year",year),("Class",cls),("Section",section));
        var amounts=ReportUi.Bar(("Outstanding balance",comparison),("Amount (Rs.)",amount),("Through (Rs.)",through),("Paper format",paper));
        var filters=HumanUi.Card("Choose Students","Placement filters select students. Each bill includes their full posted unpaid balance, including future months.");
        var filterBody=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        filterBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        filterBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var loadActions=HumanUi.ButtonRow(load);loadActions.Dock=DockStyle.Fill;loadActions.Margin=Padding.Empty;
        filterBody.Controls.Add(fields,0,0);filterBody.Controls.Add(amounts,0,1);filterBody.Controls.Add(loadActions,0,2);filters.Content.Controls.Add(filterBody);
        var students=HumanUi.Card("Students to Bill","Search keeps your checks. Select Shown checks only the search results. Review the selection before previewing.");
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var selectActions=HumanUi.ButtonRowInOrder(all,shown,none,preview);selectActions.Dock=DockStyle.Fill;selectActions.Margin=Padding.Empty;
        body.Controls.Add(HumanUi.Field("Find Student",search),0,0);body.Controls.Add(selectActions,0,1);body.Controls.Add(Grid,0,2);body.Controls.Add(selectedSummary,0,3);students.Content.Controls.Add(body);
        var root=new TableLayoutPanel{ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldCardHeight(2)+AppMetrics.ButtonRowHeight));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(filters,0,0);root.Controls.Add(students,0,1);
        var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};viewport.Controls.Add(root);
        void Arrange()=>root.Size=new Size(Math.Max(AppScale.Scale(760),viewport.ClientSize.Width-SystemInformation.VerticalScrollBarWidth),Math.Max(AppScale.Scale(650),viewport.ClientSize.Height));
        viewport.SizeChanged+=(_,_)=>Arrange();SetContent(viewport);Arrange();
        UpdateAmountFields();UpdateSummary();
        if(initialize)Defer(BindFiltersAsync);
        Disposed+=(_,_)=>{selectionSource.Dispose();roster?.Dispose();};
    }

    private OutstandingBillScope Scope()=>new(campus.SelectedId,year.SelectedId,cls.SelectedId,section.SelectedId,
        (BillBalanceComparison)comparison.SelectedIndex,amount.Value,through.Value);
    private void UpdateAmountFields(){amount.Enabled=comparison.SelectedIndex>0;through.Enabled=comparison.SelectedIndex==(int)BillBalanceComparison.Between;}
    private void FiltersChanged(){preview.Enabled=false;if(loadedScope is not null)SetStatus("Filters changed. Load Students to refresh the list.",PageStatusKind.Warning);}
    private void Commit(){Grid.EndEdit();selectionSource.EndEdit();}
    private List<int> Selected()=>roster?.AsEnumerable().Where(row=>row.Field<bool>("Include")).Select(row=>row.Field<int>("AdmissionId")).ToList()??new();
    private void UpdateSummary()
    {
        var ids=Selected();decimal total=roster?.AsEnumerable().Where(row=>row.Field<bool>("Include")).Sum(row=>Convert.ToDecimal(row["Outstanding"]))??0m;
        selectedSummary.Text=$"{ids.Count} selected · {roster?.DefaultView.Count??0} shown · Selected balance: Rs. {total:N0}";
        preview.Enabled=ids.Count>0 && loadedScope==Scope() && CurrentUser.HasPermission("Fees.View");
    }
    private void SetSelection(bool value,bool shownOnly)
    {
        if(roster is null||IsBusy)return;
        Commit();
        // Freeze the target rows before changing the bound view. Publish one
        // completed selection so checkbox edits and repainting cannot lag behind it.
        var shown=shownOnly?roster.DefaultView.Cast<DataRowView>().Select(view=>view.Row).ToHashSet():null;
        changingSelection=true;
        selectionSource.RaiseListChangedEvents=false;
        try
        {
            foreach(DataRow row in roster.Rows){row["Include"]=value&&(shown is null||shown.Contains(row));row.EndEdit();}
        }
        finally
        {
            selectionSource.RaiseListChangedEvents=true;
            selectionSource.ResetBindings(false);
            changingSelection=false;
            UpdateSummary();
        }
    }
    private bool Valid(OutstandingBillScope scope)
    {
        if(scope.Comparison!=BillBalanceComparison.Any && scope.Amount<=0){Warn("Enter a balance amount greater than zero.");amount.Focus();return false;}
        if(scope.Comparison==BillBalanceComparison.Between && scope.Through<scope.Amount){Warn("Through amount must be at least the first amount.");through.Focus();return false;}
        return true;
    }
    private async Task LoadStudentsAsync()
    {
        if(IsBusy||!CurrentUser.HasPermission("Fees.View"))return;
        var scope=Scope();if(!Valid(scope))return;
        int user=CurrentUser.UserId;
        try
        {
            await RunBusyAsync("Loading students…",async()=>{
                using var source=await readBalances(scope);
                if(IsDisposed||scope!=Scope()||CurrentUser.UserId!=user||!CurrentUser.HasPermission("Fees.View"))return;
                var next=OutstandingBillSelection.Select(source,scope);selectionSource.DataSource=null;roster?.Dispose();roster=next;loadedScope=scope;
                OutstandingBillSelection.Search(roster,search.Text);selectionSource.DataSource=roster.DefaultView;
                SetStatus($"{roster.Rows.Count} students match. Bills are refreshed before preview.");
            },this);
        }
        catch(Exception ex){if(!IsDisposed){loadedScope=null;preview.Enabled=false;Fail(ex);}}
        finally{if(!IsDisposed)UpdateSummary();}
    }
    private async Task PreviewAsync()
    {
        if(IsBusy||!CurrentUser.HasPermission("Fees.View"))return;
        Commit();var scope=Scope();var ids=Selected();bool a4=paper.SelectedIndex==1;
        if(ids.Count==0||scope!=loadedScope||!Valid(scope))return;
        try { await RunBusyAsync(BusyMessage,async()=>{
            using var fresh=await readBalances(scope);
            if(IsDisposed||scope!=Scope())return;
            var eligible=fresh.AsEnumerable().Where(row=>scope.Matches(Convert.ToDecimal(row["Outstanding"]))).Select(row=>row.Field<int>("AdmissionId")).ToHashSet();
            if(ids.Any(id=>!eligible.Contains(id))){loadedScope=null;preview.Enabled=false;Warn("Some selected students no longer match the filters. Load Students again before printing.");return;}
            await OutstandingBillsPreview.ShowAsync(FindForm(),ids,()=>!IsDisposed&&scope==Scope()&&ids.SequenceEqual(Selected()),Warn,a4);
        },this); }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{if(!IsDisposed)UpdateSummary();}
    }
    private static Task<DataTable> ReadBalancesAsync(OutstandingBillScope scope)=>DatabaseHelper.QuerySqlAsync(OutstandingBillSelection.Query,
        DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@CampusId",scope.Campus),DatabaseHelper.Int("@AcademicYearId",scope.Year),
        DatabaseHelper.Int("@ClassId",scope.Class),DatabaseHelper.Int("@SectionId",scope.Section));
    private async Task BindFiltersAsync()
    {
        try {
        var tasks=new[]{DatabaseHelper.QueryUserAsync("GetCampuses"),DatabaseHelper.QueryUserAsync("GetAcademicYears"),DatabaseHelper.QueryUserAsync("GetClasses"),ReportUi.SectionFilterListAsync()};
        await Task.WhenAll(tasks);if(IsDisposed)return;
        campus.Bind(tasks[0].Result,"CampusId","CampusName","All");year.Bind(tasks[1].Result,"AcademicYearId","YearName","All");
        cls.Bind(tasks[2].Result,"ClassId","ClassName","All");section.Bind(tasks[3].Result,"SectionId","SectionName","All");
        } catch(Exception ex){if(!IsDisposed)Fail(ex);}
    }
}
