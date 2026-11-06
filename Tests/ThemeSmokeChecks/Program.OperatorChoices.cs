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
            int year=DateTime.Today.Year;
            var current=new DateTime(year,DateTime.Today.Month,1);
            var start=current.AddMonths(1);var end=current.AddMonths(2);
            context.Rows.Add("Monthly","Scholarship",current.AddMonths(-7),current.AddMonths(5).AddDays(-1),current.AddMonths(-7));
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
                var data=new DataSet();var summary=data.Tables.Add();summary.Columns.Add("Snapshot");summary.Columns.Add("Difference",typeof(decimal));summary.Columns.Add("ReleasedPaidAmount",typeof(decimal));summary.Columns.Add("IssuedBills",typeof(int));summary.Rows.Add("reviewed-concession",-1000m,0m,1);
                var bills=data.Tables.Add();bills.Columns.Add("Month",typeof(DateTime));bills.Columns.Add("Bill Change",typeof(decimal));bills.Rows.Add(new DateTime(year,5,1),-1000m);
                var schedule=data.Tables.Add();schedule.Columns.Add("From",typeof(DateTime));schedule.Columns.Add("New Concession",typeof(decimal));schedule.Rows.Add(new DateTime(year,5,1),1000m);return data;
            }
            Check(!Field<ModernButton>("review").Enabled&&!Field<ModernTextBox>("amount").Visible,"A fee review starts with an explicit task choice rather than every scenario's controls");
            Field<ModernComboBox>("action").SelectedIndex=0;Field<ModernComboBox>("scope").SelectedIndex=1;
            var labels=Field<ModernComboBox>("concession");
            Check(Convert.ToString(labels.SelectedItem)=="Scholarship","Editing a legacy partial scholarship starts with its original label");
            Field<ModernTextBox>("amount").Text="1500";
            Microsoft.Data.SqlClient.SqlParameter[] PreviewArgs()=>(Microsoft.Data.SqlClient.SqlParameter[])typeof(ChangeFeeDiscountForm)
                .GetMethod("Args",flags)!.Invoke(form,new object[]{true})!;
            var scholarshipArgs=PreviewArgs();
            Check((string)scholarshipArgs.Single(p=>p.ParameterName=="@ConcessionLabel").Value=="Scholarship"
                &&(decimal)scholarshipArgs.Single(p=>p.ParameterName=="@DiscountAmount").Value==1500m
                &&!(bool)scholarshipArgs.Single(p=>p.ParameterName=="@FullFeeWaiver").Value,
                "An edited partial scholarship sends its chosen amount and original label without becoming a full waiver");
            labels.SelectedItem="None";
            Check(PreviewArgs().Single(p=>p.ParameterName=="@ConcessionLabel").Value is DBNull,
                "An operator can explicitly remove the legacy scholarship classification");
            Field<ModernComboBox>("throughMonth").SelectedItem=end;
            Check(Convert.ToString(labels.SelectedItem)=="None","Changing dates does not undo an explicit label removal");
            labels.SelectedItem="Scholarship";Field<ModernTextBox>("amount").Text="1000";
            var from=Field<ModernComboBox>("fromMonth");var through=Field<ModernComboBox>("throughMonth");
            Check(from.Enabled&&through.Enabled,"The operator can choose concession start and end months");
            from.SelectedItem=start;through.SelectedItem=end;Field<ModernTextBox>("reason").Text="Approved temporary scholarship";
            Click(Field<ModernButton>("save"));
            Check(queries.Count==1&&RefusesClose(),"Concession review locks its inputs and keeps its modal owner until the result is known");
            Check((string)queries[0].Single(p=>p.ParameterName=="@ConcessionLabel").Value=="Scholarship",
                "The actual SQL review retains the partial scholarship classification");
            Check((DateTime)queries[0].Single(p=>p.ParameterName=="@EffectiveFrom").Value==start&&
                (DateTime)queries[0].Single(p=>p.ParameterName=="@EffectiveThrough").Value==end,"The chosen period reaches the SQL review unchanged");
            through.SelectedItem=end.AddMonths(1);replies.Dequeue().SetResult(Plan());
            PumpUntil(()=>Field<ModernButton>("save").Enabled,"Stale concession check settles");
            Check(submissions==0,"Changing the period invalidates an in-flight financial check before any write");
            Check(!Field<ModernComboBox>("paidReduction").Visible,"Concessions preserve paid money without offering a credit/refund choice");
            CurrentUser.SignIn(7782,"Read-only fixture","Accountant",false,new[]{"Fees.View"});Click(Field<ModernButton>("save"));
            Check(submissions==0,"Concession confirmation rechecks operator permission before submitting");
            CurrentUser.SignIn(7781,"Concession fixture","Administrator",false,Array.Empty<string>());
            Click(Field<ModernButton>("save"));Click(Field<ModernButton>("save"));
            Check(queries.Count==2&&submissions==0,"Duplicate confirmation cannot start a second financial check");
            replies.Dequeue().SetResult(Plan());PumpUntil(()=>submissions==1,"Confirmation proceeds from its internal check directly to saving");
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
                var scope=Term<ModernComboBox>("scope");
                Check(scope.Items.Count==(choice.Status=="Active"&&!choice.Bill?3:1),"Closed placements and individual bills cannot choose saved-plan changes");
                Term<ModernComboBox>("action").SelectedIndex=2;Term<ModernTextBox>("newRate").Text="6000";
                Term<ModernTextBox>("reason").Text="Approved revised fee";
                var preview=(Task)typeof(ChangeFeeDiscountForm).GetMethod("ReviewAsync",flags)!.Invoke(terms,null)!;
                PumpUntil(()=>preview.IsCompleted,"Internal fee check settles");preview.GetAwaiter().GetResult();
                Check(captured is not null&&(decimal)captured.Single(p=>p.ParameterName=="@NewAmount").Value==6000m&&
                    !(bool)captured.Single(p=>p.ParameterName=="@FullFeeWaiver").Value&&!(bool)captured.Single(p=>p.ParameterName=="@ApplyAvailableCredit").Value,
                    "A fee correction changes the rate, preserves the fixed discount and leaves credit use to collection");
                Check(!choice.Bill||(long)captured!.Single(p=>p.ParameterName=="@BillingChargeId").Value==8421L,"Individual bill review retains the selected bill identity");
                Term<ModernComboBox>("action").SelectedIndex=1;Check(typeof(ChangeFeeDiscountForm).GetField("reviewed",flags)!.GetValue(terms) is null,"Choosing a waiver instead invalidates the prior rate check");
                preview=(Task)typeof(ChangeFeeDiscountForm).GetMethod("ReviewAsync",flags)!.Invoke(terms,null)!;
                PumpUntil(()=>preview.IsCompleted,"Internal waiver check settles");preview.GetAwaiter().GetResult();
                Check((bool)captured!.Single(p=>p.ParameterName=="@FullFeeWaiver").Value&&captured!.Single(p=>p.ParameterName=="@NewAmount").Value is DBNull,
                    "A full waiver is a separate task and does not carry over a hidden rate correction");
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
            using var promotion=new FMSC_SMS.Pages.Students.Enrollment.StudentPlacementPage(null,false);
            var placementType=typeof(FMSC_SMS.Pages.Students.Enrollment.StudentPlacementPage);
            T PromotionField<T>(string name)=>(T)placementType.GetField(name,flags)!.GetValue(promotion)!;
            void RefreshPromotion()=>placementType.GetMethod("RefreshActionAvailability",flags)!.Invoke(promotion,null);
            var carry=PromotionField<CheckBox>("carry");
            Check(carry.Parent is not null&&!carry.Checked&&!carry.Enabled,
                "Promotion defaults to resetting concessions and locks the choice before destination selection");
            // Provide an offline, selected source/destination fixture: enabling the
            // concession choice requires both a plan context and fee-adjust access.
            var years=new DataTable();
            years.Columns.Add("AcademicYearId",typeof(int));years.Columns.Add("YearName");
            years.Rows.Add(10,"Source year");years.Rows.Add(11,"Destination year");
            var sourceYear=PromotionField<LookupBox>("sourceYear");
            var targetYear=PromotionField<LookupBox>("targetYear");
            sourceYear.Bind(years,"AcademicYearId","YearName");sourceYear.SelectedValue=10;
            targetYear.Bind(years,"AcademicYearId","YearName");targetYear.SelectedValue=11;
            var promotionRows=new DataTable();
            promotionRows.Columns.Add("Selected",typeof(bool));promotionRows.Columns.Add("Decision");
            promotionRows.Rows.Add(false,"SKIP");
            placementType.GetField("data",flags)!.SetValue(promotion,promotionRows);
            placementType.GetField("choicesReady",flags)!.SetValue(promotion,true);
            RefreshPromotion();
            Check(carry.Enabled&&!carry.Checked,
                "A ready promotion exposes carry-concession with the legacy reset default");
            CurrentUser.SignIn(7782,"Read-only fixture","Accountant",false,new[]{"Fees.View"});
            RefreshPromotion();
            Check(!carry.Enabled,"Carrying concessions requires fee-adjustment permission");
            CurrentUser.SignIn(7781,"Concession fixture","Administrator",false,Array.Empty<string>());
            RefreshPromotion();
            Check(carry.Enabled,"The authorized operator regains the choice when the plan is ready");
            PromotionField<Control>("pageContent").Enabled=false;
            Check(!carry.Enabled,"Promotion holds the concession policy while processing");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
