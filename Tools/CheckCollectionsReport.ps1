# Loaded inside TestOutstandingAging's migration transaction. Never writes business data.
function Sum-Column($table,[string]$column) {
    [decimal]$value=0
    foreach($row in $table.Rows){if($row[$column]-ne[DBNull]::Value){$value+=[decimal]$row[$column]}}
    return $value
}
function Check-CollectionsScope([string]$Parameters,[string]$Predicate) {
    $baseline=Read-Data @"
SELECT SUM(CASE WHEN P.IsCreditApplication=0 AND P.TotalAmount>0 THEN P.TotalAmount ELSE 0 END) [Money Received],
 -SUM(CASE WHEN P.IsCreditApplication=0 AND P.TotalAmount<0 AND P.PaymentMethod=N'Refund' THEN P.TotalAmount ELSE 0 END) Refunds,
 -SUM(CASE WHEN P.IsCreditApplication=0 AND P.TotalAmount<0 AND P.PaymentMethod=N'Correction' THEN P.TotalAmount ELSE 0 END) Corrections,
 SUM(CASE WHEN P.IsCreditApplication=1 THEN P.TotalAmount ELSE 0 END) [Credit Applied],
 SUM(CASE WHEN P.IsCreditApplication=0 THEN P.TotalAmount ELSE 0 END) [Adjusted Collection],
 COUNT(DISTINCT CASE WHEN R.ReceiptNumber IS NOT NULL THEN R.ReceiptNumber WHEN P.IsCreditApplication=0 THEN NULLIF(P.ReceiptNumber,N'') END) Receipts
FROM dbo.FeePayments P JOIN dbo.Enrollments E ON E.EnrollmentId=P.EnrollmentId AND E.AdmissionId=P.AdmissionId
LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
LEFT JOIN dbo.CollectionReceiptPayments L ON L.FeePaymentId=P.FeePaymentId
LEFT JOIN dbo.CollectionReceipts R ON R.CollectionReceiptId=L.CollectionReceiptId
WHERE P.Status=N'Completed' AND ($Predicate);
"@
    foreach($view in @('Receipts','Detailed Transactions','Summary')) {
        $groups=if($view-eq'Summary'){@('Day','Week','Month','Payment Method','Class','Campus','Fee Type')}else{@('Day')}
        foreach($group in $groups) {
            $result=Read-Data "EXEC dbo.GetCollectionsAndReceiptsReport @UserId=$user,@View=N'$view',@GroupBy=N'$group' $Parameters;"
            Check ($result.Tables.Count-eq2) 'Every collection view must include authoritative totals'
            foreach($column in @('Receipts','Money Received','Refunds','Corrections','Credit Applied','Adjusted Collection')) {
                $expected=$baseline.Tables[0].Rows[0][$column];if($expected-eq[DBNull]::Value){$expected=0}
                Check ([decimal]$result.Tables[1].Rows[0][$column]-eq[decimal]$expected) "Collection totals differ from independent payments: $view/$group/$column/$Parameters"
                if($view-ne'Detailed Transactions'-and$column-ne'Receipts') {
                    Check ((Sum-Column $result.Tables[0] $column)-eq[decimal]$expected) "Collection rows do not reconcile: $view/$group/$column/$Parameters"
                }
            }
            if($view-eq'Detailed Transactions') {
                Check ((Sum-Column $result.Tables[0] 'Collection Amount')-eq[decimal]$result.Tables[1].Rows[0]['Adjusted Collection']) 'Detailed cash rows must reconcile without counting credit twice'
                Check ((Sum-Column $result.Tables[0] 'Credit Applied')-eq[decimal]$result.Tables[1].Rows[0]['Credit Applied']) 'Detailed credit rows must reconcile'
            }
            Check ($result.Tables[0].Columns.Contains('Campus')) 'Campus must be visible in every collections view'
            if($view-eq'Receipts'-or$view-eq'Detailed Transactions') {
                Check ($result.Tables[0].Columns.Contains('Class / Section')) 'Collection rows must identify class and section'
                foreach($row in $result.Tables[0].Rows) {
                    Check (![string]::IsNullOrWhiteSpace([string]$row['Receipt / Reference'])) 'A transaction is missing its readable receipt/reference'
                    Check (!([string]$row['Receipt / Reference']).StartsWith('CR-')) 'Internal credit UUID leaked into the report'
                }
            }
            $result.Dispose()
        }
    }
}
Check-CollectionsScope '' '1=1'
foreach($method in @('Cash','Account Credit','Refund','Correction','No matching method')) {
    Check-CollectionsScope ",@PaymentMethod=N'$method'" "P.PaymentMethod=N'$method'"
}
$collectionScopes=Read-Data 'SELECT DISTINCT CCS.CampusId,CCS.ClassId,CCS.SectionId FROM dbo.FeePayments P JOIN dbo.Enrollments E ON E.EnrollmentId=P.EnrollmentId JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId WHERE P.Status=N''Completed'';'
foreach($scope in $collectionScopes.Tables[0].Rows) {
    $campus=[int]$scope['CampusId'];$class=[int]$scope['ClassId'];$section=[int]$scope['SectionId']
    Check-CollectionsScope ",@CampusId=$campus,@ClassId=$class,@SectionId=$section" "CCS.CampusId=$campus AND CCS.ClassId=$class AND CCS.SectionId=$section"
}
foreach($offset in @(-1,0,1)) {
    $day=$today.AddDays($offset).ToString('yyyy-MM-dd')
    Check-CollectionsScope ",@FromDate='$day',@ToDate='$day'" "P.PaymentDate='$day'"
}
$receipts=Read-Data "EXEC dbo.GetCollectionsAndReceiptsReport @UserId=$user,@View=N'Receipts';"
$families=Read-Data 'SELECT ReceiptNumber FROM dbo.FeePayments WHERE NULLIF(ReceiptNumber,N'''') IS NOT NULL GROUP BY ReceiptNumber HAVING COUNT(DISTINCT AdmissionId)>1;'
foreach($family in $families.Tables[0].Rows) {
    foreach($row in $receipts.Tables[0].Rows) {
        if($row['Receipt / Reference']-eq$family['ReceiptNumber']){Check ($row['Scope']-eq'Family') 'Family receipt lost its identity after a reversal'}
    }
}
$savedCredit=Read-Data 'SELECT DISTINCT R.ReceiptNumber FROM dbo.CollectionReceipts R JOIN dbo.CollectionReceiptPayments L ON L.CollectionReceiptId=R.CollectionReceiptId JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId WHERE P.Status=N''Completed'' AND P.IsCreditApplication=1;'
foreach($saved in $savedCredit.Tables[0].Rows) {
    $matching=@($receipts.Tables[0].Rows|Where-Object{$_['Receipt / Reference']-eq$saved['ReceiptNumber']})
    Check ($matching.Count-eq1) 'A saved credit receipt is omitted or duplicated'
}
# Verify returned order directly against immutable payment chronology and public receipt links.
$expectedOrder=Read-Data @'
SELECT COALESCE(R.ReceiptNumber,CASE WHEN P.IsCreditApplication=0 THEN NULLIF(P.ReceiptNumber,N'') END,
 CONCAT(CASE WHEN P.IsCreditApplication=1 THEN N'Credit #' ELSE N'Payment #' END,P.FeePaymentId)) Ref
FROM dbo.FeePayments P LEFT JOIN dbo.CollectionReceiptPayments L ON L.FeePaymentId=P.FeePaymentId
LEFT JOIN dbo.CollectionReceipts R ON R.CollectionReceiptId=L.CollectionReceiptId WHERE P.Status=N'Completed'
GROUP BY COALESCE(R.ReceiptNumber,CASE WHEN P.IsCreditApplication=0 THEN NULLIF(P.ReceiptNumber,N'') END,
 CONCAT(CASE WHEN P.IsCreditApplication=1 THEN N'Credit #' ELSE N'Payment #' END,P.FeePaymentId))
ORDER BY MIN(P.PaymentDate),MIN(P.CreatedAt),MIN(P.FeePaymentId),Ref;
'@
Check ((($receipts.Tables[0].Rows|ForEach-Object{$_['Receipt / Reference']}) -join '|') -eq (($expectedOrder.Tables[0].Rows|ForEach-Object{$_['Ref']}) -join '|')) 'Receipts must follow date and original transaction sequence'
$orderedStudents=Read-Data "EXEC dbo.GetOutstandingAndAgingReport @UserId=$user,@View=N'Students';"
$expectedStudents=Read-Data @'
SELECT COALESCE(S.AdmissionNumber,S.QuickReference) Ref
FROM dbo.Students S JOIN dbo.BillingCharges B ON B.AdmissionId=S.AdmissionId
JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId JOIN dbo.Classes C ON C.ClassId=CCS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
WHERE B.Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(B.BillingChargeId)>0
GROUP BY S.AdmissionId,S.AdmissionNumber,S.QuickReference,CA.CampusName,C.ClassName,SEC.SectionName
ORDER BY MIN(CA.DisplayOrder),MIN(C.DisplayOrder),MIN(SEC.DisplayOrder),MIN(COALESCE(S.AdmissionNumberBase,TRY_CONVERT(bigint,REPLACE(S.QuickReference,N'QR-',N'')))),MIN(S.AdmissionNumberSuffix),COALESCE(S.AdmissionNumber,S.QuickReference),MIN(S.StudentName);
'@
Check ((($orderedStudents.Tables[0].Rows|ForEach-Object{$_['Adm. / QR No.']}) -join '|') -eq (($expectedStudents.Tables[0].Rows|ForEach-Object{$_['Ref']}) -join '|')) 'Owing students must follow school order and numeric admission/QR order'
foreach($view in @('Students','Charges','Aging','Families')) {
    $result=Read-Data "EXEC dbo.GetOutstandingAndAgingReport @UserId=$user,@View=N'$view';"
    Check ($result.Tables[0].Columns.Contains('Campus')) "Outstanding $view must display campus"
    $result.Dispose()
}
