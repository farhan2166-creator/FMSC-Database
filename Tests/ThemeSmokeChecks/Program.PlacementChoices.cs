using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Enrollment;

internal static partial class Program
{
    private static void CheckPlacementChoices()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        DataTable Table(params (string Name, Type Type)[] columns) {
            var table = new DataTable(); foreach (var column in columns) table.Columns.Add(column.Name, column.Type); return table;
        }
        using (var recorded = Table(("PlacementYearStart",typeof(DateTime)),("PlacementYearEnd",typeof(DateTime)))) {
            recorded.Rows.Add(DateTime.Today.AddYears(-1),DateTime.Today.AddYears(1));
            var current = typeof(StudentTransferReadmissionPage).GetMethod("IsCurrentPlacement",flags | BindingFlags.Static)!;
            Check((bool)current.Invoke(null,new object[]{recorded.Rows[0]})!,"Individual transfer choices recognize current placements");
            recorded.Rows[0]["PlacementYearEnd"]=DateTime.Today.AddDays(-1);
            Check(!(bool)current.Invoke(null,new object[]{recorded.Rows[0]})!,"Historical transfer choices retain their separate inactive-placement policy");
        }
        foreach (var task in new[] { StudentPlacementTask.AnnualPromotion, StudentPlacementTask.Transfer, StudentPlacementTask.Correction })
        {
            using var page = new StudentPlacementPage(null, false, task) { BindingContext = new BindingContext() };
            T Field<T>(string name) => (T)typeof(StudentPlacementPage).GetField(name, flags)!.GetValue(page)!;
            void Set(string name, object value) => typeof(StudentPlacementPage).GetField(name, flags)!.SetValue(page, value);
            object? Call(string name, params object[] args) => typeof(StudentPlacementPage).GetMethod(name, flags)!.Invoke(page, args);
            int[] Options(LookupBox box) => ((DataTable)box.DataSource!).AsEnumerable().Select(row => Convert.ToInt32(row[box.ValueMember])).Where(id => id > 0).ToArray();
            var campuses = Table(("CampusId",typeof(int)),("CampusName",typeof(string)));
            campuses.Rows.Add(1,"Campus A"); campuses.Rows.Add(2,"Campus B"); campuses.Rows.Add(3,"Unconfigured campus");
            var years = Table(("AcademicYearId",typeof(int)),("YearName",typeof(string)),("StartDate",typeof(DateTime)),("EndDate",typeof(DateTime)));
            years.Rows.Add(2,"Later",new DateTime(2032,1,1),new DateTime(2032,12,31));
            years.Rows.Add(9,"Source",new DateTime(2030,1,1),new DateTime(2030,12,31));
            years.Rows.Add(100,"Next",new DateTime(2031,1,1),new DateTime(2031,12,31));
            var classes = Table(("ClassId",typeof(int)),("ClassName",typeof(string)),("AcademicProgramId",typeof(int)),("DisplayOrder",typeof(int)));
            classes.Rows.Add(10,"One",1,1); classes.Rows.Add(20,"Two",1,2); classes.Rows.Add(30,"Three",1,3);
            classes.Rows.Add(40,"Unassigned",1,4); classes.Rows.Add(99,"Other program",2,1);
            var sections = Table(("SectionId",typeof(int)),("SectionName",typeof(string)));
            foreach (int id in new[] {1,2,3,4}) sections.Rows.Add(id,"Section "+id);
            var placements = Table(("ClassSectionId",typeof(int)),("CampusId",typeof(int)),("ClassId",typeof(int)),("SectionId",typeof(int)),("IsActive",typeof(bool)),("Display",typeof(string)));
            placements.Rows.Add(101,1,10,1,true,"A / One / 1"); placements.Rows.Add(102,1,10,2,false,"A / One / 2 historical");
            placements.Rows.Add(201,1,20,1,true,"A / Two / 1"); placements.Rows.Add(203,1,20,3,true,"A / Two / 3 empty");
            placements.Rows.Add(301,1,30,1,true,"A / Three / 1"); placements.Rows.Add(110,2,10,2,true,"B / One / 2");
            placements.Rows.Add(220,2,20,2,true,"B / Two / 2"); placements.Rows.Add(990,2,99,1,true,"B / Other / 1");
            var sources = Table(("ClassSectionId",typeof(int)),("CampusId",typeof(int)),("ClassId",typeof(int)),("SectionId",typeof(int)));
            sources.Rows.Add(101,1,10,1); sources.Rows.Add(102,1,10,2); sources.Rows.Add(110,2,10,2);
            Set("campusCatalog",campuses); Set("yearCatalog",years); Set("catalog",classes); Set("sectionCatalog",sections); Set("placements",placements);
            page.ReadSourceChoices = year => Task.FromResult(year is 9 or 100 ? sources.Copy() : sources.Clone());
            Call("BindPlacementChoices");
            var sourceYear = Field<LookupBox>("sourceYear"); var targetYear = Field<LookupBox>("targetYear");
            var sourceCampus = Field<LookupBox>("sourceCampus"); var sourceClass = Field<LookupBox>("sourceClass"); var sourceSection = Field<LookupBox>("sourceSection");
            var targetCampus = Field<LookupBox>("targetCampus"); var targetClass = Field<LookupBox>("targetClass");
            sourceYear.SelectedValue = 9;
            Check(Options(sourceCampus).SequenceEqual(new[]{1,2}) && Options(sourceClass).SequenceEqual(new[]{10}), task+": source choices come from actual placements in the selected year");
            Check(Field<ModernButton>("load").Enabled && !Field<ModernButton>("review").Enabled && !targetCampus.Enabled, task+": source selection unlocks loading only");
            sourceCampus.SelectedValue = 1; sourceClass.SelectedValue = 10;
            Check(Options(sourceSection).SequenceEqual(new[]{1,2}), task+": source sections exclude globally defined/unpopulated sections but retain historical sources");
            sourceSection.SelectedValue = 2;
            sourceClass.SelectedValue = 0;
            Check(sourceSection.SelectedId == 0, task+": changing class clears even a still-valid section");
            sourceClass.SelectedValue = 10; sourceSection.SelectedValue = 2;
            sourceCampus.SelectedValue = 2;
            Check(sourceClass.SelectedId == 0 && sourceSection.SelectedId == 0, task+": changing campus clears shared classes and sections");
            sourceCampus.SelectedValue = 1; sourceClass.SelectedValue = 10; sourceSection.SelectedValue = 2;
            sourceYear.SelectedValue = 100;
            Check(sourceCampus.SelectedId == 0 && sourceClass.SelectedId == 0 && sourceSection.SelectedId == 0,
                task+": changing year resets all source filters even when both years have identical choices");
            sourceYear.SelectedValue = 9;
            sourceCampus.SelectedValue = 1; sourceClass.SelectedValue = 10;
            Check(Options(targetCampus).SequenceEqual(new[]{1,2}), task+": unconfigured destination campuses are excluded");
            if (task == StudentPlacementTask.AnnualPromotion) {
                Check(targetYear.SelectedId == 100 && Options(targetYear).SequenceEqual(new[]{100,2}), "Promotion chooses the closest later year by dates rather than IDs or catalog row order");
                Check(Field<ModernDatePicker>("effective").Value.Date == new DateTime(2031,1,1), "Promotion date moves inside the compatible destination year");
            } else Check(targetYear.SelectedId == 9 && Options(targetYear).SequenceEqual(new[]{9}), task+": transfer/correction remains in the source year");
            targetCampus.SelectedValue = 2;
            Check(Options(targetClass).SequenceEqual(new[]{10,20,99}), task+": destination classes respect the selected campus assignments");
            targetClass.SelectedValue = 20;
            targetCampus.SelectedValue = 0;
            Check(targetClass.SelectedId == 0, task+": changing destination campus clears a still-valid destination class");
            if (task != StudentPlacementTask.AnnualPromotion) targetClass.SelectedValue = 20;
            var data = Table(("ClassId",typeof(int)),("CampusId",typeof(int)),("SectionId",typeof(int)),("SourcePlacementId",typeof(int)),("Destination",typeof(int)),
                ("Decision",typeof(string)),("Note",typeof(string)),("PlacementState",typeof(string)),("LegacySetupPending",typeof(bool)),("PlacementConfirmedAt",typeof(DateTime)),("Selected",typeof(bool)));
            data.Rows.Add(10,1,1,101,0,task == StudentPlacementTask.AnnualPromotion ? "PROMOTE" : task == StudentPlacementTask.Transfer ? "MOVE" : "CORRECT","","Active",false,DateTime.Now,true);
            data.Columns.Add("StudentName"); data.Columns.Add("Current");
            data.Rows[0]["StudentName"]="Fictional student"; data.Rows[0]["Current"]="A / One / 1";
            Set("data",data); Field<DataGridView>("students").DataSource=data; Call("Suggest");
            var review = Field<ModernButton>("review");
            Check(targetCampus.Enabled && review.Enabled, task+": an assigned student unlocks review with no reason");
            Call("SelectRows",false);
            Check(!review.Enabled, task+": clearing the processing selection disables review again");
            Call("SelectRows",true);
            using (var choices = (DataTable)Call("DestinationChoices",data.Rows[0],data.Rows[0]["Decision"])!)
                Check(choices.AsEnumerable().Select(row=>Convert.ToInt32(row["ClassSectionId"])).SequenceEqual(new[]{201,203}), task+": destination sections include configured empty sections, never another campus/class");
            Check(Convert.ToInt32(data.Rows[0]["Destination"]) == 201, task+": suggestion keeps the same section when its destination assignment exists");
            var cell = (DataGridViewComboBoxCell)Field<DataGridView>("students").Rows[0].Cells["Destination"];
            Check(((DataTable)cell.DataSource!).AsEnumerable().Select(row=>Convert.ToInt32(row["ClassSectionId"])).SequenceEqual(new[]{0,201,203}),
                task+": the real row dropdown is scoped, not only the suggestion helper");
            using (var common = (DataTable)Call("CommonDestinations",new[]{data.Rows[0]},data.Rows[0]["Decision"])!)
                Check(common.Rows.Count == 2, task+": Assign uses compatible choices too");
            data.Rows[0]["Destination"]=990;
            bool rejected=false;
            try { Call("ValidatePlacementChoices",(object)new[]{data.Rows[0]}); }
            catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException){rejected=true;}
            Check(rejected, task+": a forged cross-campus/class selection cannot reach processing");
            data.Rows[0]["Destination"]=201;
            if (task == StudentPlacementTask.AnnualPromotion) {
                var other=data.NewRow();other.ItemArray=(object[])data.Rows[0].ItemArray.Clone();other["ClassId"]=20;other["SourcePlacementId"]=201;
                using(var mixed=(DataTable)Call("CommonDestinations",new[]{data.Rows[0],other},"PROMOTE")!)
                    Check(mixed.Rows.Count==0,"Bulk Assign cannot apply one destination across incompatible next classes");
                foreach (var row in placements.AsEnumerable().Where(row=>Convert.ToInt32(row["CampusId"])==1 && Convert.ToInt32(row["ClassId"])==20).ToArray()) placements.Rows.Remove(row);
                Call("Suggest");
                using var missing = (DataTable)Call("DestinationChoices",data.Rows[0],"PROMOTE")!;
                Check(Convert.ToInt32(data.Rows[0]["Destination"])==0 && missing.Rows.Count==0,
                    "A missing intermediate campus assignment cannot promote into a later class");
                data.Rows[0]["SourcePlacementId"]=102;data.Rows[0]["SectionId"]=2;data.Rows[0]["Decision"]="HOLD"; Call("Suggest");
                Check(Convert.ToInt32(data.Rows[0]["Destination"])==102, "Repeat retains the student's existing inactive historical placement");
            }
            if (task == StudentPlacementTask.Correction) {
                targetClass.SelectedValue = 10;
                using var historical = (DataTable)Call("DestinationChoices",data.Rows[0],"CORRECT")!;
                Check(historical.AsEnumerable().Any(row=>Convert.ToInt32(row["ClassSectionId"])==102), "Historical correction retains inactive configured assignments");
            }
            sourceSection.SelectedValue=2;
            int selectedClass=targetClass.SelectedId;
            ((Task)Call("RefreshSourceContext",false)!).GetAwaiter().GetResult();
            Check(sourceCampus.SelectedId==1 && sourceClass.SelectedId==10 && sourceSection.SelectedId==2 && targetClass.SelectedId==selectedClass,
                task+": refreshing candidates retains still-valid source filters and destination class");
            int studentReads=0;
            page.ReadSourceChoices = _ => Task.FromResult(sources.Clone());
            page.ReadStudents = (_,_,_,_) => { studentReads++; throw new InvalidOperationException("A wider source read is forbidden in this fixture"); };
            bool missingScope=false;
            try { ((Task)Call("LoadStudents")!).GetAwaiter().GetResult(); }
            catch(InvalidOperationException){missingScope=true;}
            Check(missingScope && studentReads==0 && Field<DataTable>("data").Rows.Count==0,
                task+": disappearing source filters stop loading rather than silently widening the plan");
            sourceYear.SelectedValue = 2;
            Check(Options(sourceClass).Length == 0 && sourceSection.SelectedId == 0, task+": changing year clears stale source class and section choices");
            Check(task == StudentPlacementTask.AnnualPromotion ? targetYear.SelectedId == 0 : targetYear.SelectedId == 2,
                task+": newest year has no invented next year; same-year tasks still follow the source");
        }
    }
}
