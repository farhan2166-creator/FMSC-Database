using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckCertificateCollectionWorkflow()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(991,"Certificate workflow fixture","Administrator",false,Array.Empty<string>());
            foreach(var item in new[]{
                ("SportsCertificatePage","SportsCertificateId","selectedSportsCertificateId","certificateDate,sportName,performance",false),
                ("NoObjectionCertificatePage","NoObjectionCertificateId","selectedNoObjectionCertificateId","certificateDate",false),
                ("ProvisionalCertificatePage","ProvisionalCertificateId","selectedProvisionalCertificateId","examType,examYear,boardName,rollNumber,marksObtained,marksOutOf,grade,certificateDate,otherSubjects,subjectsList,subjectChoices",false),
                ("RegularLecturerCertificatePage","RegularLecturerCertificateId","selectedRegularLecturerCertificateId","roleTitle,subjectTaught,certificateDate",true),
                ("BankAccountOpeningCertificatePage","BankAccountOpeningCertificateId","selectedBankAccountOpeningCertificateId","bankName,branchName,designation,monthlyIncome,purpose,certificateDate",true),
                ("CharacterCertificatePage","CharacterCertificateIssueId","selectedCertificateRecordId","examType,rollNumber,remarks,academicYear,certificateDate",false),
                ("DateOfBirthCertificatePage","DateOfBirthCertificateIssueId","selectedCertificateRecordId","certificateDate",false),
                ("RegularStudentCertificatePage","RegularStudentCertificateIssueId","selectedRegularStudentCertificateId","certificateDate",false),
                ("LeaveCertificatePage","LeaveCertificateIssueId","selectedLeaveRecordId","promoted,academic,attendance,remarks",false),
                ("CollegeLeaveCertificatePage","LeaveCertificateIssueId","selectedLeaveRecordId","promoted,academic,attendance,remarks",false)})
            {
                string ns=item.Item5?"FMSC_SMS.Pages.Staff.Certificates.":"FMSC_SMS.Pages.Students.Certificates.";
                var type=typeof(LeaveCertificatePage).Assembly.GetType(ns+item.Item1,true)!;
                object?[] args=item.Item1 switch {
                    "LeaveCertificatePage"=>new object?[]{null,false},
                    "SportsCertificatePage" or "NoObjectionCertificatePage" or "ProvisionalCertificatePage"=>new object?[]{false,null,null},
                    "RegularStudentCertificatePage"=>new object?[]{false,null,null},
                    "DateOfBirthCertificatePage"=>new object?[]{false,null},
                    "BankAccountOpeningCertificatePage"=>new object?[]{false,null},
                    "RegularLecturerCertificatePage"=>Array.Empty<object?>(),
                    _=>new object?[]{false}};
                using var page=(Control)Activator.CreateInstance(type,flags|BindingFlags.Public,null,args,null)!;
                T Field<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(page)!;
                void Set(string name,object value){type.GetField(name,flags)?.SetValue(page,value);}
                void Apply()=>type.GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
                using var person=new DataTable();
                foreach(string key in new[]{"AdmissionId","StaffId","EnrollmentId","ClassId"})person.Columns.Add(key,typeof(int));
                foreach(string name in new[]{"StudentName","StaffName","FatherName","ClassName","Status","ProgramType"})person.Columns.Add(name);
                person.Columns.Add("DateOfBirth",typeof(DateTime));
                var selected=person.Rows.Add(41,41,201,1,"Student","Staff","Parent","Ten","Active","College",new DateTime(2010,1,1));
                if(item.Item5)Field<StaffPicker>("staff").SetStaff(selected);else Field<StudentPicker>("student").SetStudent(selected);
                using var rows=new DataTable();
                foreach(string key in new[]{item.Item2,"CertificateRecordId","LeaveRecordId","RegularStudentCertificateId","AdmissionId","EnrollmentId"}.Distinct())rows.Columns.Add(key,typeof(int));
                foreach(string name in new[]{"CollectionStatus","ClassName","IssueType","ExamType"})rows.Columns.Add(name);
                foreach(string date in new[]{"CollectedAt","CertificateDate","IssuedAt","DateOfBirth","VoidedAt"})rows.Columns.Add(date,typeof(DateTime));
                rows.Columns.Add("ContentVersion",typeof(byte[]));
                var row=rows.NewRow();foreach(DataColumn column in rows.Columns)if(column.DataType==typeof(int))row[column]=11;
                row[item.Item2]=101;row["AdmissionId"]=41;row["EnrollmentId"]=201;row["CollectionStatus"]="Collected";
                row["CollectedAt"]=DateTime.Today;row["CertificateDate"]=DateTime.Today;row["IssuedAt"]=DateTime.Now;row["DateOfBirth"]=new DateTime(2010,1,1);row["ClassName"]="";row["ExamType"]="SSC";row["IssueType"]="Original";row["ContentVersion"]=new byte[8];rows.Rows.Add(row);
                Set("certificateResults",rows);Set(item.Item3,item.Item2.EndsWith("IssueId")?11:101);
                Set("selectedIssueId",101);Set("selectedIssueCollected",true);Set("selectedStoredExamType","SSC");
                Set("currentEnrollmentId",201);Set("currentClassId",1);Set("currentDateOfBirth",new DateTime(2010,1,1));Set("currentAdmissionDate",new DateTime(2020,1,1));Set("savedCertificateDate",DateTime.Today);
                if(item.Item1=="CharacterCertificatePage")Field<ModernComboBox>("examType").SelectedIndex=0;
                type.GetMethod("CaptureDraftBaseline",flags)?.Invoke(page,null);
                Apply();
                var save=Field<ModernButton>(item.Item2.EndsWith("IssueId")?"createCertificateButton":"updateCertificateButton");
                Check(!save.Enabled,item.Item1+": collected copies cannot be saved over");
                Check(item.Item4.Split(',').All(name=>!Field<Control>(name).Enabled),item.Item1+": all certificate editor fields lock after collection");
                Check(Field<ModernButton>("printButton").Enabled,item.Item1+": collected copy remains printable");
                var flow=Field<CertificateCollectionFlow>("copyFlow");
                Check(!flow.Collect.Enabled,item.Item1+": collection cannot be repeated");
                Check(!Descendants(page).OfType<ModernButton>().Any(button=>button.Text.Contains("Void",StringComparison.OrdinalIgnoreCase)||button.Text.Contains("Cancel Copy",StringComparison.OrdinalIgnoreCase)),item.Item1+": cancellation is not offered");
                row["CollectedAt"]=DBNull.Value;row["CollectionStatus"]="Not Collected";Set("selectedIssueCollected",false);Apply();
                Check(Field<ModernButton>("printButton").Enabled,item.Item1+": ready copy can print before collection");
                row["VoidedAt"]=DateTime.Now;row["CollectionStatus"]="Void";Apply();
                Check(!Field<ModernButton>("printButton").Enabled&&!save.Enabled,item.Item1+": historical cancelled copy remains read-only and cannot print");
                Check(!page.IsHandleCreated,item.Item1+": lifecycle checks start no database loaders");
            }
        }
        finally {CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
