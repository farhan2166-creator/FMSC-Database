using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;

internal static partial class Program
{
    private static void CheckPaymentAdjustmentContext()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(StudentFeeAdjustmentsPage);using var page=(StudentFeeAdjustmentsPage)Activator.CreateInstance(type,flags,null,new object[]{false},null)!;
        T Field<T>(string n)=>(T)type.GetField(n,flags)!.GetValue(page)!;
        type.GetField("admissionId",flags)!.SetValue(page,41);
        var grid=Field<DataGridView>("payments");var rows=new DataTable();rows.Columns.Add("FeePaymentId",typeof(int));rows.Columns.Add("EnrollmentId",typeof(int));rows.Rows.Add(1,10);rows.Rows.Add(2,20);type.GetField("paymentRows",flags)!.SetValue(page,rows);
        var stateType=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Pages.Finance.StudentAccounts.PaymentAdjustmentState",true)!;
        for(int id=1;id<=2;id++){int index=grid.Rows.Add();grid.Rows[index].Cells["PaymentId"].Value=id;grid.Rows[index].Cells["Amount"].Value=1000m;grid.Rows[index].Tag=Activator.CreateInstance(stateType,new object[]{"Completed",1000m,new[]{"Correction","Refund","Reversal"},""});}
        void Select(int index){grid.CurrentCell=grid.Rows[index].Cells["Amount"];grid.Rows[index].Selected=true;type.GetMethod("OnPaymentSelected",flags)!.Invoke(page,null);}
        Select(0);Field<ModernTextBox>("actionAmount").Text="500";Field<ModernTextBox>("reason").Text="Payment A correction";Field<ModernDatePicker>("transactionDate").Value=DateTime.Today.AddDays(-1);
        Select(0);Check(Field<ModernTextBox>("actionAmount").Text=="500"&&Field<ModernTextBox>("reason").Text=="Payment A correction","Revisiting the same payment preserves its in-progress adjustment draft");
        Select(1);Check(Field<ModernTextBox>("actionAmount").Text=="0"&&Field<ModernTextBox>("reason").Text==""&&Field<ModernDatePicker>("transactionDate").Value.Date==DateTime.Today,"A different payment cannot inherit amount, reason or date from the first payment");
        Select(0);Check(Field<ModernTextBox>("reason").Text==""&&Field<ModernTextBox>("actionAmount").Text=="0","A→B→A does not revive an obsolete command draft");
        Field<ModernTextBox>("reason").Text="Old student";type.GetField("admissionId",flags)!.SetValue(page,42);Select(0);
        Check(Field<ModernTextBox>("reason").Text=="","A student-context change also clears payment-specific draft inputs");
        Check(!page.IsHandleCreated,"Adjustment selection fixture starts no SQL lookup or financial write");
    }
    private static void CheckPaymentDetailGeneration()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(StudentFeeAdjustmentsPage);
        var pending=new Queue<TaskCompletionSource<DataTable>>();int previews=0;
        Func<int,int,int,Task<DataTable>> read=(_,_,_)=>{var reply=new TaskCompletionSource<DataTable>();pending.Enqueue(reply);return reply.Task;};Action<DataTable> show=_=>previews++;
        using var page=(StudentFeeAdjustmentsPage)Activator.CreateInstance(type,flags,null,new object[]{false,read,show},null)!;
        var rows=new DataTable();rows.Columns.Add("FeePaymentId",typeof(int));rows.Rows.Add(1);rows.Rows.Add(2);type.GetField("paymentRows",flags)!.SetValue(page,rows);type.GetField("admissionId",flags)!.SetValue(page,41);
        void Select(int id){type.GetField("selectedPaymentId",flags)!.SetValue(page,id);type.GetMethod("ResetAdjustmentDraftForSelection",flags)!.Invoke(page,null);}
        Task Start()=>(Task)type.GetMethod("ViewPaymentDetailsAsync",flags)!.Invoke(page,null)!;
        Select(1);var a=Start();Select(2);Select(1);pending.Dequeue().SetResult(new DataTable());PumpUntil(()=>a.IsCompleted,"Old payment detail reply settles");
        Check(previews==0,"A→B→A cannot revive a details response from the previous selection generation");
        Select(2);var b=Start();pending.Dequeue().SetResult(new DataTable());PumpUntil(()=>b.IsCompleted,"Current payment detail reply settles");Check(previews==1,"A current payment detail response still opens its intended preview");
        var late=Start();page.Dispose();pending.Dequeue().SetResult(new DataTable());PumpUntil(()=>late.IsCompleted,"Disposed details response settles");Check(previews==1&&late.IsCompletedSuccessfully,"Payment details cannot open after their owner is disposed");
    }

}
