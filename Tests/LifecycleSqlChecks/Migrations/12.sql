-- Reversal is current-year, whole-month and latest-first. All validation is repeated under locks.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.RequireWholeMonthReversal
 @UserId int,@BillingMonth date,@YearId int OUTPUT,@Snapshot varchar(64) OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @@TRANCOUNT=0 THROW 54800,'Month reversal requires a transaction.',1;
 IF @BillingMonth IS NULL THROW 54801,'Choose a billing month.',1;
 IF (SELECT COUNT(*) FROM dbo.AcademicYears WITH(UPDLOCK,HOLDLOCK) WHERE IsCurrent=1)<>1
  THROW 54802,'Set exactly one current academic year first.',1;
 SELECT @YearId=AcademicYearId FROM dbo.AcademicYears WHERE IsCurrent=1;
 DECLARE @Locked int,@Latest date;
 SELECT @Locked=COUNT(*) FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AcademicYearId=@YearId;
 SELECT @Locked=COUNT(*) FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId WHERE E.AcademicYearId=@YearId;
 SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK) JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId WHERE E.AcademicYearId=@YearId;
 SELECT @Latest=MAX(B.BillingMonth) FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
  JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE E.AcademicYearId=@YearId AND B.Status<>N'Cancelled'
   AND B.BillingMonth BETWEEN DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1) AND DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1);
 IF @Latest IS NULL OR @BillingMonth<>@Latest
  THROW 54803,'Only the latest posted month in the current academic year can be reversed. Reverse later months first.',1;
 IF EXISTS(SELECT 1 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
  WHERE E.AcademicYearId=@YearId AND B.BillingMonth=@Latest AND B.Status<>N'Cancelled'
   AND (B.Status<>N'Open' OR E.Status NOT IN(N'Active',N'Pending') OR EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A WHERE A.BillingChargeId=B.BillingChargeId)))
  THROW 54804,'This entire month is protected because a charge has payment history or belongs to a closed placement. No part of the month can be reversed here.',1;
 DECLARE @Raw nvarchar(max)=CONCAT(
  (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@YearId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT B.* FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId WHERE E.AcademicYearId=@YearId AND B.BillingMonth=@Latest ORDER BY B.BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WHERE AcademicYearId=@YearId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
END;
GO
CREATE OR ALTER PROCEDURE dbo.GetReversibleMonths
 @UserId int,@CampusId int=0,@ClassId int=0,@SectionId int=0,@FeeTypeIds dbo.FeeTypeIdList READONLY
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
 IF @CampusId<>0 OR @ClassId<>0 OR @SectionId<>0 OR EXISTS(SELECT 1 FROM @FeeTypeIds)
  THROW 54805,'Month reversal applies to the entire school and all fee types. Partial filters are not allowed.',1;
 ;WITH Charges AS(
  SELECT B.*,E.Status EnrollmentStatus FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
  WHERE Y.IsCurrent=1 AND B.Status<>N'Cancelled' AND B.BillingMonth BETWEEN DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1) AND DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1)
 ), Latest AS(SELECT * FROM Charges WHERE BillingMonth=(SELECT MAX(BillingMonth) FROM Charges))
 SELECT L.BillingMonth,COUNT(*) ChargesAffected,COUNT(*) ReversibleCharges,SUM(L.NetAmount) TotalPosted,CAST(0 AS decimal(12,2)) TotalCollected
 FROM Latest L WHERE NOT EXISTS(SELECT 1 FROM Latest B WHERE B.Status<>N'Open' OR B.EnrollmentStatus NOT IN(N'Active',N'Pending') OR EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A WHERE A.BillingChargeId=B.BillingChargeId))
 GROUP BY L.BillingMonth;
