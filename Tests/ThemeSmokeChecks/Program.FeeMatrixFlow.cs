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
}
