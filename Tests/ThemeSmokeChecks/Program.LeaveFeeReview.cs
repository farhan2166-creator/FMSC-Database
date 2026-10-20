using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckLeaveFeeReview()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        var type=typeof(LeaveFeeReviewBar);
        using var charges=new DataTable();
        foreach(string name in new[]{"BillingChargeId","EnrollmentId"})charges.Columns.Add(name,typeof(int));
        foreach(string name in new[]{"FeeTypeName","Status","EnrollmentStatus"})charges.Columns.Add(name);
        foreach(string name in new[]{"NetAmount","Balance"})charges.Columns.Add(name,typeof(decimal));
        foreach(string name in new[]{"HasPaymentHistory","IsLater","CanReverse"})charges.Columns.Add(name,typeof(bool));
        foreach(string name in new[]{"BillingMonth","ChargeDate"})charges.Columns.Add(name,typeof(DateTime));
        void Row(int id,int enrollment,string fee,string status,decimal net,decimal due,bool history,bool later,bool can)
            => charges.Rows.Add(id,enrollment,fee,status,"Active",net,due,history,later,can,new DateTime(2026,later?11:10,1),new DateTime(2026,10,20));
        Row(1,101,"Tuition","Open",400,400,true,false,false);
        Row(2,101,"Annual","Open",1000,1000,true,false,false);
        Row(3,101,"Examination","PartiallyPaid",1000,800,true,false,false);
        Row(4,101,"Unpaid opening charge","Open",500,500,false,false,true);
        Row(5,100,"Previous placement","Open",300,300,false,false,false);
        Row(6,101,"Settled later fee","Paid",200,0,true,true,false);
        Row(7,101,"Zero later fee","Open",0,0,false,true,false);
        LeaveFeeReviewBar Bar(DataTable source)
        {
            var bar=new LeaveFeeReviewBar(()=>41,()=>101,()=>new DateTime(2026,10,20),()=>false);
            type.GetField("displayCharges",flags)!.SetValue(bar,source);
            type.GetField("balance",flags)!.SetValue(bar,source.AsEnumerable().Sum(row=>row.Field<decimal>("Balance")));
            return bar;
        }
        T Find<T>(Control root,string name) where T:Control=>(T)root.Controls.Find(name,true).Single();
        void Click(Control control)=>typeof(Control).GetMethod("OnClick",flags)!.Invoke(control,new object[]{EventArgs.Empty});
        void Show(Form form){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();}
        try
        {
            CurrentUser.SignIn(7781,"Leaving review fixture","Administrator",false,Array.Empty<string>());
            using var bar=Bar(charges);
            using(var dialog=bar.CreateReviewDialog())
            {
                Show(dialog);
                var grid=Find<DataGridView>(dialog,"LeaveCharges");
                var keep=Find<RadioButton>(dialog,"KeepCharges");var reverse=Find<RadioButton>(dialog,"ReverseCharges");
                var payment=Find<RadioButton>(dialog,"ReviewPayments");var history=Find<CheckBox>(dialog,"ShowHistory");
                var use=Find<ModernButton>(dialog,"UseDecision");
                Check(!keep.Checked&&!reverse.Checked&&!payment.Checked&&!use.Enabled,"Leaving review requires a deliberate fee decision");
                Check(grid.Rows.Count==5&&history.Enabled&&history.Text.Contains("2"),"The checkbox counts only extra settled or zero-amount later charges");
                Check(Convert.ToString(grid.Rows[0].Cells["ReversalReason"].Value)!.Contains("Payment history"),"A now-unpaid charge explains its retained payment history");
                Check(Convert.ToString(grid.Rows[4].Cells["ReversalReason"].Value)!.Contains("Previous placement"),"Previous-placement charges explain their separate restriction");
                reverse.Checked=true;
                Check(!use.Enabled,"Reversal requires at least one eligible charge to be selected");
                void Select(int row)=>typeof(DataGridView).GetMethod("OnCellClick",flags)!.Invoke(grid,new object[]{new DataGridViewCellEventArgs(0,row)});
                Select(0);Check(!use.Enabled,"A history-blocked unpaid charge cannot be selected for reversal");
                Select(3);Check(use.Enabled&&Find<Label>(dialog,"DecisionSummary").Text.Contains("2,500"),"Selecting an eligible charge previews the exact remaining balance");
                history.Checked=true;
                Check(grid.Rows.Count==7&&Convert.ToBoolean(grid.Rows[3].Cells["Reverse"].Value),"Showing history preserves the selected unpaid charge");
                Check(grid.Rows[5].Cells["Reverse"] is DataGridViewTextBoxCell&&grid.Rows[6].Cells["Reverse"] is DataGridViewTextBoxCell,"History-only rows cannot be selected for reversal");
                history.Checked=false;
                Check(grid.Rows.Count==5&&Convert.ToBoolean(grid.Rows[3].Cells["Reverse"].Value),"Hiding history preserves the fee decision");
                Click(use);
                Check(dialog.DialogResult==DialogResult.OK&&Convert.ToString(bar.Parameters().Single(p=>p.ParameterName=="@ReverseChargeIds").Value)=="[4]","Continue records only the reviewed eligible charge for the later certificate confirmation");
            }
            using(var dialog=bar.CreateReviewDialog())
            {
                Show(dialog);Find<RadioButton>(dialog,"KeepCharges").Checked=true;
                Check(Find<Label>(dialog,"DecisionSummary").Text.Contains("remains payable")&&Find<Label>(dialog,"DecisionSummary").Text.Contains("only when"),"Keeping dues clearly states both continued liability and deferred saving");
                Click(Find<ModernButton>(dialog,"UseDecision"));
                Check(Convert.ToString(bar.Parameters().Single(p=>p.ParameterName=="@ReverseChargeIds").Value)=="[]","Keeping dues clears the earlier reversal selection");
            }
            using(var dialog=bar.CreateReviewDialog())
            {
                Show(dialog);Find<RadioButton>(dialog,"ReviewPayments").Checked=true;
                Check(Find<ModernButton>(dialog,"UseDecision").Text=="Open Fee Collection"&&Find<Label>(dialog,"DecisionSummary").Text.Contains("has not been created"),"The collection option names the actual destination and preserves the certificate decision");
                Click(Find<ModernButton>(dialog,"UseDecision"));
                Check(dialog.DialogResult==DialogResult.Retry,"Collection requests a return and refresh rather than accepting a leave decision");
            }
            using var noEligible=charges.Clone();foreach(DataRow row in charges.Rows.Cast<DataRow>().Take(3))noEligible.ImportRow(row);
            using var blocked=Bar(noEligible);
            using(var dialog=blocked.CreateReviewDialog())
            {
                Show(dialog);
                Check(!Find<RadioButton>(dialog,"ReverseCharges").Enabled&&Find<DataGridView>(dialog,"LeaveCharges").Rows.Count==3,"The 6589-style fully/partially unpaid history remains ineligible with visible reasons");
                Check(!Find<CheckBox>(dialog,"ShowHistory").Enabled,"The history checkbox is disabled when there are no extra rows to reveal");
                var grid=Find<DataGridView>(dialog,"LeaveCharges");
                Check(grid.ColumnHeadersHeight+grid.Rows.Cast<DataGridViewRow>().Sum(row=>row.Height)<=grid.ClientSize.Height,
                    "All three charge reasons fit in the normal review window without vertical scrolling");
                string folder=Path.GetFullPath("artifacts/leave-fee-review");Directory.CreateDirectory(folder);
                using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(folder,"payment-history.png"));
            }
            CurrentUser.SignIn(7782,"Leaving-only fixture","Clerk",false,new[]{"Students.View","Students.Leave"});
            using(var dialog=bar.CreateReviewDialog())
            {
                Check(Find<RadioButton>(dialog,"KeepCharges").Enabled&&!Find<RadioButton>(dialog,"ReverseCharges").Enabled&&!Find<RadioButton>(dialog,"ReviewPayments").Enabled,"Collection permission controls financial actions while leaving the keep-dues decision available");
            }
            Check(charges.Rows.Count==7&&charges.AsEnumerable().Sum(row=>row.Field<decimal>("Balance"))==3000m,"Reviewing choices and toggling history changes no charge or balance");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
