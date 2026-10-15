-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Fees.View';

    SELECT
        X.TransactionId,
        X.TransactionType,
        X.EnrollmentId,
        X.TransactionDate,
        X.FeeTypeName,
        X.ChargeType,
        X.BillingMonth,
        X.Amount,
        X.PaidAmount,
        X.DebitAmount,
        X.FeePaymentId,
        X.ReceiptNumber,
        X.PaymentMethod,
        X.Remarks
    FROM
    (
        /* =====================================================
           BILLING CHARGES
           ===================================================== */

        SELECT
            BC.BillingChargeId AS TransactionId,
            N'CHARGE' AS TransactionType,
            BC.EnrollmentId,
            BC.ChargeDate AS TransactionDate,
            FT.FeeTypeName,
            FT.FeeTypeName AS ChargeType,
            BC.BillingMonth,

            BC.NetAmount AS Amount,

            CAST(0.00 AS DECIMAL(18,2))
                AS PaidAmount,

            BC.NetAmount AS DebitAmount,

            CAST(NULL AS INT)
                AS FeePaymentId,

            CAST(NULL AS NVARCHAR(40))
                AS ReceiptNumber,

            CAST(NULL AS NVARCHAR(30))
                AS PaymentMethod,

            BC.Remarks

        FROM dbo.BillingCharges BC

        INNER JOIN dbo.FeeTypes FT
            ON FT.FeeTypeId = BC.FeeTypeId

        WHERE BC.AdmissionId = @AdmissionId
          AND (
                @FromDate IS NULL
                OR BC.ChargeDate >= @FromDate
              )
          AND (
                @ToDate IS NULL
                OR BC.ChargeDate <= @ToDate
              )
          AND BC.Status <> N'Cancelled'


        UNION ALL


        /* =====================================================
           PAYMENTS / REFUNDS / CORRECTIONS / REVERSALS
           ===================================================== */

        SELECT
            FP.FeePaymentId AS TransactionId,

            CASE
                WHEN FP.Status = N'Reversed'
                    THEN N'PAYMENT-REVERSED'

                WHEN FP.TotalAmount < 0
                     AND FP.PaymentMethod = N'Correction'
                    THEN N'CORRECTION'

                WHEN FP.TotalAmount < 0
                    THEN N'REFUND'

                ELSE N'PAYMENT'
            END AS TransactionType,

            FP.EnrollmentId,

            FP.PaymentDate AS TransactionDate,


            /* -------------------------------------------------
               Description
               ------------------------------------------------- */

            CASE
                WHEN FP.TotalAmount < 0
                     AND FP.PaymentMethod = N'Correction'
                    THEN N'Fee Correction'

                WHEN FP.TotalAmount < 0
                    THEN N'Fee Refund'

                ELSE N'Payment'
            END AS FeeTypeName,


            /* -------------------------------------------------
               CHARGE TYPE
               
               A payment can cover multiple charges.
               Return all related fee types.
               ------------------------------------------------- */

            ISNULL(
                STUFF(
                    (
                        SELECT
                            N', ' + FT2.FeeTypeName

                        FROM dbo.FeePaymentAllocations FPA2

                        INNER JOIN dbo.BillingCharges BC2
                            ON BC2.BillingChargeId =
                               FPA2.BillingChargeId

                        INNER JOIN dbo.FeeTypes FT2
                            ON FT2.FeeTypeId =
                               BC2.FeeTypeId

                        WHERE FPA2.FeePaymentId =
                              FP.FeePaymentId

                        GROUP BY
                            FT2.FeeTypeName

                        ORDER BY
                            MIN(FPA2.FeePaymentAllocationId)

                        FOR XML PATH(N''),
                            TYPE
                    ).value(
                        N'.',
                        N'NVARCHAR(MAX)'
                    ),
                    1,
                    2,
                    N''
                ),
                N''
            ) AS ChargeType,


            NULL AS BillingMonth,


            /* -------------------------------------------------
               ACTUAL TRANSACTION AMOUNT
               
               Positive collection:
                   +1000

               Refund:
                   -500

               Correction:
                   -500

               Reversed payment:
                   0
               ------------------------------------------------- */

            CASE
                WHEN FP.Status = N'Completed'
                    THEN FP.TotalAmount

                ELSE
                    CAST(0.00 AS DECIMAL(18,2))
            END AS Amount,


            /* -------------------------------------------------
               MONEY RECEIVED / RETURNED
               ------------------------------------------------- */

            CASE
                WHEN FP.Status = N'Completed'
                    THEN FP.TotalAmount

                ELSE
                    CAST(0.00 AS DECIMAL(18,2))
            END AS PaidAmount,


            /* -------------------------------------------------
               DEBIT / REDUCTION
               
               Refund and correction are negative transactions,
               so DebitAmount remains positive for statement
               calculations.
               ------------------------------------------------- */

            CASE
                WHEN FP.Status = N'Completed'
                     AND FP.TotalAmount < 0
                    THEN -FP.TotalAmount

                ELSE
                    CAST(0.00 AS DECIMAL(18,2))
            END AS DebitAmount,


            FP.FeePaymentId,
            FP.ReceiptNumber,
            FP.PaymentMethod,


            /* -------------------------------------------------
               REMARKS
               ------------------------------------------------- */

            CASE
                WHEN FP.RelatedFeePaymentId IS NOT NULL
                    THEN CONCAT(

                        COALESCE(
                            NULLIF(
                                FP.Remarks,
                                N''
                            ),
                            N''
                        ),

                        CASE
                            WHEN NULLIF(
                                    FP.Remarks,
                                    N''
                                 ) IS NOT NULL
                                THEN N' | '

                            ELSE N''
                        END,

                        CASE
                            WHEN FP.PaymentMethod =
                                 N'Correction'
                                THEN N'Correction of receipt '

                            ELSE N'Refund of receipt '
                        END,

                        COALESCE(
                            (
                                SELECT
                                    ReceiptNumber

                                FROM dbo.FeePayments OP

                                WHERE OP.FeePaymentId =
                                      FP.RelatedFeePaymentId
                            ),
                            N''
                        )
                    )

                WHEN FP.Status = N'Reversed'
                    THEN CONCAT(

                        COALESCE(
                            NULLIF(
                                FP.Remarks,
                                N''
                            ),
                            N''
                        ),

                        CASE
                            WHEN NULLIF(
                                    FP.Remarks,
                                    N''
                                 ) IS NOT NULL
                                THEN N' | '

                            ELSE N''
                        END,

                        N'Status: Reversed'
                    )

                ELSE
                    FP.Remarks
            END AS Remarks

        FROM dbo.FeePayments FP

        WHERE FP.AdmissionId = @AdmissionId

          AND FP.Status IN
          (
              N'Completed',
              N'Reversed'
          )

          AND (
                @FromDate IS NULL
                OR FP.PaymentDate >= @FromDate
              )

          AND (
                @ToDate IS NULL
                OR FP.PaymentDate <= @ToDate
              )
    ) X

    /* =========================================================
       NEWEST TRANSACTIONS FIRST
       ========================================================= */

    ORDER BY
        X.TransactionDate DESC,

        X.TransactionId DESC;

END;
