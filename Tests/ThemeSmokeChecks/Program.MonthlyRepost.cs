using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;

internal static partial class Program
{
    private static void CheckMonthlyRepostClick() { CheckMonthlyPostingClick(3,true); CheckMonthlyPostingClick(8,true); CheckMonthlyPostingClick(3,false); }
    private static void CheckMonthlyPostingClick(int rowCount,bool reversed)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=(FeePostingPage)Activator.CreateInstance(typeof(FeePostingPage),flags,null,new object[]{false},null)!;
        T Field<T>(string name)=>(T)typeof(FeePostingPage).GetField(name,flags)!.GetValue(page)!;
        void Set(string name,object? value)=>typeof(FeePostingPage).GetField(name,flags)!.SetValue(page,value);
        using var form=new Form{Text="Monthly repost fixture",Size=new Size(1200,800)};
        form.Controls.Add(page);page.Dock=DockStyle.Fill;
        Set("suppressFilterEvents",true);
        using var years=new DataTable();years.Columns.Add("Id",typeof(int));years.Columns.Add("Name");years.Rows.Add(28,"2026-2027");
        Field<LookupBox>("academicYear").Bind(years,"Id","Name");Field<LookupBox>("academicYear").SelectedValue=28;
        using var months=new DataTable();months.Columns.Add("Id",typeof(int));months.Columns.Add("Name");months.Rows.Add(202610,"October 2026");
        Field<LookupBox>("monthBox").Bind(months,"Id","Name");Field<LookupBox>("monthBox").SelectedValue=202610;
        Set("suppressFilterEvents",false);
        form.Show();Application.DoEvents();
        using var rows=new DataTable();
        foreach(var col in new[]{"AdmissionId","EnrollmentId","FeeTypeId","StudentFeeAssignmentId"})rows.Columns.Add(col,typeof(int));
        rows.Columns.Add("BillingMonth",typeof(DateTime));rows.Columns.Add("NetAmount",typeof(decimal));rows.Columns.Add("EligibilityNote");rows.Columns.Add("StudentName");
        for(int i=0;i<rowCount;i++)rows.Rows.Add(6504+i,740+i,2,43+i,new DateTime(2026,10,1),500m,reversed ? "Previously reversed - selecting creates a new charge" : "Eligible","Test student");
        Set("eligibleRows",rows);Set("loadedMonth",new DateTime(2026,10,1));
        var signature=typeof(FeePostingPage).GetMethod("FilterSignature",flags)!.Invoke(page,null);
        Set("appliedFilterSignature",signature);
        var bind=typeof(V2Page).GetMethod("BindReportAsync",flags,null,new[]{typeof(Func<Task<DataTable>>)},null)!;
        var task=(Task)bind.Invoke(page,new object[]{(Func<Task<DataTable>>)(()=>Task.FromResult(rows))})!;
        PumpUntil(()=>task.IsCompleted,"Monthly preview binds");task.GetAwaiter().GetResult();
        typeof(FeePostingPage).GetMethod("ConfigureGrid",flags)!.Invoke(page,null);
        var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags|BindingFlags.Public)!.GetValue(page)!;
        UiHelper.ApplyGridPresentation(grid); Application.DoEvents();
        Check(!grid.Columns.Cast<DataGridViewColumn>().Any(c=>c is DataGridViewCheckBoxColumn),"Monthly posting has no selection checkboxes");
        Check(grid.ReadOnly&&grid.Columns.Cast<DataGridViewColumn>().All(c=>c.ReadOnly),"Preview stays read-only after shared grid styling");
        Check(Field<DataTable>("eligibleRows").Rows.Count==rowCount,"All eligible fees remain in the posting preview");
        bool confirmed=false;string seen="";
        using var timer=new System.Windows.Forms.Timer{Interval=30};
        timer.Tick+=(_,_)=>{
            foreach(Form dialog in Application.OpenForms.Cast<Form>().Where(f=>f!=form).ToArray())
            {
                seen=dialog.Text;
                if(dialog.Text=="Post Monthly Fees")confirmed=true;
                dialog.DialogResult=DialogResult.No;dialog.Close();
            }
        };
        Set("lookupsReady",true); timer.Start();Field<ModernButton>("postButton").PerformClick();timer.Stop();
        Check(confirmed,"Post Eligible Fees opens confirmation without checkbox interaction; seen dialog: "+seen);
        Check(!Field<bool>("posting"),"Cancelling confirmation releases the posting guard");
        form.Hide();
    }
}
