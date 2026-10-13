using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Admissions;

public sealed class QuickRegistrationPage : UserControl, IUnsavedChanges, IBusyPage
{
    private readonly QuickRegistrationFormPage form;
    public bool HasUnsavedChanges=>form.HasUnsavedChanges;
    public string UnsavedChangesMessage=>form.UnsavedChangesMessage;
    public bool IsBusy=>form.IsBusy;
    public string BusyMessage=>"Wait for the registration operation to finish.";
    public QuickRegistrationPage(Action<Func<Control>,string,string> navigate,int preselectRegistrationId=0)
    {
        Dock=DockStyle.Fill;
        var tabs=new TabControl{Dock=DockStyle.Fill,Font=AppTheme.NormalFont};
        var formTab=new TabPage("Registration Form"){BackColor=AppTheme.Surface};
        var pendingTab=new TabPage("Pending Registrations"){BackColor=AppTheme.Surface};
        form=new QuickRegistrationFormPage(navigate,preselectRegistrationId){Dock=DockStyle.Fill};
        var pending=new FMSC_SMS.Pages.Students.Maintenance.AdmissionReviewListPage(navigate,true,async id=>{
            if(await form.OpenRegistrationAsync(id))tabs.SelectedTab=formTab;
        }){Dock=DockStyle.Fill};
        pending.RegistrationReviewed+=async id=>{if(form.SavedRegistrationId==id)await form.OpenRegistrationAsync(id);};
        formTab.Controls.Add(form);pendingTab.Controls.Add(pending);
        tabs.TabPages.Add(formTab);tabs.TabPages.Add(pendingTab);
        TabStrip.Apply(tabs,AppTheme.ModuleAccent("Students"));Controls.Add(TabStrip.CreateHost(tabs));
        tabs.SelectedIndexChanged+=async(_,_)=>{if(tabs.SelectedTab==pendingTab)await pending.RefreshRecordsAsync();};
    }
}

