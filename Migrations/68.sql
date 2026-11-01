-- Readmission eligibility alignment, 1 Nov 2026.
-- Do not rewrite historical placements, certificates or fee records.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 68 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>67
 THROW 51000,'Migration 68 requires schema 67.',1;
GO

-- Individual transfer/readmission pages share this read, but keep distinct write permissions.
CREATE OR ALTER PROCEDURE dbo.GetStudentEnrollmentsForLifecycle
    @UserId INT,
    @AdmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAnyPermission @UserId,N'Enrollment.Manage',N'Students.Administer';
    IF @AdmissionId <= 0
        THROW 52001, 'A valid student is required.', 1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId)
        THROW 52002, 'The selected student was not found.', 1;

    SELECT
        E.EnrollmentId,E.AdmissionId,E.AcademicYearId,
        AY.YearName,AY.StartDate AS PlacementYearStart,AY.EndDate AS PlacementYearEnd,
        E.ClassSectionId,
        CCS.CampusId,CMP.CampusName,C.ClassId,SEC.SectionId,C.ClassName,SEC.SectionName,
        E.EnrollmentDate,E.LeavingDate,E.BillingStartDate,E.PlacementConfirmedAt,E.BillingReady,
        E.Status,
        S.Status AS StudentStatus,
        CONVERT(bit,CASE WHEN NULLIF(LTRIM(RTRIM(S.AdmissionNumber)),N'') IS NOT NULL THEN 1 ELSE 0 END) AS HasOfficialAdmissionNumber,
        CONVERT(bit,CASE WHEN EXISTS(
            SELECT 1 FROM dbo.StudentPostingHolds H
            WHERE H.AdmissionId=E.AdmissionId AND H.ResumeDate IS NULL
        ) THEN 1 ELSE 0 END) AS IsOnHold
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
    JOIN dbo.AcademicYears AY ON AY.AcademicYearId=E.AcademicYearId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
    JOIN dbo.Campuses CMP ON CMP.CampusId=CCS.CampusId
    JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
    WHERE E.AdmissionId=@AdmissionId
    ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,E.EnrollmentId DESC;
END;
GO

-- Preserve the complete existing writer; add only business eligibility checks.
DECLARE @ReAdmit nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.ReAdmitStudent',N'P'));
DECLARE @Anchor nvarchar(200)=N'THROW 56524, ''The student is already active.'', 1;';
IF @ReAdmit IS NULL OR CHARINDEX(@Anchor,@ReAdmit)=0
 OR CHARINDEX(@Anchor,@ReAdmit,CHARINDEX(@Anchor,@ReAdmit)+1)<>0
 THROW 51000,'Readmission procedure differs from expected schema 67; no SQL was altered.',1;
SET @ReAdmit=REPLACE(@ReAdmit,@Anchor,@Anchor+CHAR(10)+N'
    -- Left includes leaving certificates, recorded departures, and final-class completion.
    -- Withdrawn is accepted as a legacy synonym, not a new departure status.
    IF @OldStudentStatus NOT IN (N''Left'',N''Withdrawn'',N''Inactive'')
       OR NOT EXISTS(SELECT 1 FROM dbo.Students S WHERE S.AdmissionId=@AdmissionId
          AND NULLIF(LTRIM(RTRIM(S.AdmissionNumber)),N'''') IS NOT NULL)
        THROW 56529, ''Only officially admitted former students who left or became inactive can be readmitted. Cancelled pending registrations must use Reopen Registration.'',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WITH(UPDLOCK,HOLDLOCK)
              WHERE H.AdmissionId=@AdmissionId AND H.ResumeDate IS NULL)
        THROW 56530, ''This student is on hold. Use Lift Hold / Resume rather than readmission.'',1;
');
-- An existing module may retain a CREATE/CREATE OR ALTER definition text.
-- Re-execute it as ALTER, never CREATE, to preserve the procedure and grants.
DECLARE @ModuleHeader nvarchar(80)=UPPER(LEFT(LTRIM(@ReAdmit),80));
IF @ModuleHeader LIKE N'CREATE OR ALTER PROC%'
    SET @ReAdmit=STUFF(@ReAdmit,CHARINDEX(N'CREATE OR ALTER',UPPER(@ReAdmit)),15,N'ALTER');
ELSE IF @ModuleHeader LIKE N'CREATE PROC%'
    SET @ReAdmit=STUFF(@ReAdmit,CHARINDEX(N'CREATE',UPPER(@ReAdmit)),6,N'ALTER');
ELSE IF @ModuleHeader NOT LIKE N'ALTER PROC%'
    THROW 51000,'Procedure definition has an unexpected header; refusing an unsafe change.',1;
EXEC sys.sp_executesql @ReAdmit;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'68',SYSDATETIME(),SUSER_SNAME(),
 N'Align official former-student readmission eligibility and lifecycle reader permissions');
GO
