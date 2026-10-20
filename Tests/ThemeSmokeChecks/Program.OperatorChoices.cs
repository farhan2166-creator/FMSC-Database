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
            foreach(var choice in new[]{(Frequency:"Monthly",Status:"Left",Bill:false),(Frequency:"Yearly",Status:"Active",Bill:false),(Frequency:"As Needed",Status:"Active",Bill:true)})
            {
                var selected=context.Clone();selected.Columns.Add("Status");
                if(choice.Bill){selected.Columns.Add("BillingChargeId",typeof(long));selected.Columns.Add("BillingMonth",typeof(DateTime));}
                var selectedRow=selected.NewRow();selectedRow["Frequency"]=choice.Frequency;selectedRow["ConcessionLabel"]="Scholarship";
                selectedRow["StartDate"]=selectedRow["EnrollmentDate"]=new DateTime(year,1,1);selectedRow["EndDate"]=new DateTime(year,12,31);selectedRow["Status"]=choice.Status;
                if(choice.Bill){selectedRow["BillingChargeId"]=8421L;selectedRow["BillingMonth"]=new DateTime(year,5,1);}
                selected.Rows.Add(selectedRow);Microsoft.Data.SqlClient.SqlParameter[]? captured=null;
                Task<DataSet> TermsQuery(Microsoft.Data.SqlClient.SqlParameter[] args){captured=args;return Task.FromResult(Plan());}
                using var terms=new ChangeFeeDiscountForm(41,"Fee example",5000,1000,selectedRow,TermsQuery,Save);
                terms.StartPosition=FormStartPosition.Manual;terms.Location=new Point(-32000,-32000);terms.ShowInTaskbar=false;terms.Show();Application.DoEvents();
                T Term<T>(string name)=>(T)typeof(ChangeFeeDiscountForm).GetField(name,flags)!.GetValue(terms)!;
                var agreement=Term<CheckBox>("updateAgreement");
                Check(agreement.Enabled==(choice.Status=="Active"&&!choice.Bill)&&agreement.Checked==agreement.Enabled,"Closed placements and individual bills retain their agreement history while annual agreements can be reviewed");
                Term<CheckBox>("changeRate").Checked=true;Term<ModernTextBox>("newRate").Text="6000";
                Term<ModernTextBox>("reason").Text="Approved revised fee";Term<CheckBox>("fullWaiver").Checked=true;
                Term<CheckBox>("useCredit").Checked=true;Click(Term<ModernButton>("review"));Application.DoEvents();
                Check(captured is not null&&(decimal)captured.Single(p=>p.ParameterName=="@NewAmount").Value==6000m&&
                    (bool)captured.Single(p=>p.ParameterName=="@FullFeeWaiver").Value&&(bool)captured.Single(p=>p.ParameterName=="@ApplyAvailableCredit").Value,
                    "Annual, purchase and closed-placement reviews carry explicit rate, full-waiver and credit-use decisions");
                Check(!choice.Bill||(long)captured!.Single(p=>p.ParameterName=="@BillingChargeId").Value==8421L,"Individual bill review retains the selected bill identity");
                Term<CheckBox>("useCredit").Checked=false;Check(!Term<ModernButton>("save").Enabled,"Changing credit use invalidates the reviewed financial decision");
            }
            var issued=new DataTable();issued.Columns.Add("BillingChargeId",typeof(long));issued.Columns.Add("Charge Ref");issued.Columns.Add("Issued",typeof(DateTime));
            foreach(string name in new[]{"Bill Total","Settled","Due"})issued.Columns.Add(name,typeof(decimal));
            issued.Rows.Add(901L,"B-901",new DateTime(year,4,1),1200m,1200m,0m);issued.Rows.Add(902L,"B-902",new DateTime(year,5,1),1200m,0m,1200m);
            using(var picker=new IssuedBillPicker(issued,"Workbook purchase"))
            {
                picker.StartPosition=FormStartPosition.Manual;picker.Location=new Point(-32000,-32000);picker.ShowInTaskbar=false;picker.Show();Application.DoEvents();
                var grid=(DataGridView)typeof(IssuedBillPicker).GetField("grid",flags)!.GetValue(picker)!;
                var reviewBill=(ModernButton)typeof(IssuedBillPicker).GetField("review",flags)!.GetValue(picker)!;
                Check(grid.Rows.Count==2&&grid.ReadOnly&&!grid.Columns["BillingChargeId"].Visible,"Issued-bill review includes fully settled and unpaid purchases without allowing list edits");
                grid.CurrentCell=grid.Rows[0].Cells["Charge #"];Application.DoEvents();
                CurrentUser.SignIn(7782,"Read-only fixture","Accountant",false,new[]{"Fees.View"});Click(reviewBill);
                Check(picker.SelectedBillId==0,"Issued-bill selection rechecks adjustment authority before opening the financial review");
                CurrentUser.SignIn(7781,"Concession fixture","Administrator",false,Array.Empty<string>());Click(reviewBill);
                Check(picker.SelectedBillId==901L&&picker.DialogResult==DialogResult.OK,"The operator can select exactly the fully paid purchase instead of changing another purchase under the same fee");
            }
            using var promotion=new FMSC_SMS.Pages.Students.Enrollment.BulkPromotionPage(null,false);
            var carry=(CheckBox)typeof(FMSC_SMS.Pages.Students.Enrollment.BulkPromotionPage).GetField("carryConcessions",flags)!.GetValue(promotion)!;
            Check(carry.Enabled&&!carry.Checked,"Promotion exposes a deliberate carry-concession choice with the legacy reset default");
            typeof(FMSC_SMS.Pages.Students.Enrollment.BulkPromotionPage).GetField("busyCount",flags)!.SetValue(promotion,1);
            typeof(FMSC_SMS.Pages.Students.Enrollment.BulkPromotionPage).GetMethod("ApplyState",flags)!.Invoke(promotion,null);
            Check(!carry.Enabled,"Promotion holds the concession policy while processing");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
