-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    IF @CurrentClassId <= 0
        THROW 57300, 'A valid current class is required.', 1;
    DECLARE @CurrentDisplayOrder AS INT, @CurrentAcademicProgramId AS INT;
    SELECT @CurrentDisplayOrder = C.DisplayOrder,
           @CurrentAcademicProgramId = C.AcademicProgramId
    FROM   dbo.Classes AS C
    WHERE  C.ClassId = @CurrentClassId;
    IF @CurrentDisplayOrder IS NULL
        THROW 57301, 'The current class was not found or is inactive.', 1;
    SELECT   C.ClassId,
             C.ClassCode,
             C.ClassName,
             C.DisplayOrder,
             C.AcademicProgramId
    FROM     dbo.Classes AS C
    WHERE    C.AcademicProgramId = @CurrentAcademicProgramId
             AND (C.DisplayOrder > @CurrentDisplayOrder
                  OR (C.DisplayOrder = @CurrentDisplayOrder
                      AND C.ClassId > @CurrentClassId))
    ORDER BY C.DisplayOrder, C.ClassName, C.ClassId;
END
