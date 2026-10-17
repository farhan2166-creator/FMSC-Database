-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.GetPostableMonths
 @UserId int,@AcademicYearId int,@CampusId int=0,@ClassId int=0,@SectionId int=0,
 @FeeTypeIds dbo.FeeTypeIdList READONLY
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 DECLARE @First date,@Last date;
 SELECT @First=DATEFROMPARTS(YEAR(StartDate),MONTH(StartDate),1),
  @Last=DATEFROMPARTS(YEAR(EndDate),MONTH(EndDate),1) FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId;
 IF @First IS NULL OR @Last IS NULL OR @Last<@First OR DATEDIFF(month,@First,@Last)>23
  THROW 54741,'Choose an academic year with a valid date range of at most 24 months.',1;
 ;WITH Months AS(SELECT @First BillingMonth UNION ALL SELECT DATEADD(month,1,BillingMonth) FROM Months WHERE BillingMonth<@Last)
 SELECT M.BillingMonth,COUNT(*) ChargesToPost,COUNT(DISTINCT C.AdmissionId) EligibleStudents,SUM(C.Amount-C.DiscountAmount) TotalNet
 FROM Months M CROSS APPLY dbo.fn_MonthlyBillingCandidates(M.BillingMonth,@AcademicYearId) C
 JOIN dbo.CampusClassSections P ON P.ClassSectionId=C.ClassSectionId
 WHERE C.BlockReason IS NULL AND C.BillingChargeId IS NULL
  AND (@CampusId=0 OR P.CampusId=@CampusId) AND (@ClassId=0 OR P.ClassId=@ClassId) AND (@SectionId=0 OR P.SectionId=@SectionId)
  AND (NOT EXISTS(SELECT 1 FROM @FeeTypeIds) OR C.FeeTypeId IN(SELECT FeeTypeId FROM @FeeTypeIds))
 GROUP BY M.BillingMonth ORDER BY M.BillingMonth OPTION(MAXRECURSION 24);
END;
