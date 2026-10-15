-- Module body only. See companion XML for signatures and relationships.


BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;EXEC dbo.RequirePermission @UserId,N'Students.Admit';
 BEGIN TRAN;
 DECLARE @Date date,@Enrollment int,@Old nvarchar(max),@New nvarchar(max),@Operation uniqueidentifier=NEWID(),@Key nvarchar(200)=CONVERT(nvarchar(200),@AdmissionId);
 SELECT @Date=AdmissionDate FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Pending' AND QuickReference IS NOT NULL AND AdmissionNumber IS NULL;
 IF @Date IS NULL THROW 59701,'Select a pending registration. It may already have been completed or cancelled.',1;
 IF EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionId=@AdmissionId AND (NULLIF(LTRIM(RTRIM(StudentName)),N'') IS NULL OR
  NULLIF(LTRIM(RTRIM(FatherName)),N'') IS NULL OR Gender IS NULL OR
  NULLIF(LTRIM(RTRIM(Address)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(PlaceOfBirth)),N'') IS NULL))
  THROW 59702,'Complete and save the required details in Quick Registration review before confirming admission.',1;
 SELECT @Enrollment=EnrollmentId FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId AND Status=N'Pending';
 IF @Enrollment IS NULL THROW 59703,'The pending academic placement could not be found.',1;
 SELECT @Old=(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.ReserveAdmissionNumber @AdmissionNumber OUTPUT;
 DECLARE @Registration nvarchar(40);
 EXEC dbo.GetNextRegistrationNumber @RegistrationDate=@Date,@RegistrationNumber=@Registration OUTPUT;
 UPDATE dbo.Students SET AdmissionNumber=@AdmissionNumber,RegistrationNumber=@Registration,Status=N'Active',UpdatedAt=SYSDATETIME() WHERE AdmissionId=@AdmissionId;
 UPDATE dbo.Enrollments SET Status=N'Active',PlacementConfirmedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME() WHERE EnrollmentId=@Enrollment;
 -- Same placement and fee assignments: opening bills already issued under the registration remain untouched.
 INSERT dbo.EnrollmentHistory(EnrollmentId,AdmissionId,ActionType,ActionDate,NewClassSectionId,OldStatus,NewStatus,Remarks,UserId)
 SELECT EnrollmentId,AdmissionId,N'ADMISSION',@Date,ClassSectionId,N'Pending',N'Active',N'Quick registration completed',@UserId FROM dbo.Enrollments WHERE EnrollmentId=@Enrollment;
 INSERT dbo.StudentStatusHistory(AdmissionId,OldStatus,NewStatus,Reason,EffectiveDate,CreatedBy) VALUES(@AdmissionId,N'Pending',N'Active',N'Admission completed',@Date,@UserId);
 INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote) VALUES(@Operation,@UserId,N'COMPLETE_ADMISSION',@Key);
 SELECT @New=(SELECT * FROM dbo.Students WHERE AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 EXEC dbo.WriteAuditLog @Operation,@UserId,N'Students',@Key,N'UPDATE',@Old,@New;
 COMMIT;
 SELECT @AdmissionNumber AdmissionNumber;
END;
