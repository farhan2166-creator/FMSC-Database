SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.SaveCampusClassSections
    @UserId int, @CampusId int,
    @Assignments dbo.CampusClassSectionAssignmentType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    IF NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE CampusId = @CampusId)
        THROW 50102, 'Please select a valid campus.', 1;
    IF EXISTS (SELECT 1 FROM @Assignments A WHERE A.IsSelected = 1
        AND NOT EXISTS (SELECT 1 FROM dbo.Classes C WHERE C.ClassId = A.ClassId))
        THROW 50104, 'One or more selected classes are invalid.', 1;
    IF EXISTS (SELECT 1 FROM @Assignments A WHERE A.IsSelected = 1
        AND NOT EXISTS (SELECT 1 FROM dbo.Sections S WHERE S.SectionId = A.SectionId))
        THROW 50105, 'One or more selected sections are invalid.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        MERGE dbo.CampusClassSections WITH (HOLDLOCK) AS Target
        USING (SELECT DISTINCT @CampusId AS CampusId, ClassId, SectionId
               FROM @Assignments WHERE IsSelected = 1) AS Source
        ON Target.CampusId = Source.CampusId AND Target.ClassId = Source.ClassId
            AND Target.SectionId = Source.SectionId
        WHEN MATCHED THEN UPDATE SET IsActive = 1, UpdatedAt = SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT (CampusId, ClassId, SectionId, IsActive)
            VALUES (Source.CampusId, Source.ClassId, Source.SectionId, 1);

        UPDATE CCS SET IsActive = 0, UpdatedAt = SYSDATETIME()
        FROM dbo.CampusClassSections CCS
        WHERE CCS.CampusId = @CampusId
            AND EXISTS (SELECT 1 FROM @Assignments A
                WHERE A.ClassId = CCS.ClassId AND A.SectionId = CCS.SectionId)
            AND NOT EXISTS (SELECT 1 FROM @Assignments A
                WHERE A.ClassId = CCS.ClassId AND A.SectionId = CCS.SectionId AND A.IsSelected = 1);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
