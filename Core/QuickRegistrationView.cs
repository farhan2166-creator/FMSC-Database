namespace FMSC_SMS.Core;

internal enum RegistrationLoadOutcome { Loaded, NotFound, Failed, Superseded, Busy }

/// <summary>Validate a complete registration before changing the editor.</summary>
internal sealed record QuickRegistrationView(int Id, string Status, string Student, string Parent,
    string Contact, string Address, DateTime Joining, int Year, int Placement, string Reference,
    string FamilyReference, string AdmissionNumber, string RegistrationNumber, DataTable Fees, string Snapshot)
{
    internal static QuickRegistrationView? Read(int id, DataTable details, DataTable fees, string snapshot)
    {
        if (details.Rows.Count == 0) return null;
        if (details.Rows.Count != 1) throw new InvalidDataException("Registration details did not identify one student.");
        var row = details.Rows[0];
        foreach (string column in new[] { "AdmissionId", "Status", "StudentName", "FatherName", "ContactNumber", "Address",
            "AdmissionDate", "AcademicYearId", "ClassSectionId", "QuickReference", "AdmissionNumber", "RegistrationNumber" })
            if (!details.Columns.Contains(column)) throw new InvalidDataException($"Registration details are missing '{column}'.");
        if (id <= 0 || row["AdmissionId"] is not int returnedId || returnedId != id || string.IsNullOrWhiteSpace(snapshot))
            throw new InvalidDataException("The registration identity or saved snapshot could not be verified.");
        if (row["AdmissionDate"] is not DateTime date) throw new InvalidDataException("The registration joining date is invalid.");
        string Text(string column) => Convert.ToString(row[column]) ?? "";
        int Key(string column) => row.IsNull(column) ? 0 : row[column] is int value && value >= 0
            ? value : throw new InvalidDataException($"Registration key '{column}' is invalid.");
        string status = Text("Status");
        if (string.IsNullOrWhiteSpace(status)) throw new InvalidDataException("The registration status is missing.");
        if (status == "Pending" && (Key("AcademicYearId") == 0 || Key("ClassSectionId") == 0))
            throw new InvalidDataException("The pending registration has no complete placement.");
        if (!fees.Columns.Contains("StudentFeeAssignmentId") || !fees.Columns.Contains("HasPayments"))
            throw new InvalidDataException("Saved registration fees are missing assignment or payment state.");
        foreach (DataRow fee in fees.Rows)
            if (fee["StudentFeeAssignmentId"] is not int assignment || assignment <= 0)
                throw new InvalidDataException("A saved registration fee has no valid assignment ID.");
        return new(id, status, Text("StudentName"), Text("FatherName"), Text("ContactNumber"), Text("Address"), date,
            Key("AcademicYearId"), Key("ClassSectionId"), Text("QuickReference"),
            row.IsNull("AdmissionNumber") ? Text("QuickReference") : Text("AdmissionNumber"),
            Text("AdmissionNumber"), Text("RegistrationNumber"), PrepareFees(fees), snapshot);
    }

    internal static DataTable PrepareFees(DataTable source)
    {
        foreach (string column in new[] { "FeeTypeId", "FeeTypeName", "IsMandatory", "IsTransportLinked", "Amount", "Frequency", "Include", "Discount" })
            if (!source.Columns.Contains(column)) throw new InvalidDataException($"Registration fees are missing '{column}'.");
        foreach (var (column, type) in new[] { ("FeeTypeId", typeof(int)), ("FeeTypeName", typeof(string)), ("Frequency", typeof(string)),
            ("IsMandatory", typeof(bool)), ("IsTransportLinked", typeof(bool)), ("Include", typeof(bool)),
            ("Amount", typeof(decimal)), ("Discount", typeof(decimal)) })
            if (source.Columns[column]!.DataType != type) throw new InvalidDataException($"Registration fee column '{column}' has an unexpected type.");
        if (source.Columns.Contains("HasPayments") && source.Columns["HasPayments"]!.DataType != typeof(bool))
            throw new InvalidDataException("Registration payment flags have an unexpected type.");
        // Work on a copy so validation/derived columns never partially mutate the current bill.
        var data = source.Copy();
        foreach (DataRow row in data.Rows)
        {
            foreach (string column in new[] { "FeeTypeId", "Amount", "Discount", "Include", "IsMandatory", "IsTransportLinked" })
                if (row.IsNull(column)) throw new InvalidDataException($"Registration fee '{column}' is missing.");
            if ((int)row["FeeTypeId"] <= 0 || data.Columns.Contains("HasPayments") && row.IsNull("HasPayments"))
                throw new InvalidDataException("Registration fee identity or payment state is invalid.");
            decimal amount = Convert.ToDecimal(row["Amount"]), discount = Convert.ToDecimal(row["Discount"]);
            WholeRupees.Require(amount); WholeRupees.Require(discount);
            if (amount < 0 || discount < 0 || discount > amount) throw new InvalidDataException("A registration fee or discount is invalid.");
            _ = Convert.ToBoolean(row["Include"]); _ = Convert.ToBoolean(row["IsMandatory"]);
            _ = Convert.ToBoolean(row["IsTransportLinked"]);
            if (data.Columns.Contains("HasPayments")) _ = Convert.ToBoolean(row["HasPayments"]);
        }
        if (data.Columns.Contains("NetAmount")) data.Columns.Remove("NetAmount");
        if (data.Columns.Contains("OriginalDiscount")) data.Columns.Remove("OriginalDiscount");
        data.Columns.Add("NetAmount", typeof(decimal), "Amount - Discount");
        data.Columns.Add("OriginalDiscount", typeof(decimal));
        foreach (DataRow row in data.Rows) row["OriginalDiscount"] = row["Discount"];
        if (!data.Columns.Contains("HasPayments")) data.Columns.Add(new DataColumn("HasPayments", typeof(bool)) { DefaultValue = false });
        _ = data.AsEnumerable().Where(row => (bool)row["Include"]).Sum(row => (decimal)row["NetAmount"]);
        return data;
    }
}
