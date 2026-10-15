-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @AdmissionId <= 0
        THROW 58800, 'A valid student is required.', 1;
    IF @ExamType NOT IN (N'SSC', N'HSSC1', N'HSSC2')
        THROW 58801, 'A valid exam type is required.', 1;
    IF @ExamYear IS NULL
       OR @ExamYear < 1990
       OR @ExamYear > YEAR(SYSDATETIME()) + 1
        THROW 58802, 'A valid exam year is required.', 1;
    IF @BoardName IS NULL
       OR LTRIM(RTRIM(@BoardName)) = N''
        THROW 58803, 'Board name is required.', 1;
    IF @RollNumber IS NULL
       OR LTRIM(RTRIM(@RollNumber)) = N''
        THROW 58804, 'Roll number is required.', 1;
    IF @MarksObtained IS NULL
       OR @MarksObtained < 0
        THROW 58805, 'Marks obtained is required.', 1;
    IF @MarksOutOf IS NULL
       OR @MarksOutOf <= 0
        THROW 58806, 'Marks out of is required.', 1;
    IF @MarksObtained > @MarksOutOf
        THROW 58807, 'Marks obtained cannot exceed marks out of.', 1;
    IF @CertificateDate IS NULL
        THROW 58808, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58809, 'Certificate date cannot be in the future.', 1;
    DECLARE @StudentName AS NVARCHAR (500), @FatherName AS NVARCHAR (500), @Gender AS NVARCHAR (20), @DateOfBirth AS DATE, @ProvisionalCertificateId AS INT;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @Gender = S.Gender,
           @DateOfBirth = S.DateOfBirth
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 58810, 'The student was not found.', 1;
    INSERT  dbo.ProvisionalCertificates (AdmissionId, StudentName, FatherName, Gender, DateOfBirth, ExamType, ExamYear, BoardName, RollNumber, MarksObtained, MarksOutOf, Grade, OtherSubjects, CertificateDate, CreatedByUserId)
    VALUES                             (@AdmissionId, @StudentName, @FatherName, @Gender, @DateOfBirth, @ExamType, @ExamYear, LTRIM(RTRIM(@BoardName)), LTRIM(RTRIM(@RollNumber)), @MarksObtained, @MarksOutOf, NULLIF (LTRIM(RTRIM(@Grade)), N''), NULLIF (LTRIM(RTRIM(@OtherSubjects)), N''), @CertificateDate, @UserId);
    SET @ProvisionalCertificateId = CONVERT (INT, SCOPE_IDENTITY());
    IF @SubjectIds IS NOT NULL
       AND LTRIM(RTRIM(@SubjectIds)) <> N''
        BEGIN
            INSERT dbo.ProvisionalCertificateSubjects (ProvisionalCertificateId, SubjectId, SubjectName, DisplayOrder)
            SELECT @ProvisionalCertificateId,
                   SU.SubjectId,
                   SU.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY SU.SubjectName, SU.SubjectId)
            FROM   STRING_SPLIT (@SubjectIds, N',') AS SPL
                   INNER JOIN
                   dbo.Subjects AS SU
                   ON SU.SubjectId = TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value)))
            WHERE  TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value))) IS NOT NULL;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @ProvisionalCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @ProvisionalCertificateId AS ProvisionalCertificateId,
                                @AdmissionId AS AdmissionId,
                                @StudentName AS StudentName,
                                @ExamType AS ExamType,
                                @ExamYear AS ExamYear,
                                @RollNumber AS RollNumber,
                                @MarksObtained AS MarksObtained,
                                @MarksOutOf AS MarksOutOf,
                                @Grade AS Grade,
                                @CertificateDate AS CertificateDate,
                                JSON_QUERY((SELECT   SubjectId,
                                                     SubjectName,
                                                     DisplayOrder
                                            FROM     dbo.ProvisionalCertificateSubjects
                                            WHERE    ProvisionalCertificateId = @ProvisionalCertificateId
                                            ORDER BY DisplayOrder, SubjectId
                                            FOR      JSON PATH)) AS Subjects
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'PROVISIONAL_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'ProvisionalCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @ProvisionalCertificateId AS ProvisionalCertificateId,
           @AdmissionId AS AdmissionId;
END
