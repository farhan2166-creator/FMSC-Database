SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- RepairLeaveCertificateEnrollmentLinks
CREATE   PROCEDURE dbo.RepairLeaveCertificateEnrollmentLinks
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT L.LeaveRecordId,
               L.AdmissionId,
               L.EnrollmentId AS OldEnrollmentId,
               L.LeaveDate,
               L.ClassStudied
        INTO   #Broken
        FROM   dbo.LeaveCertificates AS L WITH (UPDLOCK, HOLDLOCK)
               LEFT OUTER JOIN
               dbo.Enrollments AS E
               ON E.EnrollmentId = L.EnrollmentId
        WHERE  E.EnrollmentId IS NULL
               OR E.AdmissionId <> L.AdmissionId;
        SELECT B.LeaveRecordId,
               E.EnrollmentId
        INTO   #Candidates
        FROM   #Broken AS B
               INNER JOIN
               dbo.Enrollments AS E
               ON E.AdmissionId = B.AdmissionId
                  AND E.Status = N'Left'
                  AND E.LeavingDate = B.LeaveDate
               INNER JOIN
               dbo.CampusClassSections AS CCS
               ON CCS.ClassSectionId = E.ClassSectionId
               INNER JOIN
               dbo.Classes AS C
               ON C.ClassId = CCS.ClassId
        WHERE  LTRIM(RTRIM(C.ClassName)) = LTRIM(RTRIM(B.ClassStudied));
        IF EXISTS (SELECT   B.LeaveRecordId
                   FROM     #Broken AS B
                            LEFT OUTER JOIN
                            #Candidates AS C
                            ON C.LeaveRecordId = B.LeaveRecordId
                   GROUP BY B.LeaveRecordId
                   HAVING   COUNT(C.EnrollmentId) <> 1)
            THROW 51730, 'A broken certificate link has no unique enrollment matching its student, class and leave date. Nothing was repaired.', 1;
        DECLARE @Count AS INT = (SELECT COUNT(*)
                                 FROM   #Broken);
        IF @Count = 0
            BEGIN
                COMMIT TRANSACTION;
                SELECT 0 AS RepairedLinks;
                RETURN;
            END
        DECLARE @Operation AS UNIQUEIDENTIFIER = NEWID();
        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
        VALUES                     (@Operation, @UserId, N'CERTIFICATE_LINK_REPAIR', N'Wrong or missing enrollment ownership; student/class/leave-date match.');
        UPDATE L
        SET    EnrollmentId = C.EnrollmentId
        FROM   dbo.LeaveCertificates AS L
               INNER JOIN
               #Candidates AS C
               ON C.LeaveRecordId = L.LeaveRecordId;
        DECLARE @Record AS INT, @OldId AS INT, @NewId AS INT, @Student AS INT, @OldJson AS NVARCHAR (MAX), @NewJson AS NVARCHAR (MAX), @RecordText AS NVARCHAR (200);
        DECLARE Repairs CURSOR LOCAL FAST_FORWARD
            FOR SELECT B.LeaveRecordId,
                       B.OldEnrollmentId,
                       C.EnrollmentId,
                       B.AdmissionId
                FROM   #Broken AS B
                       INNER JOIN
                       #Candidates AS C
                       ON C.LeaveRecordId = B.LeaveRecordId;
        OPEN Repairs;
        FETCH NEXT FROM Repairs INTO @Record, @OldId, @NewId, @Student;
        WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @RecordText = CONVERT (NVARCHAR (200), @Record);
                SET @OldJson = (SELECT @Student AS AdmissionId,
                                       @OldId AS EnrollmentId
                                FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                SET @NewJson = (SELECT @Student AS AdmissionId,
                                       @NewId AS EnrollmentId
                                FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                EXECUTE dbo.WriteAuditLog @OperationId = @Operation, @UserId = @UserId, @TableName = N'LeaveCertificates', @RecordId = @RecordText, @Action = N'UPDATE', @OldValues = @OldJson, @NewValues = @NewJson;
                FETCH NEXT FROM Repairs INTO @Record, @OldId, @NewId, @Student;
            END
        CLOSE Repairs;
        DEALLOCATE Repairs;
        DELETE dbo.AuditOperations WHERE OperationId=@Operation AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@Operation);
COMMIT TRANSACTION;
        SELECT @Count AS RepairedLinks;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        THROW;
    END CATCH
END



GO
