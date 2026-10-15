-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.Leave';

    IF @Field = N'AcademicRecord'
        SELECT DISTINCT TOP (500) AcademicRecord AS Value
        FROM dbo.LeaveCertificates
        WHERE AcademicRecord IS NOT NULL
          AND LTRIM(RTRIM(AcademicRecord)) <> N''
        ORDER BY AcademicRecord;

    ELSE IF @Field = N'AttendanceRecord'
        SELECT DISTINCT TOP (500) AttendanceRecord AS Value
        FROM dbo.LeaveCertificates
        WHERE AttendanceRecord IS NOT NULL
          AND LTRIM(RTRIM(AttendanceRecord)) <> N''
        ORDER BY AttendanceRecord;

    ELSE IF @Field = N'Remarks'
        SELECT DISTINCT TOP (500) Remarks AS Value
        FROM dbo.LeaveCertificates
        WHERE Remarks IS NOT NULL
          AND LTRIM(RTRIM(Remarks)) <> N''
        ORDER BY Remarks;

    ELSE
        THROW 51500, 'Unknown suggestion field.', 1;
END;
