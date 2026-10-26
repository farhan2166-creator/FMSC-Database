using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed partial class FeeCollectionPage
{
    private bool CanChangeFeeTerms=>counterIds is null && !IsBusy && !siblingBusy && CurrentUser.HasPermission("Fees.Adjust");
    private bool CanOpenFeeChange()
    {
        if(!CanChangeFeeTerms || !charges.EndEdit())return false;
        if(HasDraftBillDiscounts() || HasEnteredAmount(paymentAmount.Text) || !string.IsNullOrWhiteSpace(reference.Text) || !string.IsNullOrWhiteSpace(remarks.Text))
        {Warn("Finish or clear the current payment draft before changing fee terms. Your entered payment has been retained.");return false;}
        return true;
    }
    private async Task ChangeChargeConcessionAsync(long chargeId,int action)
    {
        if(!CanOpenFeeChange())return;
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Reviewing fee concession…",this);
            using var returnFocus=new DialogReturnFocus(this);
            if(action==0)await FeeConcessionActions.WaiveOnceAsync(chargeId);
            else
            {
                var bill=await Task.Run(()=>FeeDiscountPeriod.BillContext(chargeId));
                bool monthly=Convert.ToString(bill["Frequency"])=="Monthly";
                var context=monthly?await Task.Run(()=>FeeDiscountPeriod.Context(Convert.ToInt32(bill["StudentFeeAssignmentId"]))):bill;
                using var dialog=new ChangeFeeDiscountForm(Convert.ToInt32(bill["StudentFeeAssignmentId"]),Convert.ToString(bill["FeeTypeName"])??"Fee",
                    Convert.ToDecimal(bill["StandardAmount"]),Convert.ToDecimal(bill["DiscountAmount"]),context);
                dialog.ConfigureCollectionShortcut(action==2,Convert.ToDateTime(bill["BillingMonth"]));
                dialog.ShowDialog(FindForm());
                if(!dialog.Changed)return;
            }
            await ReloadFamilyChargesAsync(++loadVersion);
            RefreshEnteredPaymentAllocation();
            SetStatus(action==0?"The charge's remaining dues were discounted and saved. The fee plan is unchanged.":"Fee concession saved. Family balances have been refreshed.");
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
    }
    private async Task<DataTable> TransportAssignmentsAsync()=>await DatabaseHelper.QueryUserAsync("GetStudentFeeAssignments",DatabaseHelper.Int("@EnrollmentId",enrollmentId));
    private bool SelectedEnrollmentIsOpen=>enrollment.SelectedItem is DataRowView view && GetText(view.Row,"Status") is "Active" or "Pending";
    private async Task RefreshTransportMenuAsync(ToolStripItem start,ToolStripItem stop)
    {
        start.Enabled=stop.Enabled=false;
        if(admissionId<=0 || enrollmentId<=0 || !CanChangeFeeTerms || !SelectedEnrollmentIsOpen)return;
        int anchor=admissionId,placement=enrollmentId,version=loadVersion;
        try
        {
            using var rows=await TransportAssignmentsAsync();
            if(IsDisposed || anchor!=admissionId || placement!=enrollmentId || version!=loadVersion || !CanChangeFeeTerms)return;
            bool hasOpen=OpenTransportRows(rows).Any();start.Enabled=!hasOpen;stop.Enabled=hasOpen;
        }
        catch(Exception ex){AppExceptionHandler.LogNonFatal(ex,"Transport menu eligibility");}
    }
    private static IEnumerable<DataRow> OpenTransportRows(DataTable data)=>data.AsEnumerable().Where(row=>
        Convert.ToBoolean(row["IsTransportLinked"]) && row.IsNull("EffectiveTo"));
    private async Task OpenTransportAsync(bool start)
    {
        if(enrollmentId<=0 || !CanOpenFeeChange())return;
        if(!SelectedEnrollmentIsOpen){Info("Transport changes require the student's active or pending placement. Select that placement first.");return;}
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Reviewing transport…",this);
            using var returnFocus=new DialogReturnFocus(this);
            using var assignments=await TransportAssignmentsAsync();
            var open=OpenTransportRows(assignments).ToArray();
            bool changed;
            if(start)
            {
                if(open.Length>0){Info("Transport is already active. Use Stop Transport to change its stopping month.");return;}
                using var dialog=new AddFeeAssignmentForm(enrollmentId,transportOnly:true);
                changed=dialog.ShowDialog(FindForm())==DialogResult.OK;
            }
            else
            {
                if(open.Length==0){Info("No open transport fee plan is available to stop.");return;}
                if(open.Length>1)
                {
                    using var dialog=new FeeAssignmentsDialog(enrollmentId,admissionId,StudentNameFor(admissionId));
                    dialog.ShowDialog(FindForm());changed=dialog.ChangesMade;
                }
                else
                {
                    var row=open[0];
                    using var dialog=new DiscontinueFeeAssignmentForm(Convert.ToInt32(row["StudentFeeAssignmentId"]),Convert.ToString(row["FeeTypeName"])??"Transport",
                        row["EffectiveFrom"] is DateTime date?date:null);
                    changed=dialog.ShowDialog(FindForm())==DialogResult.OK;
                }
            }
            if(changed){await ReloadFamilyChargesAsync(++loadVersion);RefreshEnteredPaymentAllocation();SetStatus("Transport changes saved. Family fees have been refreshed.");}
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
    }
}