END;
GO
CREATE OR ALTER PROCEDURE dbo.PreviewPostingReversal
 @UserId int,@BillingMonth date,@CampusId int=0,@ClassId int=0,@SectionId int=0,@FeeTypeIds dbo.FeeTypeIdList READONLY
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @CampusId<>0 OR @ClassId<>0 OR @SectionId<>0 OR EXISTS(SELECT 1 FROM @FeeTypeIds)
  THROW 54805,'Month reversal applies to the entire school and all fee types. Partial filters are not allowed.',1;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @YearId int,@Snapshot varchar(64);
 EXEC dbo.RequireWholeMonthReversal @UserId,@BillingMonth,@YearId OUTPUT,@Snapshot OUTPUT;
 SELECT B.BillingChargeId,E.AdmissionId,S.RegistrationNumber,S.StudentName,C.CampusName,CL.ClassName,SEC.SectionName,F.FeeTypeName,
  B.ChargeKind,B.BillingMonth,B.GrossAmount,B.DiscountAmount,B.NetAmount,CAST(0 AS decimal(12,2)) CollectedAmount,B.NetAmount UncollectedAmount,
  0 HasCollectedMoney,N'Whole month eligible; no payment history' ReversalNote,@Snapshot Snapshot
 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId
 JOIN dbo.CampusClassSections CS ON CS.ClassSectionId=E.ClassSectionId JOIN dbo.Campuses C ON C.CampusId=CS.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=CS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CS.SectionId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
 WHERE E.AcademicYearId=@YearId AND B.BillingMonth=@BillingMonth AND B.Status<>N'Cancelled' ORDER BY B.BillingChargeId;
 COMMIT;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ReverseMonthlyFees
 @UserId int,@BillingMonth date,@Reason nvarchar(500),@CampusId int=0,@ClassId int=0,@SectionId int=0,@FeeTypeIds dbo.FeeTypeIdList READONLY,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 54741,'Enter a reason for reversing the whole month.',1;
 IF @CampusId<>0 OR @ClassId<>0 OR @SectionId<>0 OR EXISTS(SELECT 1 FROM @FeeTypeIds)
  THROW 54805,'Month reversal applies to the entire school and all fee types. Partial filters are not allowed.',1;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @YearId int,@Snapshot varchar(64);
 EXEC dbo.RequireWholeMonthReversal @UserId,@BillingMonth,@YearId OUTPUT,@Snapshot OUTPUT;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 54806,'The posting changed after preview. Preview the whole month again; nothing was reversed.',1;
 DECLARE @Old nvarchar(max)=(SELECT B.* FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId WHERE E.AcademicYearId=@YearId AND B.BillingMonth=@BillingMonth AND B.Status<>N'Cancelled' ORDER BY B.BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES);
 DECLARE @Reversed TABLE(BillingChargeId bigint PRIMARY KEY,AdmissionId int,Amount decimal(12,2));
 UPDATE B SET Status=N'Cancelled',Remarks=LEFT(COALESCE(B.Remarks+N' | ',N'')+N'Whole-month posting reversal: '+@Reason,500)
 OUTPUT inserted.BillingChargeId,inserted.AdmissionId,inserted.NetAmount INTO @Reversed
 FROM dbo.BillingCharges B JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId
 WHERE E.AcademicYearId=@YearId AND B.BillingMonth=@BillingMonth AND B.Status<>N'Cancelled';
 DECLARE @Count int=(SELECT COUNT(*) FROM @Reversed),@Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@BillingMonth,120),@New nvarchar(max);
 SET @New=(SELECT @YearId AcademicYearId,@BillingMonth BillingMonth,@Count ChargesReversed,@Reason Reason,JSON_QUERY((SELECT * FROM @Reversed ORDER BY BillingChargeId FOR JSON PATH)) Charges FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'FEE_POSTING_REVERSAL',@Reason);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N'BillingCharges',@Key,N'UPDATE',@Old,@New;
 COMMIT;
 SELECT @BillingMonth BillingMonth,@Count ChargesReversed,(SELECT COUNT(DISTINCT AdmissionId) FROM @Reversed) StudentsAffected,(SELECT SUM(Amount) FROM @Reversed) ReversedAmount;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'12')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'12',SYSDATETIME(),SUSER_SNAME(),N'Whole-month current-year latest-first posting reversal with payment protection and reviewed snapshots.');
GO
