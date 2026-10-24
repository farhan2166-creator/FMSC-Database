namespace FMSC_SMS.Core;

// Shared direct annual preview; the selector page and student account use the same RDLC/data path.
internal static class AnnualReceiptPreview
{
    internal static async Task ShowAsync(Form? owner,int admissionId,int academicYearId,Func<bool> isCurrent,Action<string> notify)
    {
        int user=CurrentUser.UserId;
        bool Current()=>owner?.IsDisposed!=true && CurrentUser.UserId==user
            && CurrentUser.HasPermission("Fees.Statement.View") && isCurrent();
        if(!Current())return;
            using var data = await DatabaseHelper.QueryUserAsync(
                "GetStudentAnnualFeeReceipts",
                DatabaseHelper.Int("@AdmissionId", admissionId),
                DatabaseHelper.Int("@AcademicYearId", academicYearId));
            if(!Current())return;
            if (data.Rows.Count == 0)
            {
                notify("No paid fee receipts were found for this student in the selected academic year.");
                return;
            }
            var institution = await InstitutionProfile.GetPrintIdentityAsync();
            if(!Current())return;
            if (string.IsNullOrWhiteSpace(institution.Name))
            {
                notify("Check School Profile before printing receipts.");
                return;
            }
            using var receipts = FeeDocumentData.Create();
            int pageKey = 0;
            foreach (var receipt in data.AsEnumerable().GroupBy(row => Convert.ToString(row["ReceiptNumber"]) ?? "")
                .OrderBy(group => Convert.ToDateTime(group.First()["PaymentDate"]))
                .ThenBy(group => Convert.ToInt32(group.First()["FeePaymentId"])))
            {
                if (string.IsNullOrWhiteSpace(receipt.Key)) continue;
                var header = receipt.First();
                using var lines = await FeeReceiptQueries.QueryLinesAsync(receipt.Key,"Fees.Statement.View");
                if(!Current())return;
                foreach (var enrollment in lines.AsEnumerable().Where(row => row.Field<int>("AdmissionId") == admissionId)
                    .GroupBy(row => row.Field<int>("EnrollmentId")))
                {
                    pageKey++;
                    var first = enrollment.First();
                    decimal total = enrollment.Sum(row => Convert.ToDecimal(row["Amount"]));
                    foreach (var line in enrollment)
                    {
                        var row = receipts.Rows.Add(pageKey,institution.Name,"STUDENT PAYMENT RECEIPT","STUDENT COPY",
                            receipt.Key,first["StudentName"],AdmissionNumbers.ForId(admissionId),
                            $"{first["ClassName"]} - {first["SectionName"]}",
                            Convert.ToDateTime(header["PaymentDate"]).ToString("dd-MM-yyyy hh:mm tt"),
                            "",line["FeeTypeName"],line["Amount"],0m,total,0m,header["PaymentMethod"],
                            "",false,false,institution.ContactNumber,institution.Address,
                            first["CampusName"],first["FatherName"],
                            line["BillingMonth"] is DBNull ? "" : Convert.ToDateTime(line["BillingMonth"]).ToString("MMM-yyyy",System.Globalization.CultureInfo.InvariantCulture));
                        row["PaymentEnrollmentId"] = enrollment.Key;
                    }
                }
            }
            if (receipts.Rows.Count == 0)
            {
                notify("No printable receipts were found for this academic year.");
                return;
            }
            await FeeReceiptQueries.ApplyCurrentBalanceAsync(receipts,"Fees.Statement.View");
            if(!Current())return;
            StudentReceiptReport.Show(owner,receipts,annualSheet:true);

    }
}
