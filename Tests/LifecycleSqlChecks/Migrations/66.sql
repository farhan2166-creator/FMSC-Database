-- Update navigation guidance in SQL errors without changing business rules.
-- Reissue only the existing procedure definitions; no historical schema files are rerun.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 66 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>65
 THROW 51000,'Migration 66 requires schema 65.',1;
GO
CREATE OR ALTER PROCEDURE dbo.ApplyStudentPlacementPlan
 @UserId int,@RequestId uniqueidentifier,@Plan nvarchar(max),@DestinationYearId int,
 @EffectiveDate date,@Reason nvarchar(500),@CarryConcessions bit=0,@Preview bit=1,@ExpectedReviewHash varchar(64)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
 EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
 IF @RequestId IS NULL OR ISJSON(@Plan)<>1 THROW 59801,'A valid placement plan and request are required.',1;
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59802,'Enter a reason for the placement plan or correction.',1;
 IF @EffectiveDate IS NULL OR @EffectiveDate>CONVERT(date,GETDATE()) THROW 59803,'Choose an effective date that is not in the future.',1;
 IF @@TRANCOUNT<>0 THROW 59804,'Placement plans own their transaction.',1;
 DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONCAT(@Plan,N'|',@DestinationYearId,N'|',CONVERT(varchar(10),@EffectiveDate,126),N'|',@Reason,N'|',@CarryConcessions));
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @Lock int;
 DECLARE @Resource nvarchar(255)=N'FMSC.PlacementPlan.'+CONVERT(nvarchar(36),@RequestId);
 EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=8000;
 IF @Lock<0 THROW 59805,'This plan is still processing. Check its history before retrying.',1;
 IF EXISTS(SELECT 1 FROM dbo.StudentPlacementPlans WHERE RequestId=@RequestId)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.StudentPlacementPlans WHERE RequestId=@RequestId AND UserId=@UserId AND PayloadHash=@Hash)
   THROW 59806,'This request belongs to different details. Check the original saved plan.',1;
  SELECT ResultJson FROM dbo.StudentPlacementPlans WHERE RequestId=@RequestId; COMMIT; RETURN;
 END;
 DECLARE @Rows TABLE(EnrollmentId int NOT NULL PRIMARY KEY,SourcePlacementId int NOT NULL,Stamp varchar(33) NULL,
  Action nvarchar(20) NOT NULL,DestinationPlacementId int NOT NULL,FeePolicy nvarchar(20) NULL,UndoItemId int NOT NULL);
 INSERT @Rows SELECT EnrollmentId,SourcePlacementId,Stamp,Action,DestinationPlacementId,FeePolicy,UndoItemId
 FROM OPENJSON(@Plan) WITH(EnrollmentId int,SourcePlacementId int,Stamp varchar(33),Action nvarchar(20),DestinationPlacementId int,FeePolicy nvarchar(20),UndoItemId int);
 IF NOT EXISTS(SELECT 1 FROM @Rows) THROW 59807,'Select at least one student.',1;
 IF EXISTS(SELECT 1 FROM @Rows WHERE Action NOT IN(N'PROMOTE',N'HOLD',N'MOVE',N'CORRECT',N'UNDO'))
  THROW 59808,'A plan contains an unsupported action.',1;
 IF EXISTS(SELECT 1 FROM @Rows WHERE FeePolicy IS NOT NULL AND FeePolicy NOT IN(N'NEXT_MONTH',N'SCHOOL_CUTOFF'))
  THROW 59809,'Choose an available transfer fee policy.',1;
 IF EXISTS(SELECT 1 FROM @Rows R JOIN dbo.Enrollments E ON E.EnrollmentId=R.EnrollmentId JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
  WHERE S.LegacySetupPending=1 OR S.Status<>N'Active' OR E.PlacementConfirmedAt IS NULL)
  THROW 59821,'Complete admission/legacy verification and confirm the source placement before movement.',1;
 -- Lock the reviewed source rows before calling any lifecycle writer.
 IF EXISTS(SELECT 1 FROM @Rows R LEFT JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=R.EnrollmentId
  WHERE E.EnrollmentId IS NULL OR E.Status<>N'Active' OR E.ClassSectionId<>R.SourcePlacementId
   OR ISNULL(CONVERT(varchar(33),E.UpdatedAt,126),'')<>ISNULL(R.Stamp,''))
  THROW 59810,'A reviewed placement has changed. Reload students and review again.',1;
 DECLARE @ReviewHash varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(
  (SELECT E.* FROM dbo.Enrollments E WHERE E.AdmissionId IN(SELECT E2.AdmissionId FROM dbo.Enrollments E2 JOIN @Rows R ON R.EnrollmentId=E2.EnrollmentId) ORDER BY E.EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT F.* FROM dbo.StudentFeeAssignments F WHERE F.AdmissionId IN(SELECT E.AdmissionId FROM dbo.Enrollments E JOIN @Rows R ON R.EnrollmentId=E.EnrollmentId) ORDER BY F.StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT B.* FROM dbo.BillingCharges B WHERE B.AdmissionId IN(SELECT E.AdmissionId FROM dbo.Enrollments E JOIN @Rows R ON R.EnrollmentId=E.EnrollmentId) ORDER BY B.BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT P.* FROM dbo.FeePayments P WHERE P.AdmissionId IN(SELECT E.AdmissionId FROM dbo.Enrollments E JOIN @Rows R ON R.EnrollmentId=E.EnrollmentId) ORDER BY P.FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT A.* FROM dbo.FeePaymentAllocations A WHERE A.AdmissionId IN(SELECT E.AdmissionId FROM dbo.Enrollments E JOIN @Rows R ON R.EnrollmentId=E.EnrollmentId) ORDER BY A.FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.FeeConfigurations ORDER BY FeeConfigurationId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.CampusClassSections ORDER BY ClassSectionId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.AcademicYears ORDER BY AcademicYearId FOR JSON PATH,INCLUDE_NULL_VALUES),
  (SELECT * FROM dbo.SchoolProfile ORDER BY SchoolProfileId FOR JSON PATH,INCLUDE_NULL_VALUES))),2);
 IF @Preview=0 AND (@ExpectedReviewHash IS NULL OR @ExpectedReviewHash<>@ReviewHash)
  THROW 59820,'Placement or financial setup changed after review. Review the complete plan again; nothing was applied.',1;
 INSERT dbo.StudentPlacementPlans(RequestId,UserId,PayloadHash,Reason,ResultJson) VALUES(@RequestId,@UserId,@Hash,@Reason,N'{}');
 DECLARE @Enrollment int,@Source int,@Stamp varchar(33),@Action nvarchar(20),@Target int,@Policy nvarchar(20),@Undo int;
 DECLARE @Admission int,@Year int,@Campus int,@Class int,@Section int,@TargetCampus int,@TargetClass int,@NewEnrollment int;
 DECLARE @Promoted TABLE(PromotionBatchId int,ProcessedCount int,PromotedCount int,HeldBackCount int);
 DECLARE @Moved TABLE(EnrollmentId int,AdmissionId int,NewClassSectionId int);
 DECLARE @Decisions dbo.BulkPromotionDecisionType;
 DECLARE @Effects TABLE(AdmissionId int,Action nvarchar(20),FeesBefore nvarchar(max),FeesAfter nvarchar(max),BillDifference decimal(18,2));
 DECLARE @FeesBefore nvarchar(max),@BeforeBills decimal(18,2);
 DECLARE plans CURSOR LOCAL FAST_FORWARD FOR SELECT EnrollmentId,SourcePlacementId,Stamp,Action,DestinationPlacementId,FeePolicy,UndoItemId FROM @Rows ORDER BY EnrollmentId;
 OPEN plans; FETCH NEXT FROM plans INTO @Enrollment,@Source,@Stamp,@Action,@Target,@Policy,@Undo;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SELECT @Admission=E.AdmissionId,@Year=E.AcademicYearId,@Campus=P.CampusId,@Class=P.ClassId,@Section=P.SectionId
  FROM dbo.Enrollments E JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId WHERE E.EnrollmentId=@Enrollment;
  SET @NewEnrollment=@Enrollment;
  SET @FeesBefore=(SELECT T.FeeTypeName,F.Amount,F.DiscountAmount,F.Frequency,F.EffectiveFrom,F.EffectiveTo FROM dbo.StudentFeeAssignments F JOIN dbo.FeeTypes T ON T.FeeTypeId=F.FeeTypeId WHERE F.EnrollmentId=@Enrollment AND F.CancelledAt IS NULL ORDER BY F.StudentFeeAssignmentId FOR JSON PATH);
  SELECT @BeforeBills=COALESCE(SUM(NetAmount),0) FROM dbo.BillingCharges WHERE AdmissionId=@Admission AND Status<>N'Cancelled';
  IF @Action=N'UNDO'
  BEGIN
   DECLARE @OldEnrollment int,@OldPlacement int,@OriginalAction nvarchar(20),@SavedStamp varchar(33),@SavedFingerprint binary(32),@NowFingerprint binary(32);
   SELECT @OldEnrollment=NULL,@OldPlacement=NULL,@OriginalAction=NULL,@SavedStamp=NULL;
   SELECT @OldEnrollment=SourceEnrollmentId,@OldPlacement=SourcePlacementId,@OriginalAction=Action,@SavedStamp=DestinationStamp,@SavedFingerprint=DestinationFingerprint
   FROM dbo.StudentPlacementPlanItems WITH(UPDLOCK,HOLDLOCK)
   WHERE ItemId=@Undo AND AdmissionId=@Admission AND DestinationEnrollmentId=@Enrollment AND UndoneBy IS NULL AND Action IN(N'PROMOTE',N'HOLD');
   IF @OldEnrollment IS NULL THROW 59811,'Only an unchanged promotion made through this workspace can be undone here. Use a reviewed placement correction for other moves.',1;
   EXEC dbo.RequirePermission @UserId,N'Students.Administer';
   SET @NowFingerprint=HASHBYTES('SHA2_256',CONCAT(
    (SELECT * FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment FOR JSON PATH,INCLUDE_NULL_VALUES),
    (SELECT * FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES)));
   IF @NowFingerprint<>@SavedFingerprint OR ISNULL(@Stamp,'')<>ISNULL(@SavedStamp,'') OR EXISTS(SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment AND BillingReady=1)
    OR EXISTS(SELECT 1 FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment)
    OR EXISTS(SELECT 1 FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment)
    OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.SportsCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@Admission)
    OR EXISTS(SELECT 1 FROM dbo.EnrollmentHistory WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@Enrollment)
    THROW 59812,'Return admission, billing, certificates or later movement exists. Review placement/date corrections and financial history in Student Fee Account > Payment History; this promotion cannot be undone automatically.',1;
   IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@OldEnrollment AND Status=N'Promoted' AND ClassSectionId=@OldPlacement)
    THROW 59813,'The original placement changed. Review its history before correcting it.',1;
   -- Retain the mistaken destination as a closed zero-duration placement.
   IF @EffectiveDate<(SELECT EnrollmentDate FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment)
    THROW 59819,'The correction date cannot precede the promotion.',1;
   UPDATE dbo.Enrollments SET Status=N'Left',LeavingDate=EnrollmentDate,UpdatedAt=SYSDATETIME(),Remarks=LEFT(CONCAT(Remarks,N' | Promotion corrected: ',@Reason),500) WHERE EnrollmentId=@Enrollment;
   UPDATE dbo.Enrollments SET Status=N'Active',LeavingDate=NULL,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@OldEnrollment;
   INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldClassSectionId,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
   VALUES(@OldEnrollment,@Admission,N'CORRECTION',@EffectiveDate,@Source,@OldPlacement,N'Promoted',N'Active',@Reason,@UserId);
   UPDATE dbo.StudentPlacementPlanItems SET UndoneBy=@RequestId WHERE ItemId=@Undo;
   SET @NewEnrollment=@OldEnrollment; SET @Target=@OldPlacement;
  END
  ELSE
  BEGIN
   SELECT @TargetCampus=NULL,@TargetClass=NULL;
   SELECT @TargetCampus=CampusId,@TargetClass=ClassId FROM dbo.CampusClassSections WITH(HOLDLOCK) WHERE ClassSectionId=@Target AND IsActive=1;
   IF @TargetCampus IS NULL THROW 59814,'Choose an active destination for every selected student.',1;
   IF @Action IN(N'PROMOTE',N'HOLD')
   BEGIN
    IF @Action=N'HOLD' AND @Target<>@Source THROW 59815,'Repeat Class keeps the current campus, class and section. Use a transfer after promotion for further changes.',1;
    IF @Action=N'PROMOTE' AND @TargetClass<>ISNULL((SELECT TOP(1) N.ClassId FROM dbo.Classes N JOIN dbo.Classes C ON C.ClassId=@Class
      WHERE N.AcademicProgramId=C.AcademicProgramId AND (N.DisplayOrder>C.DisplayOrder OR (N.DisplayOrder=C.DisplayOrder AND N.ClassId>C.ClassId)) ORDER BY N.DisplayOrder,N.ClassId),-1)
     THROW 59816,'Promotion must advance to the immediate next class in the same program. Final-class students need a departure/completion review.',1;
    DELETE @Decisions; INSERT @Decisions VALUES(@Admission,@Action);
    DELETE @Promoted;
    EXEC dbo.BulkPromoteStudents @UserId=@UserId,@SourceAcademicYearId=@Year,@DestinationAcademicYearId=@DestinationYearId,
     @SourceCampusId=@Campus,@SourceClassId=@Class,@SourceSectionId=@Section,@DestinationClassSectionId=@Target,@EffectiveDate=@EffectiveDate,
     @Remarks=@Reason,@Decisions=@Decisions,@CarryConcessions=@CarryConcessions,@AllowCampusChange=1;
    SELECT @NewEnrollment=EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@Admission AND AcademicYearId=@DestinationYearId AND Status=N'Active';
   END
   ELSE
   BEGIN
    IF @DestinationYearId<>@Year THROW 59817,'Transfers and placement corrections stay in the recorded academic year.',1;
    DELETE @Moved;
    IF @TargetCampus<>@Campus
     EXEC dbo.TransferEnrollmentCampus @UserId=@UserId,@EnrollmentId=@Enrollment,@NewClassSectionId=@Target,@TransferDate=@EffectiveDate,@Remarks=@Reason,@FeePolicy=@Policy,@FeeSyncReason=@Reason;
    ELSE IF @TargetClass<>@Class
     EXEC dbo.TransferEnrollmentClass @UserId=@UserId,@EnrollmentId=@Enrollment,@NewClassSectionId=@Target,@TransferDate=@EffectiveDate,@Remarks=@Reason,@FeePolicy=@Policy,@FeeSyncReason=@Reason;
    ELSE
    BEGIN
     IF @Policy IS NOT NULL THROW 59818,'Section-only changes preserve the current fees.',1;
     EXEC dbo.TransferEnrollmentClassSection @UserId=@UserId,@EnrollmentId=@Enrollment,@NewClassSectionId=@Target,@TransferDate=@EffectiveDate,@Remarks=@Reason;
    END;
   END;
  END;
  INSERT dbo.StudentPlacementPlanItems(RequestId,AdmissionId,SourceEnrollmentId,DestinationEnrollmentId,SourcePlacementId,DestinationPlacementId,Action,FeePolicy,DestinationStamp)
  SELECT @RequestId,@Admission,@Enrollment,@NewEnrollment,@Source,@Target,@Action,@Policy,CONVERT(varchar(33),UpdatedAt,126) FROM dbo.Enrollments WHERE EnrollmentId=@NewEnrollment;
  UPDATE dbo.StudentPlacementPlanItems SET DestinationFingerprint=HASHBYTES('SHA2_256',CONCAT(
   (SELECT * FROM dbo.Enrollments WHERE EnrollmentId=@NewEnrollment FOR JSON PATH,INCLUDE_NULL_VALUES),
   (SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@NewEnrollment ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES)))
  WHERE ItemId=SCOPE_IDENTITY();
  INSERT @Effects SELECT @Admission,@Action,@FeesBefore,
   (SELECT T.FeeTypeName,F.Amount,F.DiscountAmount,F.Frequency,F.EffectiveFrom,F.EffectiveTo FROM dbo.StudentFeeAssignments F JOIN dbo.FeeTypes T ON T.FeeTypeId=F.FeeTypeId WHERE F.EnrollmentId=@NewEnrollment AND F.CancelledAt IS NULL ORDER BY F.StudentFeeAssignmentId FOR JSON PATH),
   (SELECT COALESCE(SUM(NetAmount),0)-@BeforeBills FROM dbo.BillingCharges WHERE AdmissionId=@Admission AND Status<>N'Cancelled');
  FETCH NEXT FROM plans INTO @Enrollment,@Source,@Stamp,@Action,@Target,@Policy,@Undo;
 END;
 CLOSE plans; DEALLOCATE plans;
 DECLARE @Result nvarchar(max)=(SELECT @RequestId RequestId,(SELECT COUNT(*) FROM @Rows) ProcessedCount,@Preview Preview,@ReviewHash ReviewHash,
  JSON_QUERY((SELECT * FROM @Effects FOR JSON PATH)) Effects FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 UPDATE dbo.StudentPlacementPlans SET ResultJson=@Result WHERE RequestId=@RequestId;
 IF @Preview=1 ROLLBACK; ELSE COMMIT;
 SELECT @Result ResultJson;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.BulkPromoteStudents
@UserId INT, @SourceAcademicYearId INT, @DestinationAcademicYearId INT, @SourceCampusId INT, @SourceClassId INT, @SourceSectionId INT, @DestinationClassSectionId INT, @EffectiveDate DATE, @Remarks NVARCHAR (500), @Decisions dbo.BulkPromotionDecisionType READONLY, @CarryConcessions bit=0,@AllowCampusChange bit=0
AS

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @CarryConcessions=1 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
    IF @SourceAcademicYearId <= 0
        THROW 52501, 'A valid source academic year is required.', 1;
    IF @DestinationAcademicYearId <= 0
        THROW 52502, 'A valid destination academic year is required.', 1;
    IF @DestinationAcademicYearId = @SourceAcademicYearId
        THROW 52503, 'Bulk Promotion must move students into a different academic year. Use Transfer Class for same-year movement.', 1;
    IF @SourceCampusId <= 0
       OR @SourceClassId <= 0
        THROW 52504, 'A valid source campus and class are required.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
       AND @DestinationClassSectionId <= 0
        THROW 52506, 'A valid destination class/section is required for promoted students.', 1;
    IF @EffectiveDate IS NULL
        THROW 52508, 'The effective date is required.', 1;
    IF @EffectiveDate > CONVERT (DATE, SYSDATETIME())
        THROW 52509, 'The effective date cannot be in the future.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   @Decisions)
        THROW 52510, 'Select at least one student to process.', 1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision NOT IN (N'PROMOTE', N'HOLD'))
        THROW 52511, 'Bulk promotion contains an invalid decision.', 1;
    DECLARE @SourceStartDate AS DATE, @DestinationStartDate AS DATE, @DestinationEndDate AS DATE, @DestinationCampusId AS INT, @DestinationClassId AS INT;
    SELECT @SourceStartDate = StartDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @SourceAcademicYearId;
    SELECT @DestinationStartDate = StartDate, @DestinationEndDate=EndDate
    FROM   dbo.AcademicYears
    WHERE  AcademicYearId = @DestinationAcademicYearId;
    IF @SourceStartDate IS NULL
        THROW 52512, 'The source academic year is invalid.', 1;
    IF @DestinationStartDate IS NULL
        THROW 52513, 'The destination academic year is invalid.', 1;
    IF @DestinationStartDate <= @SourceStartDate
        THROW 52514, 'The destination academic year must be later than the source academic year.', 1;
    IF @EffectiveDate<@DestinationStartDate OR (@DestinationEndDate IS NOT NULL AND @EffectiveDate>@DestinationEndDate)
        THROW 52520,'The effective date must fall within the destination academic year.',1;
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            SELECT @DestinationCampusId = CCS.CampusId,
                   @DestinationClassId = CCS.ClassId
            FROM   dbo.CampusClassSections AS CCS
            WHERE  CCS.ClassSectionId = @DestinationClassSectionId;
            IF @DestinationCampusId IS NULL
                THROW 52516, 'The destination class/section is invalid.', 1;
            IF @AllowCampusChange=0 AND @DestinationCampusId <> @SourceCampusId
                THROW 52517, 'Bulk Promotion cannot move students between campuses.', 1;
        END
    ELSE
        BEGIN
            SET @DestinationClassSectionId = NULL;
        END
    IF EXISTS (SELECT 1
               FROM   @Decisions
               WHERE  Decision = N'PROMOTE')
        BEGIN
            DECLARE @MissingMandatoryFeesDest AS NVARCHAR (1000);
            SELECT @MissingMandatoryFeesDest = STRING_AGG(FT.FeeTypeName, N', ')
            FROM   dbo.FeeTypes AS FT
            WHERE  FT.IsMandatory = 1
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.FeeConfigurations AS FC
                                   WHERE  FC.CampusId = @DestinationCampusId
                                          AND FC.ClassId = @DestinationClassId
                                          AND FC.FeeTypeId = FT.FeeTypeId);
            IF @MissingMandatoryFeesDest IS NOT NULL
                BEGIN
                    DECLARE @MandatoryFeeErrorMsgDest AS NVARCHAR (1100) = N'Required fee configuration is missing for the destination class: ' + @MissingMandatoryFeesDest + N'.';
                    THROW 52519, @MandatoryFeeErrorMsgDest, 1;
                END
        END
    DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    BEGIN TRY
    BEGIN TRANSACTION;
    SELECT E.EnrollmentId,
           E.AdmissionId,
           E.ClassSectionId,
           E.UsesTransport,
           D.Decision,
           C.ClassId AS CurrentClassId,
           CCS.CampusId AS CurrentCampusId
    INTO   #Selected
    FROM   @Decisions AS D
           INNER JOIN
           dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           ON E.AdmissionId = D.AdmissionId
              AND E.Status = N'Active'
              AND E.AcademicYearId = @SourceAcademicYearId
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
           INNER JOIN
           dbo.Classes AS C
           ON C.ClassId = CCS.ClassId
    WHERE  CCS.CampusId = @SourceCampusId
           AND CCS.ClassId = @SourceClassId
           AND (@SourceSectionId = 0
                OR CCS.SectionId = @SourceSectionId);
    IF EXISTS (SELECT 1
               FROM   @Decisions AS D
                      LEFT OUTER JOIN
                      #Selected AS S
                      ON S.AdmissionId = D.AdmissionId
               WHERE  S.AdmissionId IS NULL)
        THROW 52521, 'One or more selected students are no longer active in the selected source class. Reload the candidates and try again.', 1;
    ALTER TABLE #Selected
        ADD NewClassSectionId INT NULL;
    UPDATE #Selected
    SET    NewClassSectionId = CASE WHEN Decision = N'PROMOTE' THEN @DestinationClassSectionId ELSE ClassSectionId END;
    IF EXISTS (SELECT 1
               FROM   #Selected AS S
                      INNER JOIN
                      dbo.CampusClassSections AS CCS
                      ON CCS.ClassSectionId = S.NewClassSectionId
               WHERE  @AllowCampusChange=0 AND CCS.CampusId <> @SourceCampusId)
        THROW 52522, 'One or more destination class/sections are invalid for the source campus.', 1;
    DECLARE @BatchLockResult AS INT;
    DECLARE @BatchLockResource AS NVARCHAR (255) = N'FMSC.BulkPromotion.' + CONVERT (NVARCHAR (20), @SourceAcademicYearId) + N'.' + CONVERT (NVARCHAR (20), @SourceClassId) + N'.' + CONVERT (NVARCHAR (20), @SourceCampusId);
    EXECUTE @BatchLockResult = sys.sp_getapplock @Resource = @BatchLockResource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
    IF @BatchLockResult < 0
        THROW 52523, 'Another bulk promotion is currently processing this class. Please try again after it completes.', 1;

    INSERT  dbo.PromotionBatches (SourceAcademicYearId, DestinationAcademicYearId, SourceCampusId, SourceClassId, SourceSectionId, DestinationClassSectionId, EffectiveDate, Remarks, CreatedByUserId)
    VALUES                      (@SourceAcademicYearId, @DestinationAcademicYearId, @SourceCampusId, @SourceClassId, NULLIF (@SourceSectionId, 0), @DestinationClassSectionId, @EffectiveDate, @Remarks, @UserId);
    DECLARE @PromotionBatchId AS INT = SCOPE_IDENTITY();
    IF EXISTS (SELECT 1
               FROM   (SELECT DISTINCT CCS.CampusId,
                                       CCS.ClassId
                       FROM   #Selected AS S
                              INNER JOIN
                              dbo.CampusClassSections AS CCS
                              ON CCS.ClassSectionId = S.NewClassSectionId) AS D
               WHERE  EXISTS (SELECT 1
                              FROM   dbo.FeeTypes AS FT
                              WHERE  FT.IsMandatory = 1
                                     AND NOT EXISTS (SELECT 1
                                                     FROM   dbo.FeeConfigurations AS FC
                                                     WHERE  FC.CampusId = D.CampusId
                                                            AND FC.ClassId = D.ClassId
                                                            AND FC.FeeTypeId = FT.FeeTypeId)))
        THROW 52524, 'Required fee configuration is missing for one or more destination classes.', 1;
    IF EXISTS(SELECT 1 FROM #Selected X JOIN dbo.Students S ON S.AdmissionId=X.AdmissionId WHERE S.LegacySetupPending=1) THROW 59751,'Finish legacy verification and Go Live before promoting these students.',1;
    IF EXISTS(SELECT 1 FROM #Selected S JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId WHERE E.PlacementConfirmedAt IS NULL)
        THROW 59646,'Confirm the source placement in Admission before promotion.',1;
    IF EXISTS(SELECT 1 FROM dbo.BillingCharges B WITH(UPDLOCK,HOLDLOCK)
        JOIN #Selected S ON S.EnrollmentId=B.EnrollmentId
        WHERE B.Status<>N'Cancelled' AND COALESCE(B.BillingMonth,B.ChargeDate)>DATEFROMPARTS(YEAR(DATEADD(day,-1,@EffectiveDate)),MONTH(DATEADD(day,-1,@EffectiveDate)),1))
        THROW 59632,'Charges exist after the promotion effective date. Review unpaid charges in Fee Collection or paid charges in Student Fee Account > Payment History, then retry. No students were promoted.',1;
    UPDATE E
    SET    Status      = N'Promoted',
           LeavingDate = @EffectiveDate,
           UpdatedAt   = SYSDATETIME(),
           Remarks     = COALESCE (NULLIF (@Remarks, N''), E.Remarks)
    FROM   dbo.Enrollments AS E
           INNER JOIN
           #Selected AS S
           ON S.EnrollmentId = E.EnrollmentId
    WHERE  E.Status = N'Active';
    IF @@ROWCOUNT <> (SELECT COUNT(*)
                      FROM   #Selected)
        THROW 52525, 'The source enrollments could not all be closed as historical enrollments.', 1;
    INSERT dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, BillingStartDate, Status, UsesTransport, BillingReady, Remarks, PlacementConfirmedAt)
    SELECT S.AdmissionId,
           @DestinationAcademicYearId,
           S.NewClassSectionId,
           @EffectiveDate,
           dbo.GetMonthlyBillingStartDate(@EffectiveDate),
           N'Active',
           0,
           0,
           CASE WHEN S.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END, SYSDATETIME()
    FROM   #Selected AS S;
    DECLARE @NewEnrollments TABLE (
        AdmissionId       INT           NOT NULL,
        OldEnrollmentId   INT           NOT NULL,
        NewEnrollmentId   INT           NOT NULL,
        Decision          NVARCHAR (20) NOT NULL,
        NewClassSectionId INT           NOT NULL,
        UsesTransport     BIT           NOT NULL);
    INSERT INTO @NewEnrollments (AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, NewClassSectionId, UsesTransport)
    SELECT S.AdmissionId,
           S.EnrollmentId,
           E.EnrollmentId,
           S.Decision,
           S.NewClassSectionId,
           0
    FROM   #Selected AS S
           INNER JOIN
           dbo.Enrollments AS E
           ON E.AdmissionId = S.AdmissionId
              AND E.AcademicYearId = @DestinationAcademicYearId
              AND E.Status = N'Active';
    IF (SELECT COUNT(*)
        FROM   @NewEnrollments) <> (SELECT COUNT(*)
                                    FROM   #Selected)
        THROW 52526, 'The new academic-year enrollments could not all be created.', 1;
    INSERT dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks)
    SELECT N.NewEnrollmentId,
           N.AdmissionId,
           FC.FeeTypeId,
           FC.Amount,
           0,
           FC.Frequency,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Created by bulk promotion' ELSE N'Created by bulk promotion - held back/repeat class' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = N.NewClassSectionId
           INNER JOIN
           dbo.FeeConfigurations AS FC
           ON FC.CampusId = CCS.CampusId
              AND FC.ClassId = CCS.ClassId
           INNER JOIN
           dbo.FeeTypes AS FT
           ON FT.FeeTypeId = FC.FeeTypeId
    WHERE  FT.IsMandatory = 1
           AND FT.IsTransportLinked = 0;
    -- Carry only the monthly agreement in force at the end of the source
    -- placement, never a temporary expired concession or an earlier bill waiver.
    IF @CarryConcessions=1
    UPDATE Target SET
        DiscountAmount=CASE WHEN Source.ConcessionLabel IN(N'Free',N'Waived')
            OR (Source.ConcessionLabel=N'Scholarship' AND Source.DiscountAmount=Source.Amount)
            OR Source.DiscountAmount>Target.Amount THEN Target.Amount ELSE Source.DiscountAmount END,
        ConcessionLabel=Source.ConcessionLabel,
        Remarks=LEFT(CONCAT(Target.Remarks,N' | Monthly concession carried from prior placement'),500)
    FROM dbo.StudentFeeAssignments Target
    JOIN @NewEnrollments N ON N.NewEnrollmentId=Target.EnrollmentId
    JOIN dbo.AcademicYears Y ON Y.AcademicYearId=@SourceAcademicYearId
    CROSS APPLY(SELECT CASE WHEN Y.EndDate<DATEADD(day,-1,@EffectiveDate) THEN Y.EndDate ELSE DATEADD(day,-1,@EffectiveDate) END LastDate) Cutoff
    CROSS APPLY(SELECT DATEFROMPARTS(YEAR(Cutoff.LastDate),MONTH(Cutoff.LastDate),1) LastMonth) M
    CROSS APPLY(SELECT TOP(1) A.Amount,A.DiscountAmount,A.ConcessionLabel
        FROM dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK)
        WHERE A.EnrollmentId=N.OldEnrollmentId AND A.FeeTypeId=Target.FeeTypeId AND A.Frequency=N'Monthly' AND A.CancelledAt IS NULL
            AND ISNULL(A.EffectiveFrom,'19000101')<=M.LastMonth AND ISNULL(A.EffectiveTo,'99991231')>=M.LastMonth
        ORDER BY A.EffectiveFrom DESC,A.StudentFeeAssignmentId DESC) Source
    WHERE Target.Frequency=N'Monthly';
    INSERT dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    SELECT N.OldEnrollmentId,
           N.AdmissionId,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'BULK_PROMOTE' ELSE N'BULK_HOLD' END,
           @EffectiveDate,
           S.ClassSectionId,
           N.NewClassSectionId,
           N'Active',
           N'Promoted',
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Bulk promoted to EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) ELSE N'Bulk held back/repeated class in new academic year; EnrollmentId ' + CONVERT (NVARCHAR (20), N.NewEnrollmentId) END,
           @UserId
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    INSERT dbo.PromotionBatchStudents (PromotionBatchId, AdmissionId, OldEnrollmentId, NewEnrollmentId, Decision, Remarks)
    SELECT @PromotionBatchId,
           N.AdmissionId,
           N.OldEnrollmentId,
           N.NewEnrollmentId,
           N.Decision,
           CASE WHEN N.Decision = N'PROMOTE' THEN N'Promoted to destination class.' ELSE N'Held back/repeated the current class in the destination academic year.' END
    FROM   @NewEnrollments AS N
           INNER JOIN
           #Selected AS S
           ON S.AdmissionId = N.AdmissionId;
    DECLARE @ProcessedCount AS INT = (SELECT COUNT(*)
                                      FROM   @NewEnrollments), @PromotedCount AS INT = (SELECT COUNT(*)
                                                                                        FROM   @NewEnrollments
                                                                                        WHERE  Decision = N'PROMOTE'), @HeldBackCount AS INT = (SELECT COUNT(*)
                                                                                                                                                FROM   @NewEnrollments
                                                                                                                                                WHERE  Decision = N'HOLD');
    UPDATE dbo.PromotionBatches
    SET    ProcessedCount = @ProcessedCount,
           PromotedCount  = @PromotedCount,
           HeldBackCount  = @HeldBackCount
    WHERE  PromotionBatchId = @PromotionBatchId;

