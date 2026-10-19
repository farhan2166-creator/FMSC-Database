namespace FMSC_SMS.Core;

// A statement selection may contain unrelated students. Issue individual bills,
// retaining each bill's own number, and preview them together in the approved RDLC.
internal static class OutstandingBillsPreview
{
    internal static async Task ShowAsync(Form? owner,IReadOnlyList<int> students,Func<bool> isCurrent,Action<string> notify,bool a4=false)
    {
        int user=CurrentUser.UserId;
        bool Current()=>owner?.IsDisposed!=true && CurrentUser.UserId==user
            && CurrentUser.HasPermission("Fees.View") && isCurrent();
        if(!Current())return;
        var institution=await InstitutionProfile.GetPrintIdentityAsync();
        if(!Current())return;
        if(string.IsNullOrWhiteSpace(institution.Name)){notify("Check School Profile before printing bills.");return;}
        using var combined=FeeDocumentData.Create();
        int page=0;
        foreach(int id in students.Distinct())
        {
            using var members=await FeeBillPreview.ReadMembersAsync(id,"Fees.View");
            if(!Current())return;
            using var charges=await FeeBillPreview.ReadChargesAsync(id,new[]{id});
            if(!Current())return;
            if(charges.Rows.Count==0 || charges.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Due"]))<=0m)continue;
            string parent=Convert.ToString(members.AsEnumerable().First(row=>Convert.ToInt32(row["AdmissionId"])==id)["FatherName"])??"";
            using var data=StudentBillReport.BuildData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
            using var issued=await FeeBillDocuments.IssueAsync(data,id,new[]{id});
            if(!Current())return;
            using var saved=FeeBillDocuments.ReadSavedData(issued.Rows[0]);
            page++;
            foreach(DataRow row in saved.Rows){row["ReceiptPageKey"]=page;combined.ImportRow(row);}
        }
        if(!Current())return;
        if(combined.Rows.Count==0){notify("There are no posted outstanding charges for the selected students.");return;}
        StudentBillReport.ShowData(owner,combined,a4);
    }
}
