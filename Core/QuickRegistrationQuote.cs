using System.Text.Json;
namespace FMSC_SMS.Core;

internal static class QuickRegistrationQuote
{
    internal static string Create(DataTable rates) => JsonSerializer.Serialize(rates.AsEnumerable()
        .OrderBy(r=>Convert.ToInt32(r["FeeTypeId"]))
        .Select(r=>new { Id=Convert.ToInt32(r["FeeTypeId"]), Amount=r.IsNull("Amount")?(decimal?)null:Convert.ToDecimal(r["Amount"]),
            Frequency=Convert.ToString(r["Frequency"]), Mandatory=Convert.ToBoolean(r["IsMandatory"]),
            Transport=Convert.ToBoolean(r["IsTransportLinked"]) }));

}
