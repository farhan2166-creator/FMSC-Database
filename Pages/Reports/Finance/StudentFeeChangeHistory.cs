using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Finance;

internal static class StudentFeeChangeHistory
{
    internal static async Task ShowAsync(Control owner, Func<int> selectedStudent)
    {
        int admissionId=selectedStudent();
        if (admissionId<=0) throw new InvalidOperationException("Select a student to view fee change history.");
        int userId=CurrentUser.UserId;
        var historyTask=Task.Run(() => Load(admissionId,userId));
        var institutionTask=InstitutionProfile.GetPrintIdentityAsync();
        await Task.WhenAll(historyTask,institutionTask);
        if (owner.IsDisposed || selectedStudent()!=admissionId || CurrentUser.UserId!=userId) return;
        var report=Report(await historyTask,(await institutionTask).Name,CurrentUser.Username);
        RdlcReportPreview.Show(owner.FindForm(),"Fee Change History","","ReportData",report.Data,
            fitPageWidth:true,reportDefinition:report.Definition);
    }
    internal sealed record Result(DataTable Rows, string Identity, int Events, int Incomplete);
    internal static Result Load(int admissionId, int userId)
    {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var command = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            EXEC dbo.RequirePermission @UserId,N'Fees.Statement.View';
            SELECT S.AdmissionNumber,S.StudentName,S.FatherName,C.CampusName,CL.ClassName,
                SE.SectionName,Y.YearName
            FROM dbo.Students S
            OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
                ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,E.EnrollmentId DESC) E
            LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
            LEFT JOIN dbo.Campuses C ON C.CampusId=CCS.CampusId
            LEFT JOIN dbo.Classes CL ON CL.ClassId=CCS.ClassId
            LEFT JOIN dbo.Sections SE ON SE.SectionId=CCS.SectionId
            LEFT JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE S.AdmissionId=@AdmissionId;
            SELECT S.*,F.FeeTypeName,F.DisplayOrder,Y.YearName,Y.EndDate
            FROM dbo.StudentFeeAssignments S JOIN dbo.FeeTypes F ON F.FeeTypeId=S.FeeTypeId
            JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
            JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE S.AdmissionId=@AdmissionId ORDER BY Y.StartDate DESC,F.DisplayOrder,S.EffectiveFrom;
            SELECT B.*,F.FeeTypeName,F.DisplayOrder FROM dbo.BillingCharges B
            JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
            WHERE B.AdmissionId=@AdmissionId ORDER BY B.BillingMonth,F.DisplayOrder,B.BillingChargeId;
            ;WITH Matched AS (
                SELECT DISTINCT L.OperationId FROM dbo.AuditLogs L
                WHERE L.TableName IN(N'StudentFeeAssignments',N'BillingCharges') AND (
                    (L.TableName=N'StudentFeeAssignments' AND TRY_CONVERT(int,L.RecordId) IN
                        (SELECT StudentFeeAssignmentId FROM dbo.StudentFeeAssignments WHERE AdmissionId=@AdmissionId))
                    OR (L.TableName=N'BillingCharges' AND TRY_CONVERT(bigint,L.RecordId) IN
                        (SELECT BillingChargeId FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId))
                    OR EXISTS(SELECT 1 FROM (
                        SELECT Path AS [key],Value FROM dbo.AuditValueLeaves(CASE WHEN ISJSON(L.OldValues)=1 THEN L.OldValues ELSE N'{}' END)
                        UNION ALL SELECT Path AS [key],Value FROM dbo.AuditValueLeaves(CASE WHEN ISJSON(L.NewValues)=1 THEN L.NewValues ELSE N'{}' END)
                    ) J WHERE ([key] LIKE N'%AdmissionId' AND TRY_CONVERT(int,value)=@AdmissionId)
                        OR ([key] LIKE N'%StudentFeeAssignmentId' AND TRY_CONVERT(int,value) IN
                            (SELECT StudentFeeAssignmentId FROM dbo.StudentFeeAssignments WHERE AdmissionId=@AdmissionId))
                        OR ([key] LIKE N'%BillingChargeId' AND TRY_CONVERT(bigint,value) IN
                            (SELECT BillingChargeId FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId))))
            )
            SELECT L.*,O.OperationType,O.CorrelationNote,U.Username
            FROM dbo.AuditLogs L JOIN Matched M ON M.OperationId=L.OperationId
            LEFT JOIN dbo.AuditOperations O ON O.OperationId=L.OperationId
            LEFT JOIN dbo.Users U ON U.UserId=L.UserId
            WHERE L.TableName IN(N'StudentFeeAssignments',N'BillingCharges')
            ORDER BY L.ActionDateTime,L.AuditLogId;
            """, connection) { CommandTimeout = 120 };
        command.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId", userId), DatabaseHelper.Int("@AdmissionId", admissionId) });
        var data = new DataSet();
        using var adapter = new SqlDataAdapter(command);
        adapter.Fill(data);
        return Build(data, admissionId, DateTime.Today);
    }

    internal static Result Build(DataSet data, int admissionId, DateTime today)
    {
        if (data.Tables.Count != 4 || data.Tables[0].Rows.Count != 1)
            throw new InvalidOperationException("The student's fee history could not be loaded completely.");
        var identity = data.Tables[0].Rows[0];
        string identityText = $"Admission {Text(identity,"AdmissionNumber")}   |   {Text(identity,"StudentName")}   |   Father: {Text(identity,"FatherName")}\n"
            + $"{Text(identity,"ClassName")} / {Text(identity,"SectionName")}   |   {Text(identity,"CampusName")}   |   {Text(identity,"YearName")}";
        var rows = new DataTable();
        foreach (string name in new[] { "Recorded / Status", "Fee Type / Action", "Amounts / Changes", "Effective period", "By", "Reason / Notes" }) rows.Columns.Add(name);
        var assignments = data.Tables[1].AsEnumerable().ToDictionary(r => Convert.ToInt64(r["StudentFeeAssignmentId"]));
        var charges = data.Tables[2].AsEnumerable().ToDictionary(r => Convert.ToInt64(r["BillingChargeId"]));
        DateTime month = new(today.Year,today.Month,1);
        AddSection("FEE RATE SCHEDULE", "Stored rate versions across all academic years. Status is evaluated for " + month.ToString("MMMM yyyy") + ".");
        int RateRank(DataRow r) => r["CancelledAt"]!=DBNull.Value ? 3
            : r["EffectiveTo"]!=DBNull.Value && Convert.ToDateTime(r["EffectiveTo"])<month
                || r["EndDate"]!=DBNull.Value && Convert.ToDateTime(r["EndDate"])<month ? 2
            : r["EffectiveFrom"]!=DBNull.Value && Convert.ToDateTime(r["EffectiveFrom"])>month ? 1 : 0;
        var recurringRates=data.Tables[1].AsEnumerable().Where(r => !string.Equals(Text(r,"Frequency"),"As Needed",StringComparison.OrdinalIgnoreCase)).OrderBy(RateRank).ToArray();
        foreach (DataRow r in recurringRates)
        {
            string status = r["CancelledAt"] != DBNull.Value ? "Cancelled"
                : r["EffectiveTo"] != DBNull.Value && Convert.ToDateTime(r["EffectiveTo"]) < month ? "Ended"
                : r["EndDate"] != DBNull.Value && Convert.ToDateTime(r["EndDate"]) < month ? "Ended"
                : r["EffectiveFrom"] != DBNull.Value && Convert.ToDateTime(r["EffectiveFrom"]) > month ? "Scheduled" : "Applicable";
            rows.Rows.Add(status,Text(r,"FeeTypeName"),
                $"Amount Rs. {Money(r["Amount"])}\nDiscount Rs. {Money(r["DiscountAmount"])}\nNet Rs. {Money(Convert.ToDecimal(r["Amount"])-Convert.ToDecimal(r["DiscountAmount"]))}",
                $"{Month(r["EffectiveFrom"],"From start")} to {Month(r["EffectiveTo"],"Ongoing")}","—",
                $"{Text(r,"YearName")} · {Text(r,"Frequency")}\nRate version #{r["StudentFeeAssignmentId"]}");
        }
        if (recurringRates.Length == 0) rows.Rows.Add("—","No recurring fee assignments","—","—","—","Purchases appear in posted charges and history.");
        AddSection("POSTED CHARGES", "Actual charge amounts can differ from the rate schedule. Cancelled charges remain visible as history.");
        foreach (DataRow r in data.Tables[2].Rows)
            rows.Rows.Add(Text(r,"Status")=="PartiallyPaid" ? "Partially paid" : Text(r,"Status")=="Open" ? "Unpaid" : Text(r,"Status"),Text(r,"FeeTypeName"),
                $"Gross Rs. {Money(r["GrossAmount"])}\nDiscount Rs. {Money(r["DiscountAmount"])}\nNet Rs. {Money(r["NetAmount"])}",
                Month(r["BillingMonth"],"Not recorded"),"—",$"Charge #{r["BillingChargeId"]}\n{Text(r,"Remarks")}");
        if (charges.Count == 0) rows.Rows.Add("—","No posted charges","—","—","—","No charges were found.");
        AddSection("CHANGE TIMELINE", "Oldest first. Recorded date/time is separate from the effective month. Only recorded values are shown.");
        int events = 0, incomplete = 0;
        foreach (DataRow audit in data.Tables[3].Rows)
        {
            var oldGroups = Parse(Text(audit,"OldValues"));
            var newGroups = Parse(Text(audit,"NewValues"));
            string table = Text(audit,"TableName"), operation = Text(audit,"OperationType");
            long.TryParse(Text(audit,"RecordId"),out long record);
            bool direct = table == "StudentFeeAssignments" ? assignments.ContainsKey(record) : charges.ContainsKey(record);
            bool any = false;
            foreach (string group in oldGroups.Keys.Union(newGroups.Keys).OrderBy(k => k,StringComparer.Ordinal))
            {
                var old = oldGroups.GetValueOrDefault(group) ?? new();
                var current = newGroups.GetValueOrDefault(group) ?? new();
                long GetId(string key) => long.TryParse(current.GetValueOrDefault(key) ?? old.GetValueOrDefault(key),out long id) ? id : 0;
                long assignmentId=GetId("StudentFeeAssignmentId"), chargeId=GetId("BillingChargeId");
                if (GetId("AdmissionId") != 0 && GetId("AdmissionId") != admissionId) continue;
                bool linked = GetId("AdmissionId")==admissionId || assignments.ContainsKey(assignmentId) || charges.ContainsKey(chargeId);
                if (!linked && !(direct && oldGroups.Count<=1 && newGroups.Count<=1)) continue;
                DataRow? snapshot = table == "StudentFeeAssignments"
                    ? assignments.GetValueOrDefault(assignmentId) ?? assignments.GetValueOrDefault(record)
                    : charges.GetValueOrDefault(chargeId) ?? charges.GetValueOrDefault(record);
                long feeId=GetId("FeeTypeId");
                string fee = snapshot is null ? assignments.Values.FirstOrDefault(r => Convert.ToInt64(r["FeeTypeId"])==feeId) is DataRow feeRow
                    ? Text(feeRow,"FeeTypeName") : "Fee (name not recorded)" : Text(snapshot,"FeeTypeName");
                var changes = new List<string>();
                foreach (string key in new[] { "Amount","StandardAmount","GrossAmount","DiscountAmount","NetAmount","Status","Frequency","CancelledAt" })
                    if (old.ContainsKey(key) || current.ContainsKey(key))
                        changes.Add(Label(key) + ": " + (Text(audit,"Action")=="INSERT" && !old.ContainsKey(key)
                            ? Value(current,key) : Value(old,key) + " → " + Value(current,key)));
                var periods = new List<string>();
                foreach (string key in new[] { "EffectiveFrom","EffectiveTo","BillingMonth" })
                    if (old.ContainsKey(key) || current.ContainsKey(key))
                        periods.Add(Label(key) + ": " + (Text(audit,"Action")=="INSERT" && !old.ContainsKey(key)
                            ? Value(current,key) : Value(old,key) + " → " + Value(current,key)));
                bool gap = changes.Count==0 && periods.Count==0;
                if (gap) incomplete++;
                string reason = current.GetValueOrDefault("Reason") ?? old.GetValueOrDefault("Reason")
                    ?? current.GetValueOrDefault("Remarks")
                    ?? (operation=="TRANSPORT_STOP" || operation=="FEE_SCHEDULE_REPAIR" ? Text(audit,"CorrelationNote") : "Not recorded");
                rows.Rows.Add(Convert.ToDateTime(audit["ActionDateTime"]).ToString("dd-MM-yyyy\nHH:mm:ss"),
                    fee + "\n" + (gap && operation=="FEE_DISCOUNT_CHANGE" ? "Discount update recorded" : ActionName(operation,Text(audit,"Action"),table)),
                    changes.Count>0 ? string.Join("\n",changes) : "Amounts not recorded",
                    periods.Count>0 ? string.Join("\n",periods) : "Not recorded",
                    Text(audit,"Username"),reason + $"\nAudit #{audit["AuditLogId"]}");
                events++; any=true;
            }
            // Transport-stop charge logs can contain only changed statuses, without charge IDs.
            // Include the operation once, without guessing which indexed item was which charge.
            if (!any && (direct || operation=="TRANSPORT_STOP"))
            {
                string reason=Text(audit,"CorrelationNote");
                rows.Rows.Add(Convert.ToDateTime(audit["ActionDateTime"]).ToString("dd-MM-yyyy\nHH:mm:ss"),
                    table=="BillingCharges" ? "Transport charges\nCharges cancelled" : "Fee change",
                    "Detailed linkage or values not retained in audit", "Not recorded",Text(audit,"Username"),
                    (reason.Length>0 ? reason : "Not recorded") + $"\nAudit #{audit["AuditLogId"]}");
                events++; incomplete++;
            }
        }
        if (events==0) rows.Rows.Add("—","No linked audit events","—","—","—","Stored rates and charges are shown above; earlier change details may not have been retained.");
        AddSection("HISTORY COVERAGE", $"{events} linked audit entries; {incomplete} entries with incomplete details. Times are shown as recorded. Unavailable details are marked as missing.");
        object?[] coverage=rows.Rows[rows.Rows.Count-1].ItemArray;
        rows.Rows.RemoveAt(rows.Rows.Count-1);
        DataRow coverageRow=rows.NewRow();
        coverageRow.ItemArray=coverage;
        rows.Rows.InsertAt(coverageRow,0);
        return new(rows,identityText,events,incomplete);
        void AddSection(string title,string note) => rows.Rows.Add("SECTION",title,"","","",note);
    }

    private static string Text(DataRow row,string key) => row.Table.Columns.Contains(key) && row[key]!=DBNull.Value ? Convert.ToString(row[key]) ?? "" : "";
    private static string Money(object value) => Convert.ToDecimal(value).ToString("N0");
    private static string Month(object value,string blank) => value==DBNull.Value ? blank : Convert.ToDateTime(value).ToString("MMM yyyy");
    private static string Label(string key) => key switch {
        "Amount" or "StandardAmount" => "Amount", "GrossAmount" => "Gross", "DiscountAmount" => "Discount",
        "NetAmount" => "Net", "EffectiveFrom" => "From", "EffectiveTo" => "Through", "BillingMonth" => "Charge month",
        "CancelledAt" => "Cancelled", _ => key
    };
    private static string Value(Dictionary<string,string?> values,string key)
    {
        if (!values.TryGetValue(key,out string? value)) return "Not recorded";
        if (value is null) return key=="EffectiveFrom" ? "From start" : key=="EffectiveTo" ? "Ongoing" : "None";
        if (key.Contains("Amount") && decimal.TryParse(value,NumberStyles.Any,CultureInfo.InvariantCulture,out decimal amount)) return "Rs. " + Money(amount);
        if (key is "EffectiveFrom" or "EffectiveTo" or "BillingMonth" && DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out DateTime date)) return date.ToString("MMM yyyy");
        return value;
    }
    private static string ActionName(string operation,string action,string table) => operation switch {
        "FEE_ASSIGNMENT_ADD" => "Fee assigned", "FEE_ASSIGNMENT_AMOUNT_CHANGE" => "Amount changed",
        "FEE_DISCOUNT_CHANGE" => "Discount changed", "FEE_ASSIGNMENT_DISCONTINUE" => "Fee ended",
        "FEE_ASSIGNMENT_REMOVE" => "Fee removed", "FEE_SCHEDULE_REPAIR" => "Fee period corrected",
        "TRANSPORT_STOP" => table=="BillingCharges" ? "Charges cancelled with transport" : "Transport stopped",
        "TRANSPORT_RESTART" => "Transport started / restarted",
        "FEE_CHARGE_REVERSAL" => "Charge reversed", "FEE_POSTING_REVERSAL" => "Posting reversed",
        _ => table=="BillingCharges" ? action=="INSERT" ? "Charge posted" : "Charge changed" : "Fee updated"
    };
    private static Dictionary<string,Dictionary<string,string?>> Parse(string json)
    {
        var groups = new Dictionary<string,Dictionary<string,string?>>();
        if (string.IsNullOrWhiteSpace(json)) return groups;
        try {
            using var document=JsonDocument.Parse(json);
            Walk(document.RootElement,"");
        } catch (JsonException) { return groups; }
        return groups;
        void Walk(JsonElement element,string prefix)
        {
            if (element.ValueKind==JsonValueKind.Array) { int i=0; foreach(var item in element.EnumerateArray()) Walk(item,$"{prefix}[{i++}]"); return; }
            if (element.ValueKind!=JsonValueKind.Object) return;
            foreach(var property in element.EnumerateObject())
            {
                string path=prefix.Length==0 ? property.Name : prefix+"."+property.Name;
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) { Walk(property.Value,path); continue; }
                var match=Regex.Match(path,@"^(.*)\.([^\.]+)$");
                string group=match.Success ? match.Groups[1].Value : "", key=match.Success ? match.Groups[2].Value : path;
                if (!groups.TryGetValue(group,out var fields)) groups[group]=fields=new();
                fields[key]=property.Value.ValueKind==JsonValueKind.Null ? null : property.Value.ToString();
            }
        }
    }

    internal static GridReportPreview.Report Report(Result result,string institution,string printedBy)
    {
        var report=GridReportPreview.Build(institution,"Student Fee Change History",result.Identity,result.Rows,printedBy);
        XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
        var document=XDocument.Parse(report.Definition);
        XElement root=document.Root!;
        root.Element(ns+"Width")!.Value="10.67in";
        root.Element(ns+"Page")!.Element(ns+"PageWidth")!.Value="11.69in";
        root.Element(ns+"Page")!.Element(ns+"PageHeight")!.Value="8.27in";
        foreach(var textbox in root.Descendants(ns+"PageHeader").Descendants(ns+"Textbox")) textbox.Element(ns+"Width")!.Value="10.67in";
        root.Descendants(ns+"PageHeader").First().Element(ns+"Height")!.Value="1.1in";
        var table=root.Descendants(ns+"Tablix").Single();
        table.Add(new XElement(ns+"KeepTogether",false),new XElement(ns+"RepeatColumnHeaders",true),new XElement(ns+"RepeatRowHeaders",true));
        table.Element(ns+"TablixRowHierarchy")!.Descendants(ns+"TablixMember").First().Add(new XElement(ns+"FixedData",true));
        table.Element(ns+"Width")!.Value="10.67in";
        double[] widths=[1.1,1.65,2.1,1.75,0.8,3.27];
        // Explicit page groups avoid split event rows and keep headings with the next entry.
        report.Data.Columns.Add("PageKey",typeof(int));
        int page=0;
        double used=0;
        for(int rowIndex=0;rowIndex<report.Data.Rows.Count;rowIndex++)
        {
            DataRow row=report.Data.Rows[rowIndex];
            double height=RowHeight(row);
            double needed=height;
            if(Convert.ToString(row["C0"])=="SECTION" && rowIndex+1<report.Data.Rows.Count)
                needed+=RowHeight(report.Data.Rows[rowIndex+1]);
            if(used>0 && used+needed>4.7) { page++; used=0; }
            row["PageKey"]=page;
            used+=height;
        }
        double RowHeight(DataRow row)
        {
            int lines=1;
            for(int column=0;column<widths.Length;column++)
            {
                int capacity=Math.Max(8,(int)(widths[column]*72/4.6));
                int count=(Convert.ToString(row["C"+column]) ?? "").Split('\n')
                    .Sum(line => Math.Max(1,(int)Math.Ceiling(line.Length/(double)capacity)));
                lines=Math.Max(lines,count);
            }
            return Math.Max(.2,lines*.145+.06);
        }
        root.Descendants(ns+"Fields").Single().Add(new XElement(ns+"Field",new XAttribute("Name","PageKey"),new XElement(ns+"DataField","PageKey")));
        var members=table.Element(ns+"TablixRowHierarchy")!.Element(ns+"TablixMembers")!;
        var existing=members.Elements().Select(e => new XElement(e)).ToArray();
        members.ReplaceNodes(new XElement(ns+"TablixMember",
            new XElement(ns+"Group",new XAttribute("Name","PrintedPage"),
                new XElement(ns+"GroupExpressions",new XElement(ns+"GroupExpression","=Fields!PageKey.Value")),
                new XElement(ns+"PageBreak",new XElement(ns+"BreakLocation","Between"))),
            new XElement(ns+"TablixMembers",existing)));
        int index=0;
        foreach(var column in table.Descendants(ns+"TablixColumn")) column.Element(ns+"Width")!.Value=widths[index++].ToString(CultureInfo.InvariantCulture)+"in";
        foreach(var textbox in table.Descendants(ns+"Textbox").Where(t => ((string?)t.Attribute("Name"))?.StartsWith("Value")==true))
        {
            textbox.Descendants(ns+"FontWeight").First().Value="=IIf(Fields!C0.Value = \"SECTION\", \"Bold\", \"Normal\")";
            textbox.Element(ns+"Style")!.Element(ns+"BackgroundColor")!.Value="=IIf(Fields!C0.Value = \"SECTION\", \"#E8F1EE\", IIf(RowNumber(Nothing) Mod 2 = 0, \"#F6F9FA\", \"White\"))";
            if ((string?)textbox.Attribute("Name")=="Value0") textbox.Descendants(ns+"Value").First().Value="=IIf(Fields!C0.Value = \"SECTION\", \"\", Fields!C0.Value)";
        }
        return new(document.ToString(),report.Data);
    }
}
