-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE VIEW dbo.DailyClosingPayments AS
SELECT P.FeePaymentId,P.PaymentDate,P.PaymentMethod,P.TotalAmount,P.ReceiptNumber,P.RelatedFeePaymentId,P.ReferenceNumber,P.CreatedByUserId
FROM dbo.FeePayments P JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId
JOIN dbo.Enrollments E ON E.EnrollmentId=P.EnrollmentId AND E.AdmissionId=P.AdmissionId
WHERE P.Status=N'Completed' AND P.IsCreditApplication=0;
