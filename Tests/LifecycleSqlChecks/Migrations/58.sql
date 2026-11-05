-- One transport policy for admission and collection, with an independent school cutoff.
-- No student, payment, allocation, credit or previous charge is deleted or reinstated.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 58 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>57
 THROW 51000,'Migration 58 requires schema 57.',1;
ALTER TABLE dbo.SchoolProfile ADD TransportCutoffDay int NULL;
GO
UPDATE dbo.SchoolProfile SET TransportCutoffDay=FeeProrationCutoffDay;
ALTER TABLE dbo.SchoolProfile ADD CONSTRAINT CK_SchoolProfile_TransportCutoff CHECK(TransportCutoffDay IS NULL OR TransportCutoffDay BETWEEN 1 AND 28);
CREATE TABLE dbo.TransportServiceRequests(
 RequestId uniqueidentifier NOT NULL CONSTRAINT PK_TransportServiceRequests PRIMARY KEY,
 PayloadHash varchar(64) NOT NULL,UserId int NOT NULL,EnrollmentId int NOT NULL,FeeTypeId int NOT NULL,
 IsStart bit NOT NULL,FromMonth date NOT NULL,AssignmentId int NULL,
 ChargesCreated int NOT NULL,ChargesCancelled int NOT NULL,CancelledAmount decimal(12,2) NOT NULL,
 CompletedAt datetime2(7) NOT NULL CONSTRAINT DF_TransportServiceRequests_Completed DEFAULT SYSDATETIME());
GO
CREATE OR ALTER FUNCTION dbo.GetTransportBillingMonth(@Date date)
RETURNS date
AS
BEGIN
 DECLARE @Month date=DATEFROMPARTS(YEAR(@Date),MONTH(@Date),1),
  @Cutoff int=(SELECT TOP(1) TransportCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId);
 IF @Cutoff BETWEEN 1 AND 28 AND DAY(@Date)>=@Cutoff SET @Month=DATEADD(month,1,@Month);
 RETURN @Month;
