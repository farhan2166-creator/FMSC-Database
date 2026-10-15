-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    IF @List = N'FeeTypes'
        EXEC dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    ELSE
        EXEC dbo.RequirePermission @UserId, N'System.Setup';
    IF @List = N'Classes'
    BEGIN
        ;WITH R AS (SELECT ClassId, ROW_NUMBER() OVER(PARTITION BY AcademicProgramId ORDER BY DisplayOrder,ClassId) AS N FROM dbo.Classes)
        UPDATE C SET DisplayOrder=-R.N FROM dbo.Classes C JOIN R ON R.ClassId=C.ClassId;
        UPDATE dbo.Classes SET DisplayOrder=-DisplayOrder;
    END
    ELSE IF @List = N'FeeTypes'
    BEGIN
        ;WITH R AS (SELECT FeeTypeId, ROW_NUMBER() OVER(ORDER BY DisplayOrder,FeeTypeId) AS N FROM dbo.FeeTypes)
        UPDATE C SET DisplayOrder=-R.N FROM dbo.FeeTypes C JOIN R ON R.FeeTypeId=C.FeeTypeId;
        UPDATE dbo.FeeTypes SET DisplayOrder=-DisplayOrder;
    END
    ELSE IF @List = N'AcademicPrograms'
    BEGIN
        ;WITH R AS (SELECT AcademicProgramId, ROW_NUMBER() OVER(ORDER BY DisplayOrder,AcademicProgramId) AS N FROM dbo.AcademicPrograms)
        UPDATE C SET DisplayOrder=-R.N FROM dbo.AcademicPrograms C JOIN R ON R.AcademicProgramId=C.AcademicProgramId;
        UPDATE dbo.AcademicPrograms SET DisplayOrder=-DisplayOrder;
    END
    ELSE THROW 57400, 'Unknown setup ordering list.', 1;
END;
