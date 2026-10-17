-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.GetBillingCycleStatus
 @UserId int,@AcademicYearId int,@CampusId int=0,@AsOfDate date=NULL
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Reports.View';
 SET @AsOfDate=COALESCE(@AsOfDate,CONVERT(date,GETDATE()));
 DECLARE @Month date=DATEFROMPARTS(YEAR(@AsOfDate),MONTH(@AsOfDate),1);
 ;WITH PerEnrollment AS(
  SELECT C.EnrollmentId,COUNT(*) Expected,COUNT(C.BillingChargeId) Posted
  FROM dbo.fn_MonthlyBillingCandidates(@Month,@AcademicYearId) C
  JOIN dbo.CampusClassSections P ON P.ClassSectionId=C.ClassSectionId
  WHERE C.BlockReason IS NULL AND (@CampusId=0 OR P.CampusId=@CampusId) GROUP BY C.EnrollmentId)
 SELECT @AcademicYearId AcademicYearId,@CampusId CampusId,@Month BillingMonth,COUNT(*) TotalActiveEnrollments,
  COALESCE(SUM(CASE WHEN Posted=Expected THEN 1 ELSE 0 END),0) BilledThisMonth,
  COALESCE(SUM(CASE WHEN Posted<Expected THEN 1 ELSE 0 END),0) RemainingToBill,
  COALESCE(SUM(Expected-Posted),0) MissingCharges FROM PerEnrollment;
END;