END;
GO
CREATE OR ALTER FUNCTION dbo.TransportMonthlyCharges(@EnrollmentId int)
RETURNS TABLE AS RETURN
(
 SELECT B.BillingChargeId,B.FeeTypeId,B.BillingMonth,X.NetAmount,X.Balance,
  CONVERT(bit,CASE WHEN X.NetAmount-X.Balance>0 OR COALESCE(P.Paid,0)>0
    OR (B.Status IN(N'Paid',N'PartiallyPaid') AND X.NetAmount>0) THEN 1 ELSE 0 END) HasPayment
 FROM dbo.BillingCharges B JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId AND F.IsTransportLinked=1
 CROSS APPLY(SELECT COALESCE(SUM(Q.NetAmount),0) NetAmount,COALESCE(SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)),0) Balance
   FROM dbo.BillingCharges Q WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
 OUTER APPLY(SELECT SUM(A.Amount) Paid FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId AND P.Status=N'Completed'
   JOIN dbo.BillingCharges Q ON Q.BillingChargeId=A.BillingChargeId
   WHERE Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) P
 WHERE B.EnrollmentId=@EnrollmentId AND B.CorrectionOfBillingChargeId IS NULL AND B.ChargeKind=N'Monthly' AND B.Status<>N'Cancelled'
);
GO
CREATE OR ALTER PROCEDURE dbo.ManageStudentTransport
 @UserId int,@EnrollmentId int,@IsStart bit,@FeeTypeId int=NULL,@FromMonth date=NULL,@Reason nvarchar(500)=NULL,
 @Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@RequestId uniqueidentifier=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 DECLARE @Payload varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@UserId,N'|',@EnrollmentId,N'|',@IsStart,N'|',@FeeTypeId,N'|',CONVERT(char(10),@FromMonth,120),N'|',@Reason)),2);
 BEGIN TRY
  BEGIN TRAN;
  DECLARE @Lock int,@Locked int;
  EXEC @Lock=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
  IF @Lock<0 THROW 53140,'Billing is busy. Try again shortly.',1;
  IF @Preview=0 AND @RequestId IS NULL THROW 53140,'The transport request is missing. Reopen the window.',1;
  IF @Preview=0 AND EXISTS(SELECT 1 FROM dbo.TransportServiceRequests WITH(UPDLOCK,HOLDLOCK) WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.TransportServiceRequests WHERE RequestId=@RequestId AND PayloadHash=@Payload AND UserId=@UserId)
    THROW 53140,'An earlier transport request used different choices. Reopen the fees to check its result.',1;
   SELECT AssignmentId,ChargesCreated,ChargesCancelled,CancelledAmount,FromMonth FROM dbo.TransportServiceRequests WHERE RequestId=@RequestId;
   COMMIT;RETURN;
  END;
  DECLARE @Admission int,@Campus int,@Class int,@YearStart date,@YearEnd date,@BillingStart date,@Ready bit,@Status nvarchar(30),@Confirmed datetime2(7),
   @Today date=CONVERT(date,GETDATE()),@Cutoff int=(SELECT TOP(1) TransportCutoffDay FROM dbo.SchoolProfile WITH(HOLDLOCK) ORDER BY SchoolProfileId);
  SELECT @Admission=E.AdmissionId,@Campus=C.CampusId,@Class=C.ClassId,@YearStart=Y.StartDate,@YearEnd=Y.EndDate,
   @BillingStart=COALESCE(E.BillingStartDate,E.EnrollmentDate),@Ready=E.BillingReady,@Status=E.Status,@Confirmed=E.PlacementConfirmedAt
  FROM dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
  JOIN dbo.CampusClassSections C ON C.ClassSectionId=E.ClassSectionId JOIN dbo.AcademicYears Y WITH(HOLDLOCK) ON Y.AcademicYearId=E.AcademicYearId
  WHERE E.EnrollmentId=@EnrollmentId AND E.Status IN(N'Active',N'Pending') AND S.Status=N'Active' AND S.LegacySetupPending=0;
  IF @Admission IS NULL THROW 53140,'Select the student''s active or pending placement before changing transport.',1;
  IF EXISTS(SELECT 1 FROM dbo.StudentPostingHolds WHERE AdmissionId=@Admission AND ResumeDate IS NULL)
   THROW 53140,'Transport cannot change while the student is on hold.',1;
  SELECT @Locked=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
  SELECT @Locked=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission;
  SELECT A.* INTO #Plans FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
   WHERE A.EnrollmentId=@EnrollmentId AND F.IsTransportLinked=1;
  SELECT C.FeeTypeId,F.FeeTypeName,C.Amount,C.Frequency INTO #Rates FROM dbo.FeeConfigurations C WITH(HOLDLOCK)
   JOIN dbo.FeeTypes F ON F.FeeTypeId=C.FeeTypeId AND F.IsTransportLinked=1
   WHERE C.CampusId=@Campus AND C.ClassId=@Class AND C.Frequency=N'Monthly';
  SELECT * INTO #Charges FROM dbo.TransportMonthlyCharges(@EnrollmentId);
  DECLARE @Snapshot varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(CONVERT(char(10),@Today,120),N'|',@Cutoff,N'|',CONVERT(char(10),@YearStart,120),CONVERT(char(10),@YearEnd,120),
   (SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM #Plans ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM #Rates ORDER BY FeeTypeId,Amount FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM #Charges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES))),2);
  IF @Preview=1
  BEGIN
   SELECT @Snapshot Snapshot,@Today Today,@Cutoff TransportCutoffDay,dbo.GetTransportBillingMonth(@Today) CutoffMonth,
    DATEFROMPARTS(YEAR(@BillingStart),MONTH(@BillingStart),1) BillingStartMonth,
    DATEFROMPARTS(YEAR(@YearStart),MONTH(@YearStart),1) YearStartMonth,DATEFROMPARTS(YEAR(@YearEnd),MONTH(@YearEnd),1) YearEndMonth,
    @Ready BillingReady,@Status EnrollmentStatus;
   SELECT P.*,F.FeeTypeName FROM #Plans P JOIN dbo.FeeTypes F ON F.FeeTypeId=P.FeeTypeId ORDER BY P.EffectiveFrom,P.StudentFeeAssignmentId;
   SELECT * FROM #Rates ORDER BY FeeTypeName,FeeTypeId;
   SELECT * FROM #Charges ORDER BY BillingMonth,BillingChargeId;
   COMMIT;RETURN;
  END;
  IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 53141,'Transport fees, payments or cutoff changed. Refresh the transport window before saving.',1;
  IF @FromMonth IS NULL OR DAY(@FromMonth)<>1 THROW 53142,'Choose a transport month.',1;
  DECLARE @Minimum date=dbo.GetTransportBillingMonth(@Today),@Limit date,@Maximum date=DATEFROMPARTS(YEAR(@YearEnd),MONTH(@YearEnd),1);
  SET @Limit=DATEFROMPARTS(YEAR(@BillingStart),MONTH(@BillingStart),1);IF @Limit>@Minimum SET @Minimum=@Limit;
  SET @Limit=DATEFROMPARTS(YEAR(@YearStart),MONTH(@YearStart),1);IF @Limit>@Minimum SET @Minimum=@Limit;
  SELECT @Limit=DATEADD(month,1,MAX(BillingMonth)) FROM #Charges WHERE HasPayment=1 AND (@IsStart=1 OR FeeTypeId=@FeeTypeId);
  IF @Limit>@Minimum SET @Minimum=@Limit;
  IF @IsStart=1
  BEGIN
   SELECT @Limit=DATEADD(month,1,MAX(EffectiveTo)) FROM #Plans WHERE CancelledAt IS NULL;
   IF @Limit>@Minimum SET @Minimum=@Limit;
   IF EXISTS(SELECT 1 FROM #Plans WHERE CancelledAt IS NULL AND EffectiveTo IS NULL) THROW 53143,'Transport is already active or scheduled. Refresh before changing it.',1;
   IF (SELECT COUNT(*) FROM #Rates WHERE FeeTypeId=@FeeTypeId)<>1 THROW 53143,'Choose a single monthly transport rate configured for this class.',1;
   IF @Ready<>1 OR @Confirmed IS NULL OR @Status<>N'Active' OR @Today NOT BETWEEN @YearStart AND @YearEnd THROW 53143,'Confirm the student''s billing and academic placement in the current academic year before starting transport.',1;
  END
  ELSE
  BEGIN
   SET @Maximum=DATEADD(month,1,@Maximum);
   IF NOT EXISTS(SELECT 1 FROM #Plans WHERE FeeTypeId=@FeeTypeId AND CancelledAt IS NULL AND EffectiveTo IS NULL)
    THROW 53143,'No open transport plan remains for this fee. Refresh the window.',1;
  END;
  IF @FromMonth<@Minimum OR @FromMonth>@Maximum THROW 53144,'That month is not allowed by the transport cutoff, paid months or academic year. Refresh and choose an available month.',1;
  SET @Reason=COALESCE(NULLIF(LTRIM(RTRIM(@Reason)),N''),CONCAT(CASE WHEN @IsStart=1 THEN N'Transport started from ' ELSE N'Transport stopped from ' END,CONVERT(char(10),@FromMonth,120),N'.'));
  DECLARE @Assignment int,@Created int=0,@Cancelled int=0,@CancelledAmount decimal(12,2)=0;
  IF @IsStart=1
  BEGIN
   UPDATE dbo.StudentFeeAssignments SET EffectiveTo=COALESCE(EffectiveFrom,DATEADD(month,-1,@FromMonth)),UpdatedAt=SYSDATETIME()
    WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND CancelledAt IS NOT NULL AND EffectiveTo IS NULL;
   INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,Remarks,EffectiveFrom,EffectiveTo)
    SELECT @EnrollmentId,@Admission,FeeTypeId,Amount,0,N'Monthly',@Reason,@FromMonth,NULL FROM #Rates WHERE FeeTypeId=@FeeTypeId;
   SET @Assignment=CONVERT(int,SCOPE_IDENTITY());
   IF @FromMonth=DATEFROMPARTS(YEAR(@Today),MONTH(@Today),1)
   BEGIN
    IF EXISTS(SELECT 1 FROM dbo.fn_MonthlyBillingCandidates(@FromMonth,0) WHERE StudentFeeAssignmentId=@Assignment AND BlockReason IS NOT NULL)
     THROW 53145,'The current month is not eligible for transport billing. Refresh the placement before starting transport.',1;
    IF NOT EXISTS(SELECT 1 FROM #Charges WHERE FeeTypeId=@FeeTypeId AND BillingMonth=@FromMonth)
    BEGIN
     INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,BillingMonth,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind,Remarks)
      SELECT @Assignment,@EnrollmentId,@Admission,FeeTypeId,@FromMonth,@FromMonth,@FromMonth,Amount,0,N'Monthly',@Reason FROM #Rates WHERE FeeTypeId=@FeeTypeId AND Amount>0;
     SET @Created=@@ROWCOUNT;
    END;
   END;
  END
  ELSE
  BEGIN
   SELECT @Cancelled=COUNT(*),@CancelledAmount=COALESCE(SUM(Balance),0) FROM #Charges WHERE FeeTypeId=@FeeTypeId AND BillingMonth>=@FromMonth AND HasPayment=0;
   UPDATE B SET Status=N'Cancelled',Remarks=LEFT(CONCAT(B.Remarks,N' | ',@Reason),1000)
    FROM dbo.BillingCharges B JOIN #Charges C ON B.BillingChargeId=C.BillingChargeId OR B.CorrectionOfBillingChargeId=C.BillingChargeId
    WHERE C.FeeTypeId=@FeeTypeId AND C.BillingMonth>=@FromMonth AND C.HasPayment=0 AND B.Status<>N'Cancelled';
   UPDATE dbo.StudentFeeAssignments SET CancelledAt=CASE WHEN EffectiveFrom>=@FromMonth THEN SYSDATETIME() ELSE CancelledAt END,
    EffectiveTo=CASE WHEN EffectiveFrom>=@FromMonth THEN COALESCE(EffectiveTo,EffectiveFrom) ELSE DATEADD(month,-1,@FromMonth) END,UpdatedAt=SYSDATETIME()
    WHERE EnrollmentId=@EnrollmentId AND FeeTypeId=@FeeTypeId AND CancelledAt IS NULL AND (EffectiveTo IS NULL OR EffectiveTo>=@FromMonth);
  END;
  INSERT dbo.TransportServiceRequests(RequestId,PayloadHash,UserId,EnrollmentId,FeeTypeId,IsStart,FromMonth,AssignmentId,ChargesCreated,ChargesCancelled,CancelledAmount)
   VALUES(@RequestId,@Payload,@UserId,@EnrollmentId,@FeeTypeId,@IsStart,@FromMonth,@Assignment,@Created,@Cancelled,@CancelledAmount);
  COMMIT;
  SELECT @Assignment AssignmentId,@Created ChargesCreated,@Cancelled ChargesCancelled,@CancelledAmount CancelledAmount,@FromMonth FromMonth;
 END TRY
 BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SaveSchoolProfile
@UserId INT, @SchoolName NVARCHAR (200), @AddressLine NVARCHAR (500), @City NVARCHAR (100), @ContactNumber NVARCHAR (30), @Email NVARCHAR (150), @LogoImage VARBINARY (MAX), @LogoFileName NVARCHAR (255), @LogoContentType NVARCHAR (100), @FeeProrationCutoffDay INT=NULL, @TransportCutoffDay INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    IF @SchoolName IS NULL
       OR LTRIM(RTRIM(@SchoolName)) = N''
        THROW 59000, 'School name is required.', 1;
    IF @FeeProrationCutoffDay IS NOT NULL
       AND @FeeProrationCutoffDay <> 0
       AND @FeeProrationCutoffDay NOT BETWEEN 1 AND 28
        THROW 59001, 'The fee proration cutoff day must be between 1 and 28, or left blank.', 1;
    IF @FeeProrationCutoffDay = 0
        SET @FeeProrationCutoffDay = NULL;
    IF @TransportCutoffDay IS NOT NULL AND @TransportCutoffDay<>0 AND @TransportCutoffDay NOT BETWEEN 1 AND 28 THROW 59001,'The transport cutoff day must be between 1 and 28, or left blank.',1;
    IF @TransportCutoffDay=0 SET @TransportCutoffDay=NULL;
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1
               FROM   dbo.SchoolProfile
               WHERE  SchoolProfileId = 1)
        BEGIN
            UPDATE dbo.SchoolProfile
            SET    SchoolName            = LTRIM(RTRIM(@SchoolName)),
                   AddressLine           = NULLIF (LTRIM(RTRIM(@AddressLine)), N''),
                   City                  = NULLIF (LTRIM(RTRIM(@City)), N''),
                   ContactNumber         = NULLIF (LTRIM(RTRIM(@ContactNumber)), N''),
                   Email                 = NULLIF (LTRIM(RTRIM(@Email)), N''),
                   LogoImage             = @LogoImage,
                   LogoFileName          = NULLIF (LTRIM(RTRIM(@LogoFileName)), N''),
                   LogoContentType       = NULLIF (LTRIM(RTRIM(@LogoContentType)), N''),
                   FeeProrationCutoffDay = @FeeProrationCutoffDay, TransportCutoffDay = @TransportCutoffDay,
                   UpdatedAt             = SYSDATETIME(),
                   UpdatedByUserId       = @UserId
            WHERE  SchoolProfileId = 1;
        END
    ELSE
        BEGIN
            INSERT  dbo.SchoolProfile (SchoolProfileId, SchoolName, AddressLine, City, ContactNumber, Email, LogoImage, LogoFileName, LogoContentType, FeeProrationCutoffDay, TransportCutoffDay, CreatedByUserId, UpdatedByUserId)
            VALUES                   (1, LTRIM(RTRIM(@SchoolName)), NULLIF (LTRIM(RTRIM(@AddressLine)), N''), NULLIF (LTRIM(RTRIM(@City)), N''), NULLIF (LTRIM(RTRIM(@ContactNumber)), N''), NULLIF (LTRIM(RTRIM(@Email)), N''), @LogoImage, NULLIF (LTRIM(RTRIM(@LogoFileName)), N''), NULLIF (LTRIM(RTRIM(@LogoContentType)), N''), @FeeProrationCutoffDay, @TransportCutoffDay, @UserId, @UserId);
        END
    COMMIT TRANSACTION;
    SELECT 1 AS SchoolProfileId;
END
GO

CREATE OR ALTER PROCEDURE dbo.GetSchoolProfile
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequireAuthenticatedUser @UserId;

    SELECT
        SchoolProfileId,
        SchoolName,
        AddressLine,
        City,
        ContactNumber,
        Email,
        LogoImage,
        LogoFileName,
        LogoContentType,
        FeeProrationCutoffDay, TransportCutoffDay,
        CreatedAt,
        CreatedByUserId,
        UpdatedAt,
        UpdatedByUserId
    FROM dbo.SchoolProfile
    WHERE SchoolProfileId = 1;
END;
GO

-- A newly started transport period may bill again; cancelled copies remain historical.
CREATE OR ALTER FUNCTION dbo.fn_MonthlyBillingCandidates(@Month date,@AcademicYearId int)
RETURNS TABLE
AS RETURN
 SELECT E.EnrollmentId,E.AdmissionId,E.AcademicYearId,E.ClassSectionId,
        A.StudentFeeAssignmentId,A.FeeTypeId,A.Amount,A.DiscountAmount,
        Live.BillingChargeId,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BillingCharges B
          WHERE B.EnrollmentId=E.EnrollmentId AND B.FeeTypeId=A.FeeTypeId
            AND B.BillingMonth=@Month AND B.ChargeKind=N'Monthly' AND B.Status=N'Cancelled'
            AND (B.StudentFeeAssignmentId=A.StudentFeeAssignmentId OR NOT EXISTS(SELECT 1 FROM dbo.FeeTypes FT WHERE FT.FeeTypeId=A.FeeTypeId AND FT.IsTransportLinked=1))) THEN 1 ELSE 0 END) PreviouslyCancelled,
        CONVERT(nvarchar(200),CASE
          WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL) THEN N'On hold â€” use Lift Hold / Resume'
          WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NOT NULL AND @Month>=DATEFROMPARTS(YEAR(H.HoldDate),MONTH(H.HoldDate),1) AND @Month<H.BillingFromMonth) THEN N'No fee for this month under the reviewed resume decision'
          WHEN S.Status<>N'Active' THEN N'Student is not active'
          WHEN S.LegacySetupPending=1 THEN N'Complete legacy billing setup'
          WHEN E.Status<>N'Active' THEN N'Placement is not active'
          WHEN E.BillingReady=0 THEN N'Complete billing setup or confirm return'
          WHEN E.PlacementConfirmedAt IS NULL THEN N'Confirm academic placement'
          WHEN E.BillingStartDate IS NULL THEN N'Set approved billing start date'
          WHEN E.BillingStartDate<DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1) THEN N'Review billing start before placement start'
          WHEN @Month<DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1) THEN N'Billing has not started'
          WHEN @Month<DATEFROMPARTS(YEAR(Y.StartDate),MONTH(Y.StartDate),1)
            OR @Month>DATEFROMPARTS(YEAR(Y.EndDate),MONTH(Y.EndDate),1) THEN N'Outside academic year'
          WHEN E.LeavingDate IS NOT NULL AND @Month>
            CASE WHEN P.FeeProrationCutoffDay BETWEEN 1 AND 28 AND DAY(E.LeavingDate)<P.FeeProrationCutoffDay
              THEN DATEADD(month,-1,DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1))
              ELSE DATEFROMPARTS(YEAR(E.LeavingDate),MONTH(E.LeavingDate),1) END THEN N'After monthly billing ends'
          WHEN A.EffectiveFrom IS NOT NULL AND @Month<A.EffectiveFrom THEN N'Fee starts later'
          WHEN A.EffectiveTo IS NOT NULL AND @Month>A.EffectiveTo THEN N'Fee period ended'
          ELSE NULL END) BlockReason
 FROM dbo.StudentFeeAssignments A
 JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
 JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
 JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
 OUTER APPLY(SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId) P
 OUTER APPLY(SELECT TOP(1) B.BillingChargeId FROM dbo.BillingCharges B
   WHERE B.EnrollmentId=E.EnrollmentId AND B.FeeTypeId=A.FeeTypeId
     AND B.BillingMonth=@Month AND B.ChargeKind=N'Monthly' AND B.Status<>N'Cancelled'
   ORDER BY B.BillingChargeId) Live
 WHERE A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.Amount>A.DiscountAmount
   AND (@AcademicYearId=0 OR E.AcademicYearId=@AcademicYearId);

GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'58',SYSDATETIME(),SUSER_SNAME(),N'Independent transport cutoff; shared month-based start/stop, protected payments, immediate current-month posting and request recovery');
GO
