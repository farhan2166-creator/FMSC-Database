using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

internal sealed record AdmissionNumberPlan(string? Suggested, string? Lower, string? Upper, HashSet<string> Used, string? Error)
{
    internal string? Validate(string text)
    {
        if(Error is not null) return Error;
        if(!AdmissionNumbers.TryNormalize(text,out string number)) return "Enter a valid admission number.";
        if(Used.Contains(number)) return "This admission number is already in use.";
        return null;
    }

    // Chronology guides suggestions and optional record review, never save eligibility.
    internal string? Review(string text)
    {
        if(Validate(text) is { } problem) return problem;
        AdmissionNumbers.TryNormalize(text,out string number);
        if(Lower is not null && Upper is not null && AdmissionNumbers.Compare(Lower,Upper)>=0)
            return "Existing register numbers conflict with admission-date order. No chronological number can be suggested; you can enter an unused number.";
        if(Lower is not null && AdmissionNumbers.Compare(number,Lower)<=0) return $"Suggested date order: after {Lower}.";
        if(Upper is not null && AdmissionNumbers.Compare(number,Upper)>=0) return $"Suggested date order: before {Upper}.";
        return null;
    }
}

internal static class AdmissionNumberPolicy
{
    internal static Task<string?> PreviewNextAsync() => Task.Run(() => {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var command = new SqlCommand("dbo.GetNextAdmissionNumber", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(DatabaseHelper.Int("@UserId", CurrentUser.UserId));
        return Convert.ToString(command.ExecuteScalar());
    });
    internal static Task<AdmissionNumberPlan?> LoadExistingAsync(int admissionId) => Task.Run(() => {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var command = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Students.Administer';
            SELECT AdmissionDate FROM dbo.Students WHERE AdmissionId=@AdmissionId;
            SELECT AdmissionDate,AdmissionNumber FROM dbo.Students WHERE AdmissionId<>@AdmissionId;
            """, connection);
        command.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@AdmissionId", admissionId) });
        var data = new DataSet();
        using (var adapter = new SqlDataAdapter(command)) adapter.Fill(data);
        if (data.Tables[0].Rows.Count != 1 || data.Tables[0].Rows[0].IsNull("AdmissionDate")) return null;
        DateTime date = Convert.ToDateTime(data.Tables[0].Rows[0]["AdmissionDate"]);
        // Existing records use their original admission date, independently of current enrollment.
        return Calculate(data.Tables[1], date, date, date);
    });
    internal static Task<AdmissionNumberPlan> LoadAsync(DateTime date,int yearId,bool verifiedHistoricalRegister = false) => Task.Run(()=> {
        using var connection=DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var command=new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Students.Admit';
            SELECT StartDate,EndDate FROM dbo.AcademicYears WHERE AcademicYearId=@YearId;
            SELECT AdmissionDate,AdmissionNumber FROM dbo.Students;
            """,connection);
        command.Parameters.AddRange(new[]{DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@YearId",yearId)});
        var data=new DataSet();
        using(var adapter=new SqlDataAdapter(command)) adapter.Fill(data);
        if(data.Tables[0].Rows.Count!=1) return new AdmissionNumberPlan(null,null,null,new(),"Select an academic year before assigning an admission number.");
        var year=data.Tables[0].Rows[0];
        return Calculate(data.Tables[1],date,Convert.ToDateTime(year["StartDate"]),Convert.ToDateTime(year["EndDate"]),verifiedHistoricalRegister);
    });

    internal static AdmissionNumberPlan Calculate(DataTable students,DateTime date,DateTime start,DateTime end,bool verifiedHistoricalRegister = false)
    {
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dated=new List<(DateTime Date,string Number)>();
        foreach(DataRow row in students.Rows)
        {
            if(!AdmissionNumbers.TryNormalize(Convert.ToString(row["AdmissionNumber"]),out string number)) continue;
            used.Add(number);
            if(!row.IsNull("AdmissionDate")) dated.Add((Convert.ToDateTime(row["AdmissionDate"]).Date,number));
        }
        if(date.Date<start.Date || date.Date>end.Date)
            return new(null,null,null,used,$"Admission date must be within {start:dd-MMM-yyyy} to {end:dd-MMM-yyyy}.");
        if(date.Date>DateTime.Today) return new(null,null,null,used,"Admission date cannot be in the future.");
        var order=Comparer<string>.Create(AdmissionNumbers.Compare);
        string? lower=dated.Where(item=>item.Date<date.Date).Select(item=>item.Number).OrderByDescending(number=>number,order).FirstOrDefault();
        string? upper=dated.Where(item=>item.Date>date.Date).Select(item=>item.Number).OrderBy(number=>number,order).FirstOrDefault();
        // Owner policy (BL-04): valid, unused manual numbers may override chronology advice.
        // Date/year validation and the verified-historical suggestion exception still apply.
        if(verifiedHistoricalRegister) { lower=null; upper=null; }
        if(lower is not null && upper is not null && AdmissionNumbers.Compare(lower,upper)>=0)
            return new(null,lower,upper,used,null);
        string? anchor=dated.Where(item=>item.Date<=date.Date).OrderByDescending(item=>item.Date)
            .ThenByDescending(item=>item.Number,order).Select(item=>item.Number).FirstOrDefault();
        string? suggestion=null;
        // An incomplete legacy register can end before today. Continue its whole
        // numbers whenever no later dated record bounds this admission.
        if(upper is null)
        {
            long maximum=used.Select(number=>long.Parse(new string(number.TakeWhile(char.IsDigit).ToArray()),CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
            if(maximum<long.MaxValue-1) suggestion=(maximum+1).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            // Only original admission dates determine register order. Fill an
            // available whole-number gap before inserting a letter suffix:
            // an admission between 6589 and 6591 should suggest 6590.
            string? after=anchor;
            if(lower is not null && (after is null || AdmissionNumbers.Compare(lower,after)>0)) after=lower;
            long next=after is null?1:long.Parse(new string(after.TakeWhile(char.IsDigit).ToArray()),CultureInfo.InvariantCulture)+1;
            for(int attempt=0;attempt<=used.Count && next<long.MaxValue;attempt++,next++)
            {
                string candidate=next.ToString(CultureInfo.InvariantCulture);
                if(AdmissionNumbers.Compare(candidate,upper)>=0) break;
                if(!used.Contains(candidate)) { suggestion=candidate; break; }
            }
            if(suggestion is null && anchor is not null)
            {
                // Consecutive register numbers still need historical insertion,
                // e.g. an admission between 6570 and 6571 can use 6570a.
                string main=new string(anchor.TakeWhile(char.IsDigit).ToArray());
                string suffix=anchor[main.Length..];
                for(int attempt=0;attempt<=used.Count;attempt++)
                {
                    suffix=suffix.Length==0 ? "a" : suffix[^1]=='z' ? suffix+"a" : suffix[..^1]+(char)(suffix[^1]+1);
                    string candidate=main+suffix;
                    if(candidate.Length>40 || AdmissionNumbers.Compare(candidate,upper)>=0) break;
                    if(!used.Contains(candidate) && (lower is null || AdmissionNumbers.Compare(candidate,lower)>0)) { suggestion=candidate; break; }
                }
            }
        }
        var plan=new AdmissionNumberPlan(suggestion,lower,upper,used,null);
        return suggestion is not null && plan.Review(suggestion) is not null ? plan with { Suggested=null } : plan;
    }
}
