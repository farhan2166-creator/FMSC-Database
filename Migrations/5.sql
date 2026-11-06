-- Store the communication choice entered on the admission form.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF ISNULL((SELECT TOP (1) TRY_CONVERT(int, VersionNumber)
           FROM dbo.DatabaseVersion ORDER BY VersionId DESC), 0) <> 4
    THROW 51000, 'Guardian communication preferences require schema 4.', 1;

CREATE TABLE dbo.StudentGuardianCommunication (
    AdmissionId int NOT NULL PRIMARY KEY REFERENCES dbo.Students(AdmissionId) ON DELETE CASCADE,
    Email nvarchar(254) NULL,
    Phone nvarchar(30) NULL,
    WhatsAppConsent bit NOT NULL,
    RecordedUtc datetime2 NOT NULL,
    RecordedBy int NOT NULL REFERENCES dbo.Users(UserId),
    CONSTRAINT CK_StudentGuardianCommunication_ConsentPhone
        CHECK (WhatsAppConsent = 0 OR Phone IS NOT NULL)
);

INSERT dbo.DatabaseVersion(VersionNumber, AppliedOn, AppliedBy, Notes)
VALUES (N'5', SYSDATETIME(), SUSER_SNAME(), N'Store guardian email and WhatsApp preference from admission');
