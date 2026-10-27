using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed partial class FeeCollectionPage
{
    private readonly Func<int,Task<DataTable>> readReturnMembers;
    private readonly Func<int,Task<bool>> confirmReturn;
    private readonly Func<int,Task> reloadReturnAccount;
    private ModernButton? confirmReturnButton;
    private int pendingReturnAdmissionId,pendingReturnEnrollmentId,returnLoadVersion;
    private readonly Dictionary<int,string> returnStatusByStudent=new();
    private bool CanReviewReturn => counterIds is null && admissionId>0 && !IsBusy && !siblingBusy
        && CurrentUser.HasPermission("Students.Administer") && CurrentUser.HasPermission("Fees.View");
    private bool CanConfirmSelectedReturn => CanReviewReturn && pendingReturnAdmissionId==admissionId
        && pendingReturnEnrollmentId>0;
    private bool ReturnActionAvailable => counterIds is null && admissionId>0 && pendingReturnAdmissionId==admissionId
        && pendingReturnEnrollmentId>0 && CurrentUser.HasPermission("Students.Administer") && CurrentUser.HasPermission("Fees.View");

    private void UpdateReturnAction()
    {
        if(confirmReturnButton is null)return;
        confirmReturnButton.Visible=ReturnActionAvailable;
        confirmReturnButton.Enabled=CanConfirmSelectedReturn;
        RefreshInlineAmounts();
    }
    private void ResetReturnAvailability()
    {
        returnLoadVersion++;
        pendingReturnAdmissionId=pendingReturnEnrollmentId=0;
        returnStatusByStudent.Clear();
        currentStudentAmounts.Invalidate();siblings.Invalidate();
        UpdateReturnAction();
    }
    private async Task RefreshReturnAvailabilityAsync()
    {
        ResetReturnAvailability();
        if(counterIds is not null || admissionId<=0 || !CurrentUser.HasPermission("Fees.View"))return;
        int anchor=admissionId,version=returnLoadVersion,actor=CurrentUser.UserId;
        try
        {
            using var rows=await readReturnMembers(anchor);
            if(IsDisposed || version!=returnLoadVersion || anchor!=admissionId || actor!=CurrentUser.UserId)return;
            foreach(DataRow row in rows.Rows)
            {
                string status=FamilyBillingData.Readiness(row);
                if(status=="Ready for Billing" && row.Table.Columns.Contains("ReturnConfirmedAt") && !row.IsNull("ReturnConfirmedAt"))
                    status="Return Confirmed";
                returnStatusByStudent[GetInt(row,"AdmissionId")]=status;
            }
            var pending=rows.AsEnumerable().FirstOrDefault(row=>GetInt(row,"AdmissionId")==anchor && FamilyBillingData.AwaitsReturn(row));
            if(pending is not null)
            {
                pendingReturnAdmissionId=anchor;
                pendingReturnEnrollmentId=GetInt(pending,"EnrollmentId");
            }
        }
        catch(Exception ex){AppExceptionHandler.LogNonFatal(ex,"Collection return eligibility");}
        finally
        {
            if(!IsDisposed){UpdateReturnAction();currentStudentAmounts.Invalidate();siblings.Invalidate();}
        }
    }
    private async Task ConfirmSelectedReturnAsync()
    {
        if(!CanConfirmSelectedReturn || !charges.EndEdit())return;
        if(HasDraftBillDiscounts() || HasEnteredAmount(paymentAmount.Text)
            || !string.IsNullOrWhiteSpace(reference.Text) || !string.IsNullOrWhiteSpace(remarks.Text))
        {
            Warn("Finish or clear the current payment draft before confirming return. Your entered payment has been retained.");
            return;
        }
        int anchor=admissionId,placement=pendingReturnEnrollmentId,actor=CurrentUser.UserId;
        billBusy=true;
        UpdateReturnAction();
        try
        {
            using var returnFocus=new DialogReturnFocus(this);
            using var busy=BeginBusy("Reviewing return admission…",this);
            // Recheck the promoted placement immediately before opening its existing dialog.
            using var rows=await readReturnMembers(anchor);
            if(IsDisposed || anchor!=admissionId || actor!=CurrentUser.UserId
                || !CurrentUser.HasPermission("Students.Administer") || !CurrentUser.HasPermission("Fees.View"))return;
            if(!rows.AsEnumerable().Any(row=>GetInt(row,"AdmissionId")==anchor && GetInt(row,"EnrollmentId")==placement
                && FamilyBillingData.AwaitsReturn(row)))
            {
                await reloadReturnAccount(anchor);
                SetStatus("This student is no longer awaiting that return. The account has been refreshed.");
                return;
            }
            bool confirmed;
            try{confirmed=await confirmReturn(anchor);}
            finally
            {
                // Also refresh after cancellation or an uncertain save, without submitting another return.
                if(!IsDisposed && anchor==admissionId && actor==CurrentUser.UserId)
                    await reloadReturnAccount(anchor);
            }
            if(!IsDisposed && anchor==admissionId)
                SetStatus(confirmed ? "Return confirmed. Opening charges are ready for fee collection."
                    : "Return review closed. The student's current fees have been refreshed.");
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally
        {
            billBusy=false;
            if(!IsDisposed){UpdateReturnAction();QueueLoadedAmountFocus(anchor);}
        }
    }
}
