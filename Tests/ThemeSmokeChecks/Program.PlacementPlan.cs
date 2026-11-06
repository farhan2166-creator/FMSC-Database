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
        CheckPlacementChoices();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(StudentPlacementPage page,string name)=>(T)typeof(StudentPlacementPage).GetField(name,flags)!.GetValue(page)!;
        void Call(StudentPlacementPage page,string name)=>typeof(StudentPlacementPage).GetMethod(name,flags)!.Invoke(page,null);
        IEnumerable<Control> Descendants(Control parent)=>parent.Controls.Cast<Control>().SelectMany(control=>new[]{control}.Concat(Descendants(control)));
        var titles=new[]{PageTitles.AnnualPromotion,PageTitles.PlacementTransfers,PageTitles.PlacementCorrections,PageTitles.PlacementHistory};
        var subtitles=new HashSet<string>();
        string? navigatedModule=null,navigatedTitle=null;Func<Control>? destination=null;

        foreach(StudentPlacementTask task in Enum.GetValues<StudentPlacementTask>())
        {
            using var page=new StudentPlacementPage((factory,module,title)=>{destination=factory;navigatedModule=module;navigatedTitle=title;},false,task);
            using var window=new Form{ClientSize=new Size(1200,800),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)};
            window.Controls.Add(page);window.Show();Application.DoEvents();
            var content=Field<Control>(page,"pageContent");
            Check(page.NavigationTitle==titles[(int)task],"Each placement route has its own page title.");
            Check(subtitles.Add(StudentPlacementPage.Subtitle(task)),"Each page has a task-specific subtitle.");
            Check(!Descendants(page).OfType<TabControl>().Any(),"Placement pages use the full content area without an internal tab strip.");
            Check(!page.IsBusy,"An idle placement page permits sidebar navigation.");
            var grid=Field<DataGridView>(page,"students");var data=Field<DataTable>(page,"data");
            var visibleButtons=Descendants(content).OfType<ModernButton>().Where(button=>button.Visible).ToArray();
            var shownGrid=task==StudentPlacementTask.History?Field<DataGridView>(page,"history"):grid;
            int buttonBottom=visibleButtons.Max(button=>button.PointToScreen(new Point(0,button.Height)).Y);
            var buttonHost=(ScrollableControl)visibleButtons[0].Parent!.Parent!;
            int scrollSpace=buttonHost.HorizontalScroll.Visible?SystemInformation.HorizontalScrollBarHeight:0;
            Check(shownGrid.PointToScreen(Point.Empty).Y-buttonBottom<=AppScale.Scale(10)+scrollSpace,"Empty pages do not reserve blank summary or unused scrollbar space below their buttons.");
            bool Fits(ModernButton button){var text=TextRenderer.MeasureText(button.Text,button.Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);return button.ClientSize.Width-button.Padding.Horizontal-1>=text.Width && button.ClientSize.Height-button.Padding.Vertical>=text.Height;}
            Check(visibleButtons.All(button=>Fits(button) && !button.Text.EndsWith("…") && !button.Text.EndsWith("...")),"Every action shows its complete caption with room for the actual rendered font.");
            var longest=visibleButtons.MaxBy(button=>button.Text.Length)!;var originalFont=longest.Font;
            using(var largerFont=new Font(originalFont.FontFamily,originalFont.Size*1.25f,originalFont.Style,originalFont.Unit)){
                longest.Font=largerFont;Application.DoEvents();Check(Fits(longest),"Action widths follow larger fonts without clipping their captions.");longest.Font=originalFont;
            }
            if(task==StudentPlacementTask.History)
            {
                Check(content.Contains(Field<DataGridView>(page,"history")) && !content.Contains(Field<LookupBox>(page,"sourceYear")),"History contains correction tools without source filters.");
                var rows=new DataTable();foreach(string name in new[]{"AdmissionNumber","StudentName","Action","PreviousPlacement","DestinationPlacement","Status","FeePolicy","Reason","RecordStatus"})rows.Columns.Add(name);
                rows.Columns.Add("CreatedAt",typeof(DateTime));rows.Columns.Add("RequestId",typeof(Guid));rows.Columns.Add("ItemId",typeof(int));rows.Columns.Add("UndoneBy",typeof(Guid));
                var movement=rows.NewRow();movement["AdmissionNumber"]="1001";movement["StudentName"]="Fictional History Student";movement["CreatedAt"]=DateTime.Today.AddHours(9);movement["Action"]="PROMOTE";movement["PreviousPlacement"]="Campus A / One / A";movement["DestinationPlacement"]="Campus A / Two / A";movement["Status"]="Active";movement["Reason"]="Fictional annual promotion";movement["RecordStatus"]="Recorded";movement["RequestId"]=Guid.NewGuid();movement["ItemId"]=41;rows.Rows.Add(movement);
                Field<DataGridView>(page,"history").DataSource=rows;Application.DoEvents();
                var historyGrid=Field<DataGridView>(page,"history");
                var properties=historyGrid.Columns.Cast<DataGridViewColumn>().Where(column=>column.Visible).OrderBy(column=>column.DisplayIndex).Select(column=>column.DataPropertyName).ToArray();
                Check(properties.SequenceEqual(new[]{"AdmissionNumber","StudentName","CreatedAt","Action","PreviousPlacement","DestinationPlacement","Status","FeePolicy","Reason","RecordStatus"}),"History presents identity, time, action, source/destination, status and notes in reading order.");
                Check(Convert.ToString(historyGrid.Rows[0].Cells["RecordedAt"].FormattedValue)!.Contains("09:00"),"The recorded movement timestamp includes its time of day.");
                Check(!historyGrid.Columns.Cast<DataGridViewColumn>().Any(column=>column.Visible && column.DataPropertyName is "RequestId" or "ItemId" or "UndoneBy"),"History retains recovery fields in its data without exposing request IDs as user-facing columns.");
                var selected=(DataRowView)typeof(StudentPlacementPage).GetMethod("SelectedStudent",flags)!.Invoke(page,null)!;
                Check(Convert.ToString(selected["StudentName"])=="Fictional History Student","History actions use the selected history student.");
                Check(!Field<ModernButton>(page,"undo").Enabled,"History undo requires a reason for the selected promotion.");
                Field<ModernTextBox>(page,"historyReason").Text="Fictional undo explanation";
                Check(Field<ModernButton>(page,"undo").Enabled,"A recorded promotion and reason unlock undo review.");
            }
            else
            {
                Check(content.Contains(Field<LookupBox>(page,"sourceYear")) && content.Contains(grid),"Planning pages retain source fields and a student plan.");
                Check(Field<LookupBox>(page,"sourceYear").PointToScreen(Point.Empty).X<Field<LookupBox>(page,"targetCampus").PointToScreen(Point.Empty).X,"Source fields stay on the left and destinations on the right.");
                Check(!Field<ModernButton>(page,"load").Enabled && !Field<ModernButton>(page,"review").Enabled && !Field<LookupBox>(page,"targetCampus").Enabled,"Empty planning pages gate loading, review and destination selection.");
                Check(!grid.ReadOnly && grid.Columns["Current"].ReadOnly,"Decisions are editable while current placements remain read-only.");
                var choices=(DataView)((DataGridViewComboBoxColumn)grid.Columns["Decision"]).DataSource!;
                string[] expected=task switch{StudentPlacementTask.AnnualPromotion=>new[]{"PROMOTE","HOLD","SKIP"},StudentPlacementTask.Transfer=>new[]{"MOVE","SKIP"},_=>new[]{"CORRECT","SKIP"}};
                Check(choices.Cast<DataRowView>().Select(row=>Convert.ToString(row["Value"])).SequenceEqual(expected),"Decision choices match the page's task.");
                if(task==StudentPlacementTask.AnnualPromotion)
                {
                    Check(Field<ModernComboBox>(page,"fees").Parent is null && Field<LookupBox>(page,"targetClass").Parent is null && Field<CheckBox>(page,"carry").Visible,"Annual Promotion offers concessions without transfer-only fields.");
                    var catalog=Field<DataTable>(page,"catalog");catalog.Columns.Add("ClassId",typeof(int));catalog.Columns.Add("AcademicProgramId",typeof(int));catalog.Columns.Add("DisplayOrder",typeof(int));catalog.Rows.Add(1,1,1);catalog.Rows.Add(2,1,2);
                    var placements=Field<DataTable>(page,"placements");foreach(string name in new[]{"ClassSectionId","CampusId","ClassId","SectionId"})placements.Columns.Add(name,typeof(int));placements.Columns.Add("IsActive",typeof(bool));placements.Columns.Add("Display");placements.Rows.Add(1,1,1,1,true,"Campus A / One / A");placements.Rows.Add(2,1,2,1,true,"Campus A / Two / A");
                    foreach(string name in new[]{"ClassId","CampusId","SectionId","SourcePlacementId","Destination"})data.Columns.Add(name,typeof(int));foreach(string name in new[]{"Decision","Note","PlacementState"})data.Columns.Add(name);data.Columns.Add("LegacySetupPending",typeof(bool));data.Columns.Add("PlacementConfirmedAt",typeof(DateTime));data.Columns.Add("Selected",typeof(bool));
                    data.Rows.Add(1,1,1,1,0,"PROMOTE","","Active",false,DateTime.Now,true);data.Rows.Add(2,1,1,2,0,"PROMOTE","","Active",false,DateTime.Now,true);
                    Call(page,"Suggest");Check(Convert.ToInt32(data.Rows[0]["Destination"])==2,"Promotion suggests the immediate next class and matching section.");
                    Check(Convert.ToInt32(data.Rows[1]["Destination"])==0 && Convert.ToString(data.Rows[1]["Note"])!.Contains("Final class"),"Final-class students remain an explicit exception.");
                    data.Rows[0]["Decision"]="HOLD";Call(page,"Suggest");Check(Convert.ToInt32(data.Rows[0]["Destination"])==1,"Repeat keeps the original placement.");
                    data.Rows[0]["Decision"]="SKIP";Call(page,"UpdateTotals");Check(Field<Label>(page,"totals").Text.Contains("1 skipped"),"Skipping is separate from repeating.");
                    data.Columns.Add("StudentName");data.Columns.Add("Current");data.Rows[0]["StudentName"]="Fictional Student A";data.Rows[0]["Current"]="Campus A / One / A";data.Rows[0]["Decision"]="PROMOTE";data.Rows[1]["StudentName"]="Fictional Student B";data.Rows[1]["Current"]="Campus A / Two / A";Call(page,"Suggest");
                    var destinations=placements.Copy();var blank=destinations.NewRow();blank["ClassSectionId"]=0;blank["Display"]="Choose a destination";destinations.Rows.InsertAt(blank,0);((DataGridViewComboBoxColumn)grid.Columns["Destination"]).DataSource=destinations;grid.DataSource=data;
                    var years=new DataTable();years.Columns.Add("AcademicYearId",typeof(int));years.Columns.Add("YearName");years.Rows.Add(9,"Source Year");years.Rows.Add(10,"Next Year");foreach(var name in new[]{"sourceYear","targetYear"})Field<LookupBox>(page,name).Bind(years.Copy(),"AcademicYearId","YearName");Field<LookupBox>(page,"sourceYear").SelectedValue=9;Field<LookupBox>(page,"targetYear").SelectedValue=10;Field<CheckBox>(page,"carry").Checked=true;
                }
                else
                {
                    Check(Field<LookupBox>(page,"targetYear").Parent is null && Field<ModernComboBox>(page,"fees").Visible && !Field<CheckBox>(page,"carry").Visible,"Same-year pages show fee choices without a second year or promotion concessions.");
                    if(task==StudentPlacementTask.Correction){
                        Check(Field<ModernButton>(page,"correction").Visible && Field<ModernButton>(page,"findUndo").Visible,"Corrections groups date history and promotion undo links.");
                        Call(page,"OpenPlacementHistory");Check(destination is not null && navigatedModule==PageTitles.PlacementManagement && navigatedTitle==PageTitles.PlacementHistory,"Undo opens the separate Placement History route.");
                    }else Check(!Field<ModernButton>(page,"correction").Visible && !Field<ModernButton>(page,"studentActions").Visible,"Transfers exclude unrelated correction and lifecycle tools.");
                }
                Field<ModernTextBox>(page,"reason").Text="Fictional reviewed placement change";
            }
            typeof(StudentPlacementPage).GetField("pending",flags)!.SetValue(page,(Guid.NewGuid(),"[]",9,DateTime.Today,"Pending",false,"hash"));
            typeof(StudentPlacementPage).GetMethod("RefreshActionAvailability",flags)!.Invoke(page,null);
            Check(page.IsBusy,"An unconfirmed outcome keeps the shell navigation lock on every page.");
            Check(Descendants(content).OfType<ModernButton>().Any(button=>button.Text=="Check / Retry Plan" && button.Visible && button.Enabled),"Every page retains its recovery action for an uncertain outcome.");
            Check(task==StudentPlacementTask.History?!Field<ModernTextBox>(page,"historyReason").Enabled:!Field<LookupBox>(page,"sourceYear").Enabled,"Unconfirmed plans lock their submitted details.");
            destination=null;Call(page,"OpenPlacementHistory");Check(destination is null,"Contextual navigation cannot abandon an unconfirmed result.");
            typeof(StudentPlacementPage).GetField("pending",flags)!.SetValue(page,null);typeof(StudentPlacementPage).GetMethod("RefreshActionAvailability",flags)!.Invoke(page,null);
            window.ClientSize=new Size(900,700);Application.DoEvents();Check((task==StudentPlacementTask.History?Field<DataGridView>(page,"history"):grid).Height>=100,"Every page retains a usable grid at a compact window size.");
            window.ClientSize=new Size(1600,850);Application.DoEvents();string output=Path.GetFullPath(task==StudentPlacementTask.AnnualPromotion?"artifacts/placement-workspace.png":$"artifacts/placement-page-{task}.png");Directory.CreateDirectory(Path.GetDirectoryName(output)!);using var bitmap=new Bitmap(window.ClientSize.Width,window.ClientSize.Height);window.DrawToBitmap(bitmap,window.ClientRectangle);bitmap.Save(output);
        }
        CurrentUser.SignIn(7782,"Placement without adjustments","Operator",false,new[]{"Enrollment.Manage"});
        foreach(var task in new[]{StudentPlacementTask.AnnualPromotion,StudentPlacementTask.Transfer,StudentPlacementTask.History})
        {
            using var restricted=new StudentPlacementPage(null,false,task);
            var content=Field<Control>(restricted,"pageContent");
            if(task==StudentPlacementTask.AnnualPromotion)
                Check(!Field<CheckBox>(restricted,"carry").Enabled,"Carrying monthly concessions requires SQL's Fees.Adjust grant");
            if(task==StudentPlacementTask.Transfer)
                Check(!Field<ModernComboBox>(restricted,"fees").Enabled,"Changing destination fees requires SQL's Fees.Adjust grant");
            if(task==StudentPlacementTask.History)
                Check(!Descendants(content).OfType<ModernButton>().Single(b=>b.Text=="Undo Selected Promotion").Enabled,
                    "Undo promotion requires Students.Administer as well as Enrollment.Manage");
            typeof(StudentPlacementPage).GetMethod("RefreshActionAvailability",flags)!.Invoke(restricted,null);
            if(task==StudentPlacementTask.History)
                Check(!Descendants(content).OfType<ModernButton>().Single(b=>b.Text=="Undo Selected Promotion").Enabled,
                    "Refreshing placement controls cannot reenable an unauthorized undo");
            else if(task==StudentPlacementTask.AnnualPromotion)
                Check(!Field<CheckBox>(restricted,"carry").Enabled,"Refreshing controls keeps concession carry permission-gated");
            else
                Check(!Field<ModernComboBox>(restricted,"fees").Enabled,"Refreshing controls keeps transfer fee choices permission-gated");
        }
        CurrentUser.SignIn(7781,"Placement fixture","Administrator",false,Array.Empty<string>());
    }
}
