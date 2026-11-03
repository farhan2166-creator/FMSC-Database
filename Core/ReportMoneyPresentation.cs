using System.Globalization;

namespace FMSC_SMS.Core;

// Report queries store deduction magnitudes. Sign them only at the print/export
// boundary; never change source rows, settlement arithmetic or saved documents.
internal static class ReportMoneyPresentation
{
    private static string Key(string name)=>new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool IsDeduction(string name)=>Key(name) is
        "discount" or "discounts" or "discountamount" or "discountapplied" or "discountsapplied" or "applieddiscount"
        or "assigneddiscount" or "assigneddiscountcyclers"
        or "waived" or "waivedamount" or "waiveramount" or "concessionamount" or "concessions"
        or "refunds" or "refundamount" or "corrections" or "correctionamount";

    internal static decimal Amount(DataColumn column,decimal value)
    {
        string? source=column.ExtendedProperties[ReportContextProjection.SourceColumnKey] as string;
        // Use exact money-field identities: discount percentages/counts and
        // withdrawn concessions are not deductions. Credit applied is a payment
        // source in these reports, while held credit is an account balance.
        // Negation also makes a reversed deduction an increase; Abs would lose it.
        return IsDeduction(string.IsNullOrWhiteSpace(source)?column.ColumnName:source)?-value:value;
    }

    internal static string LegacyMetric(string label,string text)
    {
        // Older print callers supply formatted totals. Keep an existing minus
        // sign and zero; structured metrics use the typed Amount path above.
        const string currency="Rs. ";
        string number=text.StartsWith(currency,StringComparison.Ordinal)?text[currency.Length..]:text;
        return IsDeduction(label)&&decimal.TryParse(number,NumberStyles.Number,CultureInfo.CurrentCulture,out decimal value)&&value>0m
            ?(text.StartsWith(currency,StringComparison.Ordinal)?currency:"")+(-value).ToString("#,##0.##",CultureInfo.CurrentCulture):text;
    }
}
