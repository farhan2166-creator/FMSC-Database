-- Persist checked AND unchecked campus assignments without deleting enrollment history.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF (SELECT TOP (1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC) <> N'2'
        THROW 57403, 'Assignment saving requires database schema version 2.', 1;

    ALTER TABLE dbo.CampusClassSections ADD IsActive bit NOT NULL
        CONSTRAINT DF_CampusClassSections_IsActive DEFAULT (1) WITH VALUES;

    EXEC sys.sp_executesql N'
ALTER PROCEDURE dbo.SaveCampusClassSections
    @UserId int, @CampusId int,
    @Assignments dbo.CampusClassSectionAssignmentType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N''System.Setup'';
    IF NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE CampusId = @CampusId)
        THROW 50102, ''Please select a valid campus.'', 1;
    IF EXISTS (SELECT 1 FROM @Assignments A WHERE A.IsSelected = 1
        AND NOT EXISTS (SELECT 1 FROM dbo.Classes C WHERE C.ClassId = A.ClassId))
        THROW 50104, ''One or more selected classes are invalid.'', 1;
    IF EXISTS (SELECT 1 FROM @Assignments A WHERE A.IsSelected = 1
        AND NOT EXISTS (SELECT 1 FROM dbo.Sections S WHERE S.SectionId = A.SectionId))
        THROW 50105, ''One or more selected sections are invalid.'', 1;
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
END';

    EXEC sys.sp_executesql N'
ALTER PROCEDURE dbo.GetClassSections
    @UserId int, @CampusId int, @ClassId int, @SectionId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequireAuthenticatedUser @UserId;
    IF @CampusId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE CampusId = @CampusId)
        THROW 50102, ''Please select a valid campus.'', 1;
    IF @ClassId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Classes WHERE ClassId = @ClassId)
        THROW 50104, ''Please select a valid class.'', 1;
    IF @SectionId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Sections WHERE SectionId = @SectionId)
        THROW 50105, ''Please select a valid section.'', 1;
    SELECT CCS.ClassSectionId, CCS.CampusId, CMP.CampusName,
           CCS.ClassId, C.ClassName, CCS.SectionId, S.SectionName
    FROM dbo.CampusClassSections CCS
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections S ON S.SectionId = CCS.SectionId
    JOIN dbo.Campuses CMP ON CMP.CampusId = CCS.CampusId
    WHERE CCS.IsActive = 1
        AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
        AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
        AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
    ORDER BY CMP.CampusName, C.ClassName, S.SectionName, S.SectionId;
END';

    INSERT dbo.DatabaseVersion (VersionNumber, AppliedOn, AppliedBy, Notes)
    VALUES (N'3', SYSDATETIME(), SUSER_SNAME(), N'Persist assignment deselection while preserving enrollment history.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
