SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- DeleteStudentSibling
CREATE   PROCEDURE dbo.DeleteStudentSibling
@UserId INT, @FamilyMemberId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    DECLARE @FamilyId AS INT, @AdmissionId AS INT;
    SELECT @FamilyId = FamilyId,
           @AdmissionId = AdmissionId
    FROM   dbo.FamilyMembers
    WHERE  FamilyMemberId = @FamilyMemberId;
    IF @FamilyId IS NULL
        THROW 56650, 'The selected family relationship was not found.', 1;
    BEGIN TRANSACTION;
    DELETE dbo.FamilyMembers
    WHERE  FamilyMemberId = @FamilyMemberId;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FamilyMembers
                   WHERE  FamilyId = @FamilyId)
        DELETE dbo.Families
        WHERE  FamilyId = @FamilyId;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT @FamilyMemberId AS FamilyMemberId,
                                                   @FamilyId AS FamilyId,
                                                   @AdmissionId AS AdmissionId
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    COMMIT TRANSACTION;
END



GO
