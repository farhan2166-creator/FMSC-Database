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
                decimal before=10000m-oldDiscount;
                if(Arg("@NewAmount") is DBNull)discount=Math.Min(discount,gross-Math.Min(paid,before));
                decimal net=(bool)Arg("@ApplyToBills")?gross-discount:before,
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
                Check(Field<ModernButton[]>(form,"taskButtons").All(button=>button.Visible&&button.Enabled),
                    "All four fee actions are directly visible and keyboard-accessible.");
                Check(!Field<ModernComboBox>(form,"scope").Visible&&!Field<ModernDatePicker>(form,"fromMonth").Visible,
                    "A single bill has a fixed scope and no irrelevant monthly date controls.");
                Check(!Field<ModernComboBox>(form,"paidReduction").Visible&&!Field<TabControl>(form,"comparisons").Visible,
                    "No overpayment question or empty comparison tabs are shown before review.");
                ClickTask(form,0);
                Check(Field<ModernComboBox>(form,"action").SelectedIndex==0&&Field<ModernComboBox>(form,"concession").Text=="None",
                    "Give Discount opens its fields without classifying an ordinary discount as Scholarship.");
                Field<ModernTextBox>(form,"amount").Text="2000";
                Check(Field<Label>(form,"taskNote").Text.Contains("Rs. 8,000"),"The draft immediately shows the selected fee's resulting net amount.");
                Check(!Field<ModernTextBox>(form,"newRate").Visible,"A discount does not display the fee-rate correction input.");
                Click(form,"review");
                Check((string)Arg("@Reason")=="Concession revised","A discount can be reviewed with no reason entered, using the existing neutral record description.");
                Check(!(bool)Arg("@UpdateAgreement")&&(bool)Arg("@ApplyToBills")&&(long)Arg("@BillingChargeId")==321L,
                    "Opening a bill changes only that bill and preserves its saved fee plan.");
                Check(!(bool)Arg("@ApplyAvailableCredit"),"Fee changes never spend advance credit.");
                Check(Arg("@ConcessionLabel") is DBNull,"The approved ordinary-discount default reaches review as an unlabelled discount.");
                Check(!Field<ModernComboBox>(form,"paidReduction").Visible,"An unpaid reduction does not ask for a credit/refund decision.");
                Check(Field<Label>(form,"impact").Text.Contains("10,000 → Rs. 8,000")&&Field<Label>(form,"impact").Text.Contains("Discount: Rs. 2,000"),
                    "The review uses the authoritative current/proposed bill totals and discount.");
                Check(Field<TabControl>(form,"comparisons").TabPages.Count==1,"A bill-only review shows only the issued-bill comparison.");
                Capture(form,"discount-unpaid");
                Click(form,"editChoices");
                Check(Field<ModernTextBox>(form,"amount").Visible&&Field<ModernTextBox>(form,"amount").Text=="2000"&&Field<ModernButton>(form,"save").Enabled,
                    "Entry edits stay on screen and allow confirmation with a fresh internal check.");
                ClickTask(form,1);
                Check(Field<ModernComboBox>(form,"action").SelectedIndex==1,"The waiver quick action selects the existing full-waiver task.");
                Capture(form,"waiver-entry");
                Check(!Field<ModernTextBox>(form,"amount").Visible&&!Field<ModernTextBox>(form,"newRate").Visible&&Field<ModernButton>(form,"save").Enabled,
                    "Switching to a waiver hides unrelated amounts and keeps same-screen confirmation.");
                Click(form,"review");Check((bool)Arg("@FullFeeWaiver")&&Arg("@NewAmount") is DBNull,"Waiver task leaves the fee rate unchanged.");
                ClickTask(form,3);Check(Field<ModernComboBox>(form,"action").SelectedIndex==3,"Remove Discount selects its existing task directly.");Click(form,"review");
                Check(!(bool)Arg("@FullFeeWaiver")&&(decimal)Arg("@DiscountAmount")==0m&&Arg("@ConcessionLabel") is DBNull,
                    "Remove Discount clears both fixed concessions and full-waiver labels explicitly.");
                Field<ModernComboBox>(form,"action").SelectedIndex=0;paid=10000;
                Click(form,"review");
                Check(!Field<ModernComboBox>(form,"paidReduction").Visible&&!Field<Label>(form,"impact").Text.Contains("Excess already paid"),
                    "Concessions retain recorded payments and never offer refund or advance credit.");
                Field<ModernComboBox>(form,"paidReduction").SelectedIndex=1;
                Check(type.GetField("reviewed",flags)!.GetValue(form) is null,"Refund selection invalidates the earlier internal check.");
                Click(form,"review");Check((string)Arg("@PaidReductionAction")=="Credit","A concession cannot submit a refund choice left from another action.");
                Capture(form,"discount-paid");
                Click(form,"save");Check(saves==1&&form.Changed&&(string)Arg("@ExpectedSnapshot")=="fee-task-snapshot",
                    "Confirmation uses the reviewed snapshot and submits once.");
            }
            var monthly=data.Copy();monthly.Columns.Remove("BillingChargeId");monthly.Rows[0]["Frequency"]="Monthly";oldDiscount=2000m;
            using(var form=new ChangeFeeDiscountForm(41,"Tuition",10000,2000,monthly.Rows[0],Query,Save))
            {
                Show(form);ClickTask(form,0);Field<ModernTextBox>(form,"amount").Text="2000";
                Capture(form,"monthly-discount-entry");
                ClickTask(form,2);
                Check(Field<ModernComboBox>(form,"action").SelectedIndex==2,"The fee correction quick action selects the existing rate-change task.");
                Field<ModernTextBox>(form,"newRate").Text="12000";Field<ModernTextBox>(form,"reason").Text="Correct agreed rate";
                Check(Field<ModernDatePicker>(form,"fromMonth").Visible&&Field<ModernDatePicker>(form,"throughMonth").Visible,
                    "Monthly fee plans offer a start and through month.");
                Check(Field<ModernTextBox>(form,"newRate").Visible&&!Field<ModernTextBox>(form,"amount").Visible,
                    "Correct Fee Amount exposes only its own amount input.");
                Click(form,"review");
                Check((bool)Arg("@UpdateAgreement")&&!(bool)Arg("@ApplyToBills")&&(decimal)Arg("@DiscountAmount")==2000m&&Arg("@ConcessionLabel") is DBNull,
                    "Plan-only correction preserves the fixed discount, accepts an empty original label and leaves bills unchanged.");
                mixedTerms=true;Click(form,"editChoices");Click(form,"review");
                Check(type.GetField("reviewed",flags)!.GetValue(form) is null&&Field<Label>(form,"impact").Text.Contains("different discount terms"),
                    "A rate-only task cannot silently replace different scheduled discount agreements.");
                mixedTerms=false;
                Field<ModernComboBox>(form,"scope").SelectedIndex=1;Check(type.GetField("reviewed",flags)!.GetValue(form) is null,"Scope changes invalidate the internal check.");
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

            monthly.Rows[0]["ConcessionLabel"]=DBNull.Value;oldDiscount=2000m;paid=0;mixedTerms=false;
            foreach(bool collection in new[]{false,true})
            foreach(int choice in Enumerable.Range(0,4))
            foreach(int scope in Enumerable.Range(0,3))
            {
                using var form=new ChangeFeeDiscountForm(41,"Tuition",10000,2000,monthly.Rows[0],Query,Save);
                if(collection)form.ConfigureCollectionAction(choice);else Field<ModernComboBox>(form,"action").SelectedIndex=choice;
                Show(form);Field<ModernComboBox>(form,"scope").SelectedIndex=scope;
                if(choice==0)Field<ModernTextBox>(form,"amount").Text="1500";
                if(choice==2)Field<ModernTextBox>(form,"newRate").Text="9000";
                Check(Field<ModernButton>(form,"save").Visible&&Field<ModernButton>(form,"save").Enabled
                    &&!Field<ModernButton>(form,"review").Visible&&!Field<ModernButton>(form,"editChoices").Visible,
                    "Every admission/collection action uses one entry screen and an enabled confirmation");
                Check(Field<Panel>(form,"actionField").Visible==!collection,
                    "Only the action selector differs between the admission and collection editors");
                int before=saves;Click(form,"save");
                Check(form.Changed&&saves==before+1&&(string)Arg("@ExpectedSnapshot")=="fee-task-snapshot",
                    "One confirmation internally checks and saves exactly once without a second review screen");
                Check((bool)Arg("@UpdateAgreement")== (scope is 0 or 1) && (bool)Arg("@ApplyToBills")== (scope is 1 or 2),
                    "Single-screen confirmation preserves the chosen fee-plan and issued-bill scope");
            }
            paid=10000m;
            using(var form=new ChangeFeeDiscountForm(41,"Tuition",10000,2000,monthly.Rows[0],Query,Save))
            {
                form.ConfigureCollectionAction(2);Show(form);
                Field<ModernComboBox>(form,"scope").SelectedIndex=1;
                Field<ModernTextBox>(form,"newRate").Text="5000";
                int before=saves;
                Check(Field<Panel>(form,"paidField").Visible&&Field<ModernComboBox>(form,"paidReduction").SelectedIndex==0,
                    "The excess-payment choice is available before confirmation with the established advance-credit default");
                Check(Field<ModernTextBox>(form,"newRate").Visible&&!Field<TabControl>(form,"comparisons").Visible,
                    "The excess-payment choice is on the original editor");
                Field<ModernComboBox>(form,"paidReduction").SelectedIndex=1;Click(form,"save");
                Check(saves==before+1&&form.Changed&&(string)Arg("@PaidReductionAction")=="Refund",
                    "One confirmation checks the excess payment and submits the chosen refund once");
            }

            object Arg(string name)=>captured!.Single(p=>p.ParameterName==name).Value;
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}

        T Field<T>(object owner,string name)=>(T)type.GetField(name,flags)!.GetValue(owner)!;
        void Click(Form form,string name)
        {
            if(name=="review")
            {
                var task=(Task)type.GetMethod("ReviewAsync",flags)!.Invoke(form,null)!;
                PumpUntil(()=>task.IsCompleted,"Internal financial check completes");task.GetAwaiter().GetResult();return;
            }
            typeof(Control).GetMethod("OnClick",flags)!.Invoke(Field<ModernButton>(form,name),new object[]{EventArgs.Empty});Application.DoEvents();
        }
        void ClickTask(Form form,int index)
        {
            typeof(Control).GetMethod("OnClick",flags)!.Invoke(Field<ModernButton[]>(form,"taskButtons")[index],new object[]{EventArgs.Empty});
            Application.DoEvents();
            Check(Field<ModernButton[]>(form,"taskButtons").Select((button,position)=>button.ThemeRole==(position==index?"TabActive":"TabInactive")).All(selected=>selected),
                "Exactly the chosen quick action is highlighted, including Remove Discount.");
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
                if(name=="monthly-discount-entry")
                {
                    var viewport=Field<Panel>(form,"reviewScroll");var input=Field<ModernTextBox>(form,"reason");
                    Check(viewport.ClientRectangle.Contains(viewport.RectangleToClient(input.RectangleToScreen(input.ClientRectangle))),
                        $"The monthly discount's reason input is visible without scrolling at {size.Width}px.");
                }
                foreach(var button in Descendants(form).OfType<ModernButton>().Where(button=>button.Visible))
                    Check(form.ClientRectangle.Contains(form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),
                        $"{name}: {button.Text} stays visible at {size.Width}px.");
                using var bitmap=new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.GetFullPath($"artifacts/fee-tasks/{name}-{size.Width}.png"));
            }
        }
    }
}
