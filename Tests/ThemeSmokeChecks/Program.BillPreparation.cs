using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckBillPreparationChoices()
    {
        CheckBulkBillReview();
        var accounts=new[]{
            new FeeBillSelection.Student(1,10,"101","Ali",100m,"Ready for Billing",""),
            new FeeBillSelection.Student(2,10,"102","Sara",200m,"Ready for Billing",""),
            new FeeBillSelection.Student(3,20,"103","Ahmed",300m,"Ready for Billing",""),
            new FeeBillSelection.Student(4,null,"104","Fatima",400m,"Ready for Billing",""),
            new FeeBillSelection.Student(5,10,"105","Unselected sibling",500m,"Ready for Billing",""),
            new FeeBillSelection.Student(6,10,"106","Settled sibling",0m,"Ready for Billing","")};
        var chosen=accounts.Where(s=>s.Id!=5).ToArray();
        Check(FeeBillSelection.Groups(chosen,false).Length==4,"Individual mode keeps four owing students separate");
        var grouped=FeeBillSelection.Groups(chosen,true);
        Check(grouped.Length==3&&grouped[0].SequenceEqual(new[]{1,2}),"Mixed selection produces one sibling bill and separate other accounts");
        Check(!grouped.SelectMany(g=>g).Contains(5)&&!grouped.SelectMany(g=>g).Contains(6),"No unselected or settled siblings are silently billed");
        Check(FeeBillSelection.Groups(accounts.Where(s=>s.Id==1||s.Id==5),true).Single().SequenceEqual(new[]{1,5}),"Only explicitly supplied members join a family bill");
        int attempted=0;
        var task=FeeBillBatch.PrepareAsync(grouped,()=>true,ids=>{
            attempted++;if(attempted==2)throw new InvalidOperationException("Simulated connection failure");
            var table=new DataTable();table.Columns.Add("BillNumber");table.Rows.Add("FB-TEST");return Task.FromResult(table);
        });
        var batch=task.GetAwaiter().GetResult();
        Check(attempted==2&&batch.Saved.Count==1&&batch.Error=="Simulated connection failure","A later failure keeps the earlier confirmed bill available and stops remaining issuance");
        Check((string)batch.Saved[0].Rows[0][0]=="FB-TEST","Recovery keeps the actual saved bill number");
        foreach(var table in batch.Saved)table.Dispose();
        var cancelled=FeeBillBatch.PrepareAsync(grouped,()=>false,_=>throw new Exception("Must not issue")).GetAwaiter().GetResult();
        Check(cancelled.Saved.Count==0,"An invalidated selection never starts another bill");

        using var original=new DataSet();
        var header=original.Tables.Add("Header");header.Columns.Add("ReceiptNumber");header.Columns.Add("ReceiptStatus");header.Columns.Add("TransactionDate",typeof(DateTime));
        var students=original.Tables.Add("Students");students.Columns.Add("AdmissionId",typeof(int));students.Columns.Add("StudentName");
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"}){header.Columns.Add(field,typeof(decimal));students.Columns.Add(field,typeof(decimal));}
        header.Rows.Add("FR-ORIGINAL","Later changes recorded",new DateTime(2026,10,1),1000m,700m,50m,30m,400m,300m);
        students.Rows.Add(1,"Ali",800m,500m,50m,10m,100m,300m);students.Rows.Add(2,"Sara",200m,200m,0m,20m,300m,0m);
        var lines=original.Tables.Add("Lines");lines.Columns.Add("AdmissionId",typeof(int));lines.Rows.Add(1);lines.Rows.Add(2);
        var plans=original.Tables.Add("Plans");plans.Columns.Add("AdmissionId",typeof(int));plans.Rows.Add(2);
        using var child=FeeReceiptChildCopy.Extract(original,1);
        Check(child.Tables[1].Rows.Count==1&&child.Tables[2].Rows.Count==1&&child.Tables[3].Rows.Count==0,"Child copy excludes siblings' fees and plans");
        Check((string)child.Tables[0].Rows[0]["ReceiptNumber"]=="FR-ORIGINAL"&&(DateTime)child.Tables[0].Rows[0]["TransactionDate"]==new DateTime(2026,10,1),"Child copy retains original receipt reference and date");
        Check((decimal)child.Tables[0].Rows[0]["CashReceived"]==800m&&(decimal)child.Tables[0].Rows[0]["CreditRemaining"]==300m&&(decimal)child.Tables[0].Rows[0]["BalanceRemaining"]==100m,"Child cash, advance and debt totals belong only to that child");
        Check((string)child.Tables[0].Rows[0]["ReceiptStatus"]=="Later changes recorded", "Child copies preserve actual status without adding a redundant explanatory paragraph");
        Check((decimal)header.Rows[0]["CashReceived"]==1000m&&students.Rows.Count==2,"Copy preparation never mutates the original family snapshot");
        bool rejected=false;try{using var invalid=FeeReceiptChildCopy.Extract(original,999);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"A child absent from the saved receipt cannot be substituted from today's family");

        Exception? error=null;
        students.Columns.Add("AdmissionNumber");students.Rows[0]["AdmissionNumber"]="101";students.Rows[1]["AdmissionNumber"]="102";
        using var copyTimer=new System.Windows.Forms.Timer {Interval=100};
        copyTimer.Tick+=(_,_)=>{
            copyTimer.Stop();var dialog=Application.OpenForms.Cast<Form>().Single(f=>f.Text=="Choose Receipt Copy");
            try
            {
                IEnumerable<Control> Walk(Control c)=>c.Controls.Cast<Control>().SelectMany(x=>new[]{x}.Concat(Walk(x)));
                var combo=Walk(dialog).OfType<ModernComboBox>().Single();
                Check(combo.Items.Count==3&&combo.SelectedIndex==0,"Receipt chooser defaults to the original family document and lists only saved children");
                combo.SelectedIndex=2;
                Walk(dialog).OfType<ModernButton>().Single(b=>b.Text=="Preview Receipt").PerformClick();
            }
            catch(Exception ex){error=ex;dialog.Close();}
        };
        copyTimer.Start();var copy=FeeReceiptCopyDialog.Choose(null,students);if(error is not null)throw error;
        Check(copy==2,"Receipt chooser returns the explicitly selected saved child identity");

        DataTable SavedBill(bool family)
        {
            using var snapshot=FeeBillPresentation.Create();if(family)snapshot.Columns.Add("FamilyRowKind");
            var line=snapshot.NewRow();line["Amount"]=100m;line["ReceiptPageKey"]=1;line["AdmissionNumberText"]=family?"101, 102":"103";snapshot.Rows.Add(line);
            var saved=new DataTable();saved.Columns.Add("BillNumber");saved.Columns.Add("IssuedAt",typeof(DateTime));saved.Columns.Add("SnapshotXml");
            saved.Rows.Add(family?"FB-TEST":"B-TEST",DateTime.Today,FeeBillDocuments.Snapshot(snapshot));return saved;
        }
        using var familyBill=SavedBill(true);using var singleBill=SavedBill(false);
        using var combined=OutstandingBillsPreview.CombineSaved(new[]{familyBill,singleBill},true);
        Check(combined.AsEnumerable().Select(r=>r.Field<int>("ReceiptPageKey")).Distinct().Count()==2,"One direct preview retains a separate document group for every bill");
        Check(combined.AsEnumerable().Select(r=>r.Field<string>("ReceiptNumber")).SequenceEqual(new[]{"FB-TEST","B-TEST"}),"Mixed preview preserves each original bill number");
        Check(Convert.ToString(combined.Rows[1]["BillStudentKey"])=="103","Standalone students remain their own bill in a family-grouped batch");
    }

    private static void CheckBulkBillReview()
    {
        DataTable Members()
        {
            var table=new DataTable();
            table.Columns.Add("AdmissionId",typeof(int));table.Columns.Add("FamilyId",typeof(int));
            foreach(string field in new[]{"AdmissionNumber","QuickReference","StudentName","Status","EnrollmentStatus"})table.Columns.Add(field);
            table.Columns.Add("BillingReady",typeof(bool));table.Columns.Add("PlacementConfirmedAt",typeof(DateTime));
            table.Rows.Add(1,10,"101",null,"Ali","Active","Active",true,DateTime.Today);
            table.Rows.Add(2,10,"102",null,"Sara","Active","Active",false,DateTime.Today);
            table.Rows.Add(3,DBNull.Value,"103",null,"Ahmed","Active","Active",true,DateTime.Today);
            table.Rows.Add(4,20,null,"QR-104","Settled","Pending","Pending",false,DBNull.Value);
            return table;
        }
        DataTable Charges()
        {
            var table=new DataTable();table.Columns.Add("AdmissionId",typeof(int));table.Columns.Add("Due",typeof(decimal));
            table.Rows.Add(1,100m);table.Rows.Add(2,200m);table.Rows.Add(1,50m);table.Rows.Add(3,300m);
            return table;
        }
        int memberReads=0,chargeReads=0;
        bool membersDisposed=false,chargesDisposed=false;
        var members=Members();var charges=Charges();
        members.Disposed+=(_,_)=>membersDisposed=true;charges.Disposed+=(_,_)=>chargesDisposed=true;
        var review=OutstandingBillsPreview.ReadAccountsAsync(new[]{1,2,3,4,1},ids=> {
            memberReads++;Check(ids.SequenceEqual(new[]{1,2,3,4}),"Bulk review reads only distinct selected students");return Task.FromResult(members);
        },ids=> {chargeReads++;return Task.FromResult(charges);}).GetAwaiter().GetResult();
        Check(memberReads==1&&chargeReads==1,"Initial bill review uses two batch reads regardless of sibling count");
        Check(review.Select(s=>s.Due).SequenceEqual(new[]{150m,200m,300m,0m}),"Batch review keeps each child's charges separate, including zero dues");
        Check(review[0].Notice==""&&review[1].Notice.Contains("Sara")&&!review[1].Notice.Contains("Ali"),"Readiness notices remain specific to the selected child");
        Check(FeeBillSelection.Groups(review,true).Select(g=>string.Join(",",g)).SequenceEqual(new[]{"1,2","3"}),"Batched family review preserves standalone and settled-student grouping");
        Check(review[0].Reference=="101"&&review[2].FamilyId is null,"Batch review preserves official references and students without a family");
        Check(review[3].Reference=="QR-104"&&review[3].Readiness=="Pending Registration","Pending children retain their temporary references and registration readiness");
        Check(membersDisposed&&chargesDisposed,"Successful batch review releases both query tables");
        bool rejected=false;
        try { OutstandingBillsPreview.ReadAccountsAsync(new[]{999},_=>Task.FromResult(Members()),_=>Task.FromResult(Charges())).GetAwaiter().GetResult(); }
        catch(InvalidOperationException){rejected=true;}
        Check(rejected,"A missing selected student stops bill preparation");
        membersDisposed=false;members=Members();members.Disposed+=(_,_)=>membersDisposed=true;
        rejected=false;
        try { OutstandingBillsPreview.ReadAccountsAsync(new[]{1},_=>Task.FromResult(members),_=>throw new InvalidOperationException("Read failed")).GetAwaiter().GetResult(); }
        catch(InvalidOperationException ex){rejected=ex.Message=="Read failed";}
        Check(rejected&&membersDisposed,"A failed charge read stops preparation and releases the successful member read");
        var largeMembers=Members();var largeCharges=Charges();
        for(int id=5;id<=25;id++)
        {
            largeMembers.Rows.Add(id,20,id.ToString(),null,"Child "+id,"Active","Active",true,DateTime.Today);
            largeCharges.Rows.Add(id,10m);
        }
        memberReads=chargeReads=0;
        review=OutstandingBillsPreview.ReadAccountsAsync(Enumerable.Range(1,25).ToArray(),
            _=>{memberReads++;return Task.FromResult(largeMembers);},
            _=>{chargeReads++;return Task.FromResult(largeCharges);}).GetAwaiter().GetResult();
        Check(review.Length==25&&memberReads==1&&chargeReads==1&&review.Sum(s=>s.Due)==860m,
            "A 25-student review uses two reads and retains every student's exact dues");
    }
}