COMMIT TRANSACTION;
    SELECT @PromotionBatchId AS PromotionBatchId,
           @ProcessedCount AS ProcessedCount,
           @PromotedCount AS PromotedCount,
           @HeldBackCount AS HeldBackCount;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CloseStudentAdmission @UserId int,@AdmissionId int,@LeavingDate date,@Reason nvarchar(500),
 @Cancel bit=0,@Preview bit=1,@ExpectedSnapshot varchar(64)=NULL,@ReverseFutureUnpaid bit=0,@PaidReductionAction nvarchar(10)=N'Credit',@MonthlyFeePolicy nvarchar(20)=N'SchoolCutoff',@ReverseFromMonth date=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 59710,'Enter the reason for leaving or cancellation.',1;
 IF @LeavingDate IS NULL OR @LeavingDate>CONVERT(date,GETDATE()) THROW 59711,'Choose a leaving date that is not in the future.',1;
 IF @MonthlyFeePolicy IS NULL OR @MonthlyFeePolicy NOT IN(N'SchoolCutoff',N'KeepIssued',N'FromMonth') THROW 59721,'Choose how existing monthly fees should be handled.',1;
 DECLARE @OwnTransaction bit=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 BEGIN TRY
 BEGIN TRAN;
 DECLARE @State nvarchar(30),@Enrollment int,@Start date,@Snapshot varchar(64),@Raw nvarchar(max),@Lock int;
 SELECT @State=Status FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Enrollment=EnrollmentId,@Start=EnrollmentDate FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Pending');
 IF @Enrollment IS NULL OR @State NOT IN(N'Active',N'Pending') THROW 59712,'This record has no open placement to close.',1;
 IF @Cancel=1 AND (@State<>N'Pending' OR EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND AdmissionNumber IS NOT NULL))
  THROW 59720,'An officially admitted student cannot be cancelled. Use Mark as Left for departure, or correct details and fees in their respective workspaces.',1;
 IF @State=N'Pending' AND @Cancel=0 THROW 59713,'Use Cancel Registration for a pending registration.',1;
 DECLARE @PlacementYearEnd date=(SELECT Y.EndDate FROM dbo.Enrollments E JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@Enrollment);
 IF @PlacementYearEnd IS NULL OR @LeavingDate>@PlacementYearEnd
  THROW 59643,'Leaving date must be within the recorded academic year. Review historical departure dates or prepare the current placement first.',1;
 IF @LeavingDate<@Start THROW 59714,'Leaving date cannot be before the placement start.',1;
 SELECT @Lock=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 SELECT @Lock=COUNT(*) FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
 IF EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE EnrollmentId=@Enrollment) THROW 59715,'This placement already has a leaving certificate. Review its recorded departure in Admission.',1;
 IF @Cancel=1 AND EXISTS(SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
    WHERE A.AdmissionId=@AdmissionId AND P.Status=N'Completed' GROUP BY A.BillingChargeId HAVING SUM(A.Amount)>0)
  THROW 59716,'Payments exist. Review refunds or reversals in Student Fee Account > Payment History before cancellation; history must be retained. For an admitted student who attended, use Mark as Left.',1;
 IF @Cancel=1 AND (EXISTS(SELECT 1 FROM dbo.SportsCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.RegularStudentCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.NoObjectionCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.DateOfBirthCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.ProvisionalCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.CharacterCertificates WHERE AdmissionId=@AdmissionId) OR EXISTS(SELECT 1 FROM dbo.LeaveCertificates WHERE AdmissionId=@AdmissionId) OR
   EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId AND EnrollmentId<>@Enrollment AND Status<>N'Cancelled'))
  THROW 59717,'This admission has academic or certificate history. Use Mark as Left rather than cancellation.',1;
 DECLARE @MonthlyBoundary date=dbo.GetMonthlyBillingStartDate(@LeavingDate);
 SET @MonthlyBoundary=DATEFROMPARTS(YEAR(@MonthlyBoundary),MONTH(@MonthlyBoundary),1);
 IF @MonthlyFeePolicy=N'KeepIssued' SET @MonthlyBoundary='99991201';
 IF @MonthlyFeePolicy=N'FromMonth'
 BEGIN
  IF @ReverseFromMonth IS NULL OR DAY(@ReverseFromMonth)<>1 OR @ReverseFromMonth<DATEFROMPARTS(YEAR(@Start),MONTH(@Start),1)
   OR @ReverseFromMonth>DATEADD(month,1,DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1))
   THROW 59721,'Choose a first month to waive, from the placement start through the month after leaving.',1;
  SET @MonthlyBoundary=@ReverseFromMonth;
 END;
 DECLARE @FutureCount int,@FutureAmount decimal(12,2),@FuturePaid int,@Blocked int;
 SELECT B.BillingChargeId,B.BillingMonth,X.NetAmount,X.Balance INTO #ClosureBills
 FROM dbo.BillingCharges B CROSS APPLY(SELECT SUM(Q.NetAmount) NetAmount,SUM(dbo.GetFeeChargeBalance(Q.BillingChargeId)) Balance
  FROM dbo.BillingCharges Q WHERE (Q.BillingChargeId=B.BillingChargeId OR Q.CorrectionOfBillingChargeId=B.BillingChargeId) AND Q.Status<>N'Cancelled') X
 WHERE B.EnrollmentId=@Enrollment AND B.Status<>N'Cancelled' AND B.ChargeKind=N'Monthly'
  AND B.CorrectionOfBillingChargeId IS NULL AND B.BillingMonth>=@MonthlyBoundary AND X.NetAmount>0;
 SELECT @FutureCount=COUNT(*),@FutureAmount=COALESCE(SUM(Balance),0),
  @FuturePaid=COALESCE(SUM(CASE WHEN NetAmount>Balance THEN 1 ELSE 0 END),0) FROM #ClosureBills;
 IF @Cancel=0 AND @FutureCount>0 EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
 SET @Blocked=CASE WHEN @Cancel=0 AND (@PaidReductionAction IS NULL OR @PaidReductionAction NOT IN(N'Credit',N'Refund')) THEN 1 ELSE 0 END;
 DECLARE @PaidReturnAmount decimal(12,2)=(SELECT COALESCE(SUM(NetAmount-Balance),0) FROM #ClosureBills);
 SET @Raw=CONCAT(@UserId,CONVERT(varchar(10),@LeavingDate,120),@Reason,CONVERT(char(1),@Cancel),@MonthlyFeePolicy,CONVERT(varchar(10),@MonthlyBoundary,120),@PaidReductionAction,(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePayments WHERE AdmissionId=@AdmissionId FOR JSON PATH),
  (SELECT * FROM dbo.FeePaymentAllocations WHERE AdmissionId=@AdmissionId FOR JSON PATH));
 SET @Snapshot=CONVERT(varchar(64),HASHBYTES('SHA2_256',@Raw),2);
 IF @Preview=1
 BEGIN
  SELECT @Snapshot Snapshot,@State CurrentStatus,@Cancel CancelAdmission,@Blocked BlockedCount,@FutureCount FutureCount,@FutureAmount FutureAmount,@FuturePaid FuturePaid,COUNT(*) ChargeCount,@PaidReturnAmount PaidReturnAmount,@MonthlyFeePolicy MonthlyFeePolicy,@MonthlyBoundary ReverseFromMonth,
   COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) Outstanding FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled';
  COMMIT;RETURN;
 END;
 IF @ExpectedSnapshot IS NULL OR @ExpectedSnapshot<>@Snapshot THROW 59719,'The record changed. Review the closure again.',1;
 IF @Blocked>0 THROW 59718,'Review charges after the leaving month. Choose reversal for unpaid future charges, or resolve paid charges in Student Fee Account > Payment History first.',1;
 IF @Cancel=1
 BEGIN
  EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
  DECLARE @Charge bigint; DECLARE @Reverse TABLE(BillingChargeId bigint,AdmissionId int,FeeTypeName nvarchar(100),BillingMonth date,AmountReversed decimal(12,2),Reason nvarchar(500),Status nvarchar(20));
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@Enrollment AND Status<>N'Cancelled'
   AND (@Cancel=1 OR COALESCE(BillingMonth,ChargeDate)>DATEFROMPARTS(YEAR(@LeavingDate),MONTH(@LeavingDate),1));
  OPEN C;FETCH NEXT FROM C INTO @Charge;
  WHILE @@FETCH_STATUS=0 BEGIN INSERT @Reverse EXEC dbo.ReverseBillingCharge @UserId,@Charge,@Reason;FETCH NEXT FROM C INTO @Charge;END;
  CLOSE C;DEALLOCATE C;
 END;
 IF @Cancel=0 AND @FutureCount>0
 BEGIN
  DECLARE @MonthlyCharge bigint,@CorrectionRequest uniqueidentifier;
  DECLARE @MonthlyResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
  DECLARE MonthlyCharges CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM #ClosureBills ORDER BY BillingChargeId;
  OPEN MonthlyCharges;FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  WHILE @@FETCH_STATUS=0 BEGIN
   SET @CorrectionRequest=NEWID();
   INSERT @MonthlyResult EXEC dbo.AdjustBillingChargeRate @UserId,@MonthlyCharge,0,@PaidReductionAction,@Reason,@CorrectionRequest;
   FETCH NEXT FROM MonthlyCharges INTO @MonthlyCharge;
  END;
  CLOSE MonthlyCharges;DEALLOCATE MonthlyCharges;
 END;
 DECLARE @NewStatus nvarchar(30)=CASE WHEN @Cancel=1 THEN N'Cancelled' ELSE N'Left' END;
 UPDATE dbo.Enrollments SET Status=@NewStatus,LeavingDate=@LeavingDate,BillingReady=0,UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 UPDATE dbo.Students SET Status=@NewStatus,WithdrawalDate=@LeavingDate,UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,OldStatus,NewStatus,Remarks,UserId) VALUES(@Enrollment,@AdmissionId,N'STATUS_CHANGE',@LeavingDate,@State,@NewStatus,@Reason,@UserId);
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,@State,@NewStatus,@Reason,@LeavingDate,@UserId);

 COMMIT;
 END TRY
 BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'66',SYSDATETIME(),SUSER_SNAME(),N'Update obsolete fee-correction navigation in admission and placement errors');
GO
