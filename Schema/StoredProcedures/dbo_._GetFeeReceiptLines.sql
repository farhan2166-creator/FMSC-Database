-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.GetFeeReceiptLines
 @UserId int,@ReceiptNumber nvarchar(40),@ReadPermission nvarchar(100)=N'Fees.View'
AS
BEGIN
 SET NOCOUNT ON;
 IF @ReadPermission NOT IN(N'Fees.View',N'Fees.Statement.View',N'Reports.View') THROW 54911,'Invalid receipt read permission.',1;
 EXEC dbo.RequirePermission @UserId,@ReadPermission;
 ;WITH Lines AS(
  SELECT P.AdmissionId,P.EnrollmentId,F.FeeTypeName,B.BillingMonth,A.Amount,P.ReceiptNumber,P.PaymentDate
  FROM dbo.FeePayments P JOIN dbo.FeePaymentAllocations A ON A.FeePaymentId=P.FeePaymentId
  JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
  WHERE P.ReceiptNumber=@ReceiptNumber AND P.RelatedFeePaymentId IS NULL AND A.Amount<>0
  UNION ALL
  SELECT P.AdmissionId,P.EnrollmentId,CASE WHEN C.EntryKind=N'Deposit' THEN N'Credit toward future fees' ELSE N'Unused credit refunded' END,
   NULL,C.Amount,P.ReceiptNumber,P.PaymentDate
  FROM dbo.StudentCreditEntries C JOIN dbo.FeePayments P ON P.FeePaymentId=C.FeePaymentId
  WHERE P.ReceiptNumber=@ReceiptNumber AND C.EntryKind IN(N'Deposit',N'Refund')
  UNION ALL
  SELECT J.AdmissionId,J.EnrollmentId,J.FeeTypeName,J.BillingMonth,CONVERT(decimal(12,2),0),P.ReceiptNumber,P.PaymentDate
  FROM (SELECT TOP(1) ReceiptNumber,PaymentDate,ConcessionLinesJson FROM dbo.FeePayments WHERE ReceiptNumber=@ReceiptNumber ORDER BY FeePaymentId) P
  CROSS APPLY OPENJSON(P.ConcessionLinesJson) WITH(AdmissionId int,EnrollmentId int,FeeTypeName nvarchar(150),BillingMonth date) J
 )
 SELECT S.StudentName,S.FatherName,L.AdmissionId,L.EnrollmentId,C.ClassName,SEC.SectionName,CAM.CampusName,
  L.FeeTypeName,L.BillingMonth,L.Amount,L.ReceiptNumber,L.PaymentDate
 FROM Lines L JOIN dbo.Students S ON S.AdmissionId=L.AdmissionId JOIN dbo.Enrollments E ON E.EnrollmentId=L.EnrollmentId
 JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId JOIN dbo.Classes C ON C.ClassId=P.ClassId
 JOIN dbo.Sections SEC ON SEC.SectionId=P.SectionId JOIN dbo.Campuses CAM ON CAM.CampusId=P.CampusId
 ORDER BY S.StudentName,L.EnrollmentId,L.BillingMonth,L.FeeTypeName;
END;
