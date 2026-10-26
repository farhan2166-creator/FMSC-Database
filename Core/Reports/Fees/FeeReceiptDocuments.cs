namespace FMSC_SMS.Core;

/// <summary>Loads saved collections and routes them to the matching receipt report.</summary>
internal static class FeeReceiptDocuments
{
    internal static async Task<bool> TryShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,string receiptNumber)
    {
        using var result=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        if(result.Tables.Count<3 || result.Tables[0].Rows.Count==0)return false;
        bool latest=await RefreshBalancesAsync(result.Tables[0].Rows[0],result.Tables[1],receiptNumber:receiptNumber);
        await ReadChildCashReceivedAsync(receiptNumber,result.Tables[0].Rows[0],result.Tables[1]);
        using var printableLines=result.Tables[2].Copy();
        await RefreshChargeConcessionsAsync(result.Tables[0].Rows[0],result.Tables[1],printableLines,latest);
        if(latest)await AddUnpaidChildFeesAsync(result.Tables[0].Rows[0],result.Tables[1],printableLines);
        using var data=BuildData(institution,result.Tables[0].Rows[0],result.Tables[1],printableLines);
        Show(owner,data);
        return true;
    }

    // A concession saved after collection changes live dues without changing the
    // original receipt. Reflect that change only in the latest printout, and only
    // for its saved fee lines; other charges and payments are not discounts.
    internal static async Task RefreshChargeConcessionsAsync(DataRow header,DataTable students,DataTable lines,bool latest,
        Func<string,Task<DataTable>>? loadChanges=null)
    {
        if(!latest)return;
        loadChanges ??= QueryChargeConcessionChangesAsync;
        using var changes=await loadChanges(Convert.ToString(header["ReceiptNumber"])??"");
        var byCharge=changes.AsEnumerable().ToDictionary(row=>Convert.ToInt64(row["ChargeId"]));
        foreach(DataRow student in students.Rows)
        {
            decimal difference=0m;
            foreach(var line in lines.AsEnumerable().Where(row=>Convert.ToInt32(row["AdmissionId"])==Convert.ToInt32(student["AdmissionId"])))
            {
                if(!byCharge.TryGetValue(Convert.ToInt64(line["ChargeId"]),out var change))continue;
                decimal original=Convert.ToDecimal(line["Discount"]);
                decimal discount=original+Convert.ToDecimal(change["DiscountChange"]);
                // AmountBefore already excluded concessions predating this
                // collection. Restore that part of the fee if one is removed.
                if(discount<0m)line["AmountBefore"]=Convert.ToDecimal(line["AmountBefore"])-discount;
                line["Discount"]=Math.Max(0m,discount);
                difference+=Convert.ToDecimal(line["Discount"])-original;
            }
            student["DiscountApplied"]=Convert.ToDecimal(student["DiscountApplied"])+difference;
            header["DiscountApplied"]=Convert.ToDecimal(header["DiscountApplied"])+difference;
        }
    }

    private static Task<DataTable> QueryChargeConcessionChangesAsync(string receiptNumber) => DatabaseHelper.QuerySqlAsync("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT L.ChargeId,
            COALESCE(CurrentTerms.NewDiscount,B.DiscountAmount)
                -COALESCE(IssueTerms.NewDiscount,B.GrossAmount-FirstTerms.PreviousNet,B.DiscountAmount) DiscountChange
        FROM dbo.CollectionReceipts R
        CROSS APPLY OPENJSON(R.SnapshotJson,'$.Lines') WITH(ChargeId bigint) L
        JOIN dbo.BillingCharges B ON B.BillingChargeId=L.ChargeId
        OUTER APPLY(SELECT TOP(1) C.PreviousNet FROM dbo.FeeBillCorrections C
            WHERE C.BillingChargeId=L.ChargeId ORDER BY C.FeeBillCorrectionId) FirstTerms
        OUTER APPLY(SELECT TOP(1) C.NewDiscount FROM dbo.FeeBillCorrections C
            WHERE C.BillingChargeId=L.ChargeId AND C.CreatedAt<=R.CreatedAt
            ORDER BY C.FeeBillCorrectionId DESC) IssueTerms
        OUTER APPLY(SELECT TOP(1) C.NewDiscount FROM dbo.FeeBillCorrections C
            WHERE C.BillingChargeId=L.ChargeId ORDER BY C.FeeBillCorrectionId DESC) CurrentTerms
        WHERE R.ReceiptNumber=@ReceiptNumber;
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@ReceiptNumber",receiptNumber));

    // Unallocated children have no saved receipt lines. Show their current dues
    // alongside the current balance, without changing the saved payment snapshot.
    internal static async Task AddUnpaidChildFeesAsync(DataRow header,DataTable students,DataTable lines,
        Func<int,Task<DataTable>>? loadFees=null)
    {
        if(students.Rows.Count<2)return;
        loadFees ??= QueryUnpaidFeesAsync;
        foreach(DataRow student in students.Rows)
        {
            int id=Convert.ToInt32(student["AdmissionId"]);
            if(lines.AsEnumerable().Any(row=>Convert.ToInt32(row["AdmissionId"])==id))continue;
            using var fees=await loadFees(id);
            foreach(DataRow fee in fees.Rows)
            {
                var row=lines.NewRow();
                foreach(DataColumn column in lines.Columns)
                    if(fees.Columns.Contains(column.ColumnName))row[column.ColumnName]=fee[column.ColumnName];
                lines.Rows.Add(row);
            }
            decimal discount=fees.AsEnumerable().Sum(row=>FamilyFeeDocumentPresentation.Money(row,"Discount"));
            if(discount!=0m)
            {
                student["DiscountApplied"]=Convert.ToDecimal(student["DiscountApplied"])+discount;
                header["DiscountApplied"]=Convert.ToDecimal(header["DiscountApplied"])+discount;
            }
        }
    }

    private static Task<DataTable> QueryUnpaidFeesAsync(int admissionId) => DatabaseHelper.QuerySqlAsync("""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT B.BillingChargeId ChargeId,B.AdmissionId,B.EnrollmentId,
            dbo.GetFeeChargeBalance(B.BillingChargeId)+B.DiscountAmount AmountBefore,
            B.DiscountAmount Discount,CONVERT(decimal(12,2),0) Cash,CONVERT(decimal(12,2),0) Credit,
            F.FeeTypeName,COALESCE(B.BillingMonth,B.ChargeDate) FeePeriod,
            C.ClassName,SEC.SectionName,CA.CampusName,dbo.GetFeeChargeBalance(B.BillingChargeId) BalanceAfter
        FROM dbo.BillingCharges B
        JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
        JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
        JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
        JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
        JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
        JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
        WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
        ORDER BY COALESCE(B.BillingMonth,B.ChargeDate),F.DisplayOrder,B.BillingChargeId;
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@AdmissionId",admissionId));

    // The collection snapshot stores each child's fee allocation. The linked
    // original payments identify who actually received any retained advance.
    private static async Task ReadChildCashReceivedAsync(string receiptNumber,DataRow header,DataTable students)
    {
        using var payments=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT P.AdmissionId,SUM(P.TotalAmount) CashReceived
            FROM dbo.CollectionReceipts R
            JOIN dbo.CollectionReceiptPayments L ON L.CollectionReceiptId=R.CollectionReceiptId
            JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId
            WHERE R.ReceiptNumber=@ReceiptNumber AND P.IsCreditApplication=0
            GROUP BY P.AdmissionId;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        ApplyChildCashReceived(header,students,payments);
    }

    internal static void ApplyChildCashReceived(DataRow header,DataTable students,DataTable payments)
    {
        var received=payments.AsEnumerable().ToDictionary(row=>Convert.ToInt32(row["AdmissionId"]),row=>Convert.ToDecimal(row["CashReceived"]));
        var ids=students.AsEnumerable().Select(row=>Convert.ToInt32(row["AdmissionId"])).ToHashSet();
        if(received.Keys.Any(id=>!ids.Contains(id))||received.Values.Sum()!=Convert.ToDecimal(header["CashReceived"]))
            throw new InvalidOperationException("The receipt's original payments could not be matched to its children. Reload before printing.");
        if(!students.Columns.Contains("CashReceived"))students.Columns.Add("CashReceived",typeof(decimal));
        foreach(DataRow student in students.Rows)
        {
            decimal amount=received.GetValueOrDefault(Convert.ToInt32(student["AdmissionId"]));
            if(amount<Convert.ToDecimal(student["CashApplied"]))
                throw new InvalidOperationException("The child's received payment could not be confirmed. Reload before printing.");
            student["CashReceived"]=amount;
        }
    }

    // Latest collections use live dues. Historical collections retain their
    // recorded child balances and never acquire charges from later months.
    internal static async Task<bool> RefreshBalancesAsync(DataRow header,DataTable students,Func<int,Task<decimal>>? loadBalance=null,
        Func<string,Task<FeeReceiptQueries.ReceiptBalanceState>>? loadState=null,string? receiptNumber=null)
    {
        loadBalance ??= admissionId=>FeeReceiptQueries.QueryCurrentBalanceAsync(admissionId);
        loadState ??= receipt=>FeeReceiptQueries.QueryBalanceStateAsync(receipt);
        var state=await loadState(receiptNumber??Convert.ToString(header["ReceiptNumber"])??"");
        var balances=new Dictionary<int,decimal>();
        foreach(DataRow student in students.Rows)
        {
            int admissionId=Convert.ToInt32(student["AdmissionId"]);
            if(!balances.TryGetValue(admissionId,out decimal balance))
            {
                if(state.IsLatest)balance=await loadBalance(admissionId);
                else if(!state.RecordedBalances.TryGetValue(admissionId,out balance))
                    throw new InvalidOperationException("This older receipt has no recorded outstanding balance. Its original figure cannot be confirmed; today's balance has not been substituted.");
                balances[admissionId]=balance;
            }
            student["BalanceRemaining"]=balance;
        }
        header["BalanceRemaining"]=balances.Values.Sum();
        return state.IsLatest;
    }

    internal static DataTable BuildData(InstitutionProfile.PrintIdentity institution,DataRow header,DataTable students,DataTable lines) =>
        students.Rows.Count>1?FamilyReceiptReport.BuildData(institution,header,students,lines)
            :StudentReceiptReport.BuildData(institution,header,students,lines);

    internal static void Show(Form? owner,DataTable data)
    {
        if(data.Columns.Contains("FamilyRowKind"))FamilyReceiptReport.Show(owner,data);
        else StudentReceiptReport.Show(owner,data);
    }
}
