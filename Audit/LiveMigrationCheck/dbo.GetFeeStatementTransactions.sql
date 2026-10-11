
CREATE   PROCEDURE dbo.GetFeeStatementTransactions
(
    @UserId      INT,
    @AdmissionId INT,
    @FromDate    DATE,
    @ToDate      DATE,
    @IncludeOpeningBalance BIT = 0
)
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Reports.View';

    ;WITH ChargeRows AS
    (
        SELECT
            bc.BillingChargeId AS TransactionId,
            bc.AdmissionId,
            bc.EnrollmentId,
            bc.ChargeDate AS TransactionDate,
            ft.FeeTypeName,
            bc.BillingMonth,
            CAST(N'CHARGE' AS nvarchar(20)) AS TransactionType,
            CAST(bc.NetAmount AS decimal(18,2)) AS DebitAmount,
            CAST(
                ISNULL(
                    (
                        SELECT SUM(fpa.Amount)
                        FROM dbo.FeePaymentAllocations AS fpa
                        INNER JOIN dbo.FeePayments AS fp
                            ON fp.FeePaymentId = fpa.FeePaymentId
                        WHERE fpa.BillingChargeId = bc.BillingChargeId
                          AND fpa.AdmissionId = bc.AdmissionId
                          AND (@ToDate IS NULL OR fp.PaymentDate <= @ToDate)
                          AND fp.Status = N'Completed'
                    ),
                    0
                ) AS decimal(18,2)) AS PaidAmount,
            CAST(0 AS decimal(18,2)) AS CreditAmount,
            CAST(
                CASE
                    WHEN bc.NetAmount -
                         ISNULL(
                             (
                                 SELECT SUM(fpa.Amount)
                                 FROM dbo.FeePaymentAllocations AS fpa
                                 INNER JOIN dbo.FeePayments AS fp
                                     ON fp.FeePaymentId = fpa.FeePaymentId
                                 WHERE fpa.BillingChargeId = bc.BillingChargeId
                                   AND fpa.AdmissionId = bc.AdmissionId
                                   AND (@ToDate IS NULL OR fp.PaymentDate <= @ToDate)
                                   AND fp.Status = N'Completed'
                             ),
                             0
                         ) < 0
                    THEN 0
                    ELSE bc.NetAmount -
                         ISNULL(
                             (
                                 SELECT SUM(fpa.Amount)
                                 FROM dbo.FeePaymentAllocations AS fpa
                                 INNER JOIN dbo.FeePayments AS fp
                                     ON fp.FeePaymentId = fpa.FeePaymentId
                                 WHERE fpa.BillingChargeId = bc.BillingChargeId
                                   AND fpa.AdmissionId = bc.AdmissionId
                                   AND (@ToDate IS NULL OR fp.PaymentDate <= @ToDate)
                                   AND fp.Status = N'Completed'
                             ),
                             0
                         )
                END AS decimal(18,2)) AS Balance,
            CAST(NULL AS int) AS FeePaymentId,
            CAST(NULL AS nvarchar(80)) AS ReceiptNumber,
            CAST(NULL AS nvarchar(60)) AS PaymentMethod,
            bc.Remarks
        FROM dbo.BillingCharges AS bc
        INNER JOIN dbo.FeeTypes AS ft
            ON ft.FeeTypeId = bc.FeeTypeId
        WHERE bc.AdmissionId = @AdmissionId
          AND (@FromDate IS NULL OR bc.ChargeDate >= @FromDate)
          AND (@ToDate IS NULL OR bc.ChargeDate <= @ToDate)
          AND bc.Status <> N'Cancelled'
    ),
    PaymentRows AS
    (
        SELECT
            CAST(fp.FeePaymentId AS bigint) AS TransactionId,
            fp.AdmissionId,
            fp.EnrollmentId,
            fp.PaymentDate AS TransactionDate,
            CAST(N'Payment' AS nvarchar(200)) AS FeeTypeName,
            CAST(NULL AS date) AS BillingMonth,
            CAST(N'PAYMENT' AS nvarchar(20)) AS TransactionType,
            CAST(0 AS decimal(18,2)) AS DebitAmount,
            CAST(
                ISNULL(
                    (
                        SELECT SUM(fpa.Amount)
                        FROM dbo.FeePaymentAllocations AS fpa
                        WHERE fpa.FeePaymentId = fp.FeePaymentId
                          AND fpa.AdmissionId = fp.AdmissionId
                    ),
                    0
                ) AS decimal(18,2)) AS PaidAmount,
            CAST(
                ISNULL(
                    (
                        SELECT SUM(fpa.Amount)
                        FROM dbo.FeePaymentAllocations AS fpa
                        WHERE fpa.FeePaymentId = fp.FeePaymentId
                          AND fpa.AdmissionId = fp.AdmissionId
                    ),
                    0
                ) AS decimal(18,2)) AS CreditAmount,
            CAST(0 AS decimal(18,2)) AS Balance,
            fp.FeePaymentId,
            fp.ReceiptNumber,
            fp.PaymentMethod,
            fp.Remarks
        FROM dbo.FeePayments AS fp
        WHERE fp.AdmissionId = @AdmissionId
          AND (@FromDate IS NULL OR fp.PaymentDate >= @FromDate)
          AND (@ToDate IS NULL OR fp.PaymentDate <= @ToDate)
          AND fp.Status = N'Completed'
    )
    SELECT
        TransactionType, TransactionId, AdmissionId, EnrollmentId, TransactionDate,
        FeeTypeName, BillingMonth, DebitAmount, PaidAmount, CreditAmount, Balance,
        FeePaymentId, ReceiptNumber, PaymentMethod, Remarks
    FROM ChargeRows

    UNION ALL

    SELECT
        TransactionType, TransactionId, AdmissionId, EnrollmentId, TransactionDate,
        FeeTypeName, BillingMonth, DebitAmount, PaidAmount, CreditAmount, Balance,
        FeePaymentId, ReceiptNumber, PaymentMethod, Remarks
    FROM PaymentRows
    WHERE PaidAmount <> 0

    ORDER BY TransactionDate, TransactionType, TransactionId;
    IF @IncludeOpeningBalance = 1
    BEGIN
        DECLARE @OpeningTo DATE = DATEADD(DAY, -1, @FromDate);
    DECLARE @TotalCharges DECIMAL(18,2) = ISNULL((
        SELECT SUM(bc.NetAmount)
        FROM dbo.BillingCharges bc
        WHERE bc.AdmissionId = @AdmissionId
          AND bc.Status <> N'Cancelled'
          AND bc.ChargeDate <= @OpeningTo
    ), 0);

    DECLARE @TotalPayments DECIMAL(18,2) = ISNULL((
        SELECT SUM(fpa.Amount)
        FROM dbo.FeePaymentAllocations fpa
        INNER JOIN dbo.FeePayments fp ON fp.FeePaymentId = fpa.FeePaymentId
        WHERE fpa.AdmissionId = @AdmissionId
          AND fp.PaymentDate <= @OpeningTo
          AND fp.Status = N'Completed'
    ), 0);

    SELECT (@TotalCharges - @TotalPayments) AS OpeningBalance;

    END;
END;



-- GetStudentRegister

