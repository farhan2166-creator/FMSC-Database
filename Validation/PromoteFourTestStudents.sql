-- One-off, explicitly requested test-data operation; not an application migration.
-- Keeps the normal procedure unchanged. The helper exists only inside this
-- transaction and accepts exactly the reviewed test cohort/date/destinations.
-- Set FMSC_TEST_PROMOTION_BACKUP to a new absolute path, then run sqlcmd -b.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 10000;
IF DB_NAME()<>N'FMSC_DB' THROW 51000,'Wrong database.',1;
IF OBJECT_ID(N'dbo.__FMSC_TestPromotion_20261019') IS NOT NULL
    THROW 51000,'Unexpected test helper already exists.',1;
DECLARE @Original nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.BulkPromoteStudents'));
IF @Original IS NULL THROW 51000,'Promotion procedure is unavailable.',1;
DECLARE @Source nvarchar(max)=REPLACE(@Original,CHAR(13),N''),@Start int;
DECLARE @Guard nvarchar(max)=N'IF @EffectiveDate > CONVERT (DATE, SYSDATETIME())'+CHAR(10)+
    N'        THROW 52509, ''The effective date cannot be in the future.'', 1;';
IF CHARINDEX(@Guard,@Source)=0 THROW 51000,'Promotion date guard changed; review before running.',1;
SET @Start=CHARINDEX(N'@UserId INT',@Source);
IF @Start=0 THROW 51000,'Unexpected promotion signature.',1;
SET @Source=N'CREATE PROCEDURE dbo.__FMSC_TestPromotion_20261019 '+SUBSTRING(@Source,@Start,LEN(@Source));
SET @Source=REPLACE(@Source,@Guard,N'
    IF @UserId<>1 OR @SourceAcademicYearId<>28 OR @DestinationAcademicYearId<>29
       OR @SourceClassId<>1 OR @SourceSectionId<>1 OR @EffectiveDate<>CONVERT(date,''20270401'')
       OR @CarryConcessions<>0
       OR NOT ((@SourceCampusId=1 AND @DestinationClassSectionId=2) OR
               (@SourceCampusId=2 AND @DestinationClassSectionId=15))
       OR EXISTS(SELECT 1 FROM @Decisions WHERE Decision<>N''PROMOTE'' OR
          (@SourceCampusId=1 AND AdmissionId NOT IN(6594,6595,6596)) OR
          (@SourceCampusId=2 AND AdmissionId<>6589))
       THROW 51000,''This helper only supports the authorized four-student test promotion.'',1;');

BACKUP DATABASE FMSC_DB TO DISK=N'$(FMSC_TEST_PROMOTION_BACKUP)' WITH COPY_ONLY,CHECKSUM;
RESTORE VERIFYONLY FROM DISK=N'$(FMSC_TEST_PROMOTION_BACKUP)' WITH CHECKSUM;

BEGIN TRY
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;
    EXEC dbo.RequirePermission @UserId=1,@PermissionCode=N'Enrollment.Manage';
    IF NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WITH(UPDLOCK,HOLDLOCK)
       WHERE AcademicYearId=29 AND YearName=N'2027-2028' AND StartDate='20270401' AND EndDate='20280331')
        THROW 51000,'Destination year changed.',1;
    IF (SELECT COUNT(*) FROM dbo.Students S WITH(UPDLOCK,HOLDLOCK)
        JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.AdmissionId=S.AdmissionId
        WHERE S.Status=N'Active' AND E.Status=N'Active')<>4
        THROW 51000,'The active cohort changed. Review before running.',1;
    IF (SELECT COUNT(*) FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId
        WHERE S.Status=N'Active' AND S.LegacySetupPending=0 AND E.Status=N'Active'
        AND E.AcademicYearId=28 AND E.PlacementConfirmedAt IS NOT NULL AND
          ((S.AdmissionId=6589 AND E.EnrollmentId=731 AND E.ClassSectionId=14) OR
           (S.AdmissionId=6594 AND E.EnrollmentId=732 AND E.ClassSectionId=1) OR
           (S.AdmissionId=6595 AND E.EnrollmentId=733 AND E.ClassSectionId=1) OR
           (S.AdmissionId=6596 AND E.EnrollmentId=734 AND E.ClassSectionId=1)))<>4
        THROW 51000,'Reviewed source placements changed.',1;
    IF EXISTS(SELECT 1 FROM dbo.Enrollments WHERE AcademicYearId=29 AND AdmissionId IN(6589,6594,6595,6596))
        THROW 51000,'A selected student already has a destination enrollment; do not retry blindly.',1;
    IF (SELECT COUNT(*) FROM dbo.CampusClassSections WHERE ClassId=2 AND SectionId=1 AND
        ((CampusId=1 AND ClassSectionId=2) OR (CampusId=2 AND ClassSectionId=15)))<>2
        THROW 51000,'Reviewed Nursery destinations changed.',1;

    DECLARE @Charges varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @Payments varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.FeePayments WITH(UPDLOCK,HOLDLOCK) ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @Allocations varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.FeePaymentAllocations WITH(UPDLOCK,HOLDLOCK) ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @Credit varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.StudentCreditEntries WITH(UPDLOCK,HOLDLOCK) ORDER BY CreditEntryId FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @OldFees varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId IN(731,732,733,734) ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @Students varbinary(32)=HASHBYTES('SHA2_256',(SELECT * FROM dbo.Students ORDER BY AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @HistoryBefore int=(SELECT COUNT(*) FROM dbo.EnrollmentHistory),@BatchBefore int=(SELECT COUNT(*) FROM dbo.PromotionBatches);

    EXEC sys.sp_executesql @Source;
    DECLARE @Boys dbo.BulkPromotionDecisionType,@Girls dbo.BulkPromotionDecisionType;
    INSERT @Boys VALUES(6594,N'PROMOTE'),(6595,N'PROMOTE'),(6596,N'PROMOTE');
    INSERT @Girls VALUES(6589,N'PROMOTE');
    EXEC dbo.__FMSC_TestPromotion_20261019 @UserId=1,@SourceAcademicYearId=28,@DestinationAcademicYearId=29,
        @SourceCampusId=1,@SourceClassId=1,@SourceSectionId=1,@DestinationClassSectionId=2,
        @EffectiveDate='20270401',@Remarks=N'User-requested test promotion on 19-Oct-2026; destination year starts 01-Apr-2027. Awaiting return confirmation.',
        @Decisions=@Boys,@CarryConcessions=0;
    EXEC dbo.__FMSC_TestPromotion_20261019 @UserId=1,@SourceAcademicYearId=28,@DestinationAcademicYearId=29,
        @SourceCampusId=2,@SourceClassId=1,@SourceSectionId=1,@DestinationClassSectionId=15,
        @EffectiveDate='20270401',@Remarks=N'User-requested test promotion on 19-Oct-2026; destination year starts 01-Apr-2027. Awaiting return confirmation.',
        @Decisions=@Girls,@CarryConcessions=0;
    DROP PROCEDURE dbo.__FMSC_TestPromotion_20261019;

    IF (SELECT COUNT(*) FROM dbo.Enrollments WHERE AdmissionId IN(6589,6594,6595,6596)
        AND AcademicYearId=29 AND Status=N'Active' AND EnrollmentDate='20270401' AND BillingReady=0
        AND PlacementConfirmedAt IS NOT NULL AND
        ((AdmissionId=6589 AND ClassSectionId=15) OR (AdmissionId IN(6594,6595,6596) AND ClassSectionId=2)))<>4
        THROW 51000,'Destination enrollment verification failed.',1;
    IF (SELECT COUNT(*) FROM dbo.Enrollments WHERE EnrollmentId IN(731,732,733,734)
        AND Status=N'Promoted' AND LeavingDate='20270401')<>4
        THROW 51000,'Source history verification failed.',1;
    IF (SELECT COUNT(*) FROM dbo.EnrollmentHistory)<>@HistoryBefore+4 OR
       (SELECT COUNT(*) FROM dbo.PromotionBatches)<>@BatchBefore+2
        THROW 51000,'Promotion history verification failed.',1;
    IF @Charges<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.BillingCharges ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
       @Payments<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.FeePayments ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
       @Allocations<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.FeePaymentAllocations ORDER BY FeePaymentAllocationId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
       @Credit<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.StudentCreditEntries ORDER BY CreditEntryId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
       @OldFees<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.StudentFeeAssignments WHERE EnrollmentId IN(731,732,733,734) ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) OR
       @Students<>HASHBYTES('SHA2_256',(SELECT * FROM dbo.Students ORDER BY AdmissionId FOR JSON PATH,INCLUDE_NULL_VALUES))
        THROW 51000,'Existing student or financial records changed; rolling back.',1;
    IF HASHBYTES('SHA2_256',@Original)<>HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'dbo.BulkPromoteStudents')))
        THROW 51000,'The standard procedure changed; rolling back.',1;
    IF @@TRANCOUNT<>1 THROW 51000,'Unexpected transaction nesting.',1;
    COMMIT;
    PRINT 'SUCCESS: Four test students promoted; existing ledgers and standard promotion procedure unchanged.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;

SELECT S.AdmissionNumber,S.StudentName,Y.YearName,C.CampusName,CL.ClassName,SE.SectionName,
    E.EnrollmentId,E.EnrollmentDate,E.BillingReady
FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId
JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
JOIN dbo.Campuses C ON C.CampusId=P.CampusId JOIN dbo.Classes CL ON CL.ClassId=P.ClassId
JOIN dbo.Sections SE ON SE.SectionId=P.SectionId
WHERE S.AdmissionId IN(6589,6594,6595,6596) AND E.Status=N'Active'
ORDER BY S.AdmissionNumber;
