-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.ApplyFeeRateCorrections
 @UserId int,@Changes nvarchar(max),@PaidReductionAction nvarchar(10),@Reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequireAnyPermission @UserId,N'Fees.Adjust',N'Fees.Configuration.Manage';
 IF ISJSON(@Changes)<>1 OR LEFT(LTRIM(@Changes),1)<>N'[' THROW 54925,'Review the bills before applying changes.',1;
 DECLARE @Rows TABLE(BillingChargeId bigint PRIMARY KEY,NewAmount decimal(12,2),ExpectedNet decimal(12,2),ExpectedBalance decimal(12,2),RequestId uniqueidentifier);
 INSERT @Rows SELECT BillingChargeId,NewAmount,ExpectedNet,ExpectedBalance,RequestId FROM OPENJSON(@Changes)
  WITH(BillingChargeId bigint,NewAmount decimal(12,2),ExpectedNet decimal(12,2),ExpectedBalance decimal(12,2),RequestId uniqueidentifier);
 IF NOT EXISTS(SELECT 1 FROM @Rows) OR EXISTS(SELECT 1 FROM @Rows WHERE NewAmount IS NULL OR ExpectedNet IS NULL OR ExpectedBalance IS NULL OR RequestId IS NULL)
  THROW 54925,'The bill review is incomplete.',1;
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @Lock int;EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 54745,'Billing is running. Try again shortly.',1;
  IF EXISTS(SELECT 1 FROM @Rows R JOIN dbo.BillingCharges B ON B.BillingChargeId=R.BillingChargeId
   JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
   LEFT JOIN dbo.FeeConfigurations F WITH(HOLDLOCK) ON F.CampusId=P.CampusId AND F.ClassId=P.ClassId AND F.FeeTypeId=B.FeeTypeId
   WHERE F.Amount IS NULL OR F.Amount<>R.NewAmount) THROW 54923,'Fee Matrix rates changed after preview. Review again.',1;
  DECLARE @Bill bigint,@New decimal(12,2),@Net decimal(12,2),@Balance decimal(12,2),@Request uniqueidentifier;
  DECLARE @Result TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
  DECLARE Bills CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId,NewAmount,ExpectedNet,ExpectedBalance,RequestId FROM @Rows ORDER BY BillingChargeId;
  OPEN Bills;FETCH NEXT FROM Bills INTO @Bill,@New,@Net,@Balance,@Request;
  WHILE @@FETCH_STATUS=0 BEGIN
   INSERT @Result EXEC dbo.AdjustBillingChargeRate @UserId,@Bill,@New,@PaidReductionAction,@Reason,@Request,@Net,@Balance;
   FETCH NEXT FROM Bills INTO @Bill,@New,@Net,@Balance,@Request;
  END;
  CLOSE Bills;DEALLOCATE Bills;COMMIT;
  SELECT * FROM @Result;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
