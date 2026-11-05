using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckAdmissionFeeFlowFixes()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        void Sign(params string[] permissions)=>CurrentUser.SignIn(920,"Fictional flow operator","Operator",false,permissions);
        try
        {
            Sign("Students.View","Fees.View","Fees.Collect");
            using var page=new FeeCollectionPage(initialize:false);
            T Field<T>(string name)=>(T)typeof(FeeCollectionPage).GetField(name,flags)!.GetValue(page)!;
            using var owner=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new(-30000,-30000),ClientSize=new(1400,900)};
            owner.Controls.Add(page);owner.Show();Application.DoEvents();
            var grid=Field<DataGridView>("charges");var row=grid.Rows[grid.Rows.Add()];
            row.Cells["BillingChargeId"].Value=1L;row.Cells["AdmissionId"].Value=1;row.Cells["EnrollmentId"].Value=1;
            row.Cells["NetAmount"].Value=row.Cells["Balance"].Value=100m;
            row.Cells["FeeTypeName"].Value="Fictional tuition";row.Cells["BillingMonth"].Value="October 2026";row.Cells["AppliedDiscount"].Value=0m;
            grid.Enabled=true;grid.CurrentCell=row.Cells["FeeTypeName"];
            foreach(var grants in new[]{Array.Empty<string>(),new[]{"Fees.View","Fees.Collect"},new[]{"Fees.View","Fees.Adjust"},new[]{"Fees.View","Fees.Collect","Fees.Adjust"}})
            {
                Sign(grants);
                typeof(DataGridView).GetMethod("OnSelectionChanged",flags)!.Invoke(grid,new object[]{EventArgs.Empty});
                typeof(FeeCollectionPage).GetMethod("BuildChargeMenu",flags)!.Invoke(page,new object[]{row.Index});
                bool allowed=grants.Contains("Fees.Collect")&&grants.Contains("Fees.Adjust");
                bool menu=Field<ContextMenuStrip>("chargeMenu").Items.Cast<ToolStripItem>().Any(item=>item.Text?.StartsWith("Cancel This",StringComparison.Ordinal)==true);
                Check(Field<ModernButton>("counterReverseButton").Enabled==allowed && menu==allowed,"Main and contextual cancellation share the finance permission boundary");
            }
            row.Cells["Balance"].Value=50m;
            Check(!(bool)typeof(FeeCollectionPage).GetMethod("CanCancelCharge",flags)!.Invoke(page,new object[]{row})!,"Partially paid charges cannot use unpaid cancellation");
            row.Cells["Balance"].Value=100m;
            var placements=new DataTable();placements.Columns.Add("EnrollmentId",typeof(int));placements.Columns.Add("Label");placements.Columns.Add("Status");
            placements.Rows.Add(1,"Pending","Pending");placements.Rows.Add(2,"Active","Active");
            Field<LookupBox>("enrollment").Bind(placements,"EnrollmentId","Label");
            foreach(int id in new[]{1,2})
            {
                Field<LookupBox>("enrollment").SelectedValue=id;
                typeof(FeeCollectionPage).GetField("transportKnown",flags)!.SetValue(page,true);
                typeof(FeeCollectionPage).GetMethod("UpdateTransportButton",flags)!.Invoke(page,null);
                Check(Field<ModernButton>("transportButton").Enabled==(id==2),"Pending transport stays in registration; active placement transport remains available");
            }
            Sign("Reports.View","Fees.View");Check(OutstandingAgingReportPage.CanOpenStudentAccount,"Aging account access does not require annual-receipt permission");
            Sign("Fees.Collect","Fees.View");Check(OutstandingAgingReportPage.CanOpenStudentAccount,"Collection access can open the contextual account");
            Sign("Fees.Statement.View","Fees.View");Check(!OutstandingAgingReportPage.CanOpenStudentAccount,"Annual receipt permission does not grant account access");
            Sign("Reports.View");Check(!OutstandingAgingReportPage.CanOpenStudentAccount,"Contextual account still requires its financial reader permission");

            var submitted=new AdmissionSubmission("TEST-FLOW","Typed Student","Typed Parent",DateTime.Today,1,false,0);
            int reads=0,writes=0;
            Task<DataTable> Empty(AdmissionSubmission _){reads++;return Task.FromResult(new DataTable());}
            var rejected=AdmissionSaveConfirmation.SubmitAsync(submitted,()=>{writes++;return Task.FromException<DataSet>(FlowSqlError(51019,60));},Empty).GetAwaiter().GetResult();
            Check(rejected.Rejected&&writes==1&&reads==0,"A verified server rollback is a rejection and is never retried or disguised as an unknown result");
            var uncertain=AdmissionSaveConfirmation.SubmitAsync(submitted,()=>{writes++;return Task.FromException<DataSet>(FlowSqlError(51019,1));},Empty).GetAwaiter().GetResult();
            Check(!uncertain.Rejected&&uncertain.Admission is null&&reads==1,"An unverified SQL failure still requires outcome recovery");
            Check(!AdmissionSaveConfirmation.IsVerifiedRejection(new IOException("Lost commit response")),"A lost response never becomes a verified rejection");
            Sign("Students.Admit","Students.View");
            using var admission=new StudentAdmissionPage();
            var type=typeof(StudentAdmissionPage);
            var name=(ModernTextBox)type.GetField("name",flags)!.GetValue(admission)!;name.Text="Retained typed student";
            type.GetField("unconfirmedAdmission",flags)!.SetValue(admission,submitted);
            var apply=(Task)type.GetMethod("ApplyAdmissionConfirmationAsync",flags)!.Invoke(admission,new object[]{submitted,rejected})!;
            Check(apply.IsCompletedSuccessfully&&type.GetField("unconfirmedAdmission",flags)!.GetValue(admission) is null&&name.Text=="Retained typed student","Verified refusal releases the admission editor without discarding its entries");
            Check(((ModernButton)type.GetField("saveButton",flags)!.GetValue(admission)!).Enabled,"Save is available for an explicit corrected submission after verified refusal");
        }
        finally {CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }

    private static SqlException FlowSqlError(int number,byte state)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var constructor=typeof(SqlError).GetConstructors(flags).First(item=>item.GetParameters().Length>=8);
        object? Value(ParameterInfo p)=>p.ParameterType==typeof(byte)?(p.Name!.Contains("State",StringComparison.OrdinalIgnoreCase)?state:(byte)16)
            :p.ParameterType==typeof(int)?(p.Name!.Contains("Number",StringComparison.OrdinalIgnoreCase)&&!p.Name.Contains("line",StringComparison.OrdinalIgnoreCase)?number:1)
            :p.ParameterType==typeof(string)?"Fictional verified refusal":p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null;
        var error=(SqlError)constructor.Invoke(constructor.GetParameters().Select(Value).ToArray());
        var errors=(SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection),true)!;
        typeof(SqlErrorCollection).GetMethod("Add",flags)!.Invoke(errors,new object[]{error});
        return (SqlException)typeof(SqlException).GetMethods(BindingFlags.Static|BindingFlags.NonPublic)
            .First(item=>item.Name=="CreateException"&&item.GetParameters().Length==2).Invoke(null,new object[]{errors,"16.0"})!;
    }

    private static void CheckAdmissionFeeFlowSqlCompatibility()
    {
        string value=Environment.GetEnvironmentVariable("FMSC_FLOW_FIX_TEST_CONNECTION")??throw new InvalidOperationException("Missing owned fixture connection");
        var settings=new SqlConnectionStringBuilder(value);
        string owner=Environment.GetEnvironmentVariable("FMSC_FLOW_FIX_TEST_OWNER")??"";
        if(!settings.InitialCatalog.Equals("FMSC_BillingChecks_"+owner,StringComparison.Ordinal)||owner.Length!=32)
            throw new InvalidOperationException("Expected uniquely owned fictional fixture");
        using var connection=new SqlConnection(value);connection.Open();
        using var command=new SqlCommand("SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_BillingChecksOwner'",connection);
        if(Convert.ToString(command.ExecuteScalar())!=owner)throw new InvalidOperationException("Fixture owner mismatch");
        DatabaseCompatibility.Validate(connection);Check(true,"Baseline 1 satisfies the complete application startup/restore contract");
    }
}
