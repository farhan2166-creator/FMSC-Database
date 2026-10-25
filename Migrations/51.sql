-- Retire certificate copy cancellation. Preserve existing historical markers and locks.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 51 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>50
 THROW 51000,'Migration 51 requires schema 50.',1;
GO
DROP PROCEDURE IF EXISTS dbo.VoidCertificateCopy;
GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
VALUES(N'51',SYSDATETIME(),SUSER_SNAME(),N'Remove certificate cancellation command: avoid confusing copy cancellation with record deletion or reversal of leaving and fees; retain existing historical records');
GO
