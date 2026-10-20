using System.Data;
using System.Reflection;
using System.Text.Json;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Finance.Collection;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static async Task CheckQuickCounterWorkspaceSqlAsync()
    {
        var target=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Core.Level2Target")
            ??throw new InvalidOperationException("Counter writes require the owned test binary.");
        target.GetMethod("ValidateSchema",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
        string root=(string)target.GetProperty("Root",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"credentials.json")));
        int Ref(string key)=>fixture.RootElement.GetProperty(key).GetInt32();
        int admin=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT UserId FROM dbo.Users WHERE Username=N'level2_admin';")).Rows[0][0]);
        CurrentUser.SignIn(admin,"Fictional quick counter","Administrator",false,Array.Empty<string>());
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,flags)!.GetValue(owner)!;
        async Task Call(object owner,string name,params object[] args)=>await (Task)owner.GetType().GetMethod(name,flags)!.Invoke(owner,args)!;
        async Task<int> Count(string sql)=>Convert.ToInt32((await DatabaseHelper.QuerySqlAsync(sql)).Rows[0][0]);
        async Task Settle(QuickRegistrationFormPage registration)
        {
            var wait=System.Diagnostics.Stopwatch.StartNew();
            while(registration.IsBusy){if(wait.Elapsed>TimeSpan.FromSeconds(20))throw new Exception("Child fee load timed out");await Task.Delay(20);}
            Check(Field<bool>(registration,"feesLoaded"),"A real child's fee quote loads before saving");
        }
        using var answers=new System.Windows.Forms.Timer{Interval=50};
        string? unexpected=null;
        string TextOf(Control control)=>control.Text+" "+string.Join(" ",control.Controls.Cast<Control>().Select(TextOf));
        answers.Tick+=(_,_)=>{
            foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray())
            {
                string title=dialog.Text.Replace(" [Fictional Level 2]","");
                if(title is "Save & Prepare" or "Confirm Family Collection" or "Confirm Fee Transaction" or "Confirm Return Admission")dialog.DialogResult=DialogResult.Yes;
                else if(title=="FMSC" && TextOf(dialog).Contains("2 of 3 prepared"))dialog.DialogResult=DialogResult.OK;
                else if(dialog.Modal && dialog.Text!="Counter fixture")
                {unexpected=dialog.Text+": "+TextOf(dialog);dialog.DialogResult=DialogResult.Cancel;dialog.Close();}
            }
        };
        answers.Start();
        using var window=new Form{Text="Counter fixture",ClientSize=new Size(1450,950),Location=new Point(-30000,-30000),StartPosition=FormStartPosition.Manual,ShowInTaskbar=false};
        using var workspace=new FamilyAdmissionWorkspace((_,_,_)=>{});
        window.Controls.Add(workspace);window.Show();
        await Call(workspace,"StartNewFamilyAsync");
        Field<ModernTextBox>(workspace,"familyParent").Text="Fictional Quick Counter Parent";
        Field<ModernTextBox>(workspace,"familyPhone").Text="03001234567";
        int placement=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=@Campus AND ClassId=@Class AND SectionId=@Section;",
            DatabaseHelper.Int("@Campus",Ref("campus")),DatabaseHelper.Int("@Class",Ref("destinationClass")),DatabaseHelper.Int("@Section",Ref("section")))).Rows[0][0]);
        async Task<QuickRegistrationFormPage> Draft(string name,bool free=false)
        {
            object child=Field<object>(workspace,"current");
            var registration=Field<QuickRegistrationFormPage>(child,"Editor");
            Field<ModernTextBox>(registration,"name").Text=name;
            Field<LookupBox>(registration,"year").SelectedValue=Ref("nextYear");
            Field<PlacementPicker>(registration,"placement").SelectedValue=placement;
            await Settle(registration);
            if(free)
            {
                foreach(DataRow row in Field<DataTable>(registration,"rates").Rows)
                {row["Discount"]=row["Amount"];if(Convert.ToString(row["Frequency"])=="Monthly")row["ConcessionLabel"]="Waived";}
                registration.GetType().GetMethod("UpdateTotal",flags)!.Invoke(registration,null);
            }
            return registration;
        }
        int initial=await Count("SELECT COUNT(*) FROM dbo.Students;");
        var first=await Draft("Counter Paying First");
        await Call(workspace,"AddNewChildAsync");var free=await Draft("Counter Free Future",true);
        await Call(workspace,"AddNewChildAsync");
        await Call(workspace,"ProcessSelectedAsync");
        Check(unexpected is null,"Preparation opens only the expected review confirmation: "+unexpected);
        Check(first.SavedRegistrationId>0 && free.SavedRegistrationId>0 && await Count("SELECT COUNT(*) FROM dbo.Students;")==initial+2,
            "Actual family host retains two successful saves when the third child is invalid");
        await Call(workspace,"ProcessSelectedAsync");
        Check(await Count("SELECT COUNT(*) FROM dbo.Students;")==initial+2,"Retry of a partially completed family does not duplicate saved children");
        var third=await Draft("Counter Paying Third");await Call(workspace,"ProcessSelectedAsync");
        Check(third.SavedRegistrationId>0 && await Count("SELECT COUNT(*) FROM dbo.Students;")==initial+3,"Correcting the remaining draft completes only that registration");
        int[] ids={first.SavedRegistrationId,free.SavedRegistrationId,third.SavedRegistrationId};
        // A synthetic future billing start exercises the missing-plan report path;
        // admission itself retains the production rule forbidding future admission dates.
        await DatabaseHelper.QuerySqlAsync("UPDATE dbo.Enrollments SET BillingStartDate=DATEADD(month,1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)) WHERE AdmissionId=@Id AND Status=N'Pending';",DatabaseHelper.Int("@Id",ids[1]));
        using var statuses=await DatabaseHelper.QuerySqlAsync("SELECT Status,AdmissionNumber FROM dbo.Students WHERE AdmissionId IN(SELECT CONVERT(int,value) FROM OPENJSON(@Ids));",DatabaseHelper.P("@Ids",JsonSerializer.Serialize(ids)));
        Check(statuses.AsEnumerable().All(r=>Convert.ToString(r["Status"])=="Pending" && r.IsNull("AdmissionNumber")),"Counter saves keep all new children pending without assigning official admission numbers");
        int billsBefore=await Count("SELECT COUNT(*) FROM dbo.BillingCharges;");
        using var bill=await FamilyAdmissionData.ReadBillAsync(ids[0],ids);
        Check(bill.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==ids[1] && Convert.ToString(r["FeeTypeName"])!.Contains("monthly plan") && Convert.ToDateTime(r["BillingMonth"])>DateTime.Today && Convert.ToDecimal(r["Due"])==0),
            "Future fully waived monthly plan stays on the real mixed-family bill");
        Check(await Count("SELECT COUNT(*) FROM dbo.BillingCharges;")==billsBefore,"Printing waiver evidence never manufactures a zero-value charge");
        using var billData=FamilyBillReport.BuildData("Fictional School","","","Fictional Parent",bill);
        using var issued=await FeeBillDocuments.IssueAsync(billData,ids[0],ids);
        using var savedBill=FeeBillDocuments.ReadSavedData(issued.Rows[0]);
        Check(savedBill.AsEnumerable().Any(r=>Convert.ToString(r["FeeDescription"])!.Contains("Waived monthly plan")),"Saved numbered bill snapshot retains the future waiver on reprint");
        string output=Path.Combine(root,"counter-evidence");Directory.CreateDirectory(output);
        void Render(DataTable data,string template,string dataset,string name)
        {
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",template);
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(FeeDocumentLayout.SizeToContent(path,data)));
            report.DataSources.Add(new ReportDataSource(dataset,data));
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(output,name+".pdf"),pdf);
            File.WriteAllBytes(Path.Combine(output,name+".png"),report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>130</DpiX><DpiY>130</DpiY></DeviceInfo>"));
            Check(pdf.Length>1000,"Established RDLC renders the saved "+name);
        }
        Render(savedBill,FamilyBillReport.TemplateFile,FamilyBillReport.DataSetName,"family-bill");
        using var waivedOnly=await FamilyAdmissionData.ReadBillAsync(ids[1],new[]{ids[1]});
        using var waivedData=StudentBillReport.BuildData("Fictional School","","","Fictional Parent",waivedOnly);
        Console.WriteLine("PASS: mixed-family waiver snapshot and established family bill RDLC.");
        using var issuedFree=await FeeBillDocuments.IssueAsync(waivedData,ids[1],new[]{ids[1]});
        using var savedFree=FeeBillDocuments.ReadSavedData(issuedFree.Rows[0]);
        Render(savedFree,StudentBillReport.TemplateFile,StudentBillReport.DataSetName,"free-student-bill");
        using(var capture=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));capture.Save(Path.Combine(output,"workspace.png"));}
        using var collection=new FeeCollectionPage(false){Dock=DockStyle.Fill};
        collection.ConfigureCounter(new[]{ids[0],ids[2]});window.Controls.Clear();window.Controls.Add(collection);
        await collection.InitializeCounterAsync();
        Check(Field<bool>(collection,"counterReady"),"Existing collection loads exactly the two selected counter children");
        var charges=Field<DataGridView>(collection,"charges");
        Check(charges.Rows.Cast<DataGridViewRow>().All(r=>new[]{ids[0],ids[2]}.Contains(Convert.ToInt32(r.Cells["AdmissionId"].Value))),"Unchecked family members never enter the payment grid");
        Field<ModernTextBox>(collection,"paymentAmount").Text="1000";
        var proposal=Field<DataGridView>(collection,"familyProposal");
        Check(proposal.Rows.Count==2 && proposal.Rows.Cast<DataGridViewRow>().Sum(r=>Convert.ToDecimal(r.Cells["Proposed"].Value))==1000m,"Entered family payment receives a suggested per-child allocation");
        foreach(var split in new[]{(Id:ids[0],Amount:600m),(Id:ids[2],Amount:400m)})
        {
            var row=proposal.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToInt32(r.Cells["AdmissionId"].Value)==split.Id);
            row.Cells["Proposed"].Value=split.Amount;
            typeof(DataGridView).GetMethod("OnCellEndEdit",flags)!.Invoke(proposal,new object[]{new DataGridViewCellEventArgs(proposal.Columns["Proposed"].Index,row.Index)});
        }
        Check(collection.HasUnsavedChanges,"Unrecorded counter payment retains a modal discard guard");
        Check(charges.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToInt32(r.Cells["AdmissionId"].Value)==ids[0]).Sum(r=>Convert.ToDecimal(r.Cells["Amount"].Value))==600m &&
            charges.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToInt32(r.Cells["AdmissionId"].Value)==ids[2]).Sum(r=>Convert.ToDecimal(r.Cells["Amount"].Value))==400m,
            "Per-child edits replace the suggested split before confirmation");
        using(var capture=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));capture.Save(Path.Combine(output,"collection.png"));}
        await Call(collection,"ProcessTransaction");
        Check(unexpected is null,"Collection opens only its established confirmation: "+unexpected);
        Check(collection.CounterReceipts.Count==2,"Existing writer confirms the receipt for both paying children. Status: "+
            ((Label)typeof(V2Page).GetField("StatusLabel",flags)!.GetValue(collection)!).Text+
            " Received: "+Field<ModernTextBox>(collection,"paymentAmount").Text+" Allocations: "+
            string.Join(", ",charges.Rows.Cast<DataGridViewRow>().Select(r=>$"{r.Cells["AdmissionId"].Value}:{r.Cells["Amount"].Value}/{r.Tag}")));
        string receipt=collection.CounterReceipts[ids[0]];
        using var payments=await DatabaseHelper.QuerySqlAsync("SELECT AdmissionId,SUM(TotalAmount) Amount FROM dbo.FeePayments WHERE ReceiptNumber=@Receipt GROUP BY AdmissionId;",DatabaseHelper.P("@Receipt",receipt));
        Check(payments.Rows.Count==2 && payments.AsEnumerable().Sum(r=>Convert.ToDecimal(r["Amount"]))==1000m && payments.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==ids[0] && Convert.ToDecimal(r["Amount"])==600m),
            "Edited 600/400 split is saved exactly once by the established family writer");
        Check(charges.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToDecimal(r.Cells["Amount"].Value)==0m) && !collection.HasUnsavedChanges,
            "After commit, both children's remaining bills are retained with no old payment draft to resubmit");
        using var receiptSet=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receipt));
        var institution=new InstitutionProfile.PrintIdentity("Fictional School","","");
        using var receiptData=FeeReceiptDocuments.BuildData(institution,receiptSet.Tables[0].Rows[0],receiptSet.Tables[1],receiptSet.Tables[2]);
        using var preparedReceipt=FamilyReceiptReport.PrepareData(receiptData);
        Render(preparedReceipt,FamilyReceiptReport.TemplateFile,FamilyReceiptReport.DataSetName,"family-receipt");
        // Recovery for another family must be checked without changing the loaded child's identity.
        using var intent=QuickRegistrationRecoveryJournal.Begin(admin,await QuickRegistrationWriter.GetScopeAsync(),new string('A',64),"Fictional unrelated pending request");
        Guid pendingId=intent.Intent.Id;intent.Dispose();
        bool separate=false;first.WorkspaceRecoveryRequested=()=>{separate=true;return Task.CompletedTask;};
        first.RefreshWorkspaceRecovery();await Call(first,"CheckSaveOutcomeAsync",false);
        Check(separate && first.SavedRegistrationId==ids[0],"Unrelated recovery is routed to a separate context without replacing a loaded child");
        QuickRegistrationRecoveryJournal.ClearVerified(pendingId);
        using var request=await DatabaseHelper.QuerySqlAsync("SELECT RequestId,PayloadHash FROM dbo.QuickRegistrationRequests WHERE AdmissionId=@Id;",DatabaseHelper.Int("@Id",ids[0]));
        var savedIntent=new QuickRegistrationRecoveryJournal.Pending((Guid)request.Rows[0]["RequestId"],admin,
            await QuickRegistrationWriter.GetScopeAsync(),Convert.ToHexString((byte[])request.Rows[0]["PayloadHash"]),"Fictional committed request with lost acknowledgement",DateTime.UtcNow);
        File.WriteAllText(QuickRegistrationRecoveryJournal.JournalPath,JsonSerializer.Serialize(savedIntent));
        using var recovered=new QuickRegistrationFormPage((_,_,_)=>{},0,false);
        await recovered.InitializeRecoveryAsync();
        Check(recovered.SavedRegistrationId==ids[0] && !recovered.HasUnresolvedSave && QuickRegistrationRecoveryJournal.ReadPending() is null,
            "Dedicated recovery verifies the committed request and clears its journal without creating another student");
        Check(first.SavedRegistrationId==ids[0],"Checking a committed request in a separate editor preserves the original family's child identity");
        int returning=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT AdmissionId FROM dbo.Students WHERE StudentName=N'Family Example Two';")).Rows[0][0]);
        using var returnWorkspace=new FamilyAdmissionWorkspace((_,_,_)=>{});
        window.Controls.Clear();window.Controls.Add(returnWorkspace);
        await Call(returnWorkspace,"LoadFamilyAsync",returning);
        Check(!Field<ModernButton>(returnWorkspace,"collect").Enabled,"Actual returning-child host blocks collection until return confirmation");
        await Call(returnWorkspace,"ProcessSelectedAsync");
        Check(Field<ModernButton>(returnWorkspace,"collect").Enabled,"Actual existing return confirmation prepares the same child for collection without navigation");
        int afterReturn=await Count("SELECT COUNT(*) FROM dbo.BillingCharges;");
        await Call(returnWorkspace,"ProcessSelectedAsync");
        Check(await Count("SELECT COUNT(*) FROM dbo.BillingCharges;")==afterReturn,"Preparing an already confirmed return does not create its opening charges twice");
        Check(unexpected is null,"The complete counter journey uses only its expected confirmation dialogs: "+unexpected);
        Console.WriteLine("PASS: actual counter preparation, partial retry, waiver snapshot, collection split, RDLC and recovery scenarios.");
    }
}
