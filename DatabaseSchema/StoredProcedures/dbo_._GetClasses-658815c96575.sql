-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAuthenticatedUser @UserId;
    SELECT C.ClassId, C.ClassCode, C.ClassName, C.AcademicProgramId,
           P.ProgramCode, P.ProgramName, P.ProgramType, C.DisplayOrder
    FROM dbo.Classes C
    JOIN dbo.AcademicPrograms P ON P.AcademicProgramId=C.AcademicProgramId
    WHERE @AcademicProgramId=0 OR C.AcademicProgramId=@AcademicProgramId
    ORDER BY P.DisplayOrder ASC, P.AcademicProgramId ASC,
             C.DisplayOrder ASC, C.ClassId ASC;
END;
