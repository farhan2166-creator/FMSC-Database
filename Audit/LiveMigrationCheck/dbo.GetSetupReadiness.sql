
-- GetSetupReadiness
CREATE   PROCEDURE dbo.GetSetupReadiness
@UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Setup AS BIT = 0, @Fees AS BIT = 0, @Calendar AS BIT = 0;
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
        SET @Setup = 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 50003
            THROW;
    END CATCH
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
        SET @Fees = 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 50003
            THROW;
    END CATCH
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'Calendar.Manage';
        SET @Calendar = 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 50003
            THROW;
    END CATCH
    IF @Setup = 0
       AND @Fees = 0
       AND @Calendar = 0
        THROW 50003, 'Setup access is required.', 1;
    DECLARE @Issues TABLE (
        Area    NVARCHAR (50) ,
        Issue   NVARCHAR (500),
        PageKey NVARCHAR (40) );
    IF @Setup = 1
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.SchoolProfile
                           WHERE  NULLIF (LTRIM(RTRIM(SchoolName)), N'') IS NOT NULL
                                  AND NULLIF (LTRIM(RTRIM(AddressLine)), N'') IS NOT NULL
                                  AND NULLIF (LTRIM(RTRIM(ContactNumber)), N'') IS NOT NULL)
                INSERT  @Issues
                VALUES (N'School profile', N'Complete the school name, address and contact number used on documents.', N'Profile');
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.Campuses)
                INSERT  @Issues
                VALUES (N'Campuses', N'Add an campus.', N'Campuses');
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.CampusClassSections AS CCS
                                  INNER JOIN
                                  dbo.Campuses AS C
                                  ON C.CampusId = CCS.CampusId
                                  INNER JOIN
                                  dbo.Classes AS CL
                                  ON CL.ClassId = CCS.ClassId
                                  INNER JOIN
                                  dbo.Sections AS S
                                  ON S.SectionId = CCS.SectionId)
                INSERT  @Issues
                VALUES (N'Placement', N'Assign classes and sections to an campus.', N'Assignments');
        END
    IF @Calendar = 1
       AND (SELECT COUNT(*)
            FROM   dbo.AcademicYears
            WHERE  IsCurrent = 1
                   AND CONVERT (DATE, GETDATE()) BETWEEN StartDate AND EndDate) <> 1
        INSERT  @Issues
        VALUES (N'Academic year', N'Select one current academic year covering today.', N'Years');
    IF @Fees = 1
        BEGIN
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.FeeTypes
                           WHERE  IsMandatory = 1
                                  AND IsTransportLinked = 0)
                INSERT  @Issues
                VALUES (N'Fee types', N'No mandatory non-transport fee type is configured. Confirm this is intentional.', N'Fees');
            INSERT @Issues
            SELECT DISTINCT N'Fee rates',
                            CONCAT(C.CampusName, N' / ', CL.ClassName, N': configure ', FT.FeeTypeName, N' (zero is allowed for an intentionally free fee).'),
                            N'Fees'
            FROM   dbo.CampusClassSections AS CCS
                   INNER JOIN
                   dbo.Campuses AS C
                   ON C.CampusId = CCS.CampusId
                   INNER JOIN
                   dbo.Classes AS CL
                   ON CL.ClassId = CCS.ClassId
                   INNER JOIN
                   dbo.Sections AS S
                   ON S.SectionId = CCS.SectionId CROSS JOIN dbo.FeeTypes AS FT
            WHERE  FT.IsMandatory = 1
                   AND FT.IsTransportLinked = 0
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.FeeConfigurations AS FC
                                   WHERE  FC.CampusId = CCS.CampusId
                                          AND FC.ClassId = CCS.ClassId
                                          AND FC.FeeTypeId = FT.FeeTypeId
                                          AND FC.Amount >= 0);
        END
    SELECT   Area,
             Issue,
             PageKey
    FROM     @Issues
    ORDER BY Area, Issue;
END



