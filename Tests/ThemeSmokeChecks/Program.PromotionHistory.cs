using System.Data;
using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Enrollment;
using Microsoft.Data.SqlClient;

internal static partial class Program
{
    private static void CheckPromotionHistoryEntry()
    {
        using (var catalog = new DataTable())
        using (var assignments = new DataTable())
        {
            catalog.Columns.Add("ClassId", typeof(int)); catalog.Columns.Add("ClassName");
            catalog.Columns.Add("DisplayOrder", typeof(int)); catalog.Columns.Add("AcademicProgramId", typeof(int));
            catalog.Rows.Add(1, "Nursery", 1, 1); catalog.Rows.Add(4, "Four", 4, 1); catalog.Rows.Add(5, "Five", 5, 1);
            assignments.Columns.Add("ClassId", typeof(int));
            assignments.Rows.Add(5); assignments.Rows.Add(4); assignments.Rows.Add(4);
            using var filtered = BulkPromotionPage.CampusClasses(catalog, assignments);
            Check(filtered.AsEnumerable().Select(row => row.Field<int>("ClassId")).SequenceEqual(new[] { 4, 5 }),
                "Promotion offers only campus-assigned classes in catalog order, without duplicating sections");
            Check(filtered.Rows[1].Field<int>("AcademicProgramId") == 1 && filtered.Rows[1].Field<int>("DisplayOrder") == 5,
                "Campus filtering preserves program and progression rules for destination classes");
            assignments.Clear();
            using var empty = BulkPromotionPage.CampusClasses(catalog, assignments);
            Check(empty.Rows.Count == 0, "A campus without assignments cannot fall back to every class");
            catalog.Rows.Add(10,"Ten",10,1);catalog.Rows.Add(20,"First Year",1,2);
            assignments.Rows.Add(4);assignments.Rows.Add(10);assignments.Rows.Add(20);
            using var missingNext=PromotionChoices.DestinationClasses(catalog,assignments,4);
            Check(missingNext.Rows.Count==0,"A missing next campus class cannot cause a jump from Four to Ten");
            assignments.Rows.Add(5);
            using var next=PromotionChoices.DestinationClasses(catalog,assignments,4);
            Check(next.Rows.Count==1&&next.Rows[0].Field<int>("ClassId")==5,"The only promotion destination is the immediate next catalog class at the campus");
            using var finalClass=PromotionChoices.DestinationClasses(catalog,assignments,10);
            Check(finalClass.Rows.Count==0,"The final program class does not jump into another program");
            using var sources=new DataTable();sources.Columns.Add("ClassId",typeof(int));sources.Columns.Add("SectionId",typeof(int));
            sources.Columns.Add("SectionName");sources.Columns.Add("SectionDisplayOrder",typeof(int));
            sources.Rows.Add(4,2,"B",2);sources.Rows.Add(4,1,"A",1);sources.Rows.Add(4,1,"A",1);sources.Rows.Add(5,3,"C",3);
            using var occupied=BulkPromotionPage.CampusClasses(catalog,sources);
            Check(occupied.AsEnumerable().Select(row=>row.Field<int>("ClassId")).SequenceEqual(new[]{4,5}),
                "Source classes contain only classes represented by the year's active placements");
            using var occupiedSections=PromotionChoices.SourceSections(sources,4);
            Check(occupiedSections.AsEnumerable().Select(row=>row.Field<int>("SectionId")).SequenceEqual(new[]{1,2}),
                "Source sections contain only populated sections in that class, without duplicates, in display order");
            sources.Clear();
            using var noSources=BulkPromotionPage.CampusClasses(catalog,sources);
            Check(noSources.Rows.Count==0,"An empty source year cannot fall back to campus classes");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        try
        {
            foreach (var access in new[] { (Role: "Operator", Permissions: new[] { "Enrollment.Manage" }, Allowed: true),
                (Role: "Accountant", Permissions: new[] { "Students.View" }, Allowed: false),
                (Role: "Administrator", Permissions: Array.Empty<string>(), Allowed: true) })
            {
                CurrentUser.SignIn(910, "Promotion fixture", access.Role, false, access.Permissions);
                using var shell = new MainDashboardForm((_, _) => { });
                var commands = (List<NavigationCommand>)typeof(MainDashboardForm).GetField("navigationCommands", flags)!.GetValue(shell)!;
                Check(commands.Count(c => c.Caption == PageTitles.StudentPromotion) == (access.Allowed ? 1 : 0),
                    "Student Promotion keeps the established permission and Administrator override");
                Check(commands.All(c => c.Caption is not ("Promotion History" or "Bulk Promotion")),
                    "The sidebar and command palette have one promotion destination");
            }
            CurrentUser.SignIn(911, "Promotion fixture", "Operator", false, new[] { "Enrollment.Manage", "Reports.View" });
            using var promotion = (BulkPromotionPage)Activator.CreateInstance(typeof(BulkPromotionPage), flags, null,
                new object?[] { null, false }, null)!;
            var promotionType = typeof(BulkPromotionPage);
            T Field<T>(string name) => (T)promotionType.GetField(name, flags)!.GetValue(promotion)!;
            void Invoke(string name) => promotionType.GetMethod(name, flags, null, Type.EmptyTypes, null)!.Invoke(promotion, null);
            DataTable Lookup(string key, string text, params (int Id, string Name)[] rows)
            {
                var table = new DataTable(); table.Columns.Add(key, typeof(int)); table.Columns.Add(text, typeof(string));
                foreach (var row in rows) table.Rows.Add(row.Id, row.Name);
                return table;
            }
            var years = Lookup("AcademicYearId", "YearName", (103, "Fixture source year"), (104, "Fixture next year"));
            var classes = Lookup("ClassId", "ClassName", (606, "Fixture Class 6"), (607, "Fixture Class 7"));
            classes.Columns.Add("DisplayOrder",typeof(int));classes.Columns.Add("AcademicProgramId",typeof(int));
            for(int index=0;index<classes.Rows.Count;index++){classes.Rows[index]["DisplayOrder"]=index+6;classes.Rows[index]["AcademicProgramId"]=1;}
            var sections = Lookup("SectionId", "SectionName", (17, "Fixture Section A"));
            foreach (var item in new[] { (Field: "sourceYear", Data: years, Key: "AcademicYearId", Text: "YearName", Id: 103),
                (Field: "sourceClass", Data: classes, Key: "ClassId", Text: "ClassName", Id: 606),
                (Field: "sourceSection", Data: sections, Key: "SectionId", Text: "SectionName", Id: 17) })
            {
                var lookup = Field<LookupBox>(item.Field); lookup.Bind(item.Data, item.Key, item.Text); lookup.SelectedValue = item.Id;
            }
            Field<LookupBox>("sourceCampus").Bind(Lookup("CampusId","CampusName",(21,"Fixture campus")),"CampusId","CampusName");
            Field<LookupBox>("sourceCampus").SelectedValue=21;
            var placements=new DataTable();placements.Columns.Add("ClassId",typeof(int));placements.Columns.Add("SectionId",typeof(int));
            placements.Columns.Add("SectionName");placements.Columns.Add("SectionDisplayOrder",typeof(int));placements.Rows.Add(606,17,"Fixture Section A",1);
            var assigned=placements.Copy();assigned.Columns.Add("ClassSectionId",typeof(int));assigned.Columns.Add("ClassName");
            assigned.Rows[0]["ClassSectionId"]=71;assigned.Rows[0]["ClassName"]="Fixture Class 6";
            assigned.Rows.Add(607,18,"Fixture Section B",2,72,"Fixture Class 7");
            promotionType.GetField("classTable",flags)!.SetValue(promotion,classes);
            promotionType.GetField("campusAssignments",flags)!.SetValue(promotion,assigned);
            promotionType.GetField("sourcePlacements",flags)!.SetValue(promotion,placements);
            Invoke("BuildDestinationClasses");
            promotionType.GetMethod("LoadSourceSections",flags)!.Invoke(promotion,null);
            Field<LookupBox>("sourceSection").SelectedValue=17;
            promotionType.GetMethod("LoadDestinationSections",flags)!.Invoke(promotion,null);
            Check(Field<LookupBox>("destinationClass").SelectedId==607&&Field<LookupBox>("destinationClass").Items.Count==2,
                "The actual form offers and selects only its immediate next campus class");
            Check(Field<LookupBox>("sourceSection").Items.Count==2&&Field<LookupBox>("destinationSection").SelectedId==72,
                "The form's populated source section and configured destination section remain distinct");
            var candidates = new DataTable(); candidates.Columns.Add("Selected", typeof(bool));
            candidates.Columns.Add("Decision", typeof(string)); candidates.Rows.Add(true, "PROMOTE");
            promotionType.GetField("candidateData", flags)!.SetValue(promotion, candidates);
            Field<DataGridView>("candidatesGrid").DataSource=candidates;
            Field<ModernTextBox>("remarks").Text = "Retain this promotion draft";
            using var parent = new Form { ClientSize = new Size(1080, 650), AutoScaleMode = AutoScaleMode.None };
            parent.Controls.Add(promotion); parent.Show(); Application.DoEvents(); Invoke("ApplyState");
            candidates.Rows[0]["Decision"]="HOLD";Application.DoEvents();
            Check(Convert.ToString(Field<DataGridView>("candidatesGrid").Rows[0].Cells["Decision"].FormattedValue)=="Repeat Class"
                &&candidates.Rows[0].Field<string>("Decision")=="HOLD","Repeat Class is displayed while the established saved decision stays compatible");
            Invoke("ApplyStateAndHint");
            Check(((Label)typeof(V2Page).GetField("StatusLabel",flags)!.GetValue(promotion)!).Text.Contains("1 to repeat class"),
                "The live selection message explains Repeat Class");
            candidates.Rows[0]["Decision"]="PROMOTE";
            Check(Field<ModernButton>("holdAllButton").Text=="Ticked: Repeat Class","The batch action names Repeat Class explicitly");
            var historyButton = Field<ModernButton>("historyButton");
            Check(promotion.NavigationTitle == PageTitles.StudentPromotion, "The actual page title agrees with navigation");
            Check(historyButton.Visible && historyButton.Enabled && historyButton.Parent is Panel,
                "History is accessible before candidates or a complete promotion setup are required");
            var header = historyButton.Parent!;
            foreach (int width in new[] { 1280, 1080, 850 })
            {
                parent.ClientSize = new Size(width, 850); Application.DoEvents();
                Check(historyButton.Left >= 0 && historyButton.Right <= header.ClientSize.Width &&
                    historyButton.Top >= 0 && historyButton.Bottom <= header.ClientSize.Height,
                    "The history accessory fits the shared header at different client widths");
                Check(header.Controls.OfType<Label>().All(l => !l.Bounds.IntersectsWith(historyButton.Bounds)),
                    "The history accessory leaves header wording readable");
                var repeat=Field<ModernButton>("holdAllButton");
                Check(repeat.Right<=repeat.Parent!.ClientSize.Width&&repeat.Width>=AppMetrics.TextWidth(repeat.Text,repeat.Font,AppScale.Scale(20)),
                    "Repeat Class remains fully readable when the promotion action bar wraps");
                Directory.CreateDirectory("artifacts/promotion-wording");
                using var capture=new Bitmap(parent.ClientSize.Width,parent.ClientSize.Height);
                parent.DrawToBitmap(capture,new Rectangle(Point.Empty,parent.ClientSize));
                capture.Save($"artifacts/promotion-wording/promotion-{width}.png");
            }
            Invoke("BeginBusy"); Check(!historyButton.Enabled, "Loading or processing disables the history shortcut");
            Invoke("OpenHistory"); Invoke("EndBusy");
            Check(historyButton.Enabled && Application.OpenForms.Count == 1, "Busy direct dispatch cannot open a window; idle restores history");
            CurrentUser.SignIn(912, "Restricted fixture", "Operator", false, Array.Empty<string>());
            Invoke("ApplyState"); Check(!historyButton.Enabled, "History follows the current account's permission");
            Invoke("OpenHistory");
            Check(Application.OpenForms.Count == 1, "Direct history dispatch cannot bypass revoked access");
            CurrentUser.SignIn(913, "Promotion-only fixture", "Operator", false, new[] { "Enrollment.Manage" }); Invoke("ApplyState");
            Check(!historyButton.Enabled, "Enrollment permission alone cannot bypass the history procedure's Reports.View requirement");
            Invoke("OpenHistory"); Check(Application.OpenForms.Count == 1, "Direct history dispatch rechecks report access");
            CurrentUser.SignIn(914, "Administrator fixture", "Administrator", false, Array.Empty<string>()); Invoke("ApplyState");
            Check(historyButton.Enabled, "Administrator retains full promotion-history authority");
            CurrentUser.SignIn(911, "Promotion fixture", "Operator", false, new[] { "Enrollment.Manage", "Reports.View" }); Invoke("ApplyState");

            using var dialog = (ApplicationWindowForm)promotionType.GetMethod("CreateHistoryDialog", flags)!.Invoke(promotion, null)!;
            var originalHistory = (PromotionHistoryPage)dialog.Controls[0];
            var initial = ((int Year, int Class, int Section))typeof(PromotionHistoryPage).GetField("initialFilters", flags)!.GetValue(originalHistory)!;
            Check(initial == (103, 606, 17), "The window snapshots source year, class and section, not destination filters");
            Check(dialog.Text == "Promotion History" && !dialog.ShowInTaskbar && originalHistory.Dock == DockStyle.Fill,
                "The shared responsive window owns a full-sized history page");
            // Substitute only the reader in this native fixture; never show a production SQL reader.
            dialog.Controls.Remove(originalHistory); originalHistory.Dispose();
            var reads = new List<SqlParameter[]>();
            Func<string, SqlParameter[], DataTable> query = (procedure, parameters) => {
                if (procedure == "GetAcademicYears") return years;
                if (procedure == "GetClasses") return classes;
                if (procedure == "GetSections") return sections;
                Check(procedure == "GetPromotionBatchesFiltered", "History retains its production read-only procedure contract");
                reads.Add(parameters);
                var batches = new DataTable(); batches.Columns.Add("PromotionBatchId", typeof(int));
                batches.Columns.Add("PromotedCount", typeof(int));batches.Columns.Add("HeldBackCount",typeof(int)); batches.Rows.Add(201, 2,1); return batches;
            };
            PromotionHistoryPage History(int year, int cls, int section) => (PromotionHistoryPage)Activator.CreateInstance(
                typeof(PromotionHistoryPage), flags, null, new object[] { year, cls, section, query }, null)!;
            using var history = History(initial.Year, initial.Class, initial.Section); dialog.Controls.Add(history);
            dialog.Show(parent);
            PumpUntil(() => reads.Count == 1, "The history window loads its carried source filters");
            Check(reads[0].Select(p => p.ParameterName).SequenceEqual(new[] { "@SourceAcademicYearId", "@SourceClassId", "@SourceSectionId" }) &&
                reads[0].Select(p => Convert.ToInt32(p.Value)).SequenceEqual(new[] { 103, 606, 17 }),
                "The first history query uses the selected source IDs");
            var historyType = typeof(PromotionHistoryPage);
            T HistoryField<T>(string name) => (T)historyType.GetField(name, flags)!.GetValue(history)!;
            var export = HistoryField<ModernButton>("exportButton");
            Check(export.Enabled && HistoryField<Label>("summary").Text.Contains("1 batch"), "Loaded history retains summary and export availability");
            Check(HistoryField<Label>("summary").Text.Contains("Repeated class: 1")
                &&((DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(history)!).Columns["HeldBackCount"].HeaderText=="Repeated class",
                "Promotion history gives repeat decisions a clear label in both the grid and summary");
            HistoryField<LookupBox>("year").SelectedValue = 104;
            Check(!export.Enabled && reads.Count == 1, "Changing history filters requires a fresh Load before export");
            historyType.GetMethod("LoadData", flags)!.Invoke(history, null);
            Check(export.Enabled && Convert.ToInt32(reads.Last()[0].Value) == 104, "Users can load a different year within the history window");
            historyType.GetMethod("ClearForm", flags)!.Invoke(history, null);
            Check(!export.Enabled && HistoryField<Label>("summary").Text.Length == 0, "Clear removes the displayed history and disables export");
            historyType.GetMethod("LoadData", flags)!.Invoke(history, null);
            Check(reads.Last().All(p => Convert.ToInt32(p.Value) == 0), "Blank history filters use the production procedure's zero-as-all contract");
            dialog.Close();
            Check(ReferenceEquals(promotionType.GetField("candidateData", flags)!.GetValue(promotion), candidates) &&
                (bool)candidates.Rows[0]["Selected"] && Field<ModernTextBox>("remarks").Text == "Retain this promotion draft" &&
                Field<LookupBox>("sourceYear").SelectedId == 103,
                "Closing history preserves promotion selections, filters and remarks");
            Check(history.IsDisposed && !promotion.IsDisposed, "The closed history window releases its page without disposing promotion");

            using var missing = History(999, 606, 17);
            int before = reads.Count; parent.Controls.Add(missing); missing.BringToFront();
            PumpUntil(() => ((LookupBox)historyType.GetField("year", flags)!.GetValue(missing)!).Items.Count > 0,
                "Stale carried filters bind the available lookup choices");
            Check(reads.Count == before && !(bool)((ModernButton)historyType.GetField("exportButton", flags)!.GetValue(missing)!).Enabled,
                "A missing carried filter refuses an accidental unfiltered history load");
            Check(((Label)typeof(V2Page).GetField("StatusLabel", flags)!.GetValue(missing)!).Text.Contains("no longer available"),
                "Unavailable carried filters give a clear recovery instruction");
            parent.Close();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
