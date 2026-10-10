SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- AddStudentSibling
CREATE   PROCEDURE dbo.AddStudentSibling
@AdmissionId INT, @SiblingAdmissionId INT, @RelationshipType NVARCHAR (30), @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Siblings';
    DECLARE @Relationship AS NVARCHAR (30) = LTRIM(RTRIM(@RelationshipType));
    IF NULLIF (@Relationship, N'') IS NULL
        THROW 56644, 'A relationship type is required when adding a sibling.', 1;
    IF @Relationship NOT IN (N'Sibling', N'Brother', N'Sister')
        THROW 56645, 'The relationship type must be Sibling, Brother or Sister.', 1;
    SET @Relationship = N'Sibling';
    IF @AdmissionId = @SiblingAdmissionId
        THROW 56640, 'A student cannot be added as their own sibling.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 56641, 'The main student was not found.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @SiblingAdmissionId)
        THROW 56642, 'The sibling student was not found.', 1;
    BEGIN TRANSACTION;
    DECLARE @FamilyId AS INT;
    SELECT @FamilyId = FamilyId
    FROM   dbo.FamilyMembers WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @AdmissionId;
    DECLARE @SiblingFamilyId AS INT;
    SELECT @SiblingFamilyId = FamilyId
    FROM   dbo.FamilyMembers WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @SiblingAdmissionId;
    IF @FamilyId IS NOT NULL
       AND @SiblingFamilyId IS NOT NULL
       AND @FamilyId <> @SiblingFamilyId
        THROW 56643, 'These students already belong to different families. Resolve the existing family groups before adding the relationship.', 1;
    IF @FamilyId IS NULL
       AND @SiblingFamilyId IS NULL
        BEGIN
            INSERT  dbo.Families (FamilyCode)
            VALUES              (NULL);
            SET @FamilyId = CONVERT (INT, SCOPE_IDENTITY());
            INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
            VALUES                   (@FamilyId, @AdmissionId, @Relationship);
            INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
            VALUES                   (@FamilyId, @SiblingAdmissionId, @Relationship);
        END
    ELSE
        IF @FamilyId IS NOT NULL
           AND @SiblingFamilyId IS NULL
            BEGIN
                INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
                VALUES                   (@FamilyId, @SiblingAdmissionId, @Relationship);
            END
        ELSE
            IF @FamilyId IS NULL
               AND @SiblingFamilyId IS NOT NULL
                BEGIN
                    INSERT  dbo.FamilyMembers (FamilyId, AdmissionId, RelationshipType)
                    VALUES                   (@SiblingFamilyId, @AdmissionId, @Relationship);
                    SET @FamilyId = @SiblingFamilyId;
                END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @FamilyId AS FamilyId,
                                                   @AdmissionId AS AdmissionId,
                                                   @SiblingAdmissionId AS SiblingAdmissionId,
                                                   @Relationship AS RelationshipType
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    COMMIT TRANSACTION;
    EXECUTE dbo.GetStudentFamily @UserId = @UserId, @AdmissionId = @AdmissionId;
END



GO