internal sealed class QuickRegistrationFormPage : V2Page, IUnsavedChanges
{
    private readonly Action<Func<Control>, string, string> navigate;
    private readonly StudentPicker family = new();
    private readonly ModernTextBox parent = HumanUi.TextBox(false, 500);
    private readonly ModernTextBox contact = HumanUi.TextBox(false, 30);
    private readonly ModernTextBox address = HumanUi.TextBox(false, 500);
    private readonly ModernTextBox name = HumanUi.TextBox(false, 500);
    private readonly LookupBox year = new();
    private readonly PlacementPicker placement = new();
    private readonly ModernDatePicker joining = HumanUi.Date();
    private readonly DataGridView fees = new() { Dock=DockStyle.Fill, AllowUserToAddRows=false, AllowUserToDeleteRows=false, AutoGenerateColumns=false, RowHeadersVisible=false };
    private readonly Label total=new(){Dock=DockStyle.Fill,AutoSize=true,Font=AppTheme.ButtonFont,TextAlign=ContentAlignment.MiddleRight,Padding=new Padding(8),Text="Bill Total: Rs. 0"};
    private DataTable placements = new(), rates = new();
    private int savedStudent, familyAnchor, loadVersion, familyLoadVersion, defaultAcademicYear;
    internal int SavedRegistrationId=>savedStudent;
    internal bool IsBusy=>busy||loadingFees||loadingFamily;
    private string savedStatus="";
    private string savedSnapshot="",expectedQuote="";
    public bool HasUnsavedChanges=>HasPendingChanges||(savedStudent==0&&(name.Text.Trim().Length>0||parent.Text.Trim().Length>0||contact.Text.Trim().Length>0||address.Text.Trim().Length>0||placement.SelectedId>0));
    public string UnsavedChangesMessage=>"Discard the unsaved registration changes?";
    private string familyReference="";
    private string[] savedBasics=Array.Empty<string>();
    private bool busy, loadingFamily, loadingFees, feesLoaded;
    private readonly FormValidator validator;
    private readonly ModernButton save, next, bill, collect;
    private readonly ModernButton linkFamily = new(){Text="Link Existing Family",ThemeRole="Secondary",AutoSize=true};
    private readonly HumanCard registrationCard;
    private readonly TableLayoutPanel registrationContent;
    private readonly TableLayoutPanel root;
    private readonly Control familyField;
    internal QuickRegistrationFormPage(Action<Func<Control>, string, string> navigate, int preselectRegistrationId=0)
        : base("Quick Registration", "Save basic details and issue the family bill. Review and confirm from Pending Registrations.")
    {
        this.navigate=navigate;
        validator=CreateValidator();
        save=AddBusyAction("Save Registration", SaveAsync, "Success");
        next=AddAction("Add Another Child", (_,_)=>ClearChild(), "Secondary");
        bill=AddBusyAction("Print Family Bill", PrintBillAsync, "Secondary");
        collect=AddAction("Collect Payment",(_,_)=>navigate(()=>new FMSC_SMS.Pages.Finance.Collection.FeeCollectionPage(savedStudent),"Fees & Billing","Fee Collection"),"Primary");
        AddAction("New Family",(_,_)=> { if(busy)return; ++familyLoadVersion;loadingFamily=false;familyAnchor=0;familyReference="";family.Clear();parent.Clear();contact.Clear();address.Clear();ShowFamilyPicker(false);ClearChild(); },"Secondary");
        GateAction(save,"Students.Admit"); GateAction(collect,"Fees.Collect");
        PhoneNumberInput.Attach(contact,()=>validator);
        registrationCard=HumanUi.Card("Registration Details", "Enter a child, or link an existing family.",accessory:linkFamily);
        registrationContent=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        registrationContent.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        registrationContent.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        familyField=HumanUi.Field("Find a family member",family);familyField.Visible=false;familyField.TabIndex=0;
        registrationContent.Controls.Add(familyField,0,0);
        var fields=Fields(("Student name",name,true,1),("Joining date",joining,true,1),("Academic year",year,true,1),("Campus / Class / Section",placement,true,1),("Parent name",parent,true,1),("Primary phone",contact,true,1),("Address",address,false,2));
        fields.TabIndex=1;registrationContent.Controls.Add(fields,0,1);registrationCard.Content.Controls.Add(registrationContent);
        linkFamily.Click+=(_,_)=>{ShowFamilyPicker(!familyField.Visible);if(familyField.Visible)family.Focus();};
        fees.Columns.Add(new DataGridViewCheckBoxColumn { Name="Include",DataPropertyName="Include",HeaderText="Include",Width=65 });
        fees.Columns.Add(new DataGridViewTextBoxColumn { Name="Fee",DataPropertyName="FeeTypeName",HeaderText="Fee",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill });
        fees.Columns.Add(new DataGridViewTextBoxColumn { Name="Frequency",DataPropertyName="Frequency",HeaderText="Frequency",ReadOnly=true,Width=100 });
        fees.Columns.Add(new DataGridViewTextBoxColumn { Name="Amount",DataPropertyName="Amount",HeaderText="Fee (Rs.)",ReadOnly=true,Width=110 });
        fees.Columns.Add(new DataGridViewTextBoxColumn { Name="Discount",DataPropertyName="Discount",HeaderText="Discount (Rs.)",ReadOnly=!CurrentUser.HasPermission("Fees.Adjust"),Width=125 });
        fees.Columns.Add(new DataGridViewTextBoxColumn { Name="NetAmount",DataPropertyName="NetAmount",HeaderText="Net (Rs.)",ReadOnly=true,Width=110 });
        fees.Columns["Amount"].DefaultCellStyle.Format=fees.Columns["Discount"].DefaultCellStyle.Format=fees.Columns["NetAmount"].DefaultCellStyle.Format="N0";
        fees.CellValidating+=(_,e)=>{
            if(e.RowIndex<0||fees.Columns[e.ColumnIndex].Name!="Discount")return;
            bool valid=fees.Rows[e.RowIndex].DataBoundItem is DataRowView row&&decimal.TryParse(Convert.ToString(e.FormattedValue),out decimal discount)&&WholeRupees.IsWhole(discount)&&discount>=0&&discount<=Convert.ToDecimal(row["Amount"]);
            e.Cancel=!valid;if(!valid)SetStatus("Enter a whole-rupee discount between zero and its fee amount.");
        };
        fees.DataError+=(_,e)=>{e.ThrowException=false;SetStatus("Enter a valid discount amount.");};
        fees.CurrentCellDirtyStateChanged+=(_,_)=>{if(fees.IsCurrentCellDirty&&fees.CurrentCell is DataGridViewCheckBoxCell)fees.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        fees.CellValueChanged+=(_,_)=>UpdateTotal();
        fees.CellEndEdit+=(_,_)=>{if(fees.CurrentRow?.DataBoundItem is DataRowView row)row.EndEdit();UpdateTotal();};
        foreach(var input in new[]{name,parent,contact,address})input.TextChanged+=(_,_)=>PendingEditChanged();
        var feeCard=HumanUi.Card("Opening Bill", "Configured fees are shared with Admission. Review sibling discounts before saving.");
        var feeContent=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};feeContent.RowStyles.Add(new RowStyle(SizeType.Percent,100));feeContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));feeContent.Controls.Add(fees,0,0);feeContent.Controls.Add(total,0,1);feeCard.Content.Controls.Add(feeContent);
        root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldCardHeight(4)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(registrationCard,0,0);root.Controls.Add(feeCard,0,1);SetContent(root);
        family.UserSelectionChanged+=async(_,_)=>await LoadFamilyAsync();
        family.SelectionCleared+=(_,_)=>{++familyLoadVersion;familyAnchor=0;familyReference="";loadingFamily=false;parent.Clear();contact.Clear();address.Clear();UpdateActions();};
        placement.SelectedIndexChanged+=async(_,_)=>await LoadFeesAsync();
        Load+=async(_,_)=>{await LoadLookupsAsync();if(preselectRegistrationId>0)await LoadRegistrationAsync(preselectRegistrationId);};UpdateActions();
    }
    private static TableLayoutPanel Fields(params (string Label,Control Input,bool Required,int Span)[] fields)
    {
        int rows=(fields.Sum(field=>field.Span)+1)/2;
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=rows};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        for(int row=0;row<rows;row++)grid.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        int cell=0;
        for(int i=0;i<fields.Length;i++){
            var field=HumanUi.Field(fields[i].Label,fields[i].Input,fields[i].Required);field.TabIndex=i;
            grid.Controls.Add(field,cell%2,cell/2);grid.SetColumnSpan(field,fields[i].Span);cell+=fields[i].Span;
        }
        return grid;
    }
    private void ShowFamilyPicker(bool visible){
        familyField.Visible=visible;
        registrationContent.RowStyles[0].Height=visible?AppMetrics.FieldRowHeight:0;
        root.RowStyles[0].Height=AppMetrics.FieldCardHeight(4)+(visible?AppMetrics.FieldRowHeight:0);
        linkFamily.Text=visible?"Hide Family Search":"Link Existing Family";
    }
    private async Task LoadLookupsAsync()
    {
        try {
            var years=await DatabaseHelper.QueryUserAsync("GetAcademicYears");
            placements=await DatabaseHelper.QueryUserAsync("GetClassSections",DatabaseHelper.Int("@CampusId",0),DatabaseHelper.Int("@ClassId",0),DatabaseHelper.Int("@SectionId",0));
            year.Bind(years,"AcademicYearId","YearName");placement.Bind(placements);
            var current=years.AsEnumerable().FirstOrDefault(r=>r["StartDate"] is DateTime start && r["EndDate"] is DateTime end && joining.Value.Date>=start.Date && joining.Value.Date<=end.Date);
            defaultAcademicYear=current is null?0:Convert.ToInt32(current["AcademicYearId"]);
            year.SelectedValue=defaultAcademicYear;
        }catch(Exception ex){Fail(ex);}
    }
    private async Task LoadFamilyAsync()
    {
        if(busy)return;
        int version=++familyLoadVersion;
        if(family.SelectedId<=0){familyAnchor=0;loadingFamily=false;UpdateActions();return;}
        loadingFamily=true;UpdateActions();
        int selected=family.SelectedId;
        try {
            var data=await DatabaseHelper.QueryUserAsync("GetStudentDetails",DatabaseHelper.Int("@AdmissionId",selected));
            if(IsDisposed||version!=familyLoadVersion||selected!=family.SelectedId||data.Rows.Count==0)return;
            var r=data.Rows[0];familyAnchor=selected;parent.Text=Convert.ToString(r["FatherName"]);contact.Text=Convert.ToString(r["ContactNumber"]);address.Text=Convert.ToString(r["Address"]);
            familyReference=Convert.ToString(r.IsNull("AdmissionNumber")?r["QuickReference"]:r["AdmissionNumber"])??"";
            ShowFamilyPicker(false);name.Focus();
        }catch(Exception ex){if(version==familyLoadVersion&&selected==family.SelectedId){familyAnchor=0;Fail(ex);}}
        finally{if(!IsDisposed&&version==familyLoadVersion){loadingFamily=false;UpdateActions();}}
    }
    private async Task LoadFeesAsync()
    {
        int version=++loadVersion;feesLoaded=false;rates=new();fees.DataSource=null;UpdateTotal();
        loadingFees=placement.SelectedId>0;UpdateActions();
        if(!loadingFees)return;
        int selectedPlacement=placement.SelectedId;
        try{
            var data=await Task.Run(()=>DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.Admit';
                """ + QuickRegistrationWriter.FeeQuery,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Placement",selectedPlacement)));
            if(IsDisposed||version!=loadVersion)return;
            var missing=data.AsEnumerable().Where(r=>r.IsNull("Amount")).ToArray();
            string missingNames=string.Join(", ",missing.Select(r=>Convert.ToString(r["FeeTypeName"])));
            foreach(var row in missing)data.Rows.Remove(row);
            expectedQuote=QuickRegistrationWriter.Quote(data);SetRates(data);feesLoaded=missing.Length==0;
            if(!feesLoaded)SetStatus("Configure the required class fees before saving: "+missingNames+".");
            foreach(DataGridViewRow row in fees.Rows)if(Convert.ToBoolean(((DataRowView)row.DataBoundItem).Row["IsMandatory"]))row.Cells["Include"].ReadOnly=true;
            UpdateTotal();
        }catch(Exception ex){if(version==loadVersion)Fail(ex);}
        finally{if(version==loadVersion){loadingFees=false;UpdateActions();}}
    }
    private void SetRates(DataTable data){
        data.Columns.Add("NetAmount",typeof(decimal),"Amount - Discount");
        data.Columns.Add("OriginalDiscount",typeof(decimal));foreach(DataRow row in data.Rows)row["OriginalDiscount"]=row["Discount"];
        if(!data.Columns.Contains("HasPayments"))data.Columns.Add(new DataColumn("HasPayments",typeof(bool)){DefaultValue=false});
        rates=data;fees.DataSource=rates;
    }
    private bool ReadyToSave=>!busy&&!loadingFamily&&!loadingFees&&feesLoaded&&(savedStudent==0||(savedStatus=="Pending"&&savedSnapshot.Length>0))&&(family.SelectedId<=0||family.SelectedId==familyAnchor);
    private string[] BasicValues()=>new[]{name.Text.Trim(),parent.Text.Trim(),contact.Text.Trim(),address.Text.Trim()};
    private bool HasPendingChanges=>savedStudent>0&&savedStatus=="Pending"&&savedBasics.Length==4&&(!savedBasics.SequenceEqual(BasicValues())||rates.AsEnumerable().Any(r=>Convert.ToDecimal(r["Discount"])!=Convert.ToDecimal(r["OriginalDiscount"])));
    private void PendingEditChanged(){UpdateActions();if(!busy&&HasPendingChanges)SetStatus("Save changes before printing or collecting payment.");}
    private void UpdateActions(){save.Enabled=ReadyToSave&&CurrentUser.HasPermission("Students.Admit");save.Text=savedStudent>0?"Save Changes":"Save Registration";next.Enabled=!busy&&savedStudent>0;bill.Enabled=!busy&&!HasPendingChanges&&savedStudent>0&&savedStatus!="Cancelled"&&CurrentUser.HasPermission("Fees.View");collect.Enabled=!busy&&!HasPendingChanges&&savedStudent>0&&savedStatus!="Cancelled"&&CurrentUser.HasPermission("Fees.Collect");
        bool editing=!busy&&(savedStudent==0||savedStatus=="Pending");family.Enabled=year.Enabled=placement.Enabled=joining.Enabled=editing&&savedStudent==0;
        name.ReadOnly=contact.ReadOnly=address.ReadOnly=!editing;
        parent.ReadOnly=!editing||familyAnchor>0||loadingFamily;
        linkFamily.Enabled=editing&&savedStudent==0&&familyAnchor==0&&!loadingFamily;
        registrationCard.SetHeaderText("Registration Details",familyAnchor>0
            ? $"Linked family · {familyReference} · Parent corrections in Admission / Review."
            : "Enter a child, or link an existing family.");
        fees.ReadOnly=!editing;
        // Switching grid ReadOnly can reset individual column/cell flags.
        // Restore the edit policy after every busy or saved-state transition.
        foreach(DataGridViewColumn column in fees.Columns)column.ReadOnly=column.Name switch{
            "Discount"=>!CurrentUser.HasPermission("Fees.Adjust"),"Include"=>savedStudent>0,_=>true};
        foreach(DataGridViewRow row in fees.Rows)if(row.DataBoundItem is DataRowView data){
            row.Cells["Include"].ReadOnly=savedStudent>0||Convert.ToBoolean(data.Row["IsMandatory"]);
            bool paid=Convert.ToBoolean(data.Row["HasPayments"]);
            row.Cells["Discount"].ReadOnly=paid||!CurrentUser.HasPermission("Fees.Adjust");
            row.Cells["Discount"].ToolTipText=paid?"This fee has a payment. Use Fee Corrections to adjust it.":"";
        }}
    private void UpdateTotal(){if(!rates.Columns.Contains("Include")){total.Text="Bill Total: Rs. 0";return;}decimal amount=rates.AsEnumerable().Where(r=>Convert.ToBoolean(r["Include"])).Sum(r=>Convert.ToDecimal(r["Amount"])-Convert.ToDecimal(r["Discount"]));total.Text=$"Bill Total: Rs. {amount:N0}";PendingEditChanged();}
    private void ClearChild()
    {
        if(busy)return;
        // Start a fresh child; only the family link and shared details carry over.
        // Invalidate any previous fee load before clearing the placement.
        ++loadVersion;savedStudent=0;savedStatus="";savedSnapshot="";expectedQuote="";loadingFees=false;feesLoaded=false;
        name.Clear();joining.Value=DateTime.Today;year.SelectedValue=defaultAcademicYear;
        placement.SelectedValue=0;rates=new();fees.DataSource=null;
        validator.Begin();UpdateTotal();UpdateActions();name.Focus();
        SetStatus("Enter the next child and choose their placement. Family details are retained.");
    }
    private async Task SaveAsync()
    {
        if(!ReadyToSave)return;
        var v=validator;v.Begin();v.Required(name);v.Required(parent);v.Required(contact);v.Required(year);v.Required(placement);
        v.Check(contact,PakistanPhoneNumber.IsValidOptional(contact.Text),PakistanPhoneNumber.ValidationMessage);
        if(!v.Finish())return;
        if(!fees.EndEdit()){Warn("Correct the discount amount before saving.");return;}var row=placements.AsEnumerable().First(r=>Convert.ToInt32(r["ClassSectionId"])==placement.SelectedId);
        var selected=rates.AsEnumerable().Where(r=>Convert.ToBoolean(r["Include"])).ToArray();
        if(selected.Any(r=>!WholeRupees.IsWhole(Convert.ToDecimal(r["Discount"]))||!WholeRupees.IsWhole(Convert.ToDecimal(r["Amount"]))||Convert.ToDecimal(r["Discount"])<0||Convert.ToDecimal(r["Discount"])>Convert.ToDecimal(r["Amount"]))) {Warn("Enter a whole-rupee discount between zero and its fee amount.");return;}
        if(savedStudent>0){await SavePendingAsync();return;}
        busy=true;UpdateActions();using var hold=BeginBusy("Saving registration…",family,parent,contact,address,name,year,placement,joining,fees);
        try{
            // Re-read the family parent before saving. The copied contact/address
            // belong to this child; a linked parent cannot be renamed here.
            if(familyAnchor>0){
                var anchor=await DatabaseHelper.QueryUserAsync("GetStudentDetails",DatabaseHelper.Int("@AdmissionId",familyAnchor));
                if(anchor.Rows.Count==0){Warn("The linked family record could not be found. Choose the family again.");return;}
                string parentName=Convert.ToString(anchor.Rows[0]["FatherName"])?.Trim()??"";
                if(parent.Text.Trim()!=parentName){parent.Text=parentName;Warn("The linked parent name has changed. Review it and save again. Correct parent details in Admission / Review.");return;}
            }
            var args=new List<SqlParameter>{DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Bit("@QuickRegistration",true),DatabaseHelper.P("@FamilyStudentId",familyAnchor>0?familyAnchor:null),
                DatabaseHelper.Date("@AdmissionDate",joining.Value.Date),DatabaseHelper.Int("@AdmissionCampusId",Convert.ToInt32(row["CampusId"])),DatabaseHelper.Int("@AcademicYearId",year.SelectedId),DatabaseHelper.Int("@ClassSectionId",placement.SelectedId),
                DatabaseHelper.P("@StudentName",name.Text.Trim()),DatabaseHelper.P("@FatherName",parent.Text.Trim()),DatabaseHelper.P("@ContactNumber",contact.Text.Trim()),DatabaseHelper.P("@Address",address.Text.Trim()),DatabaseHelper.P("@Disability","No"),DatabaseHelper.Bit("@UsesTransport",selected.Any(r=>Convert.ToBoolean(r["IsTransportLinked"]))),
                new SqlParameter("@SelectedFeeTypeIds",SqlDbType.Structured){TypeName="dbo.FeeTypeIdList",Value=Tvp.FeeTypeIds(selected.Select(r=>Convert.ToInt32(r["FeeTypeId"])))},
                new SqlParameter("@AdmissionDiscounts",SqlDbType.Structured){TypeName="dbo.AdmissionFeeDiscountType",Value=Tvp.AdmissionFeeDiscounts(selected.Where(r=>Convert.ToDecimal(r["Discount"])>0).Select(r=>(Convert.ToInt32(r["FeeTypeId"]),Convert.ToDecimal(r["Discount"]))))}};
            foreach(string key in new[]{"DateOfBirth","PlaceOfBirth","Gender","BFormNumber","Religion","BloodGroup","AlternateContactNumber","PreviousSchool","FatherOccupation"})args.Add(DatabaseHelper.P("@"+key,null));
            // Nested fee operations can return their own result sets first.
            var results=await QuickRegistrationWriter.SaveNewAsync(placement.SelectedId,expectedQuote,args.ToArray());
            var result=results.Tables.Cast<DataTable>().Last(t=>t.Columns.Contains("AdmissionId")&&t.Columns.Contains("QuickReference")&&t.Rows.Count>0);
            savedStudent=Convert.ToInt32(result.Rows[0]["AdmissionId"]);savedStatus="Pending";savedSnapshot="";familyAnchor=familyAnchor>0?familyAnchor:savedStudent;StudentSearchCache.Invalidate();
            savedBasics=BasicValues();foreach(DataRow rate in rates.Rows)rate["OriginalDiscount"]=rate["Discount"];
            string reference=result.Columns.Contains("QuickReference")?Convert.ToString(result.Rows[0]["QuickReference"])??"":$"QR-{savedStudent}";
            if(familyAnchor==savedStudent)familyReference=reference;
            busy=false;await LoadRegistrationAsync(savedStudent);busy=true;
            SetStatus($"Saved {reference}. Print the family bill, collect payment or add another child.");
        }catch(Exception ex){Fail(ex);}finally{busy=false;UpdateActions();}
    }
    private async Task LoadRegistrationAsync(int id)
    {
        if(busy||id<=0)return;busy=true;UpdateActions();
        try{
            var loaded=await QuickRegistrationWriter.LoadAsync(id);
            var data=loaded.Details;
            if(IsDisposed||data.Rows.Count==0)return;var r=data.Rows[0];
            ++familyLoadVersion;loadingFamily=false;savedStudent=id;savedStatus=Convert.ToString(r["Status"])??"";family.Clear();familyAnchor=id;ShowFamilyPicker(false);
            parent.Text=Convert.ToString(r["FatherName"]);contact.Text=Convert.ToString(r["ContactNumber"]);address.Text=Convert.ToString(r["Address"]);name.Text=Convert.ToString(r["StudentName"]);
            joining.Value=Convert.ToDateTime(r["AdmissionDate"]);
            year.SelectedValue=r["AcademicYearId"];placement.SelectedValue=r["ClassSectionId"];
            int version=++loadVersion;
            var stored=loaded.Fees;
            savedSnapshot=loaded.Snapshot;
            if(IsDisposed||version!=loadVersion)return;savedBasics=BasicValues();SetRates(stored);loadingFees=false;feesLoaded=true;UpdateTotal();
            string reference=Convert.ToString(r["QuickReference"])??"";
            familyReference=Convert.ToString(r.IsNull("AdmissionNumber")?r["QuickReference"]:r["AdmissionNumber"])??"";
            if(savedStatus=="Pending")SetStatus($"{reference} · Pending. Save edits here; review and confirm from Pending Registrations.");
            else SetStatus(savedStatus=="Active"?$"Admitted · Admission {r["AdmissionNumber"]} · Registration {r["RegistrationNumber"]}":"Registration cancelled. History is retained.");
        }catch(Exception ex){Fail(ex);}finally{busy=false;UpdateActions();}
    }
    private async Task SavePendingAsync(){
        busy=true;UpdateActions();using var hold=BeginBusy("Saving changes…",name,parent,contact,address,fees);
        bool changed=false;
        try{
            var current=await DatabaseHelper.QueryUserAsync("GetStudentDetails",DatabaseHelper.Int("@AdmissionId",savedStudent));
            if(current.Rows.Count==0||Convert.ToString(current.Rows[0]["Status"])!="Pending"){Warn("This registration is no longer pending. Refresh the list before editing.");return;}
            var row=current.Rows[0];
            // Preserve the current stored parent even if another workspace has
            // corrected it since this registration was opened.
            var args=new List<SqlParameter>{DatabaseHelper.Int("@AdmissionId",savedStudent),DatabaseHelper.P("@StudentName",name.Text.Trim()),DatabaseHelper.P("@FatherName",row["FatherName"]),DatabaseHelper.P("@ContactNumber",contact.Text.Trim()),DatabaseHelper.P("@Address",address.Text.Trim())};
            foreach(string key in new[]{"DateOfBirth","Gender","BFormNumber","Religion","BloodGroup","AlternateContactNumber","PreviousSchool","PlaceOfBirth","FatherOccupation","Disability","BiseEnrolmentNumber","CasteTribe"})args.Add(DatabaseHelper.P("@"+key,row[key]));
            args.Add(new SqlParameter("@Discounts",SqlDbType.Structured){TypeName="dbo.StudentFeeDiscountType",Value=Tvp.StudentFeeDiscounts(rates.AsEnumerable().Where(r=>Convert.ToDecimal(r["Discount"])!=Convert.ToDecimal(r["OriginalDiscount"])).Select(r=>(Convert.ToInt32(r["StudentFeeAssignmentId"]),Convert.ToDecimal(r["Discount"]))))});
            await QuickRegistrationWriter.SavePendingAsync(savedStudent,savedSnapshot,args.ToArray());StudentSearchCache.Invalidate();changed=true;
        }catch(Exception ex){Fail(ex);}finally{busy=false;UpdateActions();}
        if(changed){await LoadRegistrationAsync(savedStudent);SetStatus("Registration changes saved. Review / Confirm is on Pending Registrations.");}
    }
    internal async Task<bool> OpenRegistrationAsync(int id)
    {
        if(busy)return false;
        if(!fees.EndEdit())return false;
        if(HasUnsavedChanges&&!Confirm("Discard the unsaved registration changes and reload this record?",UiText.Dialogs.UnsavedChanges))return false;
        await LoadRegistrationAsync(id);
        return savedStudent==id;
    }
    private async Task PrintBillAsync()
    {
        if(savedStudent<=0)return;
        try{
            var data=await Task.Run(()=>DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission);
                SELECT COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,F.FeeTypeName,F.DisplayOrder FeeDisplayOrder,B.BillingMonth,B.DiscountAmount,dbo.GetFeeChargeBalance(B.BillingChargeId) Due
                FROM dbo.Students S JOIN dbo.BillingCharges B ON B.AdmissionId=S.AdmissionId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
                WHERE (S.AdmissionId=@Admission OR S.AdmissionId IN(SELECT AdmissionId FROM dbo.FamilyMembers WHERE FamilyId=@Family))
                AND B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0 ORDER BY S.AdmissionId,B.BillingMonth,F.DisplayOrder,F.FeeTypeName,B.BillingChargeId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Admission",savedStudent)));
            if(data.Rows.Count==0){Warn("There are no outstanding charges for this family.");return;}
            var institution=await InstitutionProfile.GetPrintIdentityAsync();
            AdmissionBillReport.ShowFamily(FindForm(),institution,parent.Text.Trim(),data);
        }catch(Exception ex){Fail(ex);}
    }
}
