SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   8. dbo.GetCharacterCertificateFieldSuggestions

      Only Remarks recurs in practice (Roll Number is unique per
      student/certificate, so it gets no suggestion list).
   ===================================================================== */

CREATE   PROCEDURE dbo.GetCharacterCertificateFieldSuggestions
    @UserId INT,
    @Field NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    IF @Field = N'Remarks'
        SELECT DISTINCT TOP (500) Remarks AS Value
        FROM dbo.CharacterCertificates
        WHERE Remarks IS NOT NULL
          AND LTRIM(RTRIM(Remarks)) <> N''
        ORDER BY Remarks;

    ELSE
        THROW 51600, 'Unknown suggestion field.', 1;
END;

GO
