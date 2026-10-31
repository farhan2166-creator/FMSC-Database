using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckBillPreparationChoices()
    {
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
}
