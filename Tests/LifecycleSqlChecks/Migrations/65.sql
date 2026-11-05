-- Allow cancelled registrations to reopen only into currently active destinations.
-- Forward-only procedure update: preserve existing episodes, request replay and financial history.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 65 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>64
 THROW 51000,'Migration 65 requires schema 64.',1;
GO
CREATE OR ALTER PROCEDURE dbo.ReopenPendingRegistration
 @UserId int,@AdmissionId int,@AcademicYearId int,@ClassSectionId int,@JoiningDate date,
 @Reason nvarchar(500),@RequestId uniqueidentifier,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.View';
 EXEC dbo.RequirePermission @UserId,N'Students.Admit';
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 EXEC dbo.RequirePermission @UserId,N'Fees.View';
 IF @RequestId IS NULL OR @RequestId='00000000-0000-0000-0000-000000000000' OR @JoiningDate IS NULL OR @JoiningDate>CONVERT(date,GETDATE())
  OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59840,'Choose a joining date that is not in the future and enter a reopening reason.',1;
 DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONCAT(@UserId,N'|',@AdmissionId,N'|',@AcademicYearId,N'|',@ClassSectionId,N'|',CONVERT(char(10),@JoiningDate,126),N'|',@Reason));
 DECLARE @Own bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 IF @Own=1 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Number varchar(40),@Reference nvarchar(50),@Withdrawal date,@Campus int,@Start date,@End date,@Snapshot varchar(64),@Enrollment int;
 SELECT @State=Status,@Number=AdmissionNumber,@Reference=QuickReference,@Withdrawal=WithdrawalDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.RegistrationReopenRequests WITH(UPDLOCK,HOLDLOCK) WHERE RequestId=@RequestId)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.RegistrationReopenRequests WHERE RequestId=@RequestId AND UserId=@UserId AND AdmissionId=@AdmissionId AND PayloadHash=@Hash)
   THROW 59841,'This reopening request belongs to different details. Reload the student.',1;
  SELECT @Enrollment=EnrollmentId FROM dbo.RegistrationReopenRequests WHERE RequestId=@RequestId;
  IF @Own=1 COMMIT;
  SELECT @Enrollment EnrollmentId,CONVERT(bit,1) Saved,CONVERT(varchar(64),NULL) Snapshot,CONVERT(decimal(18,2),NULL) Outstanding;
  RETURN;
 END;
 IF @State IS NULL OR @State<>N'Cancelled' OR @Number IS NOT NULL OR @Reference IS NULL
  THROW 59842,'Only a cancelled pending registration without an official admission number can be reopened. Reload the student; admitted former students use readmission.',1;
 IF EXISTS(SELECT 1 FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending'))
  THROW 59843,'This student already has an open placement. Reload Admission.',1;
 SELECT @Campus=CampusId FROM dbo.CampusClassSections WITH(HOLDLOCK) WHERE ClassSectionId=@ClassSectionId AND IsActive=1;
 SELECT @Start=StartDate,@End=EndDate FROM dbo.AcademicYears WITH(HOLDLOCK) WHERE AcademicYearId=@AcademicYearId;
 IF @Campus IS NULL OR @Start IS NULL OR @JoiningDate NOT BETWEEN @Start AND @End OR @JoiningDate<ISNULL(@Withdrawal,'00010101')
  THROW 59844,'Choose an active placement and a joining date within its academic year, on or after the recorded cancellation date.',1;
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@Hash,
  (SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.Enrollments WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.StudentFeeAssignments WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.BillingCharges WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePayments WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeePaymentAllocations WITH(HOLDLOCK) WHERE AdmissionId=@AdmissionId ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.CampusClassSections WHERE ClassSectionId=@ClassSectionId FOR JSON PATH,INCLUDE_NULL_VALUES))),2);
 DECLARE @Outstanding decimal(18,2)=(SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled');
 IF @Preview=1
 BEGIN
  IF @Own=1 COMMIT;
  SELECT CONVERT(int,NULL) EnrollmentId,CONVERT(bit,0) Saved,@Snapshot Snapshot,@Outstanding Outstanding;
  RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59845,'The registration, placement or financial records changed. Review reopening again.',1;
 INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,Status,UsesTransport,BillingReady,PlacementConfirmedAt,Remarks)
 VALUES(@AdmissionId,@AcademicYearId,@ClassSectionId,@JoiningDate,N'Pending',0,1,SYSDATETIME(),N'Reopened registration: '+@Reason);
 SET @Enrollment=CONVERT(int,SCOPE_IDENTITY());
 UPDATE dbo.Students SET Status=N'Pending',AdmissionDate=@JoiningDate,AdmissionCampusId=@Campus,WithdrawalDate=NULL,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
 VALUES(@Enrollment,@AdmissionId,N'ADMISSION',@JoiningDate,@ClassSectionId,N'Cancelled',N'Pending',N'Reopened registration: '+@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy)
 VALUES(@AdmissionId,N'Cancelled',N'Pending',@Reason,@JoiningDate,@UserId);
 INSERT dbo.RegistrationReopenRequests(RequestId,UserId,AdmissionId,EnrollmentId,PayloadHash) VALUES(@RequestId,@UserId,@AdmissionId,@Enrollment,@Hash);
 IF @Own=1 COMMIT;
 SELECT @Enrollment EnrollmentId,CONVERT(bit,1) Saved,@Snapshot Snapshot,@Outstanding Outstanding;
 END TRY
 BEGIN CATCH
  IF @Own=1 AND @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'65',SYSDATETIME(),SUSER_SNAME(),N'Guard registration reopening against inactive class-section placement');
GO
