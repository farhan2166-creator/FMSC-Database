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
        try
        {
            CurrentUser.SignIn(7781,"Fee shortcut fixture","Administrator",false,Array.Empty<string>());
            using var data=new DataTable();
            foreach(string name in new[]{"Frequency","Status","ConcessionLabel","StudentName","AdmissionNumber","YearName"})data.Columns.Add(name);
            foreach(string name in new[]{"StartDate","EndDate","EnrollmentDate"})data.Columns.Add(name,typeof(DateTime));
            var chargeMonth=new DateTime(DateTime.Today.Year,3,1);var first=new DateTime(chargeMonth.Year,1,1);var last=new DateTime(chargeMonth.Year,12,31);
            data.Rows.Add("Monthly","Active",DBNull.Value,"Child","1001","Fixture year",first,last,first);
            object Field(object owner,string name)=>owner.GetType().GetField(name,flags)!.GetValue(owner)!;
            SqlParameter[] Args(ChangeFeeDiscountForm form)=>(SqlParameter[])typeof(ChangeFeeDiscountForm).GetMethod("Args",flags)!.Invoke(form,new object[]{true})!;
            using(var form=new ChangeFeeDiscountForm(41,"Tuition",500,0,data.Rows[0]))
            {
                form.ConfigureCollectionShortcut(false,chargeMonth);
                var args=Args(form);object Arg(string name)=>args.Single(p=>p.ParameterName==name).Value;
                Check((bool)Arg("@UpdateAgreement") && (bool)Arg("@ApplyToBills"),"A charge's discount shortcut defaults to existing charges and the fee plan");
                Check((DateTime)Arg("@EffectiveFrom")==chargeMonth && (DateTime)Arg("@EffectiveThrough")==chargeMonth,
                    "The clicked charge month is preselected instead of today's month");
                ((ModernComboBox)Field(form,"scope")).SelectedIndex=0;args=Args(form);
                Check(!(bool)Arg("@ApplyToBills") && (bool)Arg("@UpdateAgreement"),"Not applying to existing charges leaves issued bills untouched");
                form.ConfigureCollectionShortcut(true,chargeMonth);args=Args(form);
                Check((bool)Arg("@FullFeeWaiver") && (string)Arg("@ConcessionLabel")=="Free" &&
                    ((ModernComboBox)Field(form,"concession")).Text=="Sibling concession",
                    "Sibling concession reuses the full-concession value while showing the owner's wording");
                Check((DateTime)Arg("@EffectiveFrom")==chargeMonth && (DateTime)Arg("@EffectiveThrough")==new DateTime(last.Year,last.Month,1),
                    "Sibling concession defaults from the charge month through this academic year");
            }
            data.Columns.Add("StudentFeeAssignmentId",typeof(int));data.Columns.Add("BillingChargeId",typeof(long));data.Columns.Add("BillingMonth",typeof(DateTime));
            data.Rows[0]["StudentFeeAssignmentId"]=41;data.Rows[0]["BillingChargeId"]=321L;data.Rows[0]["BillingMonth"]=chargeMonth;
            var waiver=FeeConcessionActions.WaiverArgs(data.Rows[0],false,"snapshot");object W(string name)=>waiver.Single(p=>p.ParameterName==name).Value;
            Check(!(bool)W("@UpdateAgreement") && (bool)W("@ApplyToBills") && (long)W("@BillingChargeId")==321L &&
                (DateTime)W("@EffectiveFrom")==chargeMonth && (DateTime)W("@EffectiveThrough")==chargeMonth,
                "Waive once targets only the clicked charge and its month, preserving the recurring fee plan");
            Check((bool)W("@FullFeeWaiver") && !(bool)W("@ApplyAvailableCredit") && (string)W("@ExpectedSnapshot")=="snapshot",
                "Immediate waiver uses the shared full-concession writer and reviewed snapshot without spending credit");
            using(var charges=new DataTable())
            {
                foreach(string name in new[]{"Reference","StudentName","FeeTypeName"})charges.Columns.Add(name);
                charges.Columns.Add("BillingMonth",typeof(DateTime));charges.Columns.Add("DiscountAmount",typeof(decimal));charges.Columns.Add("Due",typeof(decimal));
                charges.Rows.Add("1001","Child","Tuition — Free",chargeMonth,500m,0m);
                charges.Rows.Add("1002","Sibling","Tuition",chargeMonth,0m,200m);
                using var bill=FamilyBillReport.BuildData("School","","","Parent",charges);
                using var display=FamilyFeeDocumentPresentation.Bill(bill);
                var child=display.AsEnumerable().First(row=>Convert.ToString(row["ChildAdmissionNumberText"])=="1001");
                Check(Convert.ToBoolean(child["ShowChildDocument"]) && Convert.ToDecimal(child["ChildFeesCovered"])==500m &&
                    Convert.ToDecimal(child["ChildDiscountApplied"])==500m && Convert.ToDecimal(child["ChildTotalPaid"])==0m &&
                    Convert.ToString(child["FeeDescription"])!.Contains("Sibling concession") && Convert.ToString(child["ChildBillPaymentPrompt"])=="No payment required",
                    "A fully discounted child stays on the family bill with the original fee, full discount, sibling wording and zero due");
            }
            using(var page=new FeeCollectionPage(false))
            {
                var grid=(DataGridView)Field(page,"charges");var row=grid.Rows[grid.Rows.Add()];
                row.Cells["BillingChargeId"].Value=321L;row.Cells["FeeTypeName"].Value="Tuition";row.Cells["FeeTypeName"].Tag=true;
                row.Cells["Balance"].Value=200m;
                typeof(FeeCollectionPage).GetMethod("BuildChargeMenu",flags)!.Invoke(page,new object[]{row.Index});
                var menu=(ContextMenuStrip)Field(page,"chargeMenu");
                Check(new[]{"Waive once","Discount…","Sibling concession…"}.All(text=>menu.Items.Cast<ToolStripItem>().Any(item=>item.Text==text && item.Enabled)),
                    "An eligible monthly fee exposes the three concession shortcuts");
                row.Cells["FeeTypeName"].Tag=false;
                typeof(FeeCollectionPage).GetMethod("BuildChargeMenu",flags)!.Invoke(page,new object[]{row.Index});
                Check(!menu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Sibling concession…"),"A non-monthly charge does not offer an upcoming-month sibling concession");
                var studentMenu=(ContextMenuStrip)Field(page,"studentFeesMenu");
                Check(studentMenu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Start / Restart Transport…") && studentMenu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Stop Transport…"),
                    "Student Fees exposes the existing transport start and stop workflows");
            }
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
