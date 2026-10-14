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
}
