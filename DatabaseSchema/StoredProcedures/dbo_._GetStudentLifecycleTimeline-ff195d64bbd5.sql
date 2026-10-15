-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.View';

    IF @AdmissionId <= 0
        THROW 57201, 'A valid student is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.Students WHERE AdmissionId = @AdmissionId
    )
        THROW 57202, 'The selected student was not found.', 1;

    SELECT
        EventDate,
        EventType,
        Description,
        RecordedByUserId,
        CreatedAt
    FROM
    (
        SELECT
            EH.ActionDate AS EventDate,
            N'Enrollment: ' + EH.ActionType AS EventType,
            ISNULL(OSC.ClassName + N' ' + OSEC.SectionName, N'—')
                + N' → '
                + ISNULL(NSC.ClassName + N' ' + NSEC.SectionName, N'—')
                + CASE
                      WHEN EH.OldStatus IS NOT NULL
                           AND EH.NewStatus IS NOT NULL
                           AND EH.OldStatus <> EH.NewStatus
                          THEN N' (' + EH.OldStatus + N' → ' + EH.NewStatus + N')'
                      WHEN EH.NewStatus IS NOT NULL
                          THEN N' (' + EH.NewStatus + N')'
                      ELSE N''
                  END
                + ISNULL(N' — ' + EH.Remarks, N'') AS Description,
            EH.UserId AS RecordedByUserId,
            EH.CreatedAt
        FROM dbo.EnrollmentHistory EH
        LEFT JOIN dbo.CampusClassSections OCCS ON OCCS.ClassSectionId = EH.OldClassSectionId
        LEFT JOIN dbo.Classes OSC ON OSC.ClassId = OCCS.ClassId
        LEFT JOIN dbo.Sections OSEC ON OSEC.SectionId = OCCS.SectionId
        LEFT JOIN dbo.CampusClassSections NCCS ON NCCS.ClassSectionId = EH.NewClassSectionId
        LEFT JOIN dbo.Classes NSC ON NSC.ClassId = NCCS.ClassId
        LEFT JOIN dbo.Sections NSEC ON NSEC.SectionId = NCCS.SectionId
        WHERE EH.AdmissionId = @AdmissionId

        UNION ALL

        SELECT
            SH.EffectiveDate AS EventDate,
            N'Status change' AS EventType,
            SH.OldStatus + N' → ' + SH.NewStatus
                + ISNULL(N' (' + SH.Reason + N')', N'')
                + ISNULL(N' — ' + SH.Remarks, N'') AS Description,
            SH.CreatedBy AS RecordedByUserId,
            SH.CreatedAt
        FROM dbo.StudentStatusHistory SH
        WHERE SH.AdmissionId = @AdmissionId

        UNION ALL

        SELECT
            LC.LeaveDate AS EventDate,
            N'Leave certificate' AS EventType,
            N'Leave certificate created for class ' + ISNULL(LC.ClassStudied, N'—')
                + ISNULL(N' — ' + LC.Remarks, N'') AS Description,
            LC.CreatedByUserId AS RecordedByUserId,
            LC.CreatedAt
        FROM dbo.LeaveCertificates LC
        WHERE LC.AdmissionId = @AdmissionId
    ) AS Timeline
    ORDER BY EventDate DESC, CreatedAt DESC;
END;
