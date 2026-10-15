-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Calendar.Manage';
    IF NULLIF (LTRIM(RTRIM(@YearName)), N'') IS NULL
        THROW 56110, 'Academic year name is required.', 1;
    IF @EndDate < @StartDate
        THROW 56111, 'Academic year end date cannot be earlier than its start date.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.AcademicYears
               WHERE  YearName = LTRIM(RTRIM(@YearName))
                      AND AcademicYearId <> @AcademicYearId)
        THROW 56112, 'An academic year with this name already exists.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.AcademicYears
               WHERE  AcademicYearId <> @AcademicYearId
                      AND StartDate <= @EndDate
                      AND EndDate >= @StartDate)
        THROW 56115, 'These dates overlap an existing academic year. Academic years cannot cover the same calendar dates; adjust the start or end date. (Touching years are allowed: one may end the day before the next begins.)', 1;
    BEGIN TRANSACTION;
    IF @IsCurrent = 1
        UPDATE dbo.AcademicYears
        SET    IsCurrent = 0,
               UpdatedAt = SYSDATETIME()
        WHERE  AcademicYearId <> @AcademicYearId
               AND IsCurrent = 1;
    IF @AcademicYearId = 0
        BEGIN
            INSERT  dbo.AcademicYears (YearName, StartDate, EndDate, IsCurrent, Remarks)
            VALUES                   (LTRIM(RTRIM(@YearName)), @StartDate, @EndDate, @IsCurrent, NULLIF (LTRIM(RTRIM(@Remarks)), N''));
            SELECT CONVERT (INT, SCOPE_IDENTITY()) AS AcademicYearId;
        END
    ELSE
        BEGIN
            UPDATE dbo.AcademicYears
            SET    YearName  = LTRIM(RTRIM(@YearName)),
                   StartDate = @StartDate,
                   EndDate   = @EndDate,
                   IsCurrent = @IsCurrent,
                   Remarks   = NULLIF (LTRIM(RTRIM(@Remarks)), N''),
                   UpdatedAt = SYSDATETIME()
            WHERE  AcademicYearId = @AcademicYearId;
            IF @@ROWCOUNT = 0
                THROW 56113, 'The selected academic year was not found.', 1;
            SELECT @AcademicYearId AS AcademicYearId;
        END
    COMMIT TRANSACTION;
END
