using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckOutstandingBills()
    {
        OutstandingBillScope Scope(BillBalanceComparison comparison,decimal amount=1000,decimal through=2000)=>new(0,0,0,0,comparison,amount,through);
        foreach(var comparison in Enum.GetValues<BillBalanceComparison>())
        {
            Check(!Scope(comparison).Matches(0m)&&!Scope(comparison).Matches(-1m),"Paid and credit balances never become bills");
        }
        Check(Scope(BillBalanceComparison.AtLeast).Matches(1000)&&!Scope(BillBalanceComparison.AtLeast).Matches(999),"At least includes the boundary");
        Check(!Scope(BillBalanceComparison.MoreThan).Matches(1000)&&Scope(BillBalanceComparison.MoreThan).Matches(1001),"More than excludes the boundary");
        Check(Scope(BillBalanceComparison.AtMost).Matches(1000)&&!Scope(BillBalanceComparison.AtMost).Matches(1001),"At most includes the boundary");
        Check(!Scope(BillBalanceComparison.LessThan).Matches(1000)&&Scope(BillBalanceComparison.LessThan).Matches(999),"Less than excludes the boundary");
        Check(Scope(BillBalanceComparison.Exactly).Matches(1000)&&!Scope(BillBalanceComparison.Exactly).Matches(1001),"Exactly matches only the chosen amount");
        Check(Scope(BillBalanceComparison.Between).Matches(1000)&&Scope(BillBalanceComparison.Between).Matches(2000)&&!Scope(BillBalanceComparison.Between).Matches(2001),"Between includes both endpoints");
        using var balances=new DataTable();balances.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"AdmissionNumber","StudentName","FatherName","CampusName","ClassSection","AcademicYear"})balances.Columns.Add(field);
        balances.Columns.Add("Outstanding",typeof(decimal));
        balances.Rows.Add(1,"101","Fictional Ali","Example Parent","Campus A","Class 1 – A","2026-2027",800m);
        balances.Rows.Add(2,"QR-102","Fictional Bilal","Example Parent","Campus A","Class 1 – A","2026-2027",6900m);
        balances.Rows.Add(3,"103","Fictional Sara","Example Parent","Campus B","Class 1 – A","2026-2027",6900m);
        using(var filtered=OutstandingBillSelection.Select(balances,Scope(BillBalanceComparison.AtLeast,6900)))
        {
            Check(filtered.Rows.Count==2,"Balance comparisons select matching students, without changing their balance");
            OutstandingBillSelection.Search(filtered,"O'Brien [test]%");
            Check(filtered.DefaultView.Count==0,"Search treats special characters as literal text");
        }
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(990,"Bill fixture","Administrator",false,Array.Empty<string>());
            using(var shell=new FMSC_SMS.MainDashboardForm((_,_)=>{}))
            {
                var commands=(List<NavigationCommand>)typeof(FMSC_SMS.MainDashboardForm).GetField("navigationCommands",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(shell)!;
                Check(commands.Any(command=>command.Caption==PageTitles.OutstandingBills),"Bill printing is a standalone navigation destination");
            }
            using var page=new OutstandingBillsPage(false,async _=>{await Task.Delay(20);return balances.Copy();});
            using var owner=new Form{ClientSize=new Size(1500,900),AutoScaleMode=AutoScaleMode.None};owner.Controls.Add(page);owner.Show();Application.DoEvents();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            T Field<T>(string name)=>(T)typeof(OutstandingBillsPage).GetField(name,flags)!.GetValue(page)!;
            IEnumerable<Control> Walk(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Walk(c)));
            ModernButton Button(string caption)=>Walk(page).OfType<ModernButton>().Single(button=>button.Text==caption);
            void ClickButton(ModernButton control)
            {
                control.Focus();control.PerformClick();
                Application.DoEvents();
            }
            ClickButton(Button("Load Students"));
            PumpUntil(()=>!page.IsBusy&&Button("Preview Selected Bills").Enabled,"Load Students click completes its asynchronous read");
            Check(Button("Preview Selected Bills").Enabled,"Loaded owing students can be previewed");
            var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
            Check(grid.Rows.Count==3&&grid.Columns["CampusName"].Visible&&grid.Columns["ClassSection"].Visible,"Grid distinguishes campus and class/section");
            ClickButton(Button("Clear Selection"));
            Check(grid.Rows.Cast<DataGridViewRow>().All(row=>Equals(row.Cells["Include"].Value,false)),"Clear Selection unchecks every displayed row after an asynchronous load");
            ClickButton(Button("Select All"));
            Check(grid.Rows.Cast<DataGridViewRow>().All(row=>Equals(row.Cells["Include"].Value,true)),"Select All checks every displayed row after an asynchronous load");
            grid.CurrentCell=grid.Rows[0].Cells["Include"];grid.Focus();grid.BeginEdit(false);
            grid.CurrentCell.Value=false;grid.NotifyCurrentCellDirty(true);
            ClickButton(Button("Select All"));
            Check(grid.Rows.Cast<DataGridViewRow>().All(row=>Equals(row.Cells["Include"].Value,true)),"Select All replaces a current checkbox edit");
            grid.CurrentCell=grid.Rows[1].Cells["Include"];grid.Focus();grid.BeginEdit(false);
            grid.CurrentCell.Value=true;grid.NotifyCurrentCellDirty(true);
            ClickButton(Button("Clear Selection"));
            Check(grid.Rows.Cast<DataGridViewRow>().All(row=>Equals(row.Cells["Include"].Value,false))&&!Button("Preview Selected Bills").Enabled,
                "Clear Selection finishes an active checkbox edit and clears the actual preview selection");
            Field<ModernTextBox>("search").Text="No student matches this search";Application.DoEvents();
            ClickButton(Button("Select All"));
            Check(grid.Rows.Count==0&&Field<DataTable>("roster").AsEnumerable().All(row=>row.Field<bool>("Include"))&&Button("Preview Selected Bills").Enabled,
                "Select All includes the loaded students even when search hides every row");
            ClickButton(Button("Select Shown"));
            Check(!Field<DataTable>("roster").AsEnumerable().Any(row=>row.Field<bool>("Include"))&&!Button("Preview Selected Bills").Enabled,
                "Select Shown clears an existing selection when the search has no matching students");
            Field<ModernTextBox>("search").Text="";ClickButton(Button("Select All"));
            Field<ModernTextBox>("search").Text="Bilal";Application.DoEvents();
            var roster=Field<DataTable>("roster");
            Check(grid.Rows.Count==1&&roster.AsEnumerable().Count(row=>row.Field<bool>("Include"))==3,"Search retains selected students outside the visible results");
            ClickButton(Button("Select Shown"));
            Check(roster.AsEnumerable().Where(row=>row.Field<bool>("Include")).Single().Field<int>("AdmissionId")==2&&Field<Label>("selectedSummary").Text.StartsWith("1 selected"),
                "Select Shown replaces Select All with just the visible students and updates the count");
            Field<ModernTextBox>("search").Text="";Application.DoEvents();
            Check(grid.Rows.Cast<DataGridViewRow>().Count(row=>Equals(row.Cells["Include"].Value,true))==1,
                "Clearing the search reveals that hidden students were unchecked by Select Shown");
            Field<ModernTextBox>("search").Text="Bilal";Application.DoEvents();
            Button("Clear Selection").PerformClick();Check(!Button("Preview Selected Bills").Enabled,"Clearing all selection disables preview");
            Button("Select Shown").PerformClick();Check(roster.AsEnumerable().Where(row=>row.Field<bool>("Include")).Single().Field<int>("AdmissionId")==2,"Select Shown selects only matching students");
            Button("Select All").PerformClick();Check(roster.AsEnumerable().All(row=>row.Field<bool>("Include")),"Select All includes matches hidden by search");
            Field<ModernComboBox>("comparison").SelectedIndex=(int)BillBalanceComparison.AtLeast;
            Field<ModernNumericTextBox>("amount").Value=6900;
            Check(!Button("Preview Selected Bills").Enabled,"Changing amount filters invalidates preview until reload");
            var load=(Task)typeof(OutstandingBillsPage).GetMethod("LoadStudentsAsync",flags)!.Invoke(page,null)!;PumpUntil(()=>load.IsCompleted,"Changed balance filter reloads");
            Check(Field<DataTable>("roster").Rows.Count==2,"Reload applies the requested threshold to the complete balances");
            Field<ModernTextBox>("search").Text="";
            foreach(var size in new[]{new Size(1500,900),new Size(1000,720)})
            {
                owner.ClientSize=size;owner.PerformLayout();Application.DoEvents();
                var sequence=new[]{"Select All","Select Shown","Clear Selection","Preview Selected Bills"};
                var selectionButtons=Button("Select All").Parent!;
                Check(selectionButtons.Controls.Cast<Control>().Select(control=>control.Text).SequenceEqual(sequence),
                    "Selection tools appear before bill preview in natural order");
                Check(sequence.Select(Button).Select(button=>button.TabIndex).SequenceEqual(Enumerable.Range(0,4)),
                    "Keyboard navigation follows the selection then preview order");
                foreach(string caption in new[]{"Load Students","Preview Selected Bills","Select All","Select Shown","Clear Selection"})
                {
                    var button=Button(caption);Check(button.Visible,"All bill actions remain visible");
                    Check(button.Right<=button.Parent!.ClientSize.Width && button.Bottom<=button.Parent.ClientSize.Height,"Bill actions fit their container");
                }
                Check(grid.Height>=100,"Bill selection keeps usable grid height");
                var folder=Path.GetFullPath("artifacts/outstanding-bills");Directory.CreateDirectory(folder);
                using var bitmap=new Bitmap(owner.Width,owner.Height);owner.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(folder,$"selection-{size.Width}.png"));
            }
            Check(!File.Exists(Path.Combine(AppContext.BaseDirectory,"Reports","FeeReminder.rdlc")),"The retired reminder report is not shipped");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
}
