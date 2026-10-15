-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @ProvisionalCertificateId <= 0
        THROW 58820, 'A valid certificate is required.', 1;
    IF @ExamType NOT IN (N'SSC', N'HSSC1', N'HSSC2')
        THROW 58821, 'A valid exam type is required.', 1;
    IF @ExamYear IS NULL
       OR @ExamYear < 1990
       OR @ExamYear > YEAR(SYSDATETIME()) + 1
        THROW 58822, 'A valid exam year is required.', 1;
    IF @BoardName IS NULL
       OR LTRIM(RTRIM(@BoardName)) = N''
        THROW 58823, 'Board name is required.', 1;
    IF @RollNumber IS NULL
       OR LTRIM(RTRIM(@RollNumber)) = N''
        THROW 58824, 'Roll number is required.', 1;
    IF @MarksObtained IS NULL
       OR @MarksObtained < 0
        THROW 58825, 'Marks obtained is required.', 1;
    IF @MarksOutOf IS NULL
       OR @MarksOutOf <= 0
        THROW 58826, 'Marks out of is required.', 1;
    IF @MarksObtained > @MarksOutOf
        THROW 58827, 'Marks obtained cannot exceed marks out of.', 1;
    IF @CertificateDate IS NULL
        THROW 58828, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58829, 'Certificate date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT;
    DECLARE @OldValues AS NVARCHAR (MAX);
    SELECT @AdmissionId = PC.AdmissionId,
           @OldValues = (SELECT PC.ProvisionalCertificateId,
                                PC.AdmissionId,
                                PC.ExamType,
                                PC.ExamYear,
                                PC.BoardName,
                                PC.RollNumber,
                                PC.MarksObtained,
                                PC.MarksOutOf,
                                PC.Grade,
                                PC.OtherSubjects,
                                PC.CertificateDate,
                                JSON_QUERY((SELECT   SubjectId,
                                                     SubjectName,
                                                     DisplayOrder
                                            FROM     dbo.ProvisionalCertificateSubjects
                                            WHERE    ProvisionalCertificateId = @ProvisionalCertificateId
                                            ORDER BY DisplayOrder, SubjectId
                                            FOR      JSON PATH)) AS Subjects
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
    FROM   dbo.ProvisionalCertificates AS PC WITH (UPDLOCK, HOLDLOCK)
    WHERE  PC.ProvisionalCertificateId = @ProvisionalCertificateId;
    IF @AdmissionId IS NULL
        THROW 58830, 'The certificate was not found.', 1;
    UPDATE dbo.ProvisionalCertificates
    SET    ExamType        = @ExamType,
           ExamYear        = @ExamYear,
           BoardName       = LTRIM(RTRIM(@BoardName)),
           RollNumber      = LTRIM(RTRIM(@RollNumber)),
           MarksObtained   = @MarksObtained,
           MarksOutOf      = @MarksOutOf,
           Grade           = NULLIF (LTRIM(RTRIM(@Grade)), N''),
           OtherSubjects   = NULLIF (LTRIM(RTRIM(@OtherSubjects)), N''),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    DECLARE @SavedSubjectOrder TABLE (
        SubjectId    INT,
        DisplayOrder INT);
    INSERT @SavedSubjectOrder
    SELECT SubjectId,
           DisplayOrder
    FROM   dbo.ProvisionalCertificateSubjects
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    DELETE dbo.ProvisionalCertificateSubjects
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    IF @SubjectIds IS NOT NULL
       AND LTRIM(RTRIM(@SubjectIds)) <> N''
        BEGIN
            INSERT dbo.ProvisionalCertificateSubjects (ProvisionalCertificateId, SubjectId, SubjectName, DisplayOrder)
            SELECT @ProvisionalCertificateId,
                   SU.SubjectId,
                   SU.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY COALESCE (SO.DisplayOrder, 2147483647), SU.SubjectName, SU.SubjectId)
            FROM   STRING_SPLIT (@SubjectIds, N',') AS SPL
                   INNER JOIN
                   dbo.Subjects AS SU
                   ON SU.SubjectId = TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value)))
                   LEFT OUTER JOIN
                   @SavedSubjectOrder AS SO
                   ON SO.SubjectId = SU.SubjectId
            WHERE  TRY_CONVERT (INT, LTRIM(RTRIM(SPL.value))) IS NOT NULL;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @ProvisionalCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT ProvisionalCertificateId,
                                AdmissionId,
                                ExamType,
                                ExamYear,
                                BoardName,
                                RollNumber,
                                MarksObtained,
                                MarksOutOf,
                                Grade,
                                OtherSubjects,
                                CertificateDate,
                                JSON_QUERY((SELECT   SubjectId,
                                                     SubjectName,
                                                     DisplayOrder
                                            FROM     dbo.ProvisionalCertificateSubjects
                                            WHERE    ProvisionalCertificateId = @ProvisionalCertificateId
                                            ORDER BY DisplayOrder, SubjectId
                                            FOR      JSON PATH)) AS Subjects
                         FROM   dbo.ProvisionalCertificates
                         WHERE  ProvisionalCertificateId = @ProvisionalCertificateId
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'PROVISIONAL_CERTIFICATE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'ProvisionalCertificates', @RecordId = @RecordId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @ProvisionalCertificateId AS ProvisionalCertificateId,
           @AdmissionId AS AdmissionId;
END
