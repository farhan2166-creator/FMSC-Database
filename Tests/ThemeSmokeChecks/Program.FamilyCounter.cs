using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Profile;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckFamilyCounter()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(7777,"Family fixture","Administrator",false,Array.Empty<string>());
            using var members=new DataTable();
            foreach(string column in new[]{"FamilyId","FamilyMemberId","AdmissionId","EnrollmentId"})members.Columns.Add(column,typeof(int));
            foreach(string column in new[]{"StudentName","FatherName","Gender","AdmissionNumber","QuickReference","RegistrationNumber","Status","EnrollmentStatus","ClassName","SectionName","AcademicYear"})members.Columns.Add(column);
            members.Columns.Add("BillingReady",typeof(bool));members.Columns.Add("PlacementConfirmedAt",typeof(DateTime));
            for(int id=41;id<=43;id++){
                var row=members.NewRow();row["FamilyId"]=1;row["FamilyMemberId"]=id;row["AdmissionId"]=id;row["EnrollmentId"]=id+100;
                row["StudentName"]="Fictional child "+id;row["FatherName"]="Fictional parent";row["Status"]=id==43?"Pending":"Active";
                row["EnrollmentStatus"]="Active";row["BillingReady"]=false;row["AcademicYear"]="2026–2027";row["ClassName"]="Class 1";
                if(id==43)row["QuickReference"]="QR-43";else {row["AdmissionNumber"]="ADM-"+id;row["PlacementConfirmedAt"]=new DateTime(2026,4,1);}
                members.Rows.Add(row);
            }
            members.Columns.Add("IsOnHold",typeof(bool));
            var held=members.Rows[0];
            Check(!FamilyBillingData.CanCollectPostedFees(held),"An unconfirmed return remains blocked from the counter");
            held["IsOnHold"]=true;
            Check(FamilyBillingData.CanCollectPostedFees(held) && !FamilyBillingData.AwaitsReturn(held),
                "A held student with billing paused may pay retained posted dues");
            held["IsOnHold"]=false;held["BillingReady"]=true;
            Check(FamilyBillingData.CanCollectPostedFees(held),"An active billing-ready student may collect posted fees");
            held["BillingReady"]=false;
            Check(FamilyBillingData.CanCollectPostedFees(members.Rows[2]),"Pending registration retains counter access for posted fees");
            held["Status"]="Left";
            Check(!FamilyBillingData.CanCollectPostedFees(held),"A left student without a hold remains blocked from the family counter");
            held["Status"]="Active";
            string membersSql=(string)typeof(FeeBillPreview).GetField("MembersSql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
            Check(membersSql.Contains("StudentPostingHolds") && membersSql.Contains("H.ResumeDate IS NULL") && membersSql.Contains("IsOnHold"),
                "The counter member query carries the current hold flag into readiness checks");
            int reads=0,confirmations=0,confirmedId=0;
            var reply=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var page=new StudentFamilyPage(0,false,id=>{reads++;return Task.FromResult(members.Copy());},id=>{confirmations++;confirmedId=id;return reply.Task;});
            using var window=new Form{ClientSize=new Size(1150,740)};window.Controls.Add(page);window.Show();Application.DoEvents();
            var picker=(StudentPicker)typeof(StudentFamilyPage).GetField("student",flags)!.GetValue(page)!;
            picker.SetStudent(41,"Fictional child 41");
            var load=(Task)typeof(StudentFamilyPage).GetMethod("LoadFamilyForSelectedStudentAsync",flags)!.Invoke(page,null)!;
            PumpUntil(()=>load.IsCompleted,"Injected family readiness load completes");
            Check(reads==1,"Opening an injected family fixture never uses normal lookup loaders");
            var head=(Control)typeof(StudentFamilyPage).GetField("headHost",flags)!.GetValue(page)!;
            var flow=(Control)typeof(StudentFamilyPage).GetField("membersFlow",flags)!.GetValue(page)!;
            Control[] Cards()=>head.Controls.Cast<Control>().Concat(flow.Controls.Cast<Control>()).ToArray();
            int Id(Control card)=>(int)card.GetType().GetProperty("AdmissionId")!.GetValue(card)!;
            void Select(int id)=>typeof(StudentFamilyPage).GetMethod("SelectCard",flags)!.Invoke(page,new[]{Cards().Single(card=>Id(card)==id)});
            var confirm=(ModernButton)typeof(StudentFamilyPage).GetField("confirmReturnButton",flags)!.GetValue(page)!;
            Check(Cards().Length==3&&Cards().Single(card=>Id(card)==43).AccessibleName!.Contains("Pending Registration"),"New and returning family members keep distinct visible readiness");
            Check(Cards().All(card=>card.AccessibleName!.Contains("2026–2027")),"Family cards expose the actual academic year");
            Select(43);Check(!confirm.Enabled,"Pending new registration cannot be confirmed as a returning student");
            Select(41);Check(confirm.Enabled,"Only an eligible selected return enables confirmation");
            confirm.PerformClick();confirm.PerformClick();
            Check(page.IsBusy&&!confirm.Enabled&&confirmations==1&&confirmedId==41,"One click confirms only that child and blocks re-entry while awaiting its dialog");
            members.Rows[0]["BillingReady"]=true;reply.SetResult(true);
            PumpUntil(()=>!page.IsBusy&&reads==2,"Successful return refreshes the actual family read");
            Check(confirmations==1&&confirm.Enabled,"Next returning child is selected but never confirmed automatically");
            var selected=typeof(StudentFamilyPage).GetField("selectedCard",flags)!.GetValue(page)!;
            Check((int)selected.GetType().GetProperty("AdmissionId")!.GetValue(selected)! == 42,"Next waiting child is selected explicitly after refresh");
            Check(Cards().Single(card=>Id(card)==41).AccessibleName!.Contains("Ready for Billing"),"Successful return changes only that child's displayed readiness");
            reply=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            confirm.PerformClick();reply.SetResult(false);
            PumpUntil(()=>!page.IsBusy&&reads==3,"Cancelled return refreshes without inventing completion");
            Check(!Convert.ToBoolean(members.Rows[1]["BillingReady"])&&confirmations==2,"Cancelling the next child never silently marks the family ready");
            CurrentUser.SignIn(7777,"Restricted fixture","Operator",false,new[]{"Students.View","Students.Family"});
            var denied=(Task)typeof(StudentFamilyPage).GetMethod("ConfirmSelectedReturnAsync",flags)!.Invoke(page,null)!;
            Check(denied.IsCompleted&&confirmations==2,"A stale enabled button cannot bypass current return permissions");
            window.Close();

            var delayed=new TaskCompletionSource<DataTable>(TaskCreationOptions.RunContinuationsAsynchronously);
            using(var stale=new StudentFamilyPage(0,false,_=>delayed.Task,_=>throw new Exception("Stale selection must not open a return")))
            {
                var stalePicker=(StudentPicker)typeof(StudentFamilyPage).GetField("student",flags)!.GetValue(stale)!;
                stalePicker.SetStudent(41,"First fictional family");
                var oldRead=(Task)typeof(StudentFamilyPage).GetMethod("LoadFamilyForSelectedStudentAsync",flags)!.Invoke(stale,null)!;
                stalePicker.SetStudent(99,"Different fictional family");delayed.SetResult(members.Copy());
                PumpUntil(()=>oldRead.IsCompleted,"Obsolete family read settles");
                var shown=(System.Collections.IList)typeof(StudentFamilyPage).GetField("currentMemberAdmissionIds",flags)!.GetValue(stale)!;
                Check(shown.Count==0,"A late family read cannot show or enable actions for the previous selection");
            }

            using var fees=new FeeCollectionPage(false);
            typeof(FeeCollectionPage).GetField("admissionId",flags)!.SetValue(fees,41);
            typeof(FeeCollectionPage).GetField("suppressSiblingEvents",flags)!.SetValue(fees,true);
            var siblings=(CheckedListBox)typeof(FeeCollectionPage).GetField("siblings",flags)!.GetValue(fees)!;
            var choice=typeof(FeeCollectionPage).GetNestedType("SiblingChoice",BindingFlags.NonPublic)!;
            siblings.Items.Add(Activator.CreateInstance(choice,new object[]{42,"Same name"})!,true);
            siblings.Items.Add(Activator.CreateInstance(choice,new object[]{43,"Same name"})!,false);
            var ids=(int[])typeof(FeeCollectionPage).GetMethod("SelectedBillStudents",flags)!.Invoke(fees,null)!;
            Check(ids.SequenceEqual(new[]{41,42}),"Counter bill uses primary plus checked sibling identities, never names or every family member");
            var deniedBill=(Task)typeof(FeeCollectionPage).GetMethod("PrintSelectedFamilyBillAsync",flags)!.Invoke(fees,null)!;
            Check(deniedBill.IsCompleted&&!fees.IsBusy&&!fees.IsHandleCreated,"Missing Fees.View refuses printing before SQL, profile lookup or report preview");
            var amount=(ModernTextBox)typeof(FeeCollectionPage).GetField("paymentAmount",flags)!.GetValue(fees)!;
            amount.Text="500";
            var retained=(Task)typeof(FeeCollectionPage).GetMethod("OpenFamilyReturnsAsync",flags)!.Invoke(fees,null)!;
            Check(retained.IsCompleted&&amount.Text=="500"&&!fees.IsHandleCreated&&!fees.IsBusy,"Family navigation retains an unfinished payment draft and refuses a destructive refresh");
            CurrentUser.SignIn(7777,"Family fixture","Administrator",false,Array.Empty<string>());
            var discount=(ModernTextBox)typeof(FeeCollectionPage).GetField("discountAmount",flags)!.GetValue(fees)!;
            discount.Text="100";
            var draftBill=(Task)typeof(FeeCollectionPage).GetMethod("PrintSelectedFamilyBillAsync",flags)!.Invoke(fees,null)!;
            Check(draftBill.IsCompleted&&discount.Text=="100"&&!fees.IsHandleCreated&&!fees.IsBusy,"A bill cannot silently ignore an unsaved extra discount or clear that draft");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
