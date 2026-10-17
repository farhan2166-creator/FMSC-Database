-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.PreviewMonthlyFees
 @UserId int,@BillingMonth date,@CampusId int=0,@ClassId int=0,@SectionId int=0,
 @FeeTypeIds dbo.FeeTypeIdList READONLY,@ExcludeAlreadyPosted bit=1,@AcademicYearId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @BillingMonth IS NULL THROW 54710,'Choose a billing month.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
   THROW 54743,'Select a valid academic year.',1;
 DECLARE @Month date=DATEFROMPARTS(YEAR(@BillingMonth),MONTH(@BillingMonth),1);
 SELECT C.AdmissionId,C.AcademicYearId,S.RegistrationNumber,S.StudentName,CAM.CampusName,
  CL.ClassName,SEC.SectionName,F.FeeTypeId,F.FeeTypeName,C.StudentFeeAssignmentId,C.EnrollmentId,
  @Month BillingMonth,@Month ChargeDate,@Month DueDate,C.Amount GrossAmount,C.DiscountAmount,
  C.Amount-C.DiscountAmount NetAmount,CONVERT(bit,CASE WHEN C.BillingChargeId IS NULL THEN 0 ELSE 1 END) AlreadyPosted,
  CASE WHEN C.BillingChargeId IS NOT NULL THEN N'Already posted for this month'
    WHEN C.PreviouslyCancelled=1 THEN N'Previously reversed — selecting this creates a new charge' ELSE N'Eligible' END EligibilityNote,
  F.DisplayOrder
 INTO #Eligible
 FROM dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C
 JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
 JOIN dbo.CampusClassSections P ON P.ClassSectionId=C.ClassSectionId
 JOIN dbo.Campuses CAM ON CAM.CampusId=P.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=P.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=P.SectionId
 JOIN dbo.FeeTypes F ON F.FeeTypeId=C.FeeTypeId
 WHERE C.BlockReason IS NULL AND (@CampusId=0 OR P.CampusId=@CampusId)
  AND (@ClassId=0 OR P.ClassId=@ClassId) AND (@SectionId=0 OR P.SectionId=@SectionId)
  AND (NOT EXISTS(SELECT 1 FROM @FeeTypeIds) OR C.FeeTypeId IN(SELECT FeeTypeId FROM @FeeTypeIds));
 SELECT * FROM #Eligible WHERE @ExcludeAlreadyPosted=0 OR AlreadyPosted=0
 ORDER BY CampusName,ClassName,SectionName,StudentName,DisplayOrder,FeeTypeName;
 SELECT @Month BillingMonth,COUNT(DISTINCT CASE WHEN AlreadyPosted=0 THEN AdmissionId END) EligibleStudents,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN 1 ELSE 0 END),0) ChargesToPost,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN GrossAmount ELSE 0 END),0) TotalGross,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN DiscountAmount ELSE 0 END),0) TotalDiscount,
  COALESCE(SUM(CASE WHEN AlreadyPosted=0 THEN NetAmount ELSE 0 END),0) TotalNet,
  COUNT(DISTINCT CASE WHEN AlreadyPosted=1 THEN AdmissionId END) AlreadyPostedStudents,
  COALESCE(SUM(CASE WHEN AlreadyPosted=1 THEN 1 ELSE 0 END),0) AlreadyPostedCharges FROM #Eligible;
END;
