-- Automatic staff numbers and profile validation. Existing numbers are preserved.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStaff
    @UserId INT,
    @StaffNumber NVARCHAR(30) = NULL,
    @StaffName NVARCHAR(150) = NULL,
    @StaffId INT = 0,
    @FatherName NVARCHAR(150) = NULL,
    @DateOfBirth DATE = NULL,
    @Gender NVARCHAR(20) = NULL,
    @CNIC NVARCHAR(25) = NULL,
    @ContactNumber NVARCHAR(30) = NULL,
    @AlternateContactNumber NVARCHAR(30) = NULL,
    @Address NVARCHAR(500) = NULL,
    @Qualification NVARCHAR(250) = NULL,
    @Specialization NVARCHAR(250) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'Staff.Manage';
    SET @StaffName = NULLIF(LTRIM(RTRIM(@StaffName)), N'');
    SET @FatherName = NULLIF(LTRIM(RTRIM(@FatherName)), N'');
    IF @StaffName IS NULL THROW 56403, 'Staff name is required.', 1;
    IF @StaffName LIKE N'%[0-9]%' OR @FatherName LIKE N'%[0-9]%'
        THROW 56405, 'Staff and father names cannot contain numbers.', 1;
    IF @DateOfBirth > CONVERT(DATE, GETDATE())
        THROW 56406, 'Date of birth cannot be in the future.', 1;
    SET @Gender = NULLIF(LTRIM(RTRIM(@Gender)), N'');
    IF @Gender IS NOT NULL AND @Gender NOT IN (N'Male', N'Female', N'Other')
        THROW 56407, 'Choose a valid gender.', 1;
    SET @CNIC = NULLIF(LTRIM(RTRIM(@CNIC)), N'');
    IF @CNIC IS NOT NULL
    BEGIN
        IF NOT (LEN(@CNIC) = 13 AND @CNIC COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')
           AND NOT (LEN(@CNIC) = 15 AND @CNIC COLLATE Latin1_General_100_BIN2
                    LIKE N'[0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9]')
            THROW 56408, 'Enter a 13-digit CNIC, with or without hyphens.', 1;
        SET @CNIC = REPLACE(@CNIC, N'-', N'');
        SET @CNIC = STUFF(STUFF(@CNIC, 6, 0, N'-'), 14, 0, N'-');
    END;
    SET @ContactNumber = NULLIF(LTRIM(RTRIM(@ContactNumber)), N'');
    SET @AlternateContactNumber = NULLIF(LTRIM(RTRIM(@AlternateContactNumber)), N'');
    IF EXISTS
    (
        SELECT 1 FROM (VALUES (@ContactNumber), (@AlternateContactNumber)) phones(number)
        CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(number,
            N' ', N''), N'+', N''), N'-', N''), N'(', N''), N')', N'') digits) cleaned
        WHERE number IS NOT NULL AND
          (number COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9 +()-]%'
           OR digits COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%'
           OR CHARINDEX(N'+', SUBSTRING(number, 2, 30)) > 0
           OR LEN(digits) NOT BETWEEN 7 AND 15)
    ) THROW 56416, 'Contact numbers must contain 7 to 15 digits and valid phone punctuation.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        IF @StaffId = 0
        BEGIN
            -- Serializable range locks keep concurrent allocations distinct.
            DECLARE @HighestNumber BIGINT;
            SELECT @HighestNumber = COALESCE(MAX(TRY_CONVERT(BIGINT, StaffNumber)), 0)
            FROM dbo.Staff WITH (UPDLOCK, HOLDLOCK)
            WHERE StaffNumber COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%';
            IF @HighestNumber = 9223372036854775807
                THROW 56417, 'Staff numbering has reached its limit.', 1;
            SET @StaffNumber = CONVERT(NVARCHAR(30), @HighestNumber + 1);
            INSERT dbo.Staff
                (StaffNumber, StaffName, FatherName, DateOfBirth, Gender, CNIC,
                 ContactNumber, AlternateContactNumber, Address, Qualification, Specialization)
            VALUES
                (@StaffNumber, @StaffName, @FatherName, @DateOfBirth, @Gender, @CNIC,
                 @ContactNumber, @AlternateContactNumber, @Address, @Qualification, @Specialization);
            SET @StaffId = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            -- Identity numbers belong to the saved record; profile edits retain them.
            UPDATE dbo.Staff
            SET StaffName = @StaffName, FatherName = @FatherName, DateOfBirth = @DateOfBirth,
                Gender = @Gender, CNIC = @CNIC, ContactNumber = @ContactNumber,
                AlternateContactNumber = @AlternateContactNumber, Address = @Address,
                Qualification = @Qualification, Specialization = @Specialization,
                UpdatedAt = SYSDATETIME()
            WHERE StaffId = @StaffId;
            IF @@ROWCOUNT = 0 THROW 56401, 'The selected staff record was not found.', 1;
            SELECT @StaffNumber = StaffNumber FROM dbo.Staff WHERE StaffId = @StaffId;
        END;
        COMMIT TRANSACTION;
        SELECT @StaffId StaffId, @StaffNumber StaffNumber;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
