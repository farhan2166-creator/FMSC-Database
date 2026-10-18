using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;

internal static partial class Program
{
    private static void CheckAdditionalFeeScope()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var pending=new Queue<TaskCompletionSource<DataTable>>();
        Func<int,int,int,int,Task<DataTable>> load=(_,_,_,_)=>{var item=new TaskCompletionSource<DataTable>();pending.Enqueue(item);return item.Task;};
        using var page=(AdditionalFeePostingPage)Activator.CreateInstance(typeof(AdditionalFeePostingPage),flags,null,new object[]{load,false},null)!;
        page.BindingContext=new BindingContext();
        T Field<T>(string n)=>(T)typeof(AdditionalFeePostingPage).GetField(n,flags)!.GetValue(page)!;
        using var years=new DataTable();years.Columns.Add("Id",typeof(int));years.Columns.Add("Name");years.Rows.Add(1,"Year");Field<LookupBox>("year").Bind(years,"Id","Name");Field<LookupBox>("year").SelectedValue=1;
        using var campuses=years.Copy();campuses.Rows.Add(2,"Campus B");Field<LookupBox>("campus").Bind(campuses,"Id","Name");Field<LookupBox>("campus").SelectedValue=1;
        DataTable Rows(int id){var t=new DataTable();t.Columns.Add("EnrollmentId",typeof(int));foreach(var n in new[]{"AdmissionNumber","StudentName","FatherName","CampusName","ClassName","SectionName"})t.Columns.Add(n);t.Rows.Add(id,id.ToString(),"Student","Parent","Campus","Class","Section");return t;}
        Task Start()=>(Task)typeof(AdditionalFeePostingPage).GetMethod("LoadAsync",flags)!.Invoke(page,null)!;
        var a=Start();Field<LookupBox>("campus").SelectedValue=2;pending.Dequeue().SetResult(Rows(41));PumpUntil(()=>a.IsCompleted,"Obsolete candidate load settles");
        Check(Field<DataTable?>("candidates") is null&&!Field<ModernButton>("post").Enabled,"Old campus result cannot reappear under changed posting filters");
        var b=Start();pending.Dequeue().SetResult(Rows(42));PumpUntil(()=>b.IsCompleted,"Current candidate load settles");
        typeof(AdditionalFeePostingPage).GetMethod("SelectAll",flags)!.Invoke(page,null);
        Check(Field<HashSet<int>>("selected").SetEquals(new[]{42}),"Select All uses only the current loaded campus candidates");
        Field<ModernDatePicker>("date").Value=DateTime.Today.AddDays(-1);
        Check(Field<DataTable?>("candidates") is null&&Field<HashSet<int>>("selected").Count==0&&!Field<ModernButton>("post").Enabled,"Charge-date changes clear candidates, selections and posting eligibility");
        var changedDate=Start();Field<ModernDatePicker>("date").Value=DateTime.Today;
        pending.Dequeue().SetResult(Rows(44));PumpUntil(()=>changedDate.IsCompleted,"Obsolete charge-date load settles");
        Check(Field<DataTable?>("candidates") is null,"Old charge-date result cannot reappear under a different date");
        Field<LookupBox>("campus").SelectedValue=1;
        Check(Field<HashSet<int>>("selected").Count==0&&!Field<ModernButton>("post").Enabled,"Filter changes clear both selections and posting eligibility");
        var late=Start();page.Dispose();pending.Dequeue().SetResult(Rows(43));PumpUntil(()=>late.IsCompleted,"Disposed candidate load settles");
        Check(late.IsCompletedSuccessfully,"Late response after page disposal performs no UI continuation");
    }
}
