using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Collection;

// The quick counter uses the established collection writer, allocation grid and receipts.
public sealed partial class FeeCollectionPage
{
    private int[]? counterIds;
    private bool counterLoading,counterReady;
    private HumanCard? counterContextCard,counterSiblingCard;
    private TableLayoutPanel? counterSiblingPanel;
    private ModernButton? counterReverseButton;
    internal Dictionary<int,string> CounterReceipts {get;}=new();
    public bool HasUnsavedChanges=>counterIds is not null &&
        (HasEnteredAmount(paymentAmount.Text) || HasDraftBillDiscounts() ||
         !string.IsNullOrWhiteSpace(reference.Text) || !string.IsNullOrWhiteSpace(remarks.Text));
    public string UnsavedChangesMessage=>"Return to the family and discard these unrecorded payment entries? Recorded payments and receipts remain saved.";

    internal void ConfigureCounter(int[] selected)
    {
        if(counterIds is not null || selected.Length==0 || selected.Any(id=>id<=0) || selected.Distinct().Count()!=selected.Length)
            throw new ArgumentException("Select the saved children for this counter visit.",nameof(selected));
        counterIds=selected.ToArray();counterLoading=true;
        counterContextCard?.SetHeaderText("Selected Children","Close to return to this family's preparation and fee review.");
        counterSiblingCard?.SetHeaderText(counterIds.Length>1?"Payment Split":"Paying For",
            counterIds.Length>1?"Edit each child's Pay.":"Selected child for this visit.");
        foreach(var button in new[]{studentFeesMenuButton,addChargeButton,studentCreditButton,counterReverseButton})
            if(button is not null)button.Visible=false;
        selectAllSiblings.Visible=false;
        if(counterIds.Length>1 && counterSiblingPanel is not null)
        {
            if(counterSiblingCard?.Parent is TableLayoutPanel work)
            {work.ColumnStyles[0].Width=32;work.ColumnStyles[1].Width=68;}
            primaryPayer.Visible=siblings.Visible=siblingHint.Visible=false;
            for(int i=0;i<4;i++){counterSiblingPanel.RowStyles[i].SizeType=SizeType.Absolute;counterSiblingPanel.RowStyles[i].Height=0;}
            foreach(string column in new[]{"Child","Outstanding","Proposed"})
            {
                familyProposal.Columns[column].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
                familyProposal.Columns[column].MinimumWidth=90;
                familyProposal.Columns[column].FillWeight=column=="Child"?2:1;
            }
        }
        LockCounterContext();UpdateProcessCollectionAvailability();
    }

    private void LockCounterContext()
    {
        if(counterIds is null)return;
        student.Enabled=enrollment.Enabled=siblings.Enabled=selectAllSiblings.Enabled=false;
        if(nextStudentButton is not null)
        {
            nextStudentButton.Visible=false;
            // Hiding a control does not release an absolute TableLayoutPanel row.
            // This visit returns to the family, so reserve no Next Student space.
            if(nextStudentButton.Parent is TableLayoutPanel layout)
            {
                int row=layout.GetRow(nextStudentButton);
                if(row>=0 && row<layout.RowStyles.Count)
                {
                    layout.RowStyles[row].SizeType=SizeType.Absolute;
                    layout.RowStyles[row].Height=0;
                }
                // Use the actual caption, input and padding height for this
                // compact dialog, rather than the general form's extra row gap.
                static int FieldHeight(TableLayoutPanel field)
                {
                    var padding=field.Padding;
                    field.Padding=new Padding(padding.Left,padding.Top,padding.Right,
                        Math.Min(padding.Bottom,AppScale.Scale(UiDesignSystem.Spacing.Xs)));
                    return (int)Math.Ceiling(field.RowStyles[0].Height) + AppMetrics.InputControlHeight +
                        field.Padding.Vertical + field.Margin.Vertical;
                }
                foreach(int fieldRow in new[]{1,4,9})
                    if(layout.GetControlFromPosition(0,fieldRow) is TableLayoutPanel field)
                        layout.RowStyles[fieldRow].Height=FieldHeight(field);
                if(layout.GetControlFromPosition(0,8) is TableLayoutPanel details)
                    layout.RowStyles[8].Height=details.Controls.OfType<TableLayoutPanel>().Max(FieldHeight);
                foreach(int actionRow in new[]{10,11})
                    layout.RowStyles[actionRow].Height=AppMetrics.ButtonHeight+executeButton!.Margin.Vertical;
            }
        }
    }

