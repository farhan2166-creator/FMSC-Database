-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.PreviewFeeRateCorrections
 @UserId int,@CampusId int=0,@ClassId int=0,@FeeTypeId int=0,@FromMonth date,@ThroughMonth date,@EnrollmentId int=0
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequireAnyPermission @UserId,N'Fees.Adjust',N'Fees.Configuration.Manage';
 IF @FromMonth IS NULL OR @ThroughMonth IS NULL OR @FromMonth>@ThroughMonth THROW 54925,'Choose the bill months to review.',1;
 SELECT B.BillingChargeId,B.EnrollmentId,B.AdmissionId,S.StudentName,F.FeeTypeName,B.BillingMonth,
  X.ExpectedNet,X.ExpectedBalance,FC.Amount NewAmount,
  FC.Amount-CASE WHEN A.ConcessionLabel IN(N'Free',N'Waived') OR (A.ConcessionLabel=N'Scholarship' AND COALESCE(C.NewDiscount,B.DiscountAmount)=COALESCE(C.NewGross,B.GrossAmount)) THEN FC.Amount WHEN COALESCE(C.NewDiscount,B.DiscountAmount)>FC.Amount THEN FC.Amount ELSE COALESCE(C.NewDiscount,B.DiscountAmount) END TargetNet,
  FC.Amount-CASE WHEN A.ConcessionLabel IN(N'Free',N'Waived') OR (A.ConcessionLabel=N'Scholarship' AND COALESCE(C.NewDiscount,B.DiscountAmount)=COALESCE(C.NewGross,B.GrossAmount)) THEN FC.Amount WHEN COALESCE(C.NewDiscount,B.DiscountAmount)>FC.Amount THEN FC.Amount ELSE COALESCE(C.NewDiscount,B.DiscountAmount) END-X.ExpectedNet Difference
 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
 LEFT JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=B.StudentFeeAssignmentId
 JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
 JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
 JOIN dbo.FeeConfigurations FC ON FC.CampusId=P.CampusId AND FC.ClassId=P.ClassId AND FC.FeeTypeId=B.FeeTypeId
 CROSS APPLY(SELECT SUM(NetAmount) ExpectedNet,SUM(dbo.GetFeeChargeBalance(BillingChargeId)) ExpectedBalance FROM dbo.BillingCharges
  WHERE (BillingChargeId=B.BillingChargeId OR CorrectionOfBillingChargeId=B.BillingChargeId) AND Status<>N'Cancelled') X
 OUTER APPLY(SELECT TOP(1) NewDiscount,NewGross FROM dbo.FeeBillCorrections WHERE BillingChargeId=B.BillingChargeId ORDER BY FeeBillCorrectionId DESC) C
 WHERE E.Status=N'Active' AND B.Status<>N'Cancelled' AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL
  AND B.BillingMonth BETWEEN @FromMonth AND @ThroughMonth AND (@EnrollmentId=0 OR E.EnrollmentId=@EnrollmentId)
  AND (@CampusId=0 OR P.CampusId=@CampusId) AND (@ClassId=0 OR P.ClassId=@ClassId) AND (@FeeTypeId=0 OR B.FeeTypeId=@FeeTypeId)
 ORDER BY S.StudentName,B.BillingMonth,F.FeeTypeName;
END;
