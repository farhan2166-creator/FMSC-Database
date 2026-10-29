namespace FMSC_SMS.Core;

internal static class OutstandingBillsPreview
{
    internal static async Task ShowAsync(Form? owner,IReadOnlyList<int> students,Func<bool> isCurrent,Action<string> notify,bool a4=false,
        bool byFamily=false)
    {
        int user=CurrentUser.UserId;
        bool Current()=>owner?.IsDisposed!=true && CurrentUser.UserId==user
            && CurrentUser.HasPermission("Fees.View") && isCurrent();
        if(!Current())return;
        var institution=await InstitutionProfile.GetPrintIdentityAsync();
        if(!Current())return;
        if(string.IsNullOrWhiteSpace(institution.Name)){notify("Check School Profile before printing bills.");return;}
        var accounts=new List<FeeBillSelection.Student>();
        foreach(int id in students.Distinct())
        {
            using var members=await FeeBillPreview.ReadMembersAsync(id,"Fees.View");
            if(!Current())return;
            using var charges=await FeeBillPreview.ReadChargesAsync(id,new[]{id});
            if(!Current())return;
            var member=members.AsEnumerable().First(row=>Convert.ToInt32(row["AdmissionId"])==id);
            accounts.Add(new(id,member.IsNull("FamilyId")?null:Convert.ToInt32(member["FamilyId"]),
                AdmissionNumbers.ForId(id),Convert.ToString(member["StudentName"])??"",
                charges.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Due"])),FamilyBillingData.Readiness(member),
                FamilyBillingData.IncompleteNotice(members,new[]{id})));
        }
        if(!accounts.Any(s=>s.Due>0m)){notify("There are no posted outstanding charges for these students.");return;}
        var selected=accounts.ToArray();
        string notice=string.Join("\n",selected.Where(s=>s.Notice.Length>0).Select(s=>s.Notice));
        if(notice.Length>0&&!ModernMessageDialog.Confirm(owner,notice+"\n\nPrint posted outstanding charges only?","Billing Readiness",defaultYes:false))return;
        if(!Current())return;
        var groups=FeeBillSelection.Groups(selected,byFamily);
        var savedBills=new List<DataTable>();string? failure=null;
        try
        {
            var batch=await FeeBillBatch.PrepareAsync(groups,Current,async ids=> {
                    using var members=await FeeBillPreview.ReadMembersAsync(ids[0],"Fees.View");
                    if(!Current())throw new OperationCanceledException("The selected account changed. Preparation stopped.");
                    foreach(int id in ids)
                    {
                        var member=members.AsEnumerable().FirstOrDefault(row=>Convert.ToInt32(row["AdmissionId"])==id);
                        var reviewed=selected.Single(s=>s.Id==id);
                        if(member is null || (member.IsNull("FamilyId")?null:(int?)Convert.ToInt32(member["FamilyId"]))!=reviewed.FamilyId
                            || FamilyBillingData.Readiness(member)!=reviewed.Readiness)
                            throw new InvalidOperationException("Family membership or billing readiness changed. Review the remaining students again.");
                    }
                    using var charges=await FeeBillPreview.ReadChargesAsync(ids[0],ids);
                    if(!Current())throw new OperationCanceledException("The selected account changed. Preparation stopped.");
                    if(ids.Any(id=>charges.AsEnumerable().Where(r=>Convert.ToInt32(r["AdmissionId"])==id).Sum(r=>Convert.ToDecimal(r["Due"]))!=selected.Single(s=>s.Id==id).Due))
                        throw new InvalidOperationException("Outstanding amounts changed. Review the remaining students again.");
                    string parent=Convert.ToString(members.AsEnumerable().First(r=>Convert.ToInt32(r["AdmissionId"])==ids[0])["FatherName"])??"";
                    using var data=ids.Length>1?FamilyBillReport.BuildData(institution.Name,institution.ContactNumber,institution.Address,parent,charges)
                        :StudentBillReport.BuildData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
                    return await FeeBillDocuments.IssueAsync(data,ids[0],ids);
            });
            savedBills.AddRange(batch.Saved);failure=batch.Error;
            if(!Current())return;
            if(failure is not null)notify($"{savedBills.Count} of {groups.Length} bills confirmed. Preparation stopped: {failure}\nSaved bills remain in Issued Fee Bills. If the connection failed, check that history before retrying; the last bill may also have been saved.");
            if(savedBills.Count==0||!Current())return;
            using var combined=CombineSaved(savedBills,byFamily);
            if(byFamily)FamilyBillReport.ShowData(owner,combined);
            else StudentBillReport.ShowData(owner,combined,a4);
        }
        finally{foreach(var saved in savedBills)saved.Dispose();}
    }

    // Merge saved documents for one preview; each original bill keeps its number and page key.
    internal static DataTable CombineSaved(IEnumerable<DataTable> bills,bool byFamily)
    {
        var combined=new DataTable();int page=0;
        foreach(var bill in bills)
        {
            using var data=FeeBillDocuments.ReadSavedData(bill.Rows[0]);page++;
            if(byFamily&&!data.Columns.Contains("FamilyRowKind"))
            {
                data.Columns.Add("FamilyRowKind");data.Columns.Add("FamilyAmountText");
                data.Columns.Add("BillStudentKey");data.Columns.Add("BillStudentName");
                FamilyFeeDocumentPresentation.AddChildColumns(data);
                foreach(DataRow row in data.Rows)
                {
                    // These columns were added to existing rows, so fill their
                    // defaults explicitly before the family layout reads them.
                    foreach(DataColumn column in data.Columns)
                        if(column.ColumnName.StartsWith("Child",StringComparison.Ordinal)&&row.IsNull(column))row[column]=column.DefaultValue;
                    row["FamilyRowKind"]="Fee";row["FamilyAmountText"]=Convert.ToDecimal(row["Amount"]).ToString("N0");
                    row["BillStudentKey"]=row["AdmissionNumberText"];
                    row["BillStudentName"]=$"{row["AdmissionNumberText"]} · {row["StudentName"]}";
                    row["ChildFatherName"]=row["FatherName"];row["ChildCampusName"]=row["CampusName"];
                    row["ChildClassSection"]=row["ClassSection"];
                }
            }
            foreach(DataRow row in data.Rows)row["ReceiptPageKey"]=page;
            combined.Merge(data,false,MissingSchemaAction.Add);
        }
        return combined;
    }
}
