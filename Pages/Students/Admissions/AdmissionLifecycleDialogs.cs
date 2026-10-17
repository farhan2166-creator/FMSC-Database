using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Admissions;

internal sealed class CloseAdmissionDialog : ApplicationWindowForm
{
    private readonly ModernDatePicker date=HumanUi.Date();
    private readonly ModernTextBox reason=HumanUi.TextBox(true,500),impact=HumanUi.TextBox(true,0);
    private readonly ModernButton review=new(){Text="Review",ThemeRole="Secondary"},apply=new(){Text="Apply",ThemeRole="Warning"};
    private readonly CheckBox reverseFuture=new(){Text="Reverse unpaid charges after the leaving month",AutoSize=true};
    private DataRow? plan;
    private bool busy;
    private int reviewGeneration;
    internal bool Changed {get;private set;}
    internal CloseAdmissionDialog(int admission,string student,bool cancel, Func<string,SqlParameter[],Task<DataTable>>? query = null)
    {
        query ??= (procedure, parameters) => DatabaseHelper.QueryUserAsync(procedure, parameters);
        FormClosing += (_, e) => { if (busy && !Changed) e.Cancel = true; };
        Text=cancel?"Cancel Registration":"Mark as Left";StartPosition=FormStartPosition.CenterParent;Size=new Size(AppScale.Scale(720),AppScale.Scale(530));MinimumSize=Size;
        impact.ReadOnly=true;impact.Dock=DockStyle.Fill;
        var close=new ModernButton{Text="Close",DialogResult=DialogResult.Cancel};CancelButton=close;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(14)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight*2));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text=student,AutoSize=true},0,0);root.Controls.Add(HumanUi.Field(cancel?"Cancellation date":"Leaving date",date,true),0,1);root.Controls.Add(HumanUi.Field("Reason",reason,true),0,2);reverseFuture.Visible=!cancel;root.Controls.Add(reverseFuture,0,3);root.Controls.Add(impact,0,4);root.Controls.Add(HumanUi.ButtonRow(review,apply,close),0,5);foreach(var row in root.Controls.OfType<TableLayoutPanel>().Where(row=>row.AutoSize))row.Dock=DockStyle.Fill;Controls.Add(root);apply.Enabled=false;
        date.ValueChanged+=(_,_)=>Reset();reason.TextChanged+=(_,_)=>Reset();reverseFuture.CheckedChanged+=(_,_)=>Reset();
        async Task<DataTable> Run(bool preview)=>await query("CloseStudentAdmission",new[] {DatabaseHelper.Int("@AdmissionId",admission),DatabaseHelper.Date("@LeavingDate",date.Value.Date),DatabaseHelper.P("@Reason",reason.Text.Trim()),DatabaseHelper.Bit("@Cancel",cancel),DatabaseHelper.Bit("@ReverseFutureUnpaid",reverseFuture.Checked),DatabaseHelper.Bit("@Preview",preview),DatabaseHelper.P("@ExpectedSnapshot",preview?null:Convert.ToString(plan?["Snapshot"])) });
        review.Click+=async(_,_)=>{if(busy)return;Reset();int generation=reviewGeneration;busy=true;root.Enabled=false;impact.Text="Please wait for the operation to finish before closing this window.";try{var result=await Run(true);if(generation!=reviewGeneration)return;plan=result.Rows[0];impact.Text=cancel?$"Cancel this record and reverse {plan["ChargeCount"]} unpaid charge(s). Outstanding: Rs. {Convert.ToDecimal(plan["Outstanding"]):N0}.\r\nThe registration and its history remain searchable. Siblings are unchanged.":$"Close this placement on {date.Value:dd MMM yyyy}. Future fee posting stops.\r\nOutstanding Rs. {Convert.ToDecimal(plan["Outstanding"]):N0} remains collectible. Review eligible unpaid charges in Fee Collection if they should be reversed.\r\nNo payment or leave certificate is required.\r\n\r\nCharges after leaving month: {plan["FutureCount"]} · Rs. {Convert.ToDecimal(plan["FutureAmount"]):N0}.\r\n{(Convert.ToInt32(plan["FuturePaid"])>0?"Paid future charges must be reviewed in Fee Corrections.":reverseFuture.Checked?"Listed unpaid future charges will be reversed with this closure.":"Choose reversal above if future unpaid charges should be cancelled.")}";apply.Enabled=Convert.ToInt32(plan["BlockedCount"])==0;}catch(Exception ex){impact.Text=DbMessage.UserMessage(ex);}finally{busy=false;root.Enabled=true;}};
        apply.Click+=async(_,_)=>{if(busy || plan is null)return;busy=true;root.Enabled=false;impact.Text="Please wait for the operation to finish before closing this window.";try{await Run(false);Changed=true;StudentSearchCache.Invalidate();DialogResult=DialogResult.OK;Close();}catch(Exception ex){impact.Text=DbMessage.UserMessage(ex);Reset();}finally{busy=false;root.Enabled=true;}};
    }
    private void Reset(){reviewGeneration++;plan=null;apply.Enabled=false;}
}

internal sealed class CompleteAdmissionDialog : ApplicationWindowForm
{
    internal string AdmissionNumber {get;private set;}="";
    internal CompleteAdmissionDialog(string suggested,string student)
    {
        Text="Confirm Admission";StartPosition=FormStartPosition.CenterParent;Size=new Size(AppScale.Scale(540),AppScale.Scale(260));MinimumSize=Size;
        var number=HumanUi.TextBox(false,40);number.Text=suggested;
        var ok=new ModernButton{Text="Confirm Admission",ThemeRole="Success"};var cancel=new ModernButton{Text="Cancel",DialogResult=DialogResult.Cancel};CancelButton=cancel;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(14)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text=student+"\nThe existing bill, payments and family links will be retained.",AutoSize=true},0,0);root.Controls.Add(HumanUi.Field("Official admission number",number,true),0,1);root.Controls.Add(HumanUi.ButtonRow(ok,cancel),0,2);foreach(var row in root.Controls.OfType<TableLayoutPanel>().Where(row=>row.AutoSize))row.Dock=DockStyle.Fill;Controls.Add(root);
        ok.Click+=(_,_)=>{if(!AdmissionNumbers.TryNormalize(number.Text,out string normalized)){number.Focus();return;}AdmissionNumber=normalized;DialogResult=DialogResult.OK;Close();};
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
