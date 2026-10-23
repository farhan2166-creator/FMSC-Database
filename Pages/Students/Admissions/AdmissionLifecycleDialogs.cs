using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Admissions;

internal sealed class CloseAdmissionDialog : ApplicationWindowForm
{
    private readonly ModernDatePicker date=HumanUi.Date(),waiveFrom=HumanUi.Date();
    private readonly ModernComboBox monthlyPolicy=HumanUi.Combo(),paidReduction=HumanUi.Combo();
    private readonly ModernTextBox reason=HumanUi.TextBox(true,500),impact=HumanUi.TextBox(true,0);
    private readonly ModernButton review=new(){Text="Review",ThemeRole="Secondary"},apply=new(){Text="Apply",ThemeRole="Warning"};
    private readonly TableLayoutPanel root=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(14)};
    private readonly Func<string,SqlParameter[],Task<DataTable>> query;
    private readonly int admission;
    private readonly bool cancel;
    private DataRow? plan;
    private bool busy;
    private int reviewGeneration;
    internal bool Changed {get;private set;}
    internal CloseAdmissionDialog(int admission,string student,bool cancel,Func<string,SqlParameter[],Task<DataTable>>? query=null)
    {
        this.admission=admission;this.cancel=cancel;
        this.query=query??((procedure,parameters)=>DatabaseHelper.QueryUserAsync(procedure,parameters));
        Text=cancel?"Cancel Registration":"Record Departure";StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(AppScale.Scale(820),AppScale.Scale(740));MinimumSize=new Size(AppScale.Scale(680),AppScale.Scale(580));
        ShowInTaskbar=false;MinimizeBox=false;
        impact.ReadOnly=true;impact.Dock=DockStyle.Fill;impact.ScrollBars=ScrollBars.Vertical;
        impact.Text="Enter the date and reason, then select Review to see the effect before saving.";
        monthlyPolicy.Items.AddRange(new object[]{"Use the school cutoff","Keep all issued monthly bills","Waive monthly bills from a selected month"});monthlyPolicy.SelectedIndex=0;
        paidReduction.Items.AddRange(new object[]{"Keep excess payment as advance","Refund the excess payment"});paidReduction.SelectedIndex=0;
        FinanceHelp.Attach(this,paidReduction,FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this,monthlyPolicy,"Choose which monthly fees remain due when the student leaves. Review waived dues and any paid money released before saving.","Keeping existing bills leaves Rs. 400 tuition due. Waiving that eligible unpaid fee reduces outstanding by Rs. 400.");
        FinanceHelp.Attach(this,waiveFrom,FinanceHelp.Topic.Period);
        FinanceHelp.Attach(this,review,FinanceHelp.Topic.Review);
        waiveFrom.Format=DateTimePickerFormat.Custom;waiveFrom.CustomFormat="MMMM yyyy";
        waiveFrom.Value=new DateTime(date.Value.Year,date.Value.Month,1);waiveFrom.Enabled=false;
        var close=new ModernButton{Text="Close",DialogResult=DialogResult.Cancel};CancelButton=close;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight*1.5f));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text=student,AutoSize=true},0,0);root.Controls.Add(HumanUi.Field(cancel?"Cancellation date":"Leaving date",date,true),0,1);
        root.Controls.Add(HumanUi.Field("Reason",reason,true),0,2);
        var choices=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=3,Visible=!cancel,Margin=Padding.Empty};
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        choices.Controls.Add(HumanUi.Field("Already issued monthly fees",monthlyPolicy),0,0);
        var monthField=HumanUi.Field("First month to waive",waiveFrom);monthField.Visible=false;choices.Controls.Add(monthField,0,1);
        choices.Controls.Add(HumanUi.Field("If a waived fee has already been paid",paidReduction),0,2);
        monthlyPolicy.MaximumSize=Size.Empty;paidReduction.MaximumSize=Size.Empty;
        root.Controls.Add(choices,0,3);
        var explanation=new Label{AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,6,0,10),Text=cancel?"The registration and its financial history remain searchable.":
            "Only this placement closes. Earlier placements, annual fees and receipts stay recorded. Review shows the exact monthly fee waiver and any advance or refund before you apply."};
        root.Controls.Add(explanation,0,4);
        root.SizeChanged+=(_,_)=>explanation.MaximumSize=new Size(Math.Max(1,root.ClientSize.Width-root.Padding.Horizontal-explanation.Margin.Horizontal),0);
        var buttons=HumanUi.ButtonRow(review,apply,close);buttons.Dock=DockStyle.Fill;
        root.Controls.Add(impact,0,5);root.Controls.Add(buttons,0,6);Controls.Add(root);apply.Enabled=false;
        date.ValueChanged+=(_,_)=>Reset();reason.TextChanged+=(_,_)=>Reset();waiveFrom.ValueChanged+=(_,_)=>Reset();
        monthlyPolicy.SelectedIndexChanged+=(_,_)=>{
            bool selectedMonth=monthlyPolicy.SelectedIndex==2;
            waiveFrom.Enabled=selectedMonth;monthField.Visible=selectedMonth;
            choices.RowStyles[1].Height=selectedMonth?AppMetrics.FieldRowHeight:0;
            paidReduction.Enabled=monthlyPolicy.SelectedIndex!=1;Reset();
        };paidReduction.SelectedIndexChanged+=(_,_)=>Reset();
        review.Click+=async(_,_)=>await ReviewAsync();apply.Click+=async(_,_)=>await ApplyAsync();
        FormClosing+=(_,e)=>{if(busy&&!Changed)e.Cancel=true;};
    }
    private string Policy=>monthlyPolicy.SelectedIndex switch {1=>"KeepIssued",2=>"FromMonth",_=>"SchoolCutoff"};
    private Task<DataTable> Run(bool preview)=>query("CloseStudentAdmission",new[]{
        DatabaseHelper.Int("@AdmissionId",admission),DatabaseHelper.Date("@LeavingDate",date.Value.Date),DatabaseHelper.P("@Reason",reason.Text.Trim()),
        DatabaseHelper.Bit("@Cancel",cancel),DatabaseHelper.P("@MonthlyFeePolicy",Policy),
        DatabaseHelper.P("@ReverseFromMonth",Policy=="FromMonth"?new DateTime(waiveFrom.Value.Year,waiveFrom.Value.Month,1):null),
        DatabaseHelper.P("@PaidReductionAction",paidReduction.SelectedIndex==1?"Refund":"Credit"),DatabaseHelper.Bit("@Preview",preview),
        DatabaseHelper.P("@ExpectedSnapshot",preview?null:Convert.ToString(plan?["Snapshot"]))});
    private async Task ReviewAsync()
    {
        if(busy)return;Reset();int generation=reviewGeneration;busy=true;root.Enabled=false;
        impact.Text="Reviewing the placement and financial effect…";
        try
        {
            var result=await Run(true);if(IsDisposed||generation!=reviewGeneration)return;
            plan=result.Rows[0];
            if(cancel)impact.Text=$"Cancel this registration and reverse {plan["ChargeCount"]} unpaid charge(s). Outstanding: Rs. {Convert.ToDecimal(plan["Outstanding"]):N0}.\r\nHistory remains searchable; siblings are unchanged.";
            else
            {
                decimal outstanding=Convert.ToDecimal(plan["Outstanding"]),reduction=Convert.ToDecimal(plan["FutureAmount"]),paid=Convert.ToDecimal(plan["PaidReturnAmount"]);
                string policy=Policy=="KeepIssued"?"Keep all issued monthly fees collectible.":
                    Policy=="FromMonth"?$"Waive monthly fees from {waiveFrom.Value:MMM yyyy}.":"Apply the school cutoff to this departure.";
                if(Policy=="SchoolCutoff"&&plan.Table.Columns.Contains("ReverseFromMonth")&&plan["ReverseFromMonth"]!=DBNull.Value)
                    policy=$"School cutoff: waive monthly fees from {Convert.ToDateTime(plan["ReverseFromMonth"]):MMM yyyy}.";
                impact.Text=$"Close this placement on {date.Value:dd MMM yyyy}. Future automatic posting stops.\r\n{policy}\r\n\r\n"+
                    $"Current outstanding on this placement: Rs. {outstanding:N0}\r\nMonthly bill(s) reduced: {plan["FutureCount"]}\r\nUnpaid amount waived: Rs. {reduction:N0}\r\n"+
                    $"Paid amount released as {(paidReduction.SelectedIndex==1?"refund":"credit")}: Rs. {paid:N0}\r\nRemaining outstanding on this placement: Rs. {outstanding-reduction:N0}\r\n\r\n"+
                    "Earlier placement dues, annual/one-time fees and original receipts remain. No payment or leave certificate is required to record departure.";
            }
            apply.Enabled=Convert.ToInt32(plan["BlockedCount"])==0;
        }
        catch(Exception ex){impact.Text=DbMessage.UserMessage(ex);}
        finally{busy=false;if(!IsDisposed)root.Enabled=true;}
    }
    private async Task ApplyAsync()
    {
        if(busy||plan is null)return;busy=true;root.Enabled=false;impact.Text="Saving the reviewed departure…";
        try{await Run(false);Changed=true;StudentSearchCache.Invalidate();DialogResult=DialogResult.OK;Close();}
        catch(Exception ex){impact.Text="Check the student's current status and account before repeating a departure after a connection failure.\r\n"+DbMessage.UserMessage(ex);Reset();}
        finally{busy=false;if(!IsDisposed)root.Enabled=true;}
    }
    private void Reset(){reviewGeneration++;plan=null;apply.Enabled=false;}
}