    internal async Task InitializeCounterAsync()
    {
        if(counterIds is null)return;
        counterLoading=true;counterReady=false;
        try
        {
            using var hold=BeginBusy("Loading selected children…",this);
            await LoadStudentByAdmissionId(counterIds[0]);
            if(IsDisposed)return;
            if(admissionId!=counterIds[0] || student.SelectedId!=counterIds[0] || enrollmentId<=0)
                throw new InvalidOperationException("The selected child's account could not load. Close and reopen payment.");
            await RestoreCounterSelectionAsync();
            SetStatus(HasOutstandingCharges()
                ?"Enter the amount received. Review or change each child's suggested Pay amount, then Collect Payment and Print Receipt."
                :"No posted fees remain to collect for these children. Close to return to the family; a waiver is not a payment.");
        }
        catch(Exception ex){counterReady=false;charges.Rows.Clear();if(!IsDisposed)Fail(ex);}
        finally
        {
            counterLoading=false;
            if(!IsDisposed){LockCounterContext();UpdateProcessCollectionAvailability();}
        }
    }

    private async Task<DataTable> ReadCounterMembersAsync()
    {
        var members=await FeeBillPreview.ReadMembersAsync(counterIds![0],"Fees.View");
        try
        {
            FamilyBillingData.SelectMembers(members,counterIds);
            foreach(var row in members.AsEnumerable().Where(r=>counterIds.Contains(Convert.ToInt32(r["AdmissionId"]))))
                if(!FamilyBillingData.CanCollectPostedFees(row))
                    throw new InvalidOperationException("A selected child's return or billing readiness changed. Return to the family and review.");
            return members;
        }
        catch{members.Dispose();throw;}
    }

    private async Task RestoreCounterSelectionAsync()
    {
        if(counterIds is null)return;
        counterReady=false;
        using var members=await ReadCounterMembersAsync();
        charges.Rows.Clear();availableCreditByStudent.Clear();
        foreach(int id in counterIds)
        {
            var row=members.AsEnumerable().Single(r=>Convert.ToInt32(r["AdmissionId"])==id);
            int memberEnrollment=Convert.ToInt32(row["EnrollmentId"]);
            if(id!=counterIds[0])payingFor[id]=(memberEnrollment,Convert.ToString(row["StudentName"])??"");
            await AppendChargesAsync(id,memberEnrollment,expectedPrimaryAdmissionId:counterIds[0],expectedPrimaryEnrollmentId:enrollmentId);
        }
        suppressSiblingEvents=true;
        try
        {
            foreach(int id in counterIds.Skip(1))
                if(!siblings.Items.Cast<object>().OfType<SiblingChoice>().Any(c=>c.AdmissionId==id))
                    siblings.Items.Add(new SiblingChoice(id,$"{AdmissionNumbers.ForId(id)} · {payingFor[id].Name}\nSelected for this visit"),true);
            for(int i=siblings.Items.Count-1;i>=0;i--)
                if(!counterIds.Contains(SiblingAdmissionIdAt(i)))siblings.Items.RemoveAt(i);
                else siblings.SetItemChecked(i,true);
        }
        finally{suppressSiblingEvents=false;}
        SetSiblingHint("");
        if(!transactionBusy)RefreshEnteredPaymentAllocation();
        UpdateCollectionTotals();
        counterReady=true;LockCounterContext();
    }

    private async Task<bool> ValidateCounterPaymentAsync()
    {
        if(counterLoading || !counterReady)return false;
        counterLoading=true;UpdateProcessCollectionAvailability();
        try
        {
            using var members=await ReadCounterMembersAsync();
            if(admissionId!=counterIds![0] || charges.Rows.Cast<DataGridViewRow>().Any(r=>!r.IsNewRow &&
                !counterIds.Contains(GetInt(r.Cells["AdmissionId"].Value))))
                throw new InvalidOperationException("The payment selection changed. Close and reopen payment from the family.");
            return true;
        }
        catch(Exception ex){counterReady=false;Fail(ex);return false;}
        finally{counterLoading=false;LockCounterContext();UpdateProcessCollectionAvailability();}
    }

    private void RememberCounterReceipt(DataTable result,string receipt)
    {
        if(counterIds is null)return;
        foreach(DataGridViewRow row in charges.Rows)
            if(!row.IsNewRow && (GetDecimal(row.Cells["Amount"].Value)>0 ||
                GetDecimal(row.Cells["AppliedDiscount"].Value)!=GetDecimal(row.Cells["GrossAmount"].Tag)))
                CounterReceipts[GetInt(row.Cells["AdmissionId"].Value)]=string.IsNullOrWhiteSpace(receipt)?"Check Payment History":receipt;
        if(result.Columns.Contains("AdmissionId"))
            foreach(DataRow row in result.Rows)
                if(counterIds.Contains(GetInt(row,"AdmissionId")))
                    CounterReceipts[GetInt(row,"AdmissionId")]=string.IsNullOrWhiteSpace(receipt)?"Check Payment History":receipt;
    }
}
