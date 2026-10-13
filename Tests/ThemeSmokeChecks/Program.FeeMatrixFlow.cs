using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Configuration;

internal static partial class Program
{
    private static void CheckFeeMatrixHostBusy()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var matrix=(FeeRateMatrixForm)Activator.CreateInstance(typeof(FeeRateMatrixForm),flags,null,new object?[]{null,false},null)!;
        matrix.PrepareForEmbedding();
        using var window=new Form{ClientSize=new Size(960,640)};
        using var workspace=new WorkspacePage("Fee configuration","",("Matrix",()=>matrix),("Other",()=>new Panel()));
        window.Controls.Add(workspace);window.Show();Application.DoEvents();
        var tabs=(TabControl)typeof(WorkspacePage).GetField("_tabs",flags)!.GetValue(workspace)!;
        var saving=typeof(FeeRateMatrixForm).GetField("savingGrid",flags)!;
        saving.SetValue(matrix,true);
        Check(((IBusyPage)workspace).IsBusy&&workspace.BusyMessage.Contains("Fee rates"),"Embedded matrix save is visible to the dashboard through the workspace busy contract");
        tabs.SelectedIndex=1;Application.DoEvents();
        Check(tabs.SelectedIndex==0,"An active matrix save blocks a conflicting tab change");
        saving.SetValue(matrix,false);tabs.SelectedIndex=1;Application.DoEvents();
        Check(tabs.SelectedIndex==1&&!workspace.IsBusy,"After save and reload settle, ordinary tab navigation resumes");
        saving.SetValue(matrix,true);
        Check(workspace.IsBusy,"The workspace sees an active matrix operation even if that child is not the selected tab");
        saving.SetValue(matrix,false);window.Close();
    }
    private static void CheckFeeMatrixCampusDraft()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        bool discard=false;int asks=0,loads=0;
        Func<bool> confirm=()=>{asks++;return discard;};Func<Task> reload=()=>{loads++;return Task.CompletedTask;};
        using var matrix=(FeeRateMatrixForm)Activator.CreateInstance(typeof(FeeRateMatrixForm),flags,null,new object?[]{null,false,confirm,reload},null)!;
        matrix.BindingContext=new BindingContext();
        var campus=(ModernComboBox)typeof(FeeRateMatrixForm).GetField("campus",flags)!.GetValue(matrix)!;
        var grid=(DataGridView)typeof(FeeRateMatrixForm).GetField("grid",flags)!.GetValue(matrix)!;
        using var data=new System.Data.DataTable();data.Columns.Add("Id",typeof(int));data.Columns.Add("Name");data.Rows.Add(1,"Campus A");data.Rows.Add(2,"Campus B");
        campus.ValueMember="Id";campus.DisplayMember="Name";campus.DataSource=data;campus.SelectedValue=1;
        grid.Columns.Add("Frequency","Frequency");grid.Columns.Add("CL_1","Class");grid.Rows.Add("Monthly","1000");
        typeof(FeeRateMatrixForm).GetMethod("CaptureGridSnapshot",flags)!.Invoke(matrix,null);
        grid.Rows[0].Cells[1].Value="1500";int before=loads;campus.SelectedValue=2;
        Check((int)campus.SelectedValue! ==1&&loads==before&&asks==1&&Equals(grid.Rows[0].Cells[1].Value,"1500"),
            "Stay on campus change preserves the old campus and exact edited fee amount without reloading");
        discard=true;campus.SelectedValue=2;
        Check((int)campus.SelectedValue! ==2&&loads==before+1&&asks==2,"An explicit discard allows the new-campus load once");
        typeof(FeeRateMatrixForm).GetMethod("CaptureGridSnapshot",flags)!.Invoke(matrix,null);before=loads;campus.SelectedValue=1;
        Check(loads==before+1&&asks==2,"An unchanged fee grid switches campus without a discard prompt");
        typeof(FeeRateMatrixForm).GetField("savingGrid",flags)!.SetValue(matrix,true);before=loads;campus.SelectedValue=2;
        Check((int)campus.SelectedValue! ==1&&loads==before,"Programmatic campus changes cannot retarget an in-flight save");
        typeof(FeeRateMatrixForm).GetField("savingGrid",flags)!.SetValue(matrix,false);
    }

}