internal sealed class CompleteAdmissionDialog : ApplicationWindowForm
{
    internal string AdmissionNumber { get; private set; } = "";
    internal CompleteAdmissionDialog(string suggested, string student)
    {
        Text = "Confirm Admission";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(AppScale.Scale(600), AppScale.Scale(450));
        MinimumSize = Size;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        var number = HumanUi.TextBox(false, 40);
        number.Name = "OfficialAdmissionNumber";
        number.Text = suggested;
        number.Font = AppTheme.BodyBoldFont;
        number.SelectAllOnFocus = true;
        UiAccessibility.Describe(number, "Official admission number",
            "Review or change this number. It is assigned when Save & Confirm succeeds.");
        var error = new Label {
            Name = "NumberValidation", AutoSize = true, Dock = DockStyle.Top,
            ForeColor = AppTheme.Danger, Margin = new Padding(0, 6, 0, 0)
        };
        var ok = new ModernButton { Name = "SaveAndConfirm", Text = "Save & Confirm", ThemeRole = "Success" };
        var cancel = new ModernButton { Text = "Go Back", ThemeRole = "Secondary", DialogResult = DialogResult.Cancel };
        CancelButton = cancel;
        AcceptButton = ok;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(22), AutoScroll = true
        };
        for (int row = 0; row < 4; row++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label {
            Text = "Review and confirm admission", AutoSize = true, Dock = DockStyle.Top,
            Font = AppTheme.BodyBoldFont, ForeColor = AppTheme.Text, Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        root.Controls.Add(new Label {
            Name = "StudentName", Text = student, AutoSize = true, Dock = DockStyle.Top,
            ForeColor = AppTheme.Text, Margin = new Padding(0, 0, 0, 16)
        }, 0, 1);
        var numberCard = new RoundedPanel {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14),
            BackColor = AppTheme.Surface, BorderColor = AppTheme.Border, Margin = Padding.Empty
        };
        var numberLayout = new TableLayoutPanel {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        numberLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        numberLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        numberLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        numberLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        numberLayout.Controls.Add(HumanUi.Field("Official admission number", number, true), 0, 0);
        numberLayout.Controls.Add(new Label {
            Text = "Suggested number · You may change it before confirming.", AutoSize = true,
            Dock = DockStyle.Top, ForeColor = AppTheme.MutedText, Margin = new Padding(0, 6, 0, 0)
        }, 0, 1);
        numberLayout.Controls.Add(error, 0, 2);
        numberCard.Controls.Add(numberLayout);
        root.Controls.Add(numberCard, 0, 2);
        root.Controls.Add(new Label {
            Name = "CompletionEffect",
            Text = "Save & Confirm saves your completed details and assigns this admission number together.\nExisting bills, payments and family links stay with this student.",
            AutoSize = true, Dock = DockStyle.Top, ForeColor = AppTheme.MutedText,
            Margin = new Padding(0, 16, 0, 0), MaximumSize = new Size(AppScale.Scale(556), 0)
        }, 0, 3);
        root.SizeChanged += (_, _) => {
            foreach (Label label in root.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal - label.Margin.Horizontal), 0);
        };
        var buttons = HumanUi.ButtonRow(ok, cancel);
        buttons.Dock = DockStyle.Fill;
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        number.TextChanged += (_, _) => { error.Text = ""; number.IsInvalid = false; };
        Shown += (_, _) => number.Focus();
        ok.Click += (_, _) => {
            if (!AdmissionNumbers.TryNormalize(number.Text, out string normalized))
            {
                error.Text = "Enter a valid number, for example 6590 or 6590a.";
                number.IsInvalid = true;
                number.Focus();
                return;
            }
            AdmissionNumber = normalized;
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}

internal sealed class LegacyGoLiveDialog : ApplicationWindowForm
{
    private bool busy;
    internal bool Changed {get;private set;}
    internal LegacyGoLiveDialog(Func<string,SqlParameter[],Task<DataTable>>? query = null)
    {
        query ??= (procedure, parameters) => DatabaseHelper.QueryUserAsync(procedure, parameters);
        FormClosing += (_, e) => { if (busy && !Changed) e.Cancel = true; };
        Text="Finish Legacy Setup / Go Live";Size=new Size(AppScale.Scale(780),AppScale.Scale(590));MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;
        var live=HumanUi.Date();var billing=HumanUi.Date();billing.Value=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);
        var placement=HumanUi.Date();placement.ShowCheckBox=true;placement.Checked=false;
        var impact=HumanUi.TextBox(true,0);impact.ReadOnly=true;impact.Dock=DockStyle.Fill;
        var preview=new ModernButton{Text="Preview Go Live",ThemeRole="Secondary"};var apply=new ModernButton{Text="Confirm & Go Live",ThemeRole="Success",Enabled=false};var close=new ModernButton{Text="Close",DialogResult=DialogResult.Cancel};CancelButton=close;DataRow? plan=null;int reviewGeneration=0;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(14)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));for(int i=0;i<3;i++)root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text="One-time activation of verified legacy students. New admissions are independent.\nKeep recorded placement dates, or explicitly choose a shared start date below.",AutoSize=true},0,0);
        root.Controls.Add(HumanUi.Field("Go-live date",live,true),0,1);root.Controls.Add(HumanUi.Field("Billing start month",billing,true),0,2);root.Controls.Add(HumanUi.Field("Replace active legacy placement start (optional)",placement),0,3);root.Controls.Add(impact,0,4);root.Controls.Add(HumanUi.ButtonRow(preview,apply,close),0,5);foreach(var row in root.Controls.OfType<TableLayoutPanel>().Where(row=>row.AutoSize))row.Dock=DockStyle.Fill;Controls.Add(root);
        void Reset(){reviewGeneration++;plan=null;apply.Enabled=false;}live.ValueChanged+=(_,_)=>Reset();billing.ValueChanged+=(_,_)=>Reset();placement.ValueChanged+=(_,_)=>Reset();
        Task<DataTable> Run(bool review)=>query("FinishLegacyAdmissionSetup",new[] {DatabaseHelper.Date("@GoLiveDate",live.Value.Date),DatabaseHelper.Date("@BillingStart",new DateTime(billing.Value.Year,billing.Value.Month,1)),DatabaseHelper.P("@PlacementStart",placement.Checked?placement.Value.Date:null),DatabaseHelper.Bit("@Preview",review),DatabaseHelper.P("@ExpectedSnapshot",review?null:Convert.ToString(plan?["Snapshot"])) });
        preview.Click+=async(_,_)=>{if(busy)return;Reset();int generation=reviewGeneration;busy=true;root.Enabled=false;impact.Text="Please wait for the operation to finish before closing this window.";try{var result=await Run(true);if(generation!=reviewGeneration)return;plan=result.Rows[0];impact.Text=$"Active students ready: {plan["ActiveStudents"]}\r\nUnverified active students: {plan["UnverifiedStudents"]}\r\nBlocking records / date or fee issues: {plan["BlockedCount"]}\r\n\r\nBilling starts {billing.Value:MMM yyyy}. Existing bills and payments remain intact.\r\n{(placement.Checked?"Placement starts will use the selected date.":"Recorded placement starts are preserved.")}\r\n\r\nResolve blockers in Data Review / Admission before continuing. This operation can run once.\r\n\r\n"+Convert.ToString(plan["ReviewDetails"]);apply.Enabled=Convert.ToInt32(plan["BlockedCount"])==0;}catch(Exception ex){impact.Text=DbMessage.UserMessage(ex);}finally{busy=false;root.Enabled=true;}};
        apply.Click+=async(_,_)=>{if(busy || plan is null)return;busy=true;root.Enabled=false;impact.Text="Please wait for the operation to finish before closing this window.";try{await Run(false);Changed=true;StudentSearchCache.Invalidate();DialogResult=DialogResult.OK;Close();}catch(Exception ex){impact.Text=DbMessage.UserMessage(ex);Reset();}finally{busy=false;root.Enabled=true;}};
    }
}
