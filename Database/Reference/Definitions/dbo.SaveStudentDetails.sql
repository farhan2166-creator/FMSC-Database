SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- SaveStudentDetails
CREATE   PROCEDURE dbo.SaveStudentDetails
@UserId INT, @AdmissionId INT, @StudentName NVARCHAR (500), @FatherName NVARCHAR (500)=NULL, @DateOfBirth DATE=NULL, @Gender NVARCHAR (20)=NULL, @BFormNumber NVARCHAR (50)=NULL, @Religion NVARCHAR (50)=NULL, @BloodGroup NVARCHAR (10)=NULL, @ContactNumber NVARCHAR (30)=NULL, @AlternateContactNumber NVARCHAR (30)=NULL, @Address NVARCHAR (500)=NULL, @PreviousSchool NVARCHAR (500)=NULL, @PlaceOfBirth NVARCHAR (500)=NULL, @FatherOccupation NVARCHAR (250)=NULL, @Disability NVARCHAR (3)=N'No', @UsesTransport BIT=NULL, @BiseEnrolmentNumber NVARCHAR (50)=NULL, @CasteTribe NVARCHAR (100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Students
                   WHERE  AdmissionId = @AdmissionId)
        THROW 51701, 'The selected student was not found.', 1;
    IF NULLIF (LTRIM(RTRIM(@StudentName)), N'') IS NULL
        THROW 51702, 'Student name is required.', 1;
    DECLARE @AdmissionDate AS DATE;
    SELECT @AdmissionDate = AdmissionDate
    FROM   dbo.Students
    WHERE  AdmissionId = @AdmissionId;
    IF @DateOfBirth IS NOT NULL
       AND @DateOfBirth > @AdmissionDate
        THROW 51703, 'Date of birth cannot be later than the admission date.', 1;
    IF @Disability IS NULL
       OR @Disability NOT IN (N'Yes', N'No')
        THROW 51704, 'Disability must be Yes or No.', 1;
    DECLARE @OldStudentName AS NVARCHAR (500), @OldFatherName AS NVARCHAR (500), @OldDateOfBirth AS DATE, @OldGender AS NVARCHAR (20), @OldBFormNumber AS NVARCHAR (50), @OldReligion AS NVARCHAR (50), @OldBloodGroup AS NVARCHAR (10), @OldContactNumber AS NVARCHAR (30), @OldAlternateContactNumber AS NVARCHAR (30), @OldAddress AS NVARCHAR (500), @OldPreviousSchool AS NVARCHAR (500), @OldPlaceOfBirth AS NVARCHAR (500), @OldFatherOccupation AS NVARCHAR (250), @OldDisability AS NVARCHAR (3), @OldBiseEnrolmentNumber AS NVARCHAR (50), @OldCasteTribe AS NVARCHAR (100);
    SELECT @OldStudentName = StudentName,
           @OldFatherName = FatherName,
           @OldDateOfBirth = DateOfBirth,
           @OldGender = Gender,
           @OldBFormNumber = BFormNumber,
           @OldReligion = Religion,
           @OldBloodGroup = BloodGroup,
           @OldContactNumber = ContactNumber,
           @OldAlternateContactNumber = AlternateContactNumber,
           @OldAddress = Address,
           @OldPreviousSchool = PreviousSchool,
           @OldPlaceOfBirth = PlaceOfBirth,
           @OldFatherOccupation = FatherOccupation,
           @OldDisability = Disability,
           @OldBiseEnrolmentNumber = BiseEnrolmentNumber,
           @OldCasteTribe = CasteTribe
    FROM   dbo.Students
    WHERE  AdmissionId = @AdmissionId;
    DECLARE @NewStudentName AS NVARCHAR (500), @NewFatherName AS NVARCHAR (500), @NewBFormNumber AS NVARCHAR (50), @NewContactNumber AS NVARCHAR (30), @NewAlternateContactNumber AS NVARCHAR (30), @NewAddress AS NVARCHAR (500), @NewPreviousSchool AS NVARCHAR (500), @NewPlaceOfBirth AS NVARCHAR (500), @NewFatherOccupation AS NVARCHAR (250), @NewBiseEnrolmentNumber AS NVARCHAR (50), @NewCasteTribe AS NVARCHAR (100);
    SET @NewStudentName = LTRIM(RTRIM(@StudentName));
    SET @NewFatherName = NULLIF (LTRIM(RTRIM(@FatherName)), N'');
    SET @NewBFormNumber = NULLIF (LTRIM(RTRIM(@BFormNumber)), N'');
    SET @NewContactNumber = NULLIF (LTRIM(RTRIM(@ContactNumber)), N'');
    SET @NewAlternateContactNumber = NULLIF (LTRIM(RTRIM(@AlternateContactNumber)), N'');
    SET @NewAddress = NULLIF (LTRIM(RTRIM(@Address)), N'');
    SET @NewPreviousSchool = NULLIF (LTRIM(RTRIM(@PreviousSchool)), N'');
    SET @NewPlaceOfBirth = NULLIF (LTRIM(RTRIM(@PlaceOfBirth)), N'');
    SET @NewFatherOccupation = NULLIF (LTRIM(RTRIM(@FatherOccupation)), N'');
    SET @NewBiseEnrolmentNumber = NULLIF (LTRIM(RTRIM(@BiseEnrolmentNumber)), N'');
    SET @NewCasteTribe = NULLIF (LTRIM(RTRIM(@CasteTribe)), N'');
    DECLARE @StudentChanged AS BIT = 0;
    IF ISNULL(@OldStudentName, N'') <> ISNULL(@NewStudentName, N'')
       OR ISNULL(@OldFatherName, N'') <> ISNULL(@NewFatherName, N'')
       OR ISNULL(@OldDateOfBirth, '19000101') <> ISNULL(@DateOfBirth, '19000101')
       OR ISNULL(@OldGender, N'') <> ISNULL(@Gender, N'')
       OR ISNULL(@OldBFormNumber, N'') <> ISNULL(@NewBFormNumber, N'')
       OR ISNULL(@OldReligion, N'') <> ISNULL(@Religion, N'')
       OR ISNULL(@OldBloodGroup, N'') <> ISNULL(@BloodGroup, N'')
       OR ISNULL(@OldContactNumber, N'') <> ISNULL(@NewContactNumber, N'')
       OR ISNULL(@OldAlternateContactNumber, N'') <> ISNULL(@NewAlternateContactNumber, N'')
       OR ISNULL(@OldAddress, N'') <> ISNULL(@NewAddress, N'')
       OR ISNULL(@OldPreviousSchool, N'') <> ISNULL(@NewPreviousSchool, N'')
       OR ISNULL(@OldPlaceOfBirth, N'') <> ISNULL(@NewPlaceOfBirth, N'')
       OR ISNULL(@OldFatherOccupation, N'') <> ISNULL(@NewFatherOccupation, N'')
       OR ISNULL(@OldDisability, N'') <> ISNULL(@Disability, N'')
       OR ISNULL(@OldBiseEnrolmentNumber, N'') <> ISNULL(@NewBiseEnrolmentNumber, N'')
       OR ISNULL(@OldCasteTribe, N'') <> ISNULL(@NewCasteTribe, N'')
        BEGIN
            SET @StudentChanged = 1;
        END
    DECLARE @EnrollmentId AS INT = NULL, @OldUsesTransport AS BIT = NULL;
    SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                     @OldUsesTransport = E.UsesTransport
    FROM     dbo.Enrollments AS E
    WHERE    E.AdmissionId = @AdmissionId
    ORDER BY CASE WHEN E.Status = N'Active' THEN 0 ELSE 1 END, E.EnrollmentDate DESC, E.EnrollmentId DESC;
    DECLARE @TransportChanged AS BIT = 0;
    IF @UsesTransport IS NOT NULL
       AND @EnrollmentId IS NOT NULL
       AND (@OldUsesTransport IS NULL
            OR @OldUsesTransport <> @UsesTransport)
        BEGIN
            SET @TransportChanged = 1;
        END
    IF @StudentChanged = 0
       AND @TransportChanged = 0
        BEGIN
            SELECT @AdmissionId AS AdmissionId;
            RETURN;
        END
    BEGIN TRANSACTION;
    IF @StudentChanged = 1
        BEGIN
            UPDATE dbo.Students
            SET    StudentName            = @NewStudentName,
                   FatherName             = @NewFatherName,
                   DateOfBirth            = @DateOfBirth,
                   Gender                 = @Gender,
                   BFormNumber            = @NewBFormNumber,
                   Religion               = @Religion,
                   BloodGroup             = @BloodGroup,
                   ContactNumber          = @NewContactNumber,
                   AlternateContactNumber = @NewAlternateContactNumber,
                   Address                = @NewAddress,
                   PreviousSchool         = @NewPreviousSchool,
                   PlaceOfBirth           = @NewPlaceOfBirth,
                   FatherOccupation       = @NewFatherOccupation,
                   Disability             = @Disability,
                   BiseEnrolmentNumber    = @NewBiseEnrolmentNumber,
                   CasteTribe             = @NewCasteTribe,
                   UpdatedAt              = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId;
            IF @@ROWCOUNT = 0
                THROW 51705, 'The selected student record was not found.', 1;
        END
    IF @TransportChanged = 1
        BEGIN
            UPDATE dbo.Enrollments
            SET    UsesTransport = @UsesTransport,
                   UpdatedAt     = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId;
            IF @@ROWCOUNT = 0
                THROW 51707, 'The student enrollment could not be updated.', 1;
        END
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'STUDENT_UPDATE', CONVERT (NVARCHAR (200), @AdmissionId));
    IF @StudentChanged = 1
        BEGIN
            DECLARE @AuditOldValues AS NVARCHAR (MAX);
            DECLARE @AuditNewValues AS NVARCHAR (MAX);
            DECLARE @AuditRecordId AS NVARCHAR (200);
            SELECT @AuditOldValues = (SELECT @OldStudentName AS StudentName,
                                             @OldFatherName AS FatherName,
                                             @OldDateOfBirth AS DateOfBirth,
                                             @OldGender AS Gender,
                                             @OldBFormNumber AS BFormNumber,
                                             @OldReligion AS Religion,
                                             @OldBloodGroup AS BloodGroup,
                                             @OldContactNumber AS ContactNumber,
                                             @OldAlternateContactNumber AS AlternateContactNumber,
                                             @OldAddress AS Address,
                                             @OldPreviousSchool AS PreviousSchool,
                                             @OldPlaceOfBirth AS PlaceOfBirth,
                                             @OldFatherOccupation AS FatherOccupation,
                                             @OldDisability AS Disability,
                                             @OldBiseEnrolmentNumber AS BiseEnrolmentNumber,
                                             @OldCasteTribe AS CasteTribe
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SELECT @AuditNewValues = (SELECT @AdmissionId AS AdmissionId,
                                             @NewStudentName AS StudentName,
                                             @NewFatherName AS FatherName,
                                             @DateOfBirth AS DateOfBirth,
                                             @Gender AS Gender,
                                             @NewBFormNumber AS BFormNumber,
                                             @Religion AS Religion,
                                             @BloodGroup AS BloodGroup,
                                             @NewContactNumber AS ContactNumber,
                                             @NewAlternateContactNumber AS AlternateContactNumber,
                                             @NewAddress AS Address,
                                             @NewPreviousSchool AS PreviousSchool,
                                             @NewPlaceOfBirth AS PlaceOfBirth,
                                             @NewFatherOccupation AS FatherOccupation,
                                             @Disability AS Disability,
                                             @NewBiseEnrolmentNumber AS BiseEnrolmentNumber,
                                             @NewCasteTribe AS CasteTribe
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SET @AuditRecordId = CONVERT (NVARCHAR (200), @AdmissionId);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AuditRecordId, @Action = N'UPDATE', @OldValues = @AuditOldValues, @NewValues = @AuditNewValues;
        END
    IF @TransportChanged = 1
        BEGIN
            DECLARE @TransportOldValues AS NVARCHAR (MAX), @TransportNewValues AS NVARCHAR (MAX), @TransportAuditRecordId AS NVARCHAR (200);
            SELECT @TransportOldValues = (SELECT @EnrollmentId AS EnrollmentId,
                                                 @AdmissionId AS AdmissionId,
                                                 @OldUsesTransport AS UsesTransport
                                          FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SELECT @TransportNewValues = (SELECT @EnrollmentId AS EnrollmentId,
                                                 @AdmissionId AS AdmissionId,
                                                 @UsesTransport AS UsesTransport
                                          FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            SET @TransportAuditRecordId = CONVERT (NVARCHAR (200), @EnrollmentId);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @TransportAuditRecordId, @Action = N'UPDATE', @OldValues = @TransportOldValues, @NewValues = @TransportNewValues;
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @AdmissionId AS AdmissionId;
END



GO
