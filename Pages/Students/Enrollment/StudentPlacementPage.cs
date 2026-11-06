using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace FMSC_SMS.Pages.Students.Enrollment;

public enum StudentPlacementTask { AnnualPromotion, Transfer, Correction, History }

/// <summary>Dedicated placement pages sharing the same lifecycle and recovery rules.</summary>
// BD-08/09: same-year transfer/correction is distinct from next-year promotion; section-only moves do not reprice fees.
// Keep reviewed plan/recovery identity and require return confirmation before new-placement billing.
public sealed partial class StudentPlacementPage : V2Page, IBusyPage
{
    private readonly int operation;
    private readonly Action<Func<Control>,string,string>? navigate;
    private readonly LookupBox sourceYear=new(), sourceCampus=new(), sourceClass=new(), sourceSection=new();
    private readonly LookupBox targetYear=new(), targetCampus=new(), targetClass=new();
    private readonly ModernDatePicker effective=HumanUi.Date();
    private readonly ModernTextBox reason=HumanUi.TextBox(false,500);
    private readonly ModernTextBox historyReason=HumanUi.TextBox(false,500);
    private readonly ModernDatePicker historyDate=HumanUi.Date();
    private readonly CheckBox carry=new(){Text="Carry monthly concessions",AutoSize=true};
    private readonly ModernComboBox fees=HumanUi.Combo();
    private readonly DataGridView students=HumanUi.Grid(true), history=HumanUi.Grid(false);
    private readonly Label totals=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Margin=Padding.Empty};
    private readonly TableLayoutPanel body=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
    private readonly Control pageContent;
    private readonly TableLayoutPanel fields=new(){Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};
    private readonly ModernButton review,retry,studentActions,correction,findUndo;
    private readonly ModernButton load,suggest,assign,select,clear,undo,historyDates,historyRetry,records,old;
    private DataTable catalog=new(), placements=new(), data=new();
    private bool busy, binding;
    private StyledContextMenu? studentActionsMenu;
    private (Guid Id,string Json,int Year,DateTime Date,string Reason,bool Carry,string Hash)? pending;
    public bool IsBusy=>busy || pending is not null;
    public string BusyMessage=>pending is not null?"A placement plan has an unconfirmed outcome. Use Check / Retry Plan before leaving.":"A placement plan is being reviewed or processed.";
    private bool Annual=>operation==0;
    private bool ViewingHistory=>operation==3;

    public StudentPlacementPage(StudentPlacementTask task=StudentPlacementTask.AnnualPromotion,Action<Func<Control>,string,string>? navigate=null)
        : this(navigate,true,task) { }
    internal StudentPlacementPage(Action<Func<Control>,string,string>? navigate,bool initialize,StudentPlacementTask task=StudentPlacementTask.AnnualPromotion)
        : base(Title(task),Subtitle(task))
    {
        operation=(int)task;this.navigate=navigate;
        foreach(var lookup in PlanChoices)lookup.BindingContext=new BindingContext();
        fees.Items.AddRange(new object[]{"Keep current fees","Destination fees from next month","Destination fees using school cutoff"});fees.SelectedIndex=0;
        for(int i=0;i<2;i++)fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldCardHeight(2,false)));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        load=AddAction("Load Students",async(_,_)=>await Guard(LoadStudents),"Secondary");
        suggest=AddAction("Suggest",(_,_)=>Suggest(),"Secondary");SetToolTip(suggest,"Suggest destination placements for the loaded students.");
        assign=AddAction("Assign",(_,_)=>AssignHighlighted(),"Secondary");SetToolTip(assign,"Assign a decision and destination to highlighted students.");
        select=AddAction("Select All",(_,_)=>SelectRows(true),"Secondary");
        clear=AddAction("Clear Selection",(_,_)=>SelectRows(false),"Secondary");SetToolTip(clear,"Uncheck all students. Tick Process beside only the students you want to include.");
        review=AddAction("Review Promotion",async(_,_)=>await Guard(Process),"Success");
        retry=AddAction("Check / Retry Plan",async(_,_)=>await Guard(Retry),"Warning");
        records=AddAction("Refresh History",async(_,_)=>await Guard(LoadHistory),"Secondary");
        undo=AddAction("Undo Selected Promotion",async(_,_)=>await Guard(Undo),"Warning");
        // SQL requires Students.Administer in addition to Enrollment.Manage for undo.
        correction=AddAction("History / Dates",(_,_)=>OpenStudentHistory(),"Secondary");
        studentActions=AddAction("Student Actions",(_,_)=>OpenStudentActions(),"Secondary");
        findUndo=AddAction("Undo Promotion",(_,_)=>OpenPlacementHistory(),"Secondary");
        SetToolTip(findUndo,"Find a saved promotion in History and review whether it can be undone.");
        old=AddAction("Earlier Promotion History",(_,_)=> {
            if(!CurrentUser.HasPermission("Reports.View")){Warn("Report access is required.");return;}
            using var dialog=Window("Earlier Promotion History",new PromotionHistoryPage());dialog.ShowDialog(FindForm());
        },"Secondary");
        RelocateFromActions(load,suggest,assign,select,clear,review,retry,records,undo,correction,old,studentActions,findUndo);
        var actions=CenteredActionRow(new Control[]{load,select,clear},new Control[]{suggest,assign},new Control[]{studentActions,correction,findUndo},new Control[]{review,retry});
        students.Dock=DockStyle.Fill;students.AutoGenerateColumns=false;students.MultiSelect=true;students.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        students.Columns.Add(new DataGridViewCheckBoxColumn{Name="Selected",DataPropertyName="Selected",HeaderText="Process",Width=65});
        students.Columns.Add(new DataGridViewTextBoxColumn{Name="StudentName",DataPropertyName="StudentName",HeaderText="Student",ReadOnly=true,Width=170});
        students.Columns.Add(new DataGridViewTextBoxColumn{Name="Current",DataPropertyName="Current",HeaderText="Current Placement",ReadOnly=true,Width=220});
        var decisions=new DataTable();decisions.Columns.Add("Value");decisions.Columns.Add("Label");
        decisions.Rows.Add("PROMOTE","Promote");decisions.Rows.Add("HOLD","Repeat Class");decisions.Rows.Add("MOVE","Transfer");decisions.Rows.Add("CORRECT","Correct Placement");decisions.Rows.Add("SKIP","Skip for Now");
        students.Columns.Add(new DataGridViewComboBoxColumn{Name="Decision",DataPropertyName="Decision",HeaderText="Decision",DataSource=decisions,DisplayMember="Label",ValueMember="Value",Width=145});
        students.Columns.Add(new DataGridViewComboBoxColumn{Name="Destination",DataPropertyName="Destination",HeaderText="Destination Placement",ValueMember="ClassSectionId",DisplayMember="Display",Width=260});
        students.Columns.Add(new DataGridViewTextBoxColumn{Name="Note",DataPropertyName="Note",HeaderText="Review / Exception",ReadOnly=true,Width=230});
        students.CurrentCellDirtyStateChanged+=(_,_)=>{if(students.IsCurrentCellDirty)students.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        students.CellValueChanged+=(_,e)=> {
            if(binding || e.RowIndex<0 || students.Rows[e.RowIndex].DataBoundItem is not DataRowView row)return;
            if(e.ColumnIndex==students.Columns["Decision"].Index && Convert.ToString(row["Decision"])=="HOLD")row["Destination"]=row["SourcePlacementId"];
            if(e.ColumnIndex==students.Columns["Decision"].Index)RefreshDestinationCell(students.Rows[e.RowIndex]);
            UpdateTotals();
        };
        students.DataError+=(_,e)=>{e.ThrowException=false;SetStatus("Choose a destination from the available placements.",true);};
        var historyRoot=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
        historyRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(30)));
        historyRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        historyRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonHeight+AppScale.Scale(8)));historyRoot.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var historyFields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};historyFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));historyFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,75));
        historyFields.Controls.Add(HumanUi.Field("Correction Date",historyDate),0,0);historyFields.Controls.Add(HumanUi.Field("Reason for Undoing Promotion",historyReason),1,0);
        historyDates=AddAction("Student History / Dates",(_,_)=>OpenStudentHistory(),"Secondary");RelocateFromActions(historyDates);
        historyRetry=AddAction("Check / Retry Plan",async(_,_)=>await Guard(Retry),"Warning");RelocateFromActions(historyRetry);historyRetry.Visible=false;
        var historyActions=CenteredActionRow(new Control[]{records,old},new Control[]{historyDates},new Control[]{undo,historyRetry});
        historyRoot.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Review saved movements. To undo an unused promotion, select its record and enter the correction date and reason."},0,0);
        historyRoot.Controls.Add(historyFields,0,1);historyRoot.Controls.Add(historyActions,0,2);historyRoot.Controls.Add(history,0,3);history.Dock=DockStyle.Fill;
        ConfigureHistoryGrid();
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldCardHeight(2,false)+AppMetrics.FieldRowHeight));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonHeight+AppScale.Scale(8)));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.Controls.Add(fields,0,0);body.Controls.Add(actions,0,1);body.Controls.Add(totals,0,2);body.Controls.Add(students,0,3);
        void FitTotals()=>body.RowStyles[2].Height=string.IsNullOrWhiteSpace(totals.Text)?0:
            AppMetrics.TextHeight(totals.Font)*Math.Min(2,totals.Text.Split('\n').Length)+AppScale.Scale(4);
        totals.TextChanged+=(_,_)=>FitTotals();totals.FontChanged+=(_,_)=>FitTotals();
        var unusedContent=ViewingHistory?(Control)body:historyRoot;
        unusedContent.Visible=false;
        pageContent=ViewingHistory?historyRoot:body;
        SetContent(pageContent);
        if(!ViewingHistory)ConfigureTask();
        WirePlacementChoices();
        students.SelectionChanged += (_, _) => RefreshActionAvailability();
        history.SelectionChanged += (_, _) => RefreshActionAvailability();
        reason.TextChanged += (_, _) => RefreshActionAvailability();
        historyReason.TextChanged += (_, _) => RefreshActionAvailability();
        RefreshActionAvailability();
        Disposed+=(_,_)=>{
            studentActionsMenu?.Dispose();
            unusedContent.Dispose();
            foreach(var control in PlanChoices.Cast<Control>().Concat(new Control[]{effective,reason,historyReason,historyDate,fees,carry,students,history}))
                if(control.Parent is null && !control.IsDisposed)control.Dispose();
        };
        if(initialize)Defer(async()=>await Guard(Initialize));
    }

    internal static string Title(StudentPlacementTask task)=>task switch
    {
        StudentPlacementTask.AnnualPromotion=>PageTitles.AnnualPromotion,
        StudentPlacementTask.Transfer=>PageTitles.PlacementTransfers,
        StudentPlacementTask.Correction=>PageTitles.PlacementCorrections,
        StudentPlacementTask.History=>PageTitles.PlacementHistory,
        _=>throw new ArgumentOutOfRangeException(nameof(task))
    };
    internal static string Subtitle(StudentPlacementTask task)=>task switch
    {
        StudentPlacementTask.AnnualPromotion=>"Promote students into the next academic year, with repeaters and individual exceptions.",
        StudentPlacementTask.Transfer=>"Move groups between campuses, classes and sections within the same academic year.",
        StudentPlacementTask.Correction=>"Correct placements and dates with a reason, or review a promotion undo.",
        StudentPlacementTask.History=>"Review saved placement movements and safely correct unused promotions.",
        _=>throw new ArgumentOutOfRangeException(nameof(task))
    };
    private LookupBox[] PlanChoices=>new[]{sourceYear,sourceCampus,sourceClass,sourceSection,targetYear,targetCampus,targetClass};
    private void OpenPlacementHistory()
    {
        if(busy || pending is not null)return;
        if(navigate is not null){navigate(()=>new StudentPlacementPage(StudentPlacementTask.History,navigate),PageTitles.PlacementManagement,PageTitles.PlacementHistory);return;}
        var page=new StudentPlacementPage(StudentPlacementTask.History);
        using var dialog=Window(PageTitles.PlacementHistory,page);
        dialog.FormClosing+=(_,e)=>{if(page.IsBusy)e.Cancel=true;};
        dialog.ShowDialog(FindForm());
    }
    private void ConfigureTask()
    {
        var oldFields=fields.Controls.Cast<Control>().ToArray();fields.Controls.Clear();
        TableLayoutPanel Group(string title,int column){
            var card=HumanUi.Card(title,accent:Accent);card.Margin=new Padding(column==0?0:AppMetrics.SpacingSm,0,column==0?AppMetrics.SpacingSm:0,AppMetrics.SpacingSm);
            var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
            for(int i=0;i<2;i++){table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));table.RowStyles.Add(new RowStyle(SizeType.Percent,50));}
            card.Content.Controls.Add(table);fields.Controls.Add(card,column,0);return table;
        }
        void Field(TableLayoutPanel table,string label,Control control,int col,int row)=>table.Controls.Add(HumanUi.Field(label,control),col,row);
        var source=Group("Source",0);var destination=Group("Destination",1);
        Field(source,"Academic Year",sourceYear,0,0);Field(source,"Campus (blank = all)",sourceCampus,1,0);
        Field(source,"Class (blank = all)",sourceClass,0,1);Field(source,"Section (blank = all)",sourceSection,1,1);
        // Detach controls absent from this task before disposing their old field wrappers.
        foreach(var control in new Control[]{targetYear,targetCampus,targetClass,effective,fees,reason,carry})control.Parent?.Controls.Remove(control);
        if(Annual){Field(destination,"Academic Year",targetYear,0,0);Field(destination,"Campus",targetCampus,1,0);Field(destination,"Promotion Date",effective,0,1);Field(destination,"Monthly Concessions",carry,1,1);}
        else{Field(destination,"Campus",targetCampus,0,0);Field(destination,"Class",targetClass,1,0);Field(destination,operation==2?"Correction Date":"Transfer Date",effective,0,1);Field(destination,"Transfer Fee Policy",fees,1,1);}
        var explanation=HumanUi.Field(operation==2?"Correction Explanation":"Reason",reason);fields.Controls.Add(explanation,0,1);fields.SetColumnSpan(explanation,2);
        foreach(var wrapper in oldFields)wrapper.Dispose();
        carry.Visible=Annual;
        SetToolTip(targetCampus,"Leave blank to keep each student's current campus.");SetToolTip(targetClass,"Leave blank to keep each student's current class.");
        review.Text="Review";SetToolTip(review,Annual?"Review the annual promotion plan before processing.":operation==1?"Review the transfer plan before processing.":"Review the placement correction before processing.");
        correction.Visible=operation==2;findUndo.Visible=operation==2;studentActions.Visible=operation!=1;retry.Visible=pending is not null;
        studentActions.Text=Annual?"Return / Completion":"Student Actions";
        var column=(DataGridViewComboBoxColumn)students.Columns["Decision"];
        var choices=column.DataSource is DataView view?view.Table!:(DataTable)column.DataSource!;
        choices.DefaultView.RowFilter=Annual?"Value IN ('PROMOTE','HOLD','SKIP')":operation==1?"Value IN ('MOVE','SKIP')":"Value IN ('CORRECT','SKIP')";
        ((DataGridViewComboBoxColumn)students.Columns["Decision"]).DataSource=choices.DefaultView;
    }

    private async Task Guard(Func<Task> work, LookupBox? focusedLookup = null)
    {
        if(busy)return;
        bool restoreFocus = focusedLookup?.Focused == true;
        busy=true;pageContent.Enabled=false;
        try{await work();}catch(Exception ex){Fail(ex);}finally{
            busy=false;
            if (!IsDisposed)
            {
                pageContent.Enabled = true;
                RefreshActionAvailability();
                if (restoreFocus && FindForm()?.ContainsFocus == true && focusedLookup!.CanFocus)
                    focusedLookup.Focus();
            }
        }
    }

    private void RefreshActionAvailability()
    {
        if (busy || binding || Disposing || IsDisposed) return;
        bool editable = pending is null;
        bool canAdminister = CurrentUser.HasPermission("Students.Administer");
        if (ViewingHistory)
        {
            var movement = SelectedStudent();
            bool canUndo = editable && canAdminister && movement is not null
                && Convert.ToString(movement["Action"]) is ("PROMOTE" or "HOLD")
                && movement.Row.IsNull("UndoneBy");
            history.Enabled = records.Enabled = old.Enabled = editable;
            historyDates.Enabled = editable && canAdminister && movement is not null;
            historyReason.Enabled = historyDate.Enabled = canUndo;
            undo.Enabled = canUndo && !string.IsNullOrWhiteSpace(historyReason.Text);
            historyRetry.Visible = historyRetry.Enabled = !editable;
            return;
        }

        bool hasSource = editable && choicesReady && sourceYear.SelectedId > 0;
        bool hasStudents = hasSource && data.Rows.Count > 0;
        bool hasDestination = hasStudents && targetYear.SelectedId > 0;
        bool canAdjustFees = CurrentUser.HasPermission("Fees.Adjust");

        sourceYear.Enabled = editable && choicesReady && HasChoices(sourceYear);
        sourceCampus.Enabled = hasSource && HasChoices(sourceCampus);
        sourceClass.Enabled = hasSource && HasChoices(sourceClass);
        sourceSection.Enabled = hasSource && HasChoices(sourceSection);
        load.Enabled = hasSource;
        students.Enabled = hasStudents;
        targetYear.Enabled = Annual && hasStudents && HasChoices(targetYear);
        targetCampus.Enabled = hasDestination && HasChoices(targetCampus);
        targetClass.Enabled = !Annual && hasDestination && HasChoices(targetClass);
        effective.Enabled = reason.Enabled = hasDestination;
        fees.Enabled = !Annual && hasDestination && canAdjustFees;
        carry.Enabled = Annual && hasDestination && canAdjustFees;

        suggest.Enabled = hasDestination;
        assign.Enabled = hasDestination && students.SelectedRows.Count > 0;
        select.Enabled = clear.Enabled = hasStudents;
        bool hasStudent = hasStudents && SelectedStudent() is not null;
        studentActions.Enabled = hasStudent && (canAdminister || !Annual && CurrentUser.HasPermission("Enrollment.Manage"));
        correction.Enabled = hasStudent && canAdminister;
        findUndo.Enabled = editable;
        retry.Visible = retry.Enabled = !editable;

        var selected = SelectedPlanRows();
        review.Enabled = hasDestination && !string.IsNullOrWhiteSpace(reason.Text)
            && selected.Length > 0 && selected.All(row => Id(row, "Destination") > 0);
    }

    // Bound lookups have a leading blank choice.
    private static bool HasChoices(LookupBox lookup) => lookup.Items.Count > 1;

    private DataRow[] SelectedPlanRows() => data.Columns.Contains("Selected")
        ? data.AsEnumerable().Where(row => Convert.ToBoolean(row["Selected"]) && Convert.ToString(row["Decision"]) != "SKIP").ToArray()
        : Array.Empty<DataRow>();

    private async Task Initialize()
    {
        if(ViewingHistory){await LoadHistory();SetStatus("Select a movement to review its history or promotion correction.");return;}
        binding=true;
        try {
            campusCatalog=await Task.Run(()=>DatabaseHelper.QueryUser("GetCampuses"));
            yearCatalog=await Task.Run(()=>DatabaseHelper.QueryUser("GetAcademicYears"));
            catalog=await Task.Run(()=>DatabaseHelper.QueryUser("GetClasses"));
            sectionCatalog=await Task.Run(()=>DatabaseHelper.QueryUser("GetSections"));
            placements=await DatabaseHelper.QuerySqlAsync("""
                EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
                SELECT P.ClassSectionId,P.CampusId,P.ClassId,P.SectionId,P.IsActive,
                 CONCAT(C.CampusName,N' / ',K.ClassName,N' / ',S.SectionName,
                  CASE WHEN P.IsActive=0 THEN N' (inactive - historical only)' ELSE N'' END) Display
                FROM dbo.CampusClassSections P JOIN dbo.Campuses C ON C.CampusId=P.CampusId
                JOIN dbo.Classes K ON K.ClassId=P.ClassId JOIN dbo.Sections S ON S.SectionId=P.SectionId
                WHERE P.IsActive=1 OR @IncludeInactive=1 ORDER BY C.CampusName,K.DisplayOrder,S.DisplayOrder;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),
                DatabaseHelper.Bit("@IncludeInactive",Annual || operation==2));
            BindPlacementChoices();
            SetStatus("Choose a source year. Blank campus, class and section include all active placements. Load students to begin.");
        }finally{binding=false;}
    }
    private static Panel CenteredActionRow(params Control[][] groups)
    {
        var host=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
        var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty};
        host.Controls.Add(row);
        bool arranging=false;
        void Arrange(){
            if(arranging || host.IsDisposed)return;arranging=true;
            try{
                Size needed=row.GetPreferredSize(Size.Empty);
                int width=needed.Width;
                host.AutoScrollMinSize=new Size(width,0);
                row.Location=new Point(Math.Max(0,(host.ClientSize.Width-width)/2)+host.AutoScrollPosition.X,0);
                if(host.Parent is TableLayoutPanel table && table.GetRow(host) is int rowIndex && rowIndex>=0 && rowIndex<table.RowStyles.Count){
                    int height=Math.Max(AppMetrics.ButtonHeight+AppScale.Scale(8),needed.Height)+(width>host.ClientSize.Width?SystemInformation.HorizontalScrollBarHeight:0);
                    if(Math.Abs(table.RowStyles[rowIndex].Height-height)>0.5f)table.RowStyles[rowIndex].Height=height;
                }
            }finally{arranging=false;}
        }
        int index=0;
        foreach(var group in groups)for(int i=0;i<group.Length;i++){
            var button=group[i];button.TabIndex=index++;
            button.Margin=new Padding(i==0 && index>1?AppScale.Scale(16):AppScale.Scale(3),AppScale.Scale(4),AppScale.Scale(3),AppScale.Scale(4));
            bool sizing=false;
            void SizeButton(){
                if(sizing)return;sizing=true;
                try{
                    Size text=TextRenderer.MeasureText(button.Text,button.Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
                    button.MinimumSize=new Size(UiDpi.Metric(button,AppMetrics.ActionButtonMinimumWidth),0);
                    button.Width=Math.Max(button.MinimumSize.Width,text.Width+button.Padding.Horizontal+UiDpi.Metric(button,AppScale.Scale(12))+1);
                    button.Height=Math.Max(AppMetrics.ButtonHeight,text.Height+button.Padding.Vertical+UiDpi.Metric(button,AppScale.Scale(8)));
                }
                finally{sizing=false;}
            }
            SizeButton();row.Controls.Add(button);
            button.TextChanged+=(_,_)=>{SizeButton();Arrange();};button.FontChanged+=(_,_)=>{SizeButton();Arrange();};
            button.SizeChanged+=(_,_)=>{SizeButton();Arrange();};button.VisibleChanged+=(_,_)=>Arrange();
            button.DpiChangedAfterParent+=(_,_)=>{SizeButton();Arrange();};
        }
        host.SizeChanged+=(_,_)=>Arrange();host.VisibleChanged+=(_,_)=>Arrange();host.ParentChanged+=(_,_)=>Arrange();row.SizeChanged+=(_,_)=>Arrange();
        return host;
    }
    private static void Bind(LookupBox box,DataTable source,string value,string display,bool optional,string optionalLabel="All")
    {
        box.Bind(source.Copy(),value,display,optional?optionalLabel:"-- Select --");box.SelectedIndex=optional?0:-1;
    }
    private void InvalidatePlan()
    {
        if(pending is not null)return;
        students.DataSource=null;data=new();totals.Text="Selection changed. Load students again.";
        RefreshActionAvailability();
    }
    private async Task LoadStudents()
    {
        if(pending is not null)throw new InvalidOperationException(BusyMessage);
        if (sourceYear.SelectedId <= 0)
        {
            Warn("Choose the source academic year, then click Load Students.");
            return;
        }
        var scope=(Campus:sourceCampus.SelectedId,Class:sourceClass.SelectedId,Section:sourceSection.SelectedId);
        InvalidatePlan();
        if(choicesReady)await RefreshSourceContext(false);
        if((scope.Campus>0 && sourceCampus.SelectedId!=scope.Campus) || (scope.Class>0 && sourceClass.SelectedId!=scope.Class) || (scope.Section>0 && sourceSection.SelectedId!=scope.Section))
            throw new InvalidOperationException("The selected source no longer has matching placements. Check the source filters and load again.");
        data=await ReadStudents(sourceYear.SelectedId,sourceCampus.SelectedId,sourceClass.SelectedId,sourceSection.SelectedId);
        foreach(var column in new[]{"Current","Decision","Note"})data.Columns.Add(column,typeof(string));
        data.Columns.Add("Selected",typeof(bool));data.Columns.Add("Destination",typeof(int));
        binding=true;
        foreach(DataRow row in data.Rows){row["Current"]=$"{row["CampusName"]} / {row["ClassName"]} / {row["SectionName"]}";row["Selected"]=true;row["Destination"]=0;row["Decision"]=Annual?"PROMOTE":operation==2?"CORRECT":"MOVE";row["Note"]=row["PlacementState"];}
        students.DataSource=data;binding=false;
        if(!Annual)targetYear.SelectedValue=sourceYear.SelectedId;
        Suggest();
        SetStatus(Annual && targetYear.SelectedId<=0
            ? $"Loaded {data.Rows.Count:N0} active placements. Configure a later academic year before processing promotion."
            : $"Loaded {data.Rows.Count:N0} active placements. To process one student, use Clear Selection, then tick Process beside that student.");
    }
    private void Suggest()
    {
        if(pending is not null)return;
        students.EndEdit();binding=true;
        foreach(DataRow row in data.Rows){
            int campus=targetCampus.SelectedId>0?targetCampus.SelectedId:Convert.ToInt32(row["CampusId"]);
            int cls=Convert.ToInt32(row["ClassId"]);string decision=Convert.ToString(row["Decision"])!;
            if(decision=="HOLD"){row["Destination"]=row["SourcePlacementId"];continue;}
            if(Annual){var next=PromotionChoices.NextClass(catalog,cls);cls=next is null?0:Convert.ToInt32(next["ClassId"]);}
            else if(targetClass.SelectedId>0)cls=targetClass.SelectedId;
            var match=placements.AsEnumerable().Where(p=>Convert.ToInt32(p["CampusId"])==campus && Convert.ToInt32(p["ClassId"])==cls && Convert.ToInt32(p["SectionId"])==Convert.ToInt32(row["SectionId"]) && (operation==2 || Convert.ToBoolean(p["IsActive"]))).ToArray();
            row["Destination"]=match.Length==1?match[0]["ClassSectionId"]:0;
            bool configured=placements.AsEnumerable().Any(p=>Convert.ToInt32(p["CampusId"])==campus && Convert.ToInt32(p["ClassId"])==cls && (operation==2 || Convert.ToBoolean(p["IsActive"])));
            row["Note"]=cls==0?"Final class: review departure, repeat or skip":!configured?"Configure this destination class/section at the campus":match.Length!=1?"Choose a configured destination section":Convert.ToString(row["PlacementState"]);
            if(Convert.ToBoolean(row["LegacySetupPending"]) || row.IsNull("PlacementConfirmedAt"))row["Note"]="Complete admission / legacy verification before movement";
        }
        binding=false;UpdateTotals();
        RefreshDestinationCells();
    }
    private void SelectRows(bool selected){if(pending is not null)return;students.EndEdit();foreach(DataRow row in data.Rows)row["Selected"]=selected;UpdateTotals();}
    private void AssignHighlighted()
    {
        if(pending is not null || students.SelectedRows.Count==0)return;
        var selectedRows=students.SelectedRows.Cast<DataGridViewRow>().Where(row=>row.DataBoundItem is DataRowView).Select(row=>((DataRowView)row.DataBoundItem!).Row).ToArray();
        var lookup=new LookupBox();
        var decision=HumanUi.Combo();decision.BindingContext=new BindingContext();
        decision.ValueMember="Value";decision.DisplayMember="Label";decision.DataSource=((DataView)((DataGridViewComboBoxColumn)students.Columns["Decision"]).DataSource!).ToTable();decision.SelectedIndex=0;
        void BindDestinations(){string action=Convert.ToString(decision.SelectedValue)??"";lookup.Bind(CommonDestinations(selectedRows,action),"ClassSectionId","Display");lookup.SelectedIndex=-1;lookup.Enabled=action is not ("HOLD" or "SKIP");}
        decision.SelectedIndexChanged+=(_,_)=>BindDestinations();BindDestinations();
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
        panel.Controls.Add(HumanUi.Field("Decision",decision));panel.Controls.Add(HumanUi.Field("Destination Placement",lookup));
        using var dialog=Window("Assign Highlighted Students",panel);dialog.ClientSize=new Size(AppScale.Scale(700),AppScale.Scale(240));
        var apply=new ModernButton{Text="Assign",DialogResult=DialogResult.OK};panel.Controls.Add(HumanUi.DialogButtons(apply));dialog.AcceptButton=apply;
        void RefreshAssign()
        {
            string? action = Convert.ToString(decision.SelectedValue);
            apply.Enabled = action is "HOLD" or "SKIP" || lookup.SelectedId > 0;
        }
        lookup.SelectedIndexChanged += (_, _) => RefreshAssign();
        decision.SelectedIndexChanged += (_, _) => RefreshAssign();
        RefreshAssign();
        if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;
        string selectedDecision=Convert.ToString(decision.SelectedValue)!;
        if(selectedDecision!="HOLD" && selectedDecision!="SKIP" && lookup.SelectedId<=0){Warn("Choose a destination placement.");return;}
        students.EndEdit();binding=true;
        foreach(DataGridViewRow gridRow in students.SelectedRows)if(gridRow.DataBoundItem is DataRowView item){item["Decision"]=selectedDecision;item["Destination"]=selectedDecision=="HOLD"?item["SourcePlacementId"]:lookup.SelectedId;item["Selected"]=selectedDecision!="SKIP";}
        binding=false;RefreshDestinationCells();UpdateTotals();
    }
    private void UpdateTotals()
    {
        if(!data.Columns.Contains("Selected"))return;
        var chosen = SelectedPlanRows();
        string destinations=string.Join(" · ",chosen.GroupBy(r=>Convert.ToInt32(r["Destination"])).Select(group=>{
            var placement=placements.AsEnumerable().FirstOrDefault(p=>Convert.ToInt32(p["ClassSectionId"])==group.Key);
            return $"{(placement is null?"Unassigned":placement["Display"])}: {group.Count():N0}";
        }));
        totals.Text=$"{chosen.Length:N0} to process · {data.Rows.Count-chosen.Length:N0} skipped · {chosen.Count(r=>Convert.ToInt32(r["Destination"])==0):N0} missing destinations. Highlight rows to assign groups.\n{destinations}";
        SetToolTip(totals,totals.Text);
        RefreshActionAvailability();
    }
    private async Task Process()
    {
        if(pending is not null){await Retry();return;}
        students.EndEdit();
        var selected = SelectedPlanRows();
        if(selected.Length==0)throw new InvalidOperationException("Select students to process.");
        if(targetYear.SelectedId<=0 || reason.Text.Trim().Length==0)throw new InvalidOperationException("Choose the destination year and enter a reason.");
        if(selected.Any(r=>Convert.ToInt32(r["Destination"])<=0))throw new InvalidOperationException("Choose a destination for every selected student. Final-class students can repeat or be skipped for departure review.");
        if(selected.Any(r=>Annual ? Convert.ToString(r["Decision"]) is not ("PROMOTE" or "HOLD") : Convert.ToString(r["Decision"]) is not ("MOVE" or "CORRECT")))throw new InvalidOperationException("Choose decisions that match the selected action.");
        if(Annual && carry.Checked && !CurrentUser.HasPermission("Fees.Adjust"))
            throw new UnauthorizedAccessException("Carrying monthly concessions requires fee adjustment access.");
        // Check promotion order against the current catalog, not an old screen load.
        catalog=await Task.Run(()=>DatabaseHelper.QueryUser("GetClasses"));
        ValidatePlacementChoices(selected);
        // NEW PROMOTE/MOVE targets must be active. HOLD retains its old section,
        // and CORRECT may refer to an inactive historical section by design.
        foreach(var target in selected.Where(r=>Convert.ToString(r["Decision"]) is "PROMOTE" or "MOVE")
            .Select(r=>Convert.ToInt32(r["Destination"])).Distinct())
            await ActiveClassSectionPolicy.EnsureActiveAsync(target);
        var plan=selected.Select(r=> {
            string? policy=null;
            var destination=placements.AsEnumerable().Single(p=>Convert.ToInt32(p["ClassSectionId"])==Convert.ToInt32(r["Destination"]));
            bool classOrCampus=Convert.ToInt32(destination["CampusId"])!=Convert.ToInt32(r["CampusId"]) || Convert.ToInt32(destination["ClassId"])!=Convert.ToInt32(r["ClassId"]);
            if(!Annual && classOrCampus)policy=fees.SelectedIndex switch{1=>"NEXT_MONTH",2=>"SCHOOL_CUTOFF",_=>null};
            return new PlacementPlanItem(Convert.ToInt32(r["EnrollmentId"]),Convert.ToInt32(r["SourcePlacementId"]),Convert.ToString(r["Stamp"])!,Convert.ToString(r["Decision"])!,Convert.ToInt32(r["Destination"]),policy);
        }).ToArray();
        if(plan.Any(item=>item.FeePolicy is not null) && !CurrentUser.HasPermission("Fees.Adjust"))
            throw new UnauthorizedAccessException("Changing destination fees requires fee adjustment access.");
        var json=PlacementPlan.Serialize(plan);Guid id=Guid.NewGuid();
        SetStatus("Validating the complete plan, including lifecycle and fee rules…");
        var preview=await PlacementPlan.RunAsync(id,json,targetYear.SelectedId,effective.Value.Date,reason.Text.Trim(),Annual && carry.Checked,true);
        using var reviewed=JsonDocument.Parse(Convert.ToString(preview.Rows[0]["ResultJson"])!);
        if(!ReviewDetails(selected,reviewed.RootElement))
        {
            SetStatus("Review closed. Your plan is ready to edit.");
            return;
        }
        pending=(id,json,targetYear.SelectedId,effective.Value.Date,reason.Text.Trim(),Annual && carry.Checked,reviewed.RootElement.GetProperty("ReviewHash").GetString()!);
        await Retry();
    }
    private bool ReviewDetails(DataRow[] selected,JsonElement reviewed)
    {
        var summary=new DataTable();foreach(var name in new[]{"Student","From","Decision","To","Fees Before","Fees After","Bill Difference"})summary.Columns.Add(name);
        string FeeText(JsonElement effect, string property)
        {
            // SQL FOR JSON omits null properties when there are no assignments.
            if (!effect.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                return "None";
            string? json = value.GetString();
            if (string.IsNullOrWhiteSpace(json)) return "None";
            using var feesDoc = JsonDocument.Parse(json);
            if (feesDoc.RootElement.GetArrayLength() == 0) return "None";
            return string.Join("; ",feesDoc.RootElement.EnumerateArray().Select(f=>{
            string period=f.TryGetProperty("EffectiveFrom",out var from)?$" from {from.GetDateTime():dd-MMM-yyyy}":"";
            if(f.TryGetProperty("EffectiveTo",out var until))period+=$" through {until.GetDateTime():dd-MMM-yyyy}";
            return $"{f.GetProperty("FeeTypeName").GetString()}: {f.GetProperty("Amount").GetDecimal()-f.GetProperty("DiscountAmount").GetDecimal():N0} / {f.GetProperty("Frequency").GetString()}{period}";
        }));
        }
        foreach(var row in selected){var dest=placements.AsEnumerable().Single(p=>Convert.ToInt32(p["ClassSectionId"])==Convert.ToInt32(row["Destination"]));var effect=reviewed.GetProperty("Effects").EnumerateArray().Single(e=>e.GetProperty("AdmissionId").GetInt32()==Convert.ToInt32(row["AdmissionId"]));summary.Rows.Add(row["StudentName"],row["Current"],row["Decision"],dest["Display"],FeeText(effect,"FeesBefore"),FeeText(effect,"FeesAfter"),effect.GetProperty("BillDifference").GetDecimal().ToString("N0"));}
        var grid=HumanUi.Grid(false);grid.Dock=DockStyle.Fill;grid.DataSource=summary;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(12)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,85));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonRowHeight));
        root.Controls.Add(new Label{Dock=DockStyle.Fill,Text=$"{selected.Length:N0} students · {effective.Value:dd-MMM-yyyy} · {targetYear.Text}\n{reason.Text.Trim()}\n"+(Annual?"New placements await return admission before billing. "+(carry.Checked?"Carry monthly concessions.":"Reset concessions for return review."):fees.Text+". Paid reductions remain as student advance.")+" All selected students succeed together; a failure rolls back the entire plan."},0,0);
        root.Controls.Add(grid,0,1);var apply=new ModernButton{Text="Process Reviewed Plan",DialogResult=DialogResult.OK};var cancel=new ModernButton{Text="Back to Plan",DialogResult=DialogResult.Cancel};root.Controls.Add(HumanUi.DialogButtons(apply,cancel),0,2);
        using var dialog=Window("Review Placement Plan",root);dialog.CancelButton=cancel;return dialog.ShowDialog(FindForm())==DialogResult.OK;
    }
    private async Task Retry()
    {
        if(pending is not {} plan){SetStatus("There is no unconfirmed plan.");return;}
        try {
            await PlacementPlan.RunAsync(plan.Id,plan.Json,plan.Year,plan.Date,plan.Reason,plan.Carry,false,plan.Hash);
        }catch(SqlException ex) when(ex.Number>0 && ex.Class<20){pending=null;throw;}
        catch {SetStatus("The outcome is unconfirmed. Check / Retry Plan uses the same request and cannot process it twice.",true);throw;}
        pending=null;
        if(!ViewingHistory){students.DataSource=null;data=new();totals.Text="Plan saved. Reload students to see remaining placements.";}
        SetStatus(ViewingHistory?$"Promotion correction saved: {plan.Id}. Both placements remain in history.":Annual?$"Placement plan saved: {plan.Id}. Confirm Return Admission before billing the new placements.":$"Placement changes saved: {plan.Id}.");
        if(ViewingHistory)await LoadHistory();
        else if(choicesReady)await RefreshSourceContext(false);
    }
    private async Task LoadHistory()
    {
        history.DataSource=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
            SELECT TOP(2000) I.ItemId,P.RequestId,P.CreatedAt,S.AdmissionNumber,S.StudentName,I.AdmissionId,I.Action,
             CONCAT(OC.CampusName,N' / ',OK.ClassName,N' / ',OS.SectionName) PreviousPlacement,
             CONCAT(NC.CampusName,N' / ',NK.ClassName,N' / ',NS.SectionName) DestinationPlacement,
             P.Reason,I.FeePolicy,I.UndoneBy,I.DestinationEnrollmentId,E.AcademicYearId,
             E.ClassSectionId CurrentPlacementId,CONVERT(varchar(33),E.UpdatedAt,126) CurrentStamp,E.Status,
             CASE WHEN I.UndoneBy IS NULL THEN N'Recorded' ELSE N'Undone' END RecordStatus
            FROM dbo.StudentPlacementPlanItems I JOIN dbo.StudentPlacementPlans P ON P.RequestId=I.RequestId
            JOIN dbo.Students S ON S.AdmissionId=I.AdmissionId JOIN dbo.Enrollments E ON E.EnrollmentId=I.DestinationEnrollmentId
            JOIN dbo.CampusClassSections O ON O.ClassSectionId=I.SourcePlacementId
            JOIN dbo.Campuses OC ON OC.CampusId=O.CampusId JOIN dbo.Classes OK ON OK.ClassId=O.ClassId JOIN dbo.Sections OS ON OS.SectionId=O.SectionId
            JOIN dbo.CampusClassSections N ON N.ClassSectionId=I.DestinationPlacementId
            JOIN dbo.Campuses NC ON NC.CampusId=N.CampusId JOIN dbo.Classes NK ON NK.ClassId=N.ClassId JOIN dbo.Sections NS ON NS.SectionId=N.SectionId
            ORDER BY I.ItemId DESC;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId));
    }
    private void ConfigureHistoryGrid()
    {
        history.AutoGenerateColumns=false;
        void Column(string name,string property,string caption,int width,string? format=null){
            var column=new DataGridViewTextBoxColumn{Name=name,DataPropertyName=property,HeaderText=caption,ReadOnly=true,Width=AppScale.Scale(width)};
            if(format is not null)column.DefaultCellStyle.Format=format;
            history.Columns.Add(column);
        }
        Column("AdmissionNumber","AdmissionNumber","Adm. No.",100);
        Column("StudentName","StudentName","Student",170);
        Column("RecordedAt","CreatedAt","Recorded On",165,"dd-MMM-yyyy hh:mm tt");
        Column("Action","Action","Action",135);
        Column("PreviousPlacement","PreviousPlacement","Previous Placement",220);
        Column("DestinationPlacement","DestinationPlacement","Destination Placement",220);
        Column("CurrentStatus","Status","Current Status",110);
        Column("FeePolicy","FeePolicy","Fee Policy",130);
        Column("Reason","Reason","Reason",220);
        Column("RecordStatus","RecordStatus","Record Status",110);
        history.CellFormatting+=(_,e)=>{
            if(e.RowIndex<0 || e.ColumnIndex<0)return;
            string name=history.Columns[e.ColumnIndex].Name;
            if(name=="Action"){
                e.Value=Convert.ToString(e.Value) switch{"PROMOTE"=>"Promoted","HOLD"=>"Repeated Class","MOVE"=>"Transferred","CORRECT"=>"Placement Corrected","UNDO"=>"Promotion Undone",var text=>text};
                e.FormattingApplied=true;
            }
            else if(name=="FeePolicy"){
                e.Value=Convert.ToString(e.Value) switch{"NEXT_MONTH"=>"From next month","SCHOOL_CUTOFF"=>"School cutoff",var text=>text};
                e.FormattingApplied=true;
            }
        };
    }
    private async Task Undo()
    {
        if(!CurrentUser.HasPermission("Students.Administer"))
            throw new UnauthorizedAccessException("Student administration access is required to undo a promotion.");
        if(pending is not null)throw new InvalidOperationException(BusyMessage);
        if(history.CurrentRow?.DataBoundItem is not DataRowView row)throw new InvalidOperationException("Select a promotion in History first.");
        if(historyReason.Text.Trim().Length==0)throw new InvalidOperationException("Enter the reason for undoing the selected promotion on Placement History.");
        var item=new PlacementPlanItem(Convert.ToInt32(row["DestinationEnrollmentId"]),Convert.ToInt32(row["CurrentPlacementId"]),Convert.ToString(row["CurrentStamp"])!,"UNDO",0,null,Convert.ToInt32(row["ItemId"]));
        string json=PlacementPlan.Serialize(new[]{item});Guid id=Guid.NewGuid();int year=Convert.ToInt32(row["AcademicYearId"]);
        var preview=await PlacementPlan.RunAsync(id,json,year,historyDate.Value.Date,historyReason.Text.Trim(),false,true);
        using var reviewed=JsonDocument.Parse(Convert.ToString(preview.Rows[0]["ResultJson"])!);
        if(ModernMessageDialog.Show(this,$"Restore the previous placement for {row["StudentName"]}?\n\nThe mistaken destination and original promotion history remain recorded.\nReason: {historyReason.Text.Trim()}","Review Promotion Correction",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
        pending=(id,json,year,historyDate.Value.Date,historyReason.Text.Trim(),false,reviewed.RootElement.GetProperty("ReviewHash").GetString()!);await Retry();
    }
    private async void OpenStudentHistory()
    {
        if(busy || pending is not null)return;
        if(!CurrentUser.HasPermission("Students.Administer")){Warn("Student administration access is required.");return;}
        DataRowView? row=SelectedStudent();
        if(row is null){Warn("Select a student first.");return;}
        using var dialog=new StudentPlacementCorrectionDialog(Convert.ToInt32(row["AdmissionId"]),Convert.ToString(row["StudentName"])!);dialog.ShowDialog(FindForm());
        if (!dialog.Changed) return;
        if (ViewingHistory) await Guard(LoadHistory);
        else InvalidatePlan();
    }
    private DataRowView? SelectedStudent()=>ViewingHistory?history.CurrentRow?.DataBoundItem as DataRowView:students.CurrentRow?.DataBoundItem as DataRowView;
    private void OpenStudentActions()
    {
        if(busy || pending is not null)return;
        var row=SelectedStudent();if(row is null){Warn("Select a student first.");return;}
        int admission=Convert.ToInt32(row["AdmissionId"]);string name=Convert.ToString(row["StudentName"])!;
        // WinForms still uses the menu after Closed while dispatching an item click.
        studentActionsMenu?.Dispose();
        var menu = studentActionsMenu = new StyledContextMenu();
        void Add(string caption,Func<Form> createDialog,string permission)
        {
            var item=menu.Items.Add(caption);
            item.Enabled=CurrentUser.HasPermission(permission);
            item.Click+=(_,_)=>{
                try
                {
                    using var dialog = createDialog();
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK) InvalidatePlan();
                }
                catch (Exception ex) { Fail(ex); }
            };
        }
        Add("Record Departure / Final-Class Completion…",()=>new CloseAdmissionDialog(admission,name,false),"Students.Administer");
        Add("Confirm Return Admission…",()=>new ConfirmReturnAdmissionDialog(admission),"Students.Administer");
        if(!Annual){
            Add("Re-Admit…",()=>new StudentReadmissionDialog(admission,name),"Enrollment.Manage");
            Add("Put on Hold…",()=>new StudentPostingHoldDialog(admission,name,false),"Students.Administer");
            Add("Lift Hold / Resume…",()=>new StudentPostingHoldDialog(admission,name,true),"Students.Administer");
        }
        menu.Show(studentActions, new Point(0, studentActions.Height));
    }
    private static ApplicationWindowForm Window(string title,Control content)
    {
        var form=new ApplicationWindowForm{Text=title,StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(AppScale.Scale(1050),AppScale.Scale(650)),MinimumSize=new Size(AppScale.Scale(720),AppScale.Scale(420)),ShowInTaskbar=false};
        form.Controls.Add(content);content.Dock=DockStyle.Fill;return form;
    }
}
