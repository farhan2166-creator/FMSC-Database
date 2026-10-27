using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckCollectionFeeActions()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,flags)!.GetValue(owner)!;
        object? Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,flags)!.Invoke(owner,args);
        try
        {
            CurrentUser.SignIn(7781,"Collection action fixture","Administrator",false,Array.Empty<string>());
            using var context=new DataTable();
            foreach(string name in new[]{"Frequency","Status","ConcessionLabel","StudentName","AdmissionNumber","YearName"})context.Columns.Add(name);
            foreach(string name in new[]{"StartDate","EndDate","EnrollmentDate","BillingMonth"})context.Columns.Add(name,typeof(DateTime));
            context.Columns.Add("StudentFeeAssignmentId",typeof(int));context.Columns.Add("BillingChargeId",typeof(long));
            context.Columns.Add("Balance",typeof(decimal));
            int year=DateTime.Today.Year;var month=new DateTime(year,3,1);
            var row=context.Rows.Add("Monthly","Active",DBNull.Value,"Preview Student","542","Preview year",new DateTime(year,1,1),new DateTime(year,12,31),new DateTime(year,1,1),month,41,321L,300m);
            SqlParameter[]? captured=null;int reads=0,writes=0;
            Task<DataSet> Review(SqlParameter[] args)
            {
                reads++;captured=args;var result=new DataSet();var summary=result.Tables.Add();
                summary.Columns.Add("Snapshot");summary.Columns.Add("Difference",typeof(decimal));summary.Columns.Add("ReleasedPaidAmount",typeof(decimal));summary.Columns.Add("IssuedBills",typeof(int));
                decimal net=700m-Convert.ToDecimal(args.Single(p=>p.ParameterName=="@DiscountAmount").Value);
                summary.Rows.Add("focused-bill",net-500m,0m,1);
                var bills=result.Tables.Add();bills.Columns.Add("Current Bill",typeof(decimal));bills.Columns.Add("Proposed Bill",typeof(decimal));bills.Rows.Add(500m,net);
                result.Tables.Add();return Task.FromResult(result);
            }
            Task<MutationWriteResult> Save(SqlParameter[] args){writes++;captured=args;return Task.FromResult(new MutationWriteResult(MutationOutcome.Saved));}
            object Arg(string name)=>captured!.Single(p=>p.ParameterName==name).Value;
            void Show(Form form){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();}
            void Capture(Form form,string name)
            {
                Directory.CreateDirectory("artifacts/collection-actions");
                using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(Path.GetFullPath($"artifacts/collection-actions/{name}.png"));
                foreach(var button in Descendants(form).OfType<ModernButton>().Where(button=>button.Visible))
                    Check(form.ClientRectangle.Contains(form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),"Focused dialog buttons stay within the window");
            }
            using(var form=new ChangeFeeDiscountForm(41,"Library Fee",700m,200m,row,Review,Save))
            {
                form.ConfigureCollectionBill();Show(form);
                Check(Field<ModernButton[]>(form,"taskButtons").All(button=>!button.Visible&&!button.Enabled),"A collection discount has no action-switching buttons");
                Check(!Field<ModernComboBox>(form,"scope").Visible&&!Field<ModernComboBox>(form,"concession").Visible
                    &&!Field<ModernTextBox>(form,"newRate").Visible&&!Field<ModernDatePicker>(form,"fromMonth").Visible,
                    "The focused bill has no fee-plan, type, rate, waiver or date choices");
                Field<ModernTextBox>(form,"reason").Text="Approved discount";
                Field<ModernTextBox>(form,"amount").Text="200";
                Call(form,"ReviewAsync");Check(reads==0,"An unchanged discount does not submit a review");
                Field<ModernTextBox>(form,"amount").Text="500";
                Call(form,"ReviewAsync");Check(reads==0&&Field<Label>(form,"impact").Text.Contains("Waive Remaining Dues"),"A full remaining waiver must use the dedicated action");
                Field<ModernTextBox>(form,"amount").Text="300";
                Check(Field<Label>(form,"taskNote").Text.Contains("Rs. 200"),"The draft preserves paid money and shows the resulting unpaid balance");
                Capture(form,"change-discount-entry");
                Call(form,"ReviewAsync");Application.DoEvents();
                Check(reads==1&&!(bool)Arg("@UpdateAgreement")&&(bool)Arg("@ApplyToBills")&&(long)Arg("@BillingChargeId")==321L
                    &&!(bool)Arg("@FullFeeWaiver")&&Arg("@NewAmount") is DBNull&&Arg("@ConcessionLabel") is DBNull,
                    "The focused discount changes only the selected bill with no hidden plan/rate/waiver action");
                Check((DateTime)Arg("@EffectiveFrom")==month&&(DateTime)Arg("@EffectiveThrough")==month,"A monthly fee still targets exactly its clicked bill period");
                Capture(form,"change-discount-review");
                Call(form,"SaveAsync");Check(writes==1&&form.Changed&&(string)Arg("@ExpectedSnapshot")=="focused-bill","Confirmation uses the reviewed snapshot once");
            }
            using(var form=new ChangeFeeDiscountForm(41,"Library Fee",700m,200m,row,Review,Save))
            {
                form.ConfigureCollectionBill(removeDiscount:true);Show(form);
                Check(!Field<ModernTextBox>(form,"amount").Visible&&Field<ModernTextBox>(form,"reason").Visible,"Removing a discount asks only for its reason");
                Field<ModernTextBox>(form,"reason").Text="Discount withdrawn";Call(form,"ReviewAsync");
                Check((decimal)Arg("@DiscountAmount")==0m&&!(bool)Arg("@FullFeeWaiver")&&!(bool)Arg("@UpdateAgreement"),"Removal clears only the selected bill discount");
                Capture(form,"remove-discount-review");
                Field<ModernComboBox>(form,"action").SelectedIndex=1;int before=reads;Call(form,"ReviewAsync");
                Check(reads==before,"A hidden action change cannot escape the locked task");
            }
            row["Balance"]=0m;
            using(var form=new ChangeFeeDiscountForm(41,"Library Fee",700m,700m,row,Review,Save))
            {
                bool refused=false;try{form.ConfigureCollectionBill();}catch(InvalidOperationException){refused=true;}
                Check(refused,"A newly settled or waived bill is rechecked before opening an editable collection dialog");
            }
            using(var form=new ChangeFeeDiscountForm(0,"Fee Change Recovery",0m,0m))
            {
                form.ConfigureRecoveryOnly();Show(form);
                Check(Field<ModernButton[]>(form,"taskButtons").All(button=>!button.Visible)&&!Field<ModernButton>(form,"review").Visible,
                    "Save recovery is available separately without opening financial edit choices");
            }
            var waiver=FeeConcessionActions.WaiverArgs(row,false,"snapshot");
            Check(!(bool)waiver.Single(p=>p.ParameterName=="@UpdateAgreement").Value
                &&(bool)waiver.Single(p=>p.ParameterName=="@FullFeeWaiver").Value
                &&!(bool)waiver.Single(p=>p.ParameterName=="@ApplyAvailableCredit").Value,
                "Direct waiver still preserves the saved fee plan and does not spend credit");
            using(var page=new FeeCollectionPage(false))
            {
                var grid=Field<DataGridView>(page,"charges");var fee=grid.Rows[grid.Rows.Add()];
                fee.Cells["BillingChargeId"].Value=321L;fee.Cells["FeeTypeName"].Value="Library Fee";fee.Cells["BillingMonth"].Value="Mar";
                fee.Cells["Balance"].Value=300m;fee.Cells["GrossAmount"].Value=700m;fee.Cells["GrossAmount"].Tag=0m;
                fee.Cells["AppliedDiscount"].Value=0m;fee.Cells["NetAmount"].Value=700m;fee.Cells["Amount"].Value=0m;
                var menu=Field<ContextMenuStrip>(page,"chargeMenu");
                string[] Menu(){Call(page,"BuildChargeMenu",fee.Index);return menu.Items.OfType<ToolStripMenuItem>().Where(item=>item.Enabled).Select(item=>item.Text??"").ToArray();}
                var items=Menu();
                Check(items.Contains("Give Discount…")&&items.Contains("Waive Remaining Dues")&&items.Contains("View Fee Details"),"Unpaid fees expose the three focused actions");
                Check(!items.Any(text=>text.Contains("Sibling")||text.Contains("Review bill terms")||text.Contains("Remove Discount")),"Collection does not duplicate fee-plan and generic term-editing choices");
                fee.Cells["AppliedDiscount"].Value=200m;fee.Cells["GrossAmount"].Tag=200m;items=Menu();
                Check(items.Contains("Change Discount…")&&items.Contains("Remove Discount…")&&!items.Contains("Give Discount…"),"An existing discount offers change and remove rather than another give action");
                fee.Cells["Balance"].Value=0m;fee.Cells["AppliedDiscount"].Value=700m;fee.Cells["GrossAmount"].Tag=700m;
                using var source=new DataTable();source.Columns.Add("PendingWaiver",typeof(bool));source.Columns.Add("PaidAmount",typeof(decimal));var waived=source.Rows.Add(true,0m);
                typeof(FeeCollectionPage).GetMethod("ApplyWaiverReviewState",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{fee,waived});
                Check(fee.ReadOnly&&fee.Cells["Pay"] is DataGridViewTextBoxCell&&fee.Cells["FeeTypeName"].Value!.ToString()!.Contains("Waived"),"Waived rows are clearly identified and cannot receive payment");
                items=Menu();Check(items.SequenceEqual(new[]{"View Fee Details"}),"A fully waived fee offers no discount, waive, correction or cancellation action");
                Check(!(bool)Call(page,"HasOutstandingCharges")!,"A saved waiver does not enable collection");
                Check(!((IEnumerable<DataGridViewRow>)Call(page,"SettlementRows",null!,null!)!).Any(),"A saved waiver is excluded from payment and credit allocation");
                Call(page,"ClearPayNowAmounts");Check((string)fee.Cells["Pay"].Value=="—","Payment resets preserve the nonpayable row marker");
                Call(page,"UpdateProcessCollectionAvailability");
                Check(Field<ModernButton>(page,"printBillButton").Text=="Print Fee Changes"&&!Field<ModernButton>(page,"executeButton").Enabled,
                    "A waiver-only visit offers its acknowledgement instead of a payment or zero-total Outstanding bill");
                CurrentUser.SignIn(7782,"Read-only fixture","Accountant",false,new[]{"Fees.View"});
                fee.Cells["Balance"].Value=100m;items=Menu();Check(items.SequenceEqual(new[]{"View Fee Details"}),"Read-only users get only fee details even when dues remain");
            }
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
