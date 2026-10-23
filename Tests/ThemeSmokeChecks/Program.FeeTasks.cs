using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckFeeTaskFlow()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(ChangeFeeDiscountForm);
        Directory.CreateDirectory("artifacts/fee-tasks");
        try
        {
            CurrentUser.SignIn(7781,"Fee task preview","Administrator",false,Array.Empty<string>());
            var data=new DataTable();
            foreach(string name in new[]{"Frequency","Status","ConcessionLabel","StudentName","AdmissionNumber","YearName"})data.Columns.Add(name);
            foreach(string name in new[]{"StartDate","EndDate","EnrollmentDate","BillingMonth"})data.Columns.Add(name,typeof(DateTime));
            data.Columns.Add("BillingChargeId",typeof(long));
            var first=new DateTime(DateTime.Today.Year,1,1);var end=first.AddYears(1).AddDays(-1);
            data.Rows.Add("One Time","Active",DBNull.Value,"Preview student","6592","Preview year",first,end,first,first,321L);
            SqlParameter[]? captured=null;decimal paid=0,oldDiscount=0;int saves=0;bool mixedTerms=false;
            Task<DataSet> Query(SqlParameter[] args)
            {
                captured=args;
                decimal gross=Arg("@NewAmount") is DBNull?10000m:Convert.ToDecimal(Arg("@NewAmount"));
                decimal discount=(bool)Arg("@FullFeeWaiver")?gross:Convert.ToDecimal(Arg("@DiscountAmount"));
                decimal before=10000m-oldDiscount,net=(bool)Arg("@ApplyToBills")?gross-discount:before,
                    released=Math.Max(0,Math.Min(paid,before)-net);
                var plan=new DataSet();var summary=plan.Tables.Add();
                summary.Columns.Add("Snapshot");summary.Columns.Add("Difference",typeof(decimal));summary.Columns.Add("ReleasedPaidAmount",typeof(decimal));summary.Columns.Add("IssuedBills",typeof(int));
                summary.Rows.Add("fee-task-snapshot",net-before,released,1);
                var bills=plan.Tables.Add();bills.Columns.Add("Current Bill",typeof(decimal));bills.Columns.Add("Proposed Bill",typeof(decimal));bills.Rows.Add(before,net);
                var periods=plan.Tables.Add();periods.Columns.Add("New Fee",typeof(decimal));periods.Columns.Add("New Concession",typeof(decimal));
                periods.Columns.Add("Old Fee",typeof(decimal));periods.Columns.Add("Old Concession",typeof(decimal));
                periods.Rows.Add(gross,discount,10000m,mixedTerms?oldDiscount+500m:oldDiscount);
                return Task.FromResult(plan);
            }
            Task<MutationWriteResult> Save(SqlParameter[] args)
            {saves++;captured=args;return Task.FromResult(new MutationWriteResult(MutationOutcome.Saved));}
            using(var form=new ChangeFeeDiscountForm(41,"Admission Fee",10000,0,data.Rows[0],Query,Save))
            {
                Show(form);Capture(form,"choose-task");
                Check(!Field<ModernComboBox>(form,"scope").Visible&&!Field<ModernDatePicker>(form,"fromMonth").Visible,
                    "A single bill has a fixed scope and no irrelevant monthly date controls.");
                Check(!Field<ModernComboBox>(form,"paidReduction").Visible&&!Field<TabControl>(form,"comparisons").Visible,
                    "No overpayment question or empty comparison tabs are shown before review.");
                Field<ModernComboBox>(form,"action").SelectedIndex=0;
                Field<ModernTextBox>(form,"amount").Text="2000";Field<ModernTextBox>(form,"reason").Text="Approved admission discount";
                Check(!Field<ModernTextBox>(form,"newRate").Visible,"A discount does not display the fee-rate correction input.");
                Click(form,"review");
                Check(!(bool)Arg("@UpdateAgreement")&&(bool)Arg("@ApplyToBills")&&(long)Arg("@BillingChargeId")==321L,
                    "Opening a bill changes only that bill and preserves its saved fee plan.");
                Check(!(bool)Arg("@ApplyAvailableCredit"),"Fee changes never spend advance credit.");
                Check(!Field<ModernComboBox>(form,"paidReduction").Visible,"An unpaid reduction does not ask for a credit/refund decision.");
                Check(Field<Label>(form,"impact").Text.Contains("10,000 → Rs. 8,000")&&Field<Label>(form,"impact").Text.Contains("Discount: Rs. 2,000"),
                    "The review uses the authoritative current/proposed bill totals and discount.");
                Check(Field<TabControl>(form,"comparisons").TabPages.Count==1,"A bill-only review shows only the issued-bill comparison.");
                Capture(form,"discount-unpaid");
                Click(form,"editChoices");
                Check(Field<ModernTextBox>(form,"amount").Visible&&Field<ModernTextBox>(form,"amount").Text=="2000"&&!Field<ModernButton>(form,"save").Enabled,
                    "Edit Choices preserves the entries and requires a fresh review.");
                Field<ModernComboBox>(form,"action").SelectedIndex=1;
                Check(!Field<ModernTextBox>(form,"amount").Visible&&!Field<ModernTextBox>(form,"newRate").Visible&&!Field<ModernButton>(form,"save").Enabled,
                    "Switching to a waiver hides unrelated amounts and invalidates the discount preview.");
                Click(form,"review");Check((bool)Arg("@FullFeeWaiver")&&Arg("@NewAmount") is DBNull,"Waiver task leaves the fee rate unchanged.");
                Field<ModernComboBox>(form,"action").SelectedIndex=3;Click(form,"review");
                Check(!(bool)Arg("@FullFeeWaiver")&&(decimal)Arg("@DiscountAmount")==0m&&Arg("@ConcessionLabel") is DBNull,
                    "Remove Discount clears both fixed concessions and full-waiver labels explicitly.");
                Field<ModernComboBox>(form,"action").SelectedIndex=0;paid=10000;
                Click(form,"review");
                Check(Field<ModernComboBox>(form,"paidReduction").Visible&&Field<Label>(form,"impact").Text.Contains("Excess already paid: Rs. 2,000"),
                    "Only an actual overpayment reveals the excess-payment decision.");
                Field<ModernComboBox>(form,"paidReduction").SelectedIndex=1;
                Check(!Field<ModernButton>(form,"save").Enabled,"Refund selection invalidates the earlier credit review.");
                Click(form,"review");Check((string)Arg("@PaidReductionAction")=="Refund","Refund intent reaches the financial preview.");
                Capture(form,"discount-paid");
                Click(form,"save");Check(saves==1&&form.Changed&&(string)Arg("@ExpectedSnapshot")=="fee-task-snapshot",
                    "Confirmation uses the reviewed snapshot and submits once.");
            }
            var monthly=data.Copy();monthly.Columns.Remove("BillingChargeId");monthly.Rows[0]["Frequency"]="Monthly";oldDiscount=2000m;
            using(var form=new ChangeFeeDiscountForm(41,"Tuition",10000,2000,monthly.Rows[0],Query,Save))
            {
                Show(form);Field<ModernComboBox>(form,"action").SelectedIndex=2;
                Field<ModernTextBox>(form,"newRate").Text="12000";Field<ModernTextBox>(form,"reason").Text="Correct agreed rate";
                Check(Field<ModernDatePicker>(form,"fromMonth").Visible&&Field<ModernDatePicker>(form,"throughMonth").Visible,
                    "Monthly fee plans offer a start and through month.");
                Check(Field<ModernTextBox>(form,"newRate").Visible&&!Field<ModernTextBox>(form,"amount").Visible,
                    "Correct Fee Amount exposes only its own amount input.");
                Click(form,"review");
                Check((bool)Arg("@UpdateAgreement")&&!(bool)Arg("@ApplyToBills")&&(decimal)Arg("@DiscountAmount")==2000m&&Arg("@ConcessionLabel") is DBNull,
                    "Plan-only correction preserves the fixed discount, accepts an empty original label and leaves bills unchanged.");
                mixedTerms=true;Click(form,"editChoices");Click(form,"review");
                Check(!Field<ModernButton>(form,"save").Enabled&&Field<Label>(form,"impact").Text.Contains("different discount terms"),
                    "A rate-only task cannot silently replace different scheduled discount agreements.");
                mixedTerms=false;
                Field<ModernComboBox>(form,"scope").SelectedIndex=1;Check(!Field<ModernButton>(form,"save").Enabled,"Scope changes invalidate the review.");
                Click(form,"review");Check((bool)Arg("@UpdateAgreement")&&(bool)Arg("@ApplyToBills"),"Combined scope explicitly changes plan and bills.");
                Capture(form,"monthly-correction");
                Field<ModernComboBox>(form,"scope").SelectedIndex=2;Click(form,"review");
                Check(!(bool)Arg("@UpdateAgreement")&&(bool)Arg("@ApplyToBills"),"Existing-bills scope does not change the saved plan.");
            }
            monthly.Rows[0]["ConcessionLabel"]="Free";oldDiscount=10000m;
            using(var form=new ChangeFeeDiscountForm(41,"Tuition",10000,10000,monthly.Rows[0],Query,Save))
            {
                Show(form);Field<ModernComboBox>(form,"action").SelectedIndex=2;
                Field<ModernTextBox>(form,"newRate").Text="12000";Field<ModernTextBox>(form,"reason").Text="Correct waived rate";Click(form,"review");
                Check((bool)Arg("@FullFeeWaiver")&&(string)Arg("@ConcessionLabel")=="Free"&&Field<Label>(form,"impact").Text.Contains("full waiver is retained"),
                    "Correcting a waived fee preserves the full waiver and explains that effect.");
            }

            object Arg(string name)=>captured!.Single(p=>p.ParameterName==name).Value;
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}

        T Field<T>(object owner,string name)=>(T)type.GetField(name,flags)!.GetValue(owner)!;
        void Click(Form form,string name)
        {
            typeof(Control).GetMethod("OnClick",flags)!.Invoke(Field<ModernButton>(form,name),new object[]{EventArgs.Empty});Application.DoEvents();
        }
        void Show(Form form)
        {form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();}
        void Capture(Form form,string name)
        {
            foreach(var size in new[]{new Size(850,650),new Size(1000,780)})
            {
                form.Size=size;form.PerformLayout();Application.DoEvents();
                var root=Descendants(form).OfType<TableLayoutPanel>().Single(panel=>panel.AutoScroll);
                Check(!root.HorizontalScroll.Visible,$"{name} fits horizontally at {size.Width}px.");
                foreach(var button in Descendants(form).OfType<ModernButton>().Where(button=>button.Visible))
                    Check(form.ClientRectangle.Contains(form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),
                        $"{name}: {button.Text} stays visible at {size.Width}px.");
                using var bitmap=new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.GetFullPath($"artifacts/fee-tasks/{name}-{size.Width}.png"));
            }
        }
    }
}
