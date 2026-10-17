using System.Data;
using System.Reflection;
using System.Text.Json;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

internal static partial class Program
{
    private static async Task CheckFamilyCounterSqlAsync()
    {
        // This mode is provisioned/cleaned by Run-Level2. A normal app binary
        // cannot reach the first SQL call, and every guarded open checks ownership.
        var target=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Core.Level2Target")
            ?? throw new InvalidOperationException("Family SQL checks require the owned Level 2 binary.");
        target.GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
        string root=(string)target.GetProperty("Root",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"credentials.json")));
        int Ref(string key)=>fixture.RootElement.GetProperty(key).GetInt32();
        var admins=await DatabaseHelper.QuerySqlAsync("SELECT UserId FROM dbo.Users WHERE Username=N'level2_admin';");
        int admin=Convert.ToInt32(admins.Rows[0]["UserId"]);
        CurrentUser.SignIn(admin,"Fictional family SQL owner","Administrator",false,Array.Empty<string>());
        async Task<int> Count(string text)=>Convert.ToInt32((await DatabaseHelper.QuerySqlAsync(text)).Rows[0][0]);
        Check(await Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Family SQL checks require the empty fictional reference fixture");
        int year=DateTime.UtcNow.Year;
        async Task<int> Admit(string name,bool current,bool quick,int? anchor=null)
        {
            await DatabaseHelper.QueryUserAsync("AdmitStudent",
                DatabaseHelper.Date("@AdmissionDate",new DateTime(current?year:year-1,4,1)),
                DatabaseHelper.Int("@AdmissionCampusId",Ref("campus")),DatabaseHelper.Int("@AcademicYearId",Ref(current?"nextYear":"sourceYear")),
                DatabaseHelper.Int("@ClassSectionId",Convert.ToInt32((await DatabaseHelper.QuerySqlAsync(
                    "SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=@Campus AND ClassId=@Class AND SectionId=@Section;",
                    DatabaseHelper.Int("@Campus",Ref("campus")),DatabaseHelper.Int("@Class",Ref("sourceClass")),DatabaseHelper.Int("@Section",Ref("section")))).Rows[0][0])),
                DatabaseHelper.P("@StudentName",name),DatabaseHelper.P("@FatherName","Fictional Family Parent"),DatabaseHelper.Date("@DateOfBirth",new DateTime(year-10,1,1)),
                DatabaseHelper.P("@PlaceOfBirth","Example City"),DatabaseHelper.P("@Gender","Male"),DatabaseHelper.P("@BFormNumber",null),
                DatabaseHelper.P("@Religion",null),DatabaseHelper.P("@BloodGroup",null),DatabaseHelper.P("@ContactNumber",null),
                DatabaseHelper.P("@AlternateContactNumber",null),DatabaseHelper.P("@Address","Fictional Test Address"),DatabaseHelper.P("@PreviousSchool",null),
                DatabaseHelper.P("@FatherOccupation",null),DatabaseHelper.P("@Disability","No"),DatabaseHelper.Bit("@UsesTransport",false),
                new SqlParameter("@SelectedFeeTypeIds",SqlDbType.Structured){TypeName="dbo.FeeTypeIdList",Value=Tvp.FeeTypeIds(Array.Empty<int>())},
                new SqlParameter("@AdmissionDiscounts",SqlDbType.Structured){TypeName="dbo.AdmissionFeeDiscountType",Value=Tvp.AdmissionFeeDiscounts(Array.Empty<(int,decimal)>())},
                DatabaseHelper.Bit("@QuickRegistration",quick),DatabaseHelper.P("@FamilyStudentId",anchor));
            return Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT AdmissionId FROM dbo.Students WHERE StudentName=@Name;",DatabaseHelper.P("@Name",name))).Rows[0][0]);
        }
        int first=await Admit("Family Example One",false,false);
        int second=await Admit("Family Example Two",false,false,first);
        var decisions=new DataTable();decisions.Columns.Add("AdmissionId",typeof(int));decisions.Columns.Add("Decision");
        decisions.Rows.Add(first,"PROMOTE");decisions.Rows.Add(second,"PROMOTE");
        int destination=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=@Campus AND ClassId=@Class AND SectionId=@Section;",
            DatabaseHelper.Int("@Campus",Ref("campus")),DatabaseHelper.Int("@Class",Ref("destinationClass")),DatabaseHelper.Int("@Section",Ref("section")))).Rows[0][0]);
        await DatabaseHelper.QueryUserAsync("BulkPromoteStudents",DatabaseHelper.Int("@SourceAcademicYearId",Ref("sourceYear")),DatabaseHelper.Int("@DestinationAcademicYearId",Ref("nextYear")),
            DatabaseHelper.Int("@SourceCampusId",Ref("campus")),DatabaseHelper.Int("@SourceClassId",Ref("sourceClass")),DatabaseHelper.Int("@SourceSectionId",Ref("section")),
            DatabaseHelper.Int("@DestinationClassSectionId",destination),DatabaseHelper.Date("@EffectiveDate",new DateTime(year,1,1)),DatabaseHelper.P("@Remarks","Fictional family return scenario"),
            new SqlParameter("@Decisions",SqlDbType.Structured){TypeName="dbo.BulkPromotionDecisionType",Value=decisions});
        int added=await Admit("Family Example New Child",true,true,first);
        var members=await FamilyBillPreview.ReadMembersAsync(added);
        Check(members.Rows.Count==3&&members.AsEnumerable().Count(FamilyBillingData.AwaitsReturn)==2,"A linked QR child retains both promoted siblings awaiting return");
        Check(FamilyBillingData.Readiness(members.AsEnumerable().Single(row=>Convert.ToInt32(row["AdmissionId"])==added))=="Pending Registration","New family child remains pending after linking");
        int chargesBefore=await Count("SELECT COUNT(*) FROM dbo.BillingCharges;");
        var bill=await FamilyBillPreview.ReadChargesAsync(added,new[]{first,second,added});
        Check(bill.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Due"]))==21000m,"Initial family bill contains two older 7000 dues and the new child's 7000 opening charges only");
        Check(bill.AsEnumerable().Any(row=>Convert.ToString(row["Reference"])!.StartsWith("QR-")),"Mixed family bill preserves the new child's temporary reference");
        Check(await Count("SELECT COUNT(*) FROM dbo.BillingCharges;")==chargesBefore,"Reading a family bill creates no charges");
        int enrollment=Convert.ToInt32(members.AsEnumerable().Single(row=>Convert.ToInt32(row["AdmissionId"])==first)["EnrollmentId"]);
        await DatabaseHelper.QueryUserAsync("ConfirmPromotedAdmission",DatabaseHelper.Int("@EnrollmentId",enrollment),DatabaseHelper.Date("@ReturnDate",new DateTime(year,4,1)),
            DatabaseHelper.Bit("@UsesTransport",false),new SqlParameter("@FeeChoices",SqlDbType.Structured){TypeName="dbo.ReturnFeeChoiceType",Value=Tvp.ReturnFeeChoices(new[]{(Ref("tuition"),6000m,0m),(Ref("admissionFee"),2000m,0m)})});
        members=await FamilyBillPreview.ReadMembersAsync(first);
        Check(members.AsEnumerable().Count(FamilyBillingData.AwaitsReturn)==1,"Confirming one return leaves the other sibling waiting");
        bill=await FamilyBillPreview.ReadChargesAsync(first,new[]{first,second,added});
        Check(bill.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Due"]))==29000m,"Refreshed family bill adds exactly the confirmed child's 8000 opening charges");
        var selected=await FamilyBillPreview.ReadChargesAsync(first,new[]{first,added});
        Check(selected.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Due"]))==22000m,"Selected-child bill excludes the unchecked sibling's 7000 dues");
        int outsider=await Admit("Unrelated Family Example",true,false);
        bool refused=false;try{await FamilyBillPreview.ReadChargesAsync(first,new[]{first,outsider});}catch(SqlException ex) when(ex.Number==51000){refused=true;}
        Check(refused,"SQL refuses a selected student from an unrelated family");
        await DatabaseHelper.ExecuteUserAsync("AddStudentSibling",DatabaseHelper.Int("@AdmissionId",first),
            DatabaseHelper.Int("@SiblingAdmissionId",outsider),DatabaseHelper.P("@RelationshipType","Sibling"));
        refused=false;try{await FamilyBillPreview.ReadChargesAsync(first,new[]{first,second,added},wholeFamily:true);}catch(SqlException ex) when(ex.Number==51000){refused=true;}
        Check(refused,"A newly linked child invalidates a stale whole-family bill rather than silently disappearing");
        var member=members.AsEnumerable().Single(row=>Convert.ToInt32(row["AdmissionId"])==second);
        await DatabaseHelper.ExecuteUserAsync("DeleteStudentSibling",DatabaseHelper.Int("@FamilyMemberId",Convert.ToInt32(member["FamilyMemberId"])));
        refused=false;try{await FamilyBillPreview.ReadChargesAsync(first,new[]{first,second});}catch(SqlException ex) when(ex.Number==51000){refused=true;}
        Check(refused,"SQL refuses a stale selection after a family link changes");
        CurrentUser.SignIn(int.MaxValue,"Unprivileged fictional fixture","Operator",false,Array.Empty<string>());
        refused=false;try{await FamilyBillPreview.ReadChargesAsync(first,new[]{first});}catch(SqlException ex) when(ex.Number==50003){refused=true;}
        Check(refused,"SQL independently refuses a bill read for an unknown user identity");
        Check(await Count("SELECT COUNT(*) FROM dbo.FeePayments;")==0,"Bill reads and explicit return confirmation never record a payment");
    }
}
