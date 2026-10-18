using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckOperatorConcessionChoices()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            CurrentUser.SignIn(7781,"Concession fixture","Administrator",false,Array.Empty<string>());
            var context=new DataTable();context.Columns.Add("Frequency");context.Columns.Add("ConcessionLabel");
            foreach(string column in new[]{"StartDate","EndDate","EnrollmentDate"})context.Columns.Add(column,typeof(DateTime));
            int year=DateTime.Today.Year;context.Rows.Add("Monthly","Scholarship",new DateTime(year,1,1),new DateTime(year,12,31),new DateTime(year,1,1));
            var replies=new Queue<TaskCompletionSource<DataSet>>();var queries=new List<Microsoft.Data.SqlClient.SqlParameter[]>();
            Task<DataSet> Query(Microsoft.Data.SqlClient.SqlParameter[] args){queries.Add(args);var reply=new TaskCompletionSource<DataSet>();replies.Enqueue(reply);return reply.Task;}
            var writes=new Queue<TaskCompletionSource<MutationWriteResult>>();int submissions=0;
            Task<MutationWriteResult> Save(Microsoft.Data.SqlClient.SqlParameter[] args){submissions++;var reply=new TaskCompletionSource<MutationWriteResult>();writes.Enqueue(reply);return reply.Task;}
            using var form=new ChangeFeeDiscountForm(41,"Tuition",5000,1000,context.Rows[0],Query,Save);
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();
            T Field<T>(string name)=>(T)typeof(ChangeFeeDiscountForm).GetField(name,flags)!.GetValue(form)!;
            void Click(Control control)=>typeof(Control).GetMethod("OnClick",flags)!.Invoke(control,new object[]{EventArgs.Empty});
            bool RefusesClose(){var args=new FormClosingEventArgs(CloseReason.UserClosing,false);typeof(Form).GetMethod("OnFormClosing",flags)!.Invoke(form,new object[]{args});return args.Cancel;}
            DataSet Plan()
            {
                var data=new DataSet();var summary=data.Tables.Add();summary.Columns.Add("Snapshot");summary.Columns.Add("Difference",typeof(decimal));summary.Columns.Add("ReleasedPaidAmount",typeof(decimal));summary.Columns.Add("IssuedBills",typeof(int));summary.Rows.Add("reviewed-concession",-1000m,500m,1);
                var bills=data.Tables.Add();bills.Columns.Add("Month",typeof(DateTime));bills.Columns.Add("Bill Change",typeof(decimal));bills.Rows.Add(new DateTime(year,5,1),-1000m);
                var schedule=data.Tables.Add();schedule.Columns.Add("From",typeof(DateTime));schedule.Columns.Add("New Concession",typeof(decimal));schedule.Rows.Add(new DateTime(year,5,1),1000m);return data;
            }
            var from=Field<ModernDatePicker>("fromMonth");var through=Field<ModernDatePicker>("throughMonth");
            Check(from.Enabled&&through.Enabled,"The operator can choose concession start and end months");
            from.Value=new DateTime(year,5,1);through.Value=new DateTime(year,6,1);Field<ModernTextBox>("reason").Text="Approved temporary scholarship";
            Click(Field<ModernButton>("review"));
            Check(queries.Count==1&&RefusesClose(),"Concession review locks its inputs and keeps its modal owner until the result is known");
            Check((DateTime)queries[0].Single(p=>p.ParameterName=="@EffectiveFrom").Value==new DateTime(year,5,1)&&
                (DateTime)queries[0].Single(p=>p.ParameterName=="@EffectiveThrough").Value==new DateTime(year,6,1),"The chosen period reaches the SQL review unchanged");
            through.Value=new DateTime(year,7,1);replies.Dequeue().SetResult(Plan());
            PumpUntil(()=>Field<ModernButton>("review").Enabled,"Stale concession review settles");
            Check(!Field<ModernButton>("save").Enabled,"Changing the period invalidates an in-flight financial review");
            Click(Field<ModernButton>("review"));replies.Dequeue().SetResult(Plan());PumpUntil(()=>Field<ModernButton>("save").Enabled,"Current concession review settles");
            Check(Field<DataGridView>("bills").ReadOnly&&Field<Label>("impact").Text.Contains("500"),"The review presents paid money released and a read-only bill comparison");
            CurrentUser.SignIn(7782,"Read-only fixture","Accountant",false,new[]{"Fees.View"});Click(Field<ModernButton>("save"));
            Check(submissions==0,"Concession confirmation rechecks operator permission before submitting");
            CurrentUser.SignIn(7781,"Concession fixture","Administrator",false,Array.Empty<string>());
            Click(Field<ModernButton>("save"));Click(Field<ModernButton>("save"));
            Check(submissions==1&&RefusesClose(),"Concession save refuses duplicate submission and close while its outcome is pending");
            writes.Dequeue().SetResult(new MutationWriteResult(MutationOutcome.Unconfirmed));
            PumpUntil(()=>Field<ModernButton>("check").Enabled,"Unconfirmed concession save settles");
            Click(Field<ModernButton>("save"));
            Check(submissions==1&&!Field<ModernButton>("save").Enabled&&Field<Label>("impact").Text.Contains("Check Save Outcome"),"An unconfirmed concession directs the operator to recovery and blocks another financial write");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
