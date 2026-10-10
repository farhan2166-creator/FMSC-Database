SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   PROCEDURE dbo.GetNextAdmissionNumber @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Students.Admit';
    SELECT CONVERT(VARCHAR(40), LastNumber + 1) AS AdmissionNumber
    FROM dbo.AdmissionNumberCounter WHERE CounterId=1;
END;

GO
