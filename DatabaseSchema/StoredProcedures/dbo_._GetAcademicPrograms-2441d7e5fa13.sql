-- Module body only. See companion XML for signatures and relationships.

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
