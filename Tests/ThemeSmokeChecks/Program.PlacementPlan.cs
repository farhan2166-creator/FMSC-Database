using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Enrollment;

internal static partial class Program
{
    private static void CheckPlacementWorkspace()
    {
        CurrentUser.SignIn(7781,"Placement fixture","Administrator",false,Array.Empty<string>());
        using var page=new StudentPlacementPage(null,false);
        using var window=new Form{ClientSize=new Size(1200,800),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)};
        window.Controls.Add(page);window.Show();Application.DoEvents();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(StudentPlacementPage).GetField(name,flags)!.GetValue(page)!;
        void Call(string name)=>typeof(StudentPlacementPage).GetMethod(name,flags)!.Invoke(page,null);
        var catalog=Field<DataTable>("catalog");catalog.Columns.Add("ClassId",typeof(int));catalog.Columns.Add("AcademicProgramId",typeof(int));catalog.Columns.Add("DisplayOrder",typeof(int));catalog.Rows.Add(1,1,1);catalog.Rows.Add(2,1,2);
        var placements=Field<DataTable>("placements");foreach(string name in new[]{"ClassSectionId","CampusId","ClassId","SectionId"})placements.Columns.Add(name,typeof(int));placements.Columns.Add("Display");placements.Rows.Add(1,1,1,1,"Campus A / One / A");placements.Rows.Add(2,1,2,1,"Campus A / Two / A");
        var data=Field<DataTable>("data");foreach(string name in new[]{"ClassId","CampusId","SectionId","SourcePlacementId","Destination"})data.Columns.Add(name,typeof(int));foreach(string name in new[]{"Decision","Note","PlacementState"})data.Columns.Add(name);data.Columns.Add("LegacySetupPending",typeof(bool));data.Columns.Add("PlacementConfirmedAt",typeof(DateTime));data.Columns.Add("Selected",typeof(bool));
        data.Rows.Add(1,1,1,1,0,"PROMOTE","","Active",false,DateTime.Now,true);
        data.Rows.Add(2,1,1,2,0,"PROMOTE","","Active",false,DateTime.Now,true);
        Call("Suggest");
        Check(Convert.ToInt32(data.Rows[0]["Destination"])==2,"Annual plan suggests the immediate next class and matching section.");
        Check(Convert.ToInt32(data.Rows[1]["Destination"])==0 && Convert.ToString(data.Rows[1]["Note"])!.Contains("Final class"),"Final-class students get an explicit exception rather than an invented destination.");
        data.Rows[0]["Decision"]="HOLD";Call("Suggest");Check(Convert.ToInt32(data.Rows[0]["Destination"])==1,"Repeat keeps its original placement.");
        data.Rows[0]["Decision"]="SKIP";Call("UpdateTotals");Check(Field<Label>("totals").Text.Contains("1 skipped"),"Skip is counted separately from repeating.");
        var tabs=Field<TabControl>("tabs");
        Check(tabs.TabPages.Cast<TabPage>().Select(tab=>tab.Text).SequenceEqual(new[]{"Annual Promotion","Transfers & Sections","Corrections","History"}),"Related workflows have four dedicated task tabs.");
        Check(!page.IsBusy,"Idle workspace permits navigation.");
        data.Columns.Add("StudentName");data.Columns.Add("Current");
        data.Rows[0]["StudentName"]="Fictional Student A";data.Rows[0]["Current"]="Campus A / One / A";data.Rows[0]["Decision"]="PROMOTE";
        data.Rows[1]["StudentName"]="Fictional Student B";data.Rows[1]["Current"]="Campus A / Two / A";
        Call("Suggest");
        var choices=placements.Copy();var blank=choices.NewRow();blank["ClassSectionId"]=0;blank["Display"]="Choose a destination";choices.Rows.InsertAt(blank,0);
        var grid=Field<DataGridView>("students");((DataGridViewComboBoxColumn)grid.Columns["Destination"]).DataSource=choices;grid.DataSource=data;
        Application.DoEvents();
        Check(!grid.ReadOnly && grid.Columns["Current"].ReadOnly,"Placement decisions are editable while current placement remains read-only.");
        window.ClientSize=new Size(900,700);Application.DoEvents();
        Check(grid.Height>=100 && grid.Width>600,"The planner retains a usable student grid at a compact window size.");
        window.ClientSize=new Size(1200,800);Application.DoEvents();
        var annualData=Field<DataTable>("data");
        var years=new DataTable();years.Columns.Add("AcademicYearId",typeof(int));years.Columns.Add("YearName");years.Rows.Add(9,"Source Year");years.Rows.Add(10,"Next Year");
        foreach(var name in new[]{"sourceYear","targetYear"})Field<LookupBox>(name).Bind(years.Copy(),"AcademicYearId","YearName");
        Field<LookupBox>("sourceYear").SelectedValue=9;Field<LookupBox>("targetYear").SelectedValue=10;
        Field<ModernTextBox>("reason").Text="Annual draft";Field<CheckBox>("carry").Checked=true;
        Check(Field<ModernComboBox>("fees").Parent is null && Field<LookupBox>("targetClass").Parent is null,"Annual promotion omits transfer-only fields.");
        tabs.SelectedIndex=1;Application.DoEvents();
        Check(Field<LookupBox>("targetYear").Parent is null && Field<ModernComboBox>("fees").Parent is not null,"Transfers show fee policy and use the source year without a redundant destination year.");
        Check(Field<LookupBox>("targetYear").SelectedId==9 && Field<ModernTextBox>("reason").Text=="",$"A new transfer draft stays in its source year and has its own reason (source={Field<LookupBox>("sourceYear").SelectedId}, target={Field<LookupBox>("targetYear").SelectedId}, reason={Field<ModernTextBox>("reason").Text}).");
        Check(!Field<CheckBox>("carry").Visible && !Field<ModernButton>("studentActions").Visible && !Field<ModernButton>("findUndo").Visible,"Transfer tab hides promotion concessions, lifecycle actions and undo controls.");
        var transferData=Field<DataTable>("data");Field<ModernTextBox>("reason").Text="Transfer draft";
        var decisionChoices=(DataView)((DataGridViewComboBoxColumn)grid.Columns["Decision"]).DataSource!;
        Check(decisionChoices.Cast<DataRowView>().Select(row=>Convert.ToString(row["Value"])).SequenceEqual(new[]{"MOVE","SKIP"}),"Transfer decisions contain only Transfer and Skip.");
        tabs.SelectedIndex=2;Application.DoEvents();
        Check(Field<ModernButton>("correction").Visible && Field<ModernButton>("findUndo").Visible,"Correction tools are grouped on the Corrections tab.");
        Check(((DataView)((DataGridViewComboBoxColumn)grid.Columns["Decision"]).DataSource!).Cast<DataRowView>().Select(row=>Convert.ToString(row["Value"])).SequenceEqual(new[]{"CORRECT","SKIP"}),"Correction decisions contain only Correct Placement and Skip.");
        Field<ModernTextBox>("reason").Text="Correction draft";
        tabs.SelectedIndex=3;Application.DoEvents();
        Check(!tabs.SelectedTab!.Contains(Field<LookupBox>("sourceYear")) && tabs.SelectedTab.Contains(Field<ModernTextBox>("historyReason")),"History displays its own undo explanation instead of plan filters.");
        var historyRows=new DataTable();historyRows.Columns.Add("StudentName");historyRows.Rows.Add("Fictional History Student");Field<DataGridView>("history").DataSource=historyRows;Application.DoEvents();
        var historySelection=(DataRowView)typeof(StudentPlacementPage).GetMethod("SelectedStudent",flags)!.Invoke(page,null)!;
        Check(Convert.ToString(historySelection["StudentName"])=="Fictional History Student","History correction actions use the selected history student rather than a planning draft.");
        Field<ModernTextBox>("historyReason").Text="Undo explanation";
        tabs.SelectedIndex=0;Application.DoEvents();
        Check(ReferenceEquals(Field<DataTable>("data"),annualData) && Field<ModernTextBox>("reason").Text=="Annual draft" && Field<CheckBox>("carry").Checked && Field<LookupBox>("targetYear").SelectedId==10,"Switching tabs restores annual selections, decisions, reason and concessions.");
        tabs.SelectedIndex=1;Application.DoEvents();
        Check(ReferenceEquals(Field<DataTable>("data"),transferData) && Field<ModernTextBox>("reason").Text=="Transfer draft","The transfer draft remains separate from promotion and correction drafts.");
        tabs.SelectedIndex=2;Application.DoEvents();Check(Field<ModernTextBox>("reason").Text=="Correction draft","Correction drafts survive a history visit.");
        tabs.SelectedIndex=3;Application.DoEvents();Check(Field<ModernTextBox>("historyReason").Text=="Undo explanation","Undo has a separate retained explanation.");
        typeof(StudentPlacementPage).GetField("pending",flags)!.SetValue(page,(Guid.NewGuid(),"[]",9,DateTime.Today,"Pending",false,"hash"));
        tabs.SelectedIndex=0;Check(tabs.SelectedIndex==3 && page.IsBusy,"An unconfirmed result blocks task switching.");
        typeof(StudentPlacementPage).GetMethod("SetPendingControls",flags)!.Invoke(page,new object[]{tabs});
        Check(!Field<ModernTextBox>("historyReason").Enabled,"Pending recovery locks the history correction details.");
        IEnumerable<Control> Descendants(Control parent)=>parent.Controls.Cast<Control>().SelectMany(control=>new[]{control}.Concat(Descendants(control)));
        Check(Descendants(tabs.SelectedTab!).OfType<ModernButton>().Any(button=>button.Text=="Check / Retry Plan" && button.Visible && button.Enabled),"History keeps its recovery action available while an undo outcome is unconfirmed.");
        typeof(StudentPlacementPage).GetField("pending",flags)!.SetValue(page,null);
        typeof(StudentPlacementPage).GetMethod("SetPendingControls",flags)!.Invoke(page,new object[]{tabs});
        tabs.SelectedIndex=0;Application.DoEvents();
        string output=Path.GetFullPath("artifacts/placement-workspace.png");Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var bitmap=new Bitmap(window.ClientSize.Width,window.ClientSize.Height);window.DrawToBitmap(bitmap,window.ClientRectangle);bitmap.Save(output);
        foreach(int task in new[]{1,2,3}){tabs.SelectedIndex=task;Application.DoEvents();using var taskBitmap=new Bitmap(window.ClientSize.Width,window.ClientSize.Height);window.DrawToBitmap(taskBitmap,window.ClientRectangle);taskBitmap.Save(Path.GetFullPath($"artifacts/placement-workspace-tab-{task}.png"));}
    }
}
