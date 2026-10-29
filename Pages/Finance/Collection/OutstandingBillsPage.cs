using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed class OutstandingBillsPage : V2Page,IBusyPage
{
    private readonly LookupBox campus=new(),year=new(),cls=new(),section=new();
    private readonly ModernComboBox comparison=HumanUi.Combo(),paper=HumanUi.Combo(),billGrouping=HumanUi.Combo();
    private readonly ModernNumericTextBox amount=HumanUi.Number(),through=HumanUi.Number();
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
        billGrouping.Items.AddRange(new object[]{"Individual","By family"});billGrouping.SelectedIndex=0;
        foreach(var number in new[]{amount,through}){number.DecimalPlaces=0;number.WholeRupeesOnly=true;}
        var load=AddBusyAction("Load Students",LoadStudentsAsync,"Primary");
        preview=AddBusyAction("Preview Selected Bills",PreviewAsync,"Secondary");preview.Enabled=false;
        FinanceHelp.Attach(this,preview,"Prepare bills for the selected students using their posted unpaid fees. This does not receive payment.","A bill showing Rs. 1,000 due is a request for payment; the student still owes it until money or advance is applied.");
        FinanceHelp.Attach(this,comparison,"Choose which students to include by the size of their outstanding balance. This only filters the list.","At least Rs. 1,000 includes a student owing Rs. 1,000 or Rs. 1,500, but excludes one owing Rs. 500.");
        preview.EnabledChanged+=(_,_)=>{
            if(preview.Enabled && (Selected().Count==0 || loadedScope!=Scope() || !CurrentUser.HasPermission("Fees.View")))preview.Enabled=false;
        };
        var all=AddAction("Select All",(_,_)=>SetSelection(true),"Secondary");
        var none=AddAction("Clear Selection",(_,_)=>SetSelection(false),"Secondary");
        FinanceHelp.Attach(this,all,"Check every student in the loaded bulk list.","Load a class, then Select All to include every matching student.");
        FinanceHelp.Attach(this,none,"Uncheck every student in the loaded bulk list.","Clear Selection leaves no students selected for bill preview.");
        RelocateFromActions(load,preview,all,none);
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
        foreach(var lookup in new[]{campus,year,cls,section})lookup.SelectedIndexChanged+=(_,_)=>FiltersChanged();
        comparison.SelectedIndexChanged+=(_,_)=>{UpdateAmountFields();FiltersChanged();};
        amount.TextChanged+=(_,_)=>FiltersChanged();through.TextChanged+=(_,_)=>FiltersChanged();

        var fields=ReportUi.Bar(("Campus",campus),("Academic Year",year),("Class",cls),("Section",section));
        var amounts=ReportUi.Bar(("Student balance",comparison),("Amount (Rs.)",amount),("Through (Rs.)",through),("Paper format",paper));
        var filters=HumanUi.Card("Bulk Bill Filters","Choose placement, balance, paper and grouping. Each bill includes the full posted unpaid balance, including future months.");
        var filterBody=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        filterBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        var loadActions=HumanUi.ButtonRow(load);loadActions.Dock=DockStyle.Fill;
        loadActions.Margin=new Padding(0,Math.Max(0,AppMetrics.FieldRowHeight-AppMetrics.ButtonHeight-AppScale.Scale(4)),0,0);
        var printOptions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        printOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));printOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        printOptions.Controls.Add(ReportUi.Bar(("Bill grouping",billGrouping)),0,0);printOptions.Controls.Add(loadActions,1,0);
        filterBody.Controls.Add(fields,0,0);filterBody.Controls.Add(amounts,0,1);filterBody.Controls.Add(printOptions,0,2);filters.Content.Controls.Add(filterBody);
        var students=HumanUi.Card("Students to Bill","All matching students start selected. Uncheck any students to exclude before previewing the bulk bills.");
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var selectActions=HumanUi.ButtonRowInOrder(all,none,preview);selectActions.Dock=DockStyle.Fill;selectActions.Margin=Padding.Empty;
        body.Controls.Add(selectActions,0,0);body.Controls.Add(Grid,0,1);body.Controls.Add(selectedSummary,0,2);students.Content.Controls.Add(body);
        var root=new TableLayoutPanel{ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldCardHeight(3)));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
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
        selectedSummary.Text=$"{ids.Count} selected of {roster?.Rows.Count??0} loaded · Selected balance: Rs. {total:N0} · Full posted outstanding — all periods, including future posted months";
        preview.Enabled=ids.Count>0 && loadedScope==Scope() && CurrentUser.HasPermission("Fees.View");
    }
    private void SetSelection(bool value)
    {
        if(roster is null||IsBusy)return;
        Commit();
        // Publish one completed selection after committing the active checkbox.
        changingSelection=true;
        selectionSource.RaiseListChangedEvents=false;
        try
        {
            foreach(DataRow row in roster.Rows){row["Include"]=value;row.EndEdit();}
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
                selectionSource.DataSource=roster.DefaultView;
                SetStatus($"{roster.Rows.Count} students match. Balances and readiness are reviewed before numbered bills are prepared.");
            },this);
        }
        catch(Exception ex){if(!IsDisposed){loadedScope=null;preview.Enabled=false;Fail(ex);}}
        finally{if(!IsDisposed)UpdateSummary();}
    }
    private async Task PreviewAsync()
    {
        if(IsBusy||!CurrentUser.HasPermission("Fees.View"))return;
        Commit();var scope=Scope();var ids=Selected();bool a4=paper.SelectedIndex==1,byFamily=billGrouping.SelectedIndex==1;
        if(ids.Count==0||scope!=loadedScope||!Valid(scope))return;
        try { await RunBusyAsync(BusyMessage,async()=>{
            using var fresh=await readBalances(scope);
            if(IsDisposed||scope!=Scope())return;
            var eligible=fresh.AsEnumerable().Where(row=>scope.Matches(Convert.ToDecimal(row["Outstanding"]))).Select(row=>row.Field<int>("AdmissionId")).ToHashSet();
            if(ids.Any(id=>!eligible.Contains(id))){loadedScope=null;preview.Enabled=false;Warn("Some selected students no longer match the filters. Load Students again before printing.");return;}
            await OutstandingBillsPreview.ShowAsync(FindForm(),ids,()=>!IsDisposed&&scope==Scope()&&ids.SequenceEqual(Selected())&&byFamily==(billGrouping.SelectedIndex==1),Warn,a4,byFamily);
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
