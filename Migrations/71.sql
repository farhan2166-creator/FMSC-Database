-- Editable starter programs. Preserve every existing program and class assignment.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 71 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>70
 THROW 51000,'Migration 71 requires schema 70.',1;

DECLARE @OrderLock int;
EXEC @OrderLock = sys.sp_getapplock @Resource=N'SetupOrder:AcademicPrograms',
 @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
IF @OrderLock<0 THROW 57401,'Unable to lock program ordering. Please try again.',1;

DECLARE @LastOrder int = (SELECT ISNULL(MAX(DisplayOrder),0) FROM dbo.AcademicPrograms WITH(UPDLOCK,HOLDLOCK));
;WITH Starters AS
(
 SELECT * FROM (VALUES
  (1,N'School',N'PRI',N'Primary'),
  (2,N'School',N'MID',N'Middle'),
  (3,N'School',N'SEC',N'Secondary'),
  (4,N'College',N'FSC-PM',N'F.Sc (Pre-Medical)'),
  (5,N'College',N'FSC-PE',N'F.Sc (Pre-Engineering)'),
  (6,N'College',N'ICS',N'ICS'),
  (7,N'College',N'ICOM',N'I.Com'),
  (8,N'College',N'FA',N'FA')
 ) S(Sequence,ProgramType,ProgramCode,ProgramName)
), Missing AS
(
 SELECT S.* FROM Starters S
 WHERE NOT EXISTS
 (SELECT 1 FROM dbo.AcademicPrograms P
  WHERE P.ProgramType=S.ProgramType
    AND LTRIM(RTRIM(P.ProgramName))=S.ProgramName)
)
INSERT dbo.AcademicPrograms(ProgramType,ProgramCode,ProgramName,DisplayOrder,CreatedAt)
SELECT S.ProgramType,
 -- Codes are optional. Preserve an existing owner's use of a suggested code.
 CASE WHEN EXISTS(SELECT 1 FROM dbo.AcademicPrograms P WHERE P.ProgramCode=S.ProgramCode)
      THEN NULL ELSE S.ProgramCode END,
 S.ProgramName,@LastOrder+CONVERT(int,ROW_NUMBER() OVER(ORDER BY S.Sequence)),SYSUTCDATETIME()
FROM Missing S;

INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'71',SYSDATETIME(),SUSER_SNAME(),N'Add editable school stages and college study streams without changing existing programs or assignments');
GO
