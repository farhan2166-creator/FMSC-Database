using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Configuration;

internal static partial class Program
{
    private static void CheckFeeMatrixClearing()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(FeeRateMatrixForm);
        using var matrix=new FeeRateMatrixForm(null,false,()=>true,()=>Task.CompletedTask);
        matrix.BindingContext=new BindingContext();
        T Field<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(matrix)!;
        object Item(string nested,params (string Name,object Value)[] values)
        {
            var item=Activator.CreateInstance(type.GetNestedType(nested,BindingFlags.NonPublic)!)!;
            foreach(var value in values)item.GetType().GetField(value.Name)!.SetValue(item,value.Value);
            return item;
        }
        var classes=Field<System.Collections.IList>("classes");
        classes.Add(Item("ClassColumn",("ClassId",1),("ClassName","Class 1")));
        classes.Add(Item("ClassColumn",("ClassId",2),("ClassName","Class 2")));
        Field<System.Collections.IList>("feeTypeRows").Add(Item("FeeTypeRow",("FeeTypeId",3),("FeeTypeName","Tuition"),("DefaultFrequency","Monthly")));
        var existing=Field<System.Collections.IDictionary>("existing");
        existing.Add((1,3),Item("ExistingCell",("FeeConfigurationId",11),("Amount",500m),("Frequency","Monthly"),("Remarks","Original")));
        var grid=Field<DataGridView>("grid");
        type.GetMethod("BuildGridColumns",flags)!.Invoke(matrix,null);
        type.GetMethod("BuildGridRows",flags)!.Invoke(matrix,null);
        type.GetMethod("CaptureGridSnapshot",flags)!.Invoke(matrix,null);
        System.Runtime.CompilerServices.ITuple Plan()=>(System.Runtime.CompilerServices.ITuple)type.GetMethod("BuildSavePlan",flags)!.Invoke(matrix,new object[]{7})!;
        int Count(System.Runtime.CompilerServices.ITuple plan,int index)=>((System.Collections.IList)plan[index]!).Count;
        var saved=grid.Rows[0].Cells["CL_1"];var blank=grid.Rows[0].Cells["CL_2"];
        saved.Value="";var plan=Plan();
        Check(Count(plan,0)==0&&Count(plan,1)==1,"A manually cleared saved amount becomes a removal instead of being skipped");
        var clear=((System.Collections.IList)plan[1]!)[0]!;
        string json=System.Text.Json.JsonSerializer.Serialize(clear,clear.GetType());
        Check(json.Contains("\"FeeConfigurationId\":11")&&json.Contains("\"ExpectedAmount\":500")&&json.Contains("\"CampusId\":7"),
            "The clear payload retains the loaded rate and its identity for stale-save protection");
        saved.Value="0";plan=Plan();
        Check(Count(plan,0)==1&&Count(plan,1)==0,"An explicit zero remains an upsert, distinct from an unconfigured cell");
        saved.Value="500";blank.Value="";plan=Plan();
        Check(Count(plan,0)==0&&Count(plan,1)==0,"An unchanged amount and an already empty cell require no write");
        blank.Value="250";saved.Value=null;plan=Plan();
        Check(Count(plan,0)==1&&Count(plan,1)==1,"A removal and a new rate are assembled into one save plan");
        using var host=new Form { ClientSize=new Size(900,500),ShowInTaskbar=false,
            StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000) };
        // Host only the grid so no deferred campus/SQL loader can run.
        grid.Parent!.Controls.Remove(grid);grid.Dock=DockStyle.Fill;host.Controls.Add(grid);host.Show();Application.DoEvents();
        saved.Value="500";blank.Value="250";grid.CurrentCell=saved;grid.ClearSelection();
        saved.Selected=true;blank.Selected=true;grid.Rows[0].Cells["FeeType"].Selected=true;
        var key=new KeyEventArgs(Keys.Delete);
        typeof(Control).GetMethod("OnKeyDown",flags)!.Invoke(grid,new object[]{key});
        Check(key.Handled&&key.SuppressKeyPress&&saved.Value is null&&blank.Value is null,"Delete clears selected amount cells and consumes the key");
        Check(Convert.ToString(grid.Rows[0].Cells["FeeType"].Value)=="Tuition"&&Convert.ToString(grid.Rows[0].Cells["Frequency"].Value)=="Monthly",
            "Delete leaves fee labels and frequency intact");
        Check(matrix.HasUnsavedChanges,"A cleared amount remains dirty until it is saved");
        type.GetMethod("RestoreSelectedCells",flags)!.Invoke(matrix,null);
        Check(Convert.ToString(saved.Value)=="500"&&blank.Value is null,"Restore selected cells undoes clearing");
        type.GetField("savingGrid",flags)!.SetValue(matrix,true);saved.Value="500";
        type.GetMethod("ClearSelectedAmounts",flags)!.Invoke(matrix,null);
        Check(Convert.ToString(saved.Value)=="500","An in-flight save refuses a conflicting clear");
        type.GetField("savingGrid",flags)!.SetValue(matrix,false);
        Check(!matrix.IsHandleCreated,"Matrix edit checks never start a campus or database load");
        host.Close();
    }

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
