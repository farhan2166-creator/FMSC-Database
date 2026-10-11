
-- GetAcademicPrograms
CREATE   PROCEDURE dbo.GetAcademicPrograms
@UserId INT, @ProgramType NVARCHAR (30)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequireAuthenticatedUser @UserId;
    SELECT   AcademicProgramId,
             ProgramCode,
             ProgramName,
             ProgramType,
             DisplayOrder
    FROM     dbo.AcademicPrograms
    WHERE    (NULLIF (LTRIM(RTRIM(@ProgramType)), N'') IS NULL
              OR ProgramType = @ProgramType)
    ORDER BY DisplayOrder, AcademicProgramId;
END



