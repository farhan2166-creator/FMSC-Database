using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckAdmissionNumberPolicy()
    {
        DateTime first=new(2025,4,4),target=new(2026,10,1),later=new(2026,10,15);
        DataTable Register(params (string Number,DateTime? Admission)[] rows)
        {
            var table=new DataTable();
            table.Columns.Add("AdmissionNumber");table.Columns.Add("AdmissionDate",typeof(DateTime));
            table.Columns.Add("EnrollmentDate",typeof(DateTime));table.Columns.Add("BillingStartDate",typeof(DateTime));
            foreach(var row in rows)table.Rows.Add(row.Number,(object?)row.Admission??DBNull.Value,new DateTime(2026,10,20),new DateTime(2026,11,1));
            return table;
        }
        AdmissionNumberPlan Plan(DataTable table,DateTime? date=null)=>AdmissionNumberPolicy.Calculate(table,date??target,
            new DateTime(2026,1,1),new DateTime(2026,12,31));
        using var gap=Register(("6589",first),("6591",later));
        var plan=Plan(gap);
        Check(plan.Suggested=="6590"&&plan.Lower=="6589"&&plan.Upper=="6591",
            "The October admission between 6589 and 6591 uses available whole number 6590, before any suffix.");
        foreach(DataRow row in gap.Rows){row["EnrollmentDate"]=new DateTime(2025,1,1);row["BillingStartDate"]=new DateTime(2027,1,1);}
        Check(Plan(gap).Suggested=="6590"&&gap.Rows[0].Field<DateTime>("AdmissionDate")==first,
            "Placement and billing dates cannot alter first-admission chronology or overwrite the original admission date.");
        Check(plan.Validate("7000") is null&&plan.Review("7000") is not null,
            "Chronology remains advice; a valid unused manual number can still be saved.");
        Check(plan.Validate("6589") is not null,"Already assigned numbers remain invalid.");
        using var used=Register(("6589",first),("6590",null),("6591",later));
        Check(Plan(used).Suggested=="6589a","An occupied whole-number gap falls back to a suffix without duplicating an undated number.");
        using var occupiedSuffix=Register(("6589",first),("6589a",first),("6591",later));
        Check(Plan(occupiedSuffix).Suggested=="6590","A used suffix does not hide an available whole-number gap.");
        using var several=Register(("10",first),("11",null),("12",null),("15",later));
        Check(Plan(several).Suggested=="13","Several occupied whole numbers are skipped before falling back to suffixes.");
        using var consecutive=Register(("6570",first),("6571",later),("6589",later));
        Check(Plan(consecutive).Suggested=="6570a","Historical insertion between consecutive whole numbers still uses a suffix.");
        consecutive.Rows.Add("6570a",first,new DateTime(2026,1,1),new DateTime(2026,1,1));
        Check(Plan(consecutive).Suggested=="6570b","Historical insertion preserves existing suffixes and chooses the next unused one.");
        using var sameDay=Register(("6589",target),("6590",target),("6592",later));
        Check(Plan(sameDay).Suggested=="6591","Admissions on the same date retain their assigned numbers and precede the next whole-number suggestion.");
        using var suffixUpper=Register(("6589",first),("6590a",later));
        Check(Plan(suffixUpper).Suggested=="6590","A whole number can fit before a later record with that number's suffix.");
        using var earlier=Register(("3",later));
        Check(Plan(earlier).Suggested=="1","An admission before the first dated record can use an available positive whole number.");
        using var earliest=Register(("1",later));
        Check(Plan(earliest).Suggested is null,"No invalid zero or negative number is suggested before number 1.");
        Check(Plan(gap,new DateTime(2026,10,20)).Suggested=="6592",
            "Admissions after the latest dated record continue the register instead of filling earlier gaps.");
        using var conflicting=Register(("6600",first),("6591",later));
        Check(Plan(conflicting).Suggested is null&&Plan(conflicting).Validate("7000") is null,
            "Conflicting historical chronology does not trigger automatic renumbering or forbid valid manual numbers.");
        using var normalized=Register(("6589",first),("06590",null),("6591",later));
        Check(Plan(normalized).Suggested=="6589a","Normalized leading zeros cannot make an occupied whole number look free.");
        using var limit=Register(((long.MaxValue-2).ToString(),first),((long.MaxValue-1).ToString(),later));
        Check(Plan(limit).Suggested==(long.MaxValue-2)+"a","A bounded gap near the numeric limit safely falls back to a suffix.");
        Check(Plan(limit,new DateTime(2026,10,20)).Suggested is null,"Register exhaustion cannot overflow or suggest an invalid number.");
        Check(AdmissionNumberPolicy.Calculate(gap,target,target.AddDays(1),target.AddMonths(1)).Error is not null,
            "Existing admission-date validation is preserved.");
        Check(gap.Rows.Count==2&&gap.Rows[0].Field<string>("AdmissionNumber")=="6589"&&gap.Rows[1].Field<string>("AdmissionNumber")=="6591",
            "Suggesting a number never renumbers or changes the supplied register.");
    }
}
