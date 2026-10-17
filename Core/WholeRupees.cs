namespace FMSC_SMS.Core;

// Owner policy (F-01): reject fractional rupees; never round inputs or rewrite historical amounts.
internal static class WholeRupees
{
    internal static bool IsWhole(decimal value) => value == decimal.Truncate(value);

    internal static object? ValidateParameter(string name, object? value)
    {
        if (value is decimal amount && name.TrimStart('@').ToUpperInvariant() is
            "AMOUNT" or "NEWAMOUNT" or "DISCOUNT" or "DISCOUNTAMOUNT" or "TOTALAMOUNT" or "MONTHLYINCOME")
            Require(amount);
        return value;
    }

    internal static DataTable ValidateTable(DataTable table)
    {
        foreach (DataColumn column in table.Columns)
            if (column.DataType == typeof(decimal))
                foreach (DataRow row in table.Rows)
                    if (!row.IsNull(column)) Require((decimal)row[column]);
        return table;
    }

    internal static void Require(decimal amount)
    {
        if (!IsWhole(amount))
            throw new ArgumentException("Fees, discounts and payments must be whole rupees. Fractions are not rounded; correct the amount before saving.");
    }
}
