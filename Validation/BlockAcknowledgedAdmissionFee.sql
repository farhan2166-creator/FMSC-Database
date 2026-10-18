-- Prevent later Add Fee actions from charging an admission fee acknowledged at activation.
-- Does not change existing charges, assignments, acknowledgements, or receipts.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.StudentAdmissionFeeAcknowledgements', N'U') IS NULL
        THROW 51000, 'Admission fee acknowledgement schema is required.', 1;
    DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.AddStudentFeeAssignment', N'P'));
    IF @Definition IS NULL
        THROW 51000, 'AddStudentFeeAssignment definition is unavailable.', 1;
    IF CHARINDEX(N'FMSC_ACKNOWLEDGED_ADMISSION_FEE_GUARD', @Definition)=0
    BEGIN
        -- Patch the installed procedure without replacing its other billing rules.
        DECLARE @Anchor nvarchar(100)=N'BEGIN TRANSACTION;';
        IF (LEN(@Definition)-LEN(REPLACE(@Definition,@Anchor,N'')))/LEN(@Anchor)<>1
            THROW 51000, 'Unexpected AddStudentFeeAssignment transaction structure; review migration before applying.', 1;
        DECLARE @Guard nvarchar(max)=N'
    -- FMSC_ACKNOWLEDGED_ADMISSION_FEE_GUARD
    IF EXISTS(
        SELECT 1 FROM dbo.Enrollments E
        JOIN dbo.StudentAdmissionFeeAcknowledgements A WITH(UPDLOCK,HOLDLOCK)
            ON A.AdmissionId=E.AdmissionId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=@FeeTypeId
        WHERE E.EnrollmentId=@EnrollmentId AND F.FeeTypeCode=N''ADMISSION'')
        THROW 59971, ''This student''''s admission fee is recorded as already paid before the system. It cannot be charged again.'', 1;
';
        SET @Definition=REPLACE(@Definition,@Anchor,@Anchor+@Guard);
        DECLARE @ProcedureAt int=CHARINDEX(N'PROCEDURE',@Definition);
        IF @ProcedureAt=0 OR LTRIM(LEFT(@Definition,@ProcedureAt-1)) NOT IN(N'CREATE',N'ALTER',N'CREATE OR ALTER')
            THROW 51000, 'Unexpected procedure header; review migration before applying.', 1;
        SET @Definition=N'ALTER '+SUBSTRING(@Definition,@ProcedureAt,LEN(@Definition));
        EXEC sys.sp_executesql @Definition;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
