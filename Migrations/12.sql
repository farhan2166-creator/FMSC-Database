-- Recalculate the next admission number after renumbering; student identity stays unchanged.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
GO
-- SetStudentAdmissionNumber
CREATE OR ALTER PROCEDURE dbo.SetStudentAdmissionNumber
@UserId INT, @AdmissionId INT, @AdmissionNumber VARCHAR (40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF @@TRANCOUNT = 0
        THROW 59205, 'Save the admission number together with student details.', 1;
    SET @AdmissionNumber = dbo.NormalizeAdmissionNumber(@AdmissionNumber);
    IF @AdmissionNumber IS NULL
        THROW 59203, 'Use a positive admission number with an optional letter suffix, such as 1 or 1a.', 1;
    DECLARE @Last AS BIGINT;
    SELECT @Last = LastNumber
    FROM   dbo.AdmissionNumberCounter WITH (UPDLOCK, HOLDLOCK)
    WHERE  CounterId = 1;
    IF @Last IS NULL THROW 59202, 'The admission number counter is missing.', 1;
    DECLARE @OldNumber AS VARCHAR (40);
    SELECT @OldNumber = AdmissionNumber
    FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
    WHERE  AdmissionId = @AdmissionId;
    IF @OldNumber IS NULL
        THROW 51710, 'The selected student was not found.', 1;
    IF @OldNumber = @AdmissionNumber
        RETURN;
    IF EXISTS (SELECT 1
               FROM   dbo.Students
               WHERE  AdmissionNumber = @AdmissionNumber
                      AND AdmissionId <> @AdmissionId)
        THROW 59204, 'This admission number is already assigned to another student. Choose a different number.', 1;

    UPDATE dbo.Students
    SET    AdmissionNumber = @AdmissionNumber,
           UpdatedAt       = SYSDATETIME()
    WHERE  AdmissionId = @AdmissionId;
    -- All allocators lock the counter first, so renumbering and allocation stay serialized.
    -- Letter suffixes contribute their numeric base: 6590a belongs to register number 6590.
    DECLARE @HighestAssigned BIGINT;
    SELECT @HighestAssigned = ISNULL(MAX(TRY_CONVERT(BIGINT,
        LEFT(AdmissionNumber, PATINDEX('%[^0-9]%', AdmissionNumber + 'x') - 1))), 0)
    FROM dbo.Students;
    UPDATE dbo.AdmissionNumberCounter SET LastNumber = @HighestAssigned WHERE CounterId = 1;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID(), @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @AdmissionId);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ADMISSION_NUMBER_CHANGE', @RecordId);
    DECLARE @OldValues AS NVARCHAR (MAX) = (SELECT @OldNumber AS AdmissionNumber
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    DECLARE @NewValues AS NVARCHAR (MAX) = (SELECT @AdmissionNumber AS AdmissionNumber
                                            FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXECUTE dbo.WriteAuditLog @OperationId, @UserId, N'Students', @RecordId, N'UPDATE', @OldValues, @NewValues;
DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
END
GO
COMMIT TRANSACTION;
GO
