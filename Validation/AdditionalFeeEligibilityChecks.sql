SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
DECLARE @C TABLE(EnrollmentId int,AdmissionNumber varchar(40),RegistrationNumber nvarchar(100),StudentName nvarchar(500),FatherName nvarchar(500),CampusName nvarchar(500),ClassName nvarchar(500),SectionName nvarchar(500),BillingStartDate date);
INSERT @C EXEC dbo.GetAdditionalFeeCandidates @UserId=1,@AcademicYearId=28,@ChargeDate='20261017';
IF EXISTS(SELECT 1 FROM @C WHERE AdmissionNumber='6591') THROW 51000,'November-start student appears in October candidates.',1;
IF NOT EXISTS(SELECT 1 FROM @C WHERE AdmissionNumber='6505') THROW 51001,'Eligible student is missing.',1;
SELECT AdmissionNumber,StudentName,BillingStartDate FROM @C;
DECLARE @Selection dbo.ManualChargeSelectionType;
INSERT @Selection SELECT EnrollmentId FROM @C;
DECLARE @Request uniqueidentifier=NEWID(),@Count int=(SELECT COUNT(*) FROM @C);
BEGIN TRAN;
EXEC dbo.PostAdditionalFee @UserId=1,@RequestId=@Request,@FeeTypeId=4,@ChargeDate='20261017',@Amount=400,@DiscountAmount=0,@Reason=N'Rollback additional-fee eligibility validation',@Selection=@Selection;
IF (SELECT COUNT(*) FROM dbo.BillingCharges WHERE ManualRequestId=@Request)<>@Count THROW 51002,'Eligible batch count mismatch.',1;
ROLLBACK;
IF EXISTS(SELECT 1 FROM dbo.BillingCharges WHERE ManualRequestId=@Request) OR EXISTS(SELECT 1 FROM dbo.ManualFeePostingRequests WHERE RequestId=@Request) THROW 51003,'Posting rollback failed.',1;
DELETE @Selection;
INSERT @Selection SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=(SELECT AdmissionId FROM dbo.Students WHERE AdmissionNumber='6591') AND Status=N'Active';
DECLARE @Rejected bit=0;
SET @Request=NEWID();
BEGIN TRY
 EXEC dbo.PostAdditionalFee @UserId=1,@RequestId=@Request,@FeeTypeId=4,@ChargeDate='20261017',@Amount=400,@DiscountAmount=0,@Reason=N'Invalid-date rejection validation',@Selection=@Selection;
END TRY BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 IF ERROR_NUMBER()<>53025 OR ERROR_MESSAGE() NOT LIKE N'%billing starts on%' THROW;
 SET @Rejected=1;
END CATCH;
IF @Rejected=0 THROW 51004,'Posting before billing start was accepted.',1;
PRINT 'PASS: date-aware list, valid batch posting with full rollback, and clear pre-billing-start rejection.';
