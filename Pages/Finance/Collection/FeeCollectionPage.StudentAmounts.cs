using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed partial class FeeCollectionPage
{
    private readonly FamilyStudentList currentStudentAmounts = new() {
        Dock=DockStyle.Fill,CompactRows=true,CheckBoxOnly=true,IntegralHeight=false,
        BorderStyle=BorderStyle.None,BackColor=AppTheme.Surface,Margin=Padding.Empty
    };
    private readonly Dictionary<int,decimal> siblingBaseOwed = new();

    private void ConfigureInlineAmounts()
    {
        currentStudentAmounts.ItemCheck+=(_,e)=>e.NewValue=CheckState.Checked;
        currentStudentAmounts.SetPaymentRows(_=>InlinePaymentInfo(admissionId));
        currentStudentAmounts.SetStatusRows(_=>returnStatusByStudent.GetValueOrDefault(admissionId,""));
        ((FamilyStudentList)siblings).SetPaymentRows(
        index=>InlinePaymentInfo(SiblingAdmissionIdAt(index)));
        ((FamilyStudentList)siblings).SetStatusRows(index=>returnStatusByStudent.GetValueOrDefault(SiblingAdmissionIdAt(index),""));
    }

    private void RefreshInlineAmounts()
    {
        if(counterIds is not null)return;
        if(admissionId>0)
        {
            string label=primaryPayer.Text.Replace("☑ ","");
            if(currentStudentAmounts.Items.Count==0)currentStudentAmounts.Items.Add(label,true);
            else if(!Equals(currentStudentAmounts.Items[0],label))currentStudentAmounts.Items[0]=label;
        }
        else currentStudentAmounts.Items.Clear();
        if(counterSiblingPanel is not null)
            counterSiblingPanel.RowStyles[0].Height=admissionId>0?currentStudentAmounts.GetItemRectangle(0).Height+
                (ReturnActionAvailable && confirmReturnButton is not null?confirmReturnButton.Height+AppScale.Scale(4):0):0;
        currentStudentAmounts.Invalidate();siblings.Invalidate();
    }

    private (decimal Total,decimal Pay) InlinePaymentInfo(int id)
    {
        var row=familyProposal.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(item=>GetInt(item.Cells["AdmissionId"].Value)==id);
        return (row is null?siblingBaseOwed.GetValueOrDefault(id):GetDecimal(row.Cells["Outstanding"].Value),
            row is null?0m:GetDecimal(row.Cells["Proposed"].Value));
    }
}
