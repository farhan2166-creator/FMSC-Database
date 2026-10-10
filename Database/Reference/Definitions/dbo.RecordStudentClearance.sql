SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   PROCEDURE dbo.RecordStudentClearance
    @UserId              INT,
    @AdmissionId         INT,
    @OutstandingBalance  DECIMAL(18,2),
    @ChecklistJson       NVARCHAR(MAX),
    @Remarks             NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE AdmissionId = @AdmissionId)
        THROW 52703, 'The student was not found.', 1;

    INSERT INTO dbo.StudentClearanceRecords
    (
        AdmissionId,
        OutstandingBalance,
        ChecklistJson,
        Remarks,
        ClearedByUserId
    )
    VALUES
    (
        @AdmissionId,
        @OutstandingBalance,
        @ChecklistJson,
        @Remarks,
        @UserId
    );

    SELECT SCOPE_IDENTITY() AS ClearanceRecordId;
END;

GO
