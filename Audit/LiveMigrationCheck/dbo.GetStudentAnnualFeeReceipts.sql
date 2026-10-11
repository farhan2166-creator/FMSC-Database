
-- GetStudentAnnualFeeReceipts
CREATE   PROCEDURE dbo.GetStudentAnnualFeeReceipts
@UserId INT, @AdmissionId INT, @AcademicYearId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Statement.View';
    IF @AdmissionId <= 0
        THROW 59801, 'A valid student is required.', 1;
    IF @AcademicYearId <= 0
        THROW 59802, 'A valid academic year is required.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 59803, 'The selected student was not found.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicYears
                   WHERE  AcademicYearId = @AcademicYearId)
        THROW 59804, 'The selected academic year was not found.', 1;
    WITH     OriginalPayments
    AS       (SELECT FP.FeePaymentId,
                     FP.AdmissionId,
                     FP.EnrollmentId,
                     FP.PaymentDate,
                     FP.ReceiptNumber,
                     FP.TotalAmount AS IssuedAmount,
                     FP.Status AS PaymentStatus,
                     FP.PaymentMethod,
                     FP.ReferenceNumber,
                     ISNULL(R.ReversedAmount, 0) AS ReversedAmount
              FROM   dbo.FeePayments AS FP
                     INNER JOIN
                     dbo.Enrollments AS E
                     ON E.EnrollmentId = FP.EnrollmentId OUTER APPLY (SELECT SUM(-RV.TotalAmount) AS ReversedAmount
                                                                      FROM   dbo.FeePayments AS RV
                                                                      WHERE  RV.RelatedFeePaymentId = FP.FeePaymentId
                                                                             AND RV.TotalAmount < 0 AND RV.Status=N'Completed') AS R
              WHERE  FP.AdmissionId = @AdmissionId
                     AND E.AcademicYearId = @AcademicYearId
                     AND FP.RelatedFeePaymentId IS NULL AND FP.Status IN (N'Completed',N'Reversed'))
    SELECT   OP.FeePaymentId,
             OP.ReceiptNumber,
             OP.PaymentDate,
             OP.PaymentMethod,
             OP.ReferenceNumber,
             OP.IssuedAmount,
             CASE WHEN OP.PaymentStatus=N'Reversed' THEN OP.IssuedAmount ELSE OP.ReversedAmount END AS ReversedAmount,
             CASE WHEN OP.PaymentStatus=N'Reversed' THEN CAST(0 AS decimal(12,2)) ELSE OP.IssuedAmount - OP.ReversedAmount END AS LiveAmount,
             CASE WHEN OP.PaymentStatus=N'Reversed' THEN N'Fully reversed' WHEN OP.ReversedAmount <= 0 THEN N'Issued' WHEN OP.IssuedAmount - OP.ReversedAmount <= 0 THEN N'Fully reversed' ELSE N'Partially reversed' END AS ReceiptStatus,
             S.AdmissionId,
             S.StudentName,
             S.FatherName,
             S.RegistrationNumber,
             AY.YearName AS AcademicYearName,
             CAMP.CampusName,
             C.ClassName,
             SEC.SectionName,
             SP.SchoolName,
             FT.FeeTypeName,
             BC.BillingMonth,
             FPA.Amount AS LineAmount,
             FT.DisplayOrder AS LineOrder
    FROM     OriginalPayments AS OP
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = OP.AdmissionId
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = OP.EnrollmentId
             INNER JOIN
             dbo.AcademicYears AS AY
             ON AY.AcademicYearId = E.AcademicYearId
             LEFT OUTER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             LEFT OUTER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = CCS.CampusId
             LEFT OUTER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             LEFT OUTER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId CROSS JOIN (SELECT   TOP (1) SchoolName
                                                          FROM     dbo.SchoolProfile
                                                          ORDER BY SchoolProfileId) AS SP
             INNER JOIN
             dbo.FeePaymentAllocations AS FPA
             ON FPA.FeePaymentId = OP.FeePaymentId
             INNER JOIN
             dbo.BillingCharges AS BC
             ON BC.BillingChargeId = FPA.BillingChargeId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId
    WHERE    FPA.Amount <> 0
    ORDER BY OP.PaymentDate, OP.FeePaymentId, CASE WHEN BC.BillingMonth IS NULL THEN 0 ELSE 1 END, BC.BillingMonth, FT.DisplayOrder, FT.FeeTypeName;
END

