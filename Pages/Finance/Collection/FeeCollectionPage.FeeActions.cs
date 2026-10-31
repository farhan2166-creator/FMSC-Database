using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed partial class FeeCollectionPage
{
    private enum ChargeConcessionAction { Discount=0, Waive=1, CorrectAmount=2, RemoveDiscount=3 }
    private static bool IsWaiverReviewRow(DataGridViewRow row)=>row.Cells["PendingWaiver"].Value is true;
    private bool HasPendingWaiverRows()=>charges.Rows.Cast<DataGridViewRow>().Any(row=>!row.IsNewRow&&IsWaiverReviewRow(row));
    private bool HasCollectionDraft()=>HasDraftBillDiscounts()||HasEnteredAmount(paymentAmount.Text)
        ||!string.IsNullOrWhiteSpace(reference.Text)||!string.IsNullOrWhiteSpace(remarks.Text);
    private static void ApplyWaiverReviewState(DataGridViewRow row,DataRow source)
    {
        bool waived=CollectionChargeReview.IsPendingWaiver(source);
        row.Cells["PendingWaiver"].Value=waived;
        if(!waived)return;
        string state=Convert.ToDecimal(source["PaidAmount"])>0m?"Paid + waived":"Waived";
        row.Cells["FeeTypeName"].Value+=$" — {state}";
        row.Cells["FeeTypeName"].ToolTipText="Saved waiver awaiting a receipt or fee-change acknowledgement. No payment is due on this fee.";
        row.Cells["Pay"]=new DataGridViewTextBoxCell{Value="—"};
        row.Cells["Amount"].Value=0m;
        row.ReadOnly=true;
        row.DefaultCellStyle.ForeColor=AppTheme.MutedText;
        row.Tag=false;
    }

    private bool CanChangeFeeTerms=>counterIds is null && !IsBusy && !siblingBusy && CurrentUser.HasPermission("Fees.Adjust");
    private async Task CheckFeeChangeSaveAsync()
    {
        if(!CanOpenFeeChange())return;
        billBusy=true;
        try
        {
            using var returnFocus=new DialogReturnFocus(this);
            using var dialog=new ChangeFeeDiscountForm(0,"Fee Change Recovery",0m,0m);
            dialog.ConfigureRecoveryOnly();dialog.ShowDialog(FindForm());
            if(dialog.Changed&&admissionId>0)await ReloadFamilyChargesAsync(++loadVersion);
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
    }
    private bool CanOpenFeeChange()
    {
        if(!CanChangeFeeTerms || !charges.EndEdit())return false;
        if(HasCollectionDraft())
        {Warn("Finish or clear the current payment draft before changing fee terms. Your entered payment has been retained.");return false;}
        return true;
    }
    private async Task ChangeChargeConcessionAsync(long chargeId,ChargeConcessionAction action)
    {
        if(!CanOpenFeeChange())return;
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Reviewing fee concession…",this);
            using var returnFocus=new DialogReturnFocus(this);
            {
                var bill=await Task.Run(()=>FeeDiscountPeriod.BillContext(chargeId));
                int assignmentId=Convert.ToInt32(bill["StudentFeeAssignmentId"]);
                var context=await Task.Run(()=>FeeDiscountPeriod.Context(assignmentId));
                using var dialog=CreateCollectionFeeDialog(assignmentId,context,action,bill["BillingMonth"] is DateTime month?month:null);
                dialog.ShowDialog(FindForm());
                if(!dialog.Changed)return;
            }
            await ReloadFamilyChargesAsync(++loadVersion);
            RefreshEnteredPaymentAllocation();
            SetStatus("Fee changes saved for the selected scope and period. Balances have been refreshed.");
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;}
    }
    private static ChangeFeeDiscountForm CreateCollectionFeeDialog(int assignmentId,DataRow context,ChargeConcessionAction action,DateTime? chargeMonth=null)
    {
        var dialog=new ChangeFeeDiscountForm(assignmentId,Convert.ToString(context["FeeTypeName"])??"Fee",
            Convert.ToDecimal(context["StandardAmount"]),Convert.ToDecimal(context["DiscountAmount"]),context);
        try
        {
            dialog.ConfigureCollectionAction((int)action,chargeMonth);
            return dialog;
        }
        catch { dialog.Dispose(); throw; }
    }
    private async Task<DataTable> TransportAssignmentsAsync()=>await DatabaseHelper.QueryUserAsync("GetStudentFeeAssignments",DatabaseHelper.Int("@EnrollmentId",enrollmentId));
    private bool SelectedEnrollmentIsOpen=>enrollment.SelectedItem is DataRowView view && GetText(view.Row,"Status")=="Active";
    private bool transportStarts=true,transportKnown;
    private void UpdateTransportButton()
    {
        if(transportButton is null)return;
        transportButton.Text=transportStarts?"Start Transport":"Stop Transport";
        transportButton.Enabled=transportKnown&&CanChangeFeeTerms&&SelectedEnrollmentIsOpen;
        transportButton.Width=TextRenderer.MeasureText(transportButton.Text,transportButton.Font).Width+AppScale.Scale(24);
    }
    private async Task RefreshTransportButtonAsync()
    {
        transportKnown=false;UpdateTransportButton();
        if(!initialize||admissionId<=0||enrollmentId<=0||!CurrentUser.HasPermission("Fees.Adjust")||!SelectedEnrollmentIsOpen)return;
        int anchor=admissionId,placement=enrollmentId,version=loadVersion;
        try
        {
            using var rows=await TransportAssignmentsAsync();
            if(IsDisposed || anchor!=admissionId || placement!=enrollmentId || version!=loadVersion)return;
            transportStarts=!OpenTransportRows(rows).Any();transportKnown=true;UpdateTransportButton();
        }
        catch(Exception ex){AppExceptionHandler.LogNonFatal(ex,"Transport menu eligibility");}
    }
    private static IEnumerable<DataRow> OpenTransportRows(DataTable data)=>data.AsEnumerable().Where(row=>
        Convert.ToBoolean(row["IsTransportLinked"]) && row.IsNull("EffectiveTo"));
    private async Task OpenTransportAsync(bool start)
    {
        if(enrollmentId<=0 || !CanOpenFeeChange())return;
        if(!SelectedEnrollmentIsOpen){Info("Transport changes require completed admission and an active placement. Edit a pending registration's transport fee in its registration fee plan.");return;}
        billBusy=true;
        try
        {
            using var busy=BeginBusy("Reviewing transport…",this);
            using var returnFocus=new DialogReturnFocus(this);
            using var assignments=await TransportAssignmentsAsync();
            var open=OpenTransportRows(assignments).ToArray();
            if(start&&open.Length>0){Info("Transport is already active. Reload the fees to stop it.");return;}
            if(!start&&open.Length==0){Info("No open transport fee plan is available to stop. Reload the fees to start it.");return;}
            using var dialog=new TransportServiceDialog(enrollmentId,start);
            bool changed=dialog.ShowDialog(FindForm())==DialogResult.OK;
            if(changed){await ReloadFamilyChargesAsync(++loadVersion);RefreshEnteredPaymentAllocation();SetStatus("Transport changes saved. Family fees have been refreshed.");}
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{billBusy=false;await RefreshTransportButtonAsync();UpdateTransportButton();}
    }
}
