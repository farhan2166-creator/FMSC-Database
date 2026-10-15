-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.View';
    IF @AdmissionId IS NOT NULL
       AND @AdmissionId <= 0
        THROW 52901, 'A valid student is required.', 1;
    IF @AdmissionId IS NOT NULL
       AND NOT EXISTS (SELECT 1
                       FROM   dbo.Students
                       WHERE  AdmissionId = @AdmissionId)
        THROW 52902, 'The selected student was not found.', 1;
    SELECT   E.AdmissionId,
             COUNT(*) AS ActiveEnrollmentCount,
             MAX(E.EnrollmentId) AS AnyActiveEnrollmentId
    INTO     #ActiveEnrollmentCounts
    FROM     dbo.Enrollments AS E
    WHERE    E.Status = N'Active'
    GROUP BY E.AdmissionId;
    SELECT H.AdmissionId,
           H.NewStatus,
           H.EffectiveDate,
           H.CreatedAt,
           ROW_NUMBER() OVER (PARTITION BY H.AdmissionId ORDER BY H.CreatedAt DESC, H.StatusHistoryId DESC) AS rn
    INTO   #LatestStatusHistory
    FROM   dbo.StudentStatusHistory AS H;
    SELECT L.AdmissionId,
           L.LeaveRecordId,
           L.LeaveDate,
           L.CreatedAt,
           ROW_NUMBER() OVER (PARTITION BY L.AdmissionId ORDER BY L.CreatedAt DESC, L.LeaveRecordId DESC) AS rn
    INTO   #LatestLeaveCertificate
    FROM   dbo.LeaveCertificates AS L;
    SELECT SFA.AdmissionId,
           SFA.EnrollmentId,
           SFA.StudentFeeAssignmentId,
           E.Status AS EnrollmentStatus
    INTO   #OrphanedFeeAssignments
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           dbo.Enrollments AS E
           ON E.EnrollmentId = SFA.EnrollmentId
    WHERE  SFA.CancelledAt IS NULL
           AND E.Status IN (N'Left', N'Promoted');
    SELECT E.AdmissionId,
           E.EnrollmentId,
           E.EnrollmentDate,
           ISNULL(E.LeavingDate, '99991231') AS EffectiveEndDate
    INTO   #EnrollmentRanges
    FROM   dbo.Enrollments AS E;
    SELECT AdmissionId,
           EnrollmentId,
           EarliestEnrollmentDate
    INTO   #EarliestEnrollment
    FROM   (SELECT E.AdmissionId,
                   E.EnrollmentId,
                   E.EnrollmentDate AS EarliestEnrollmentDate,
                   ROW_NUMBER() OVER (PARTITION BY E.AdmissionId ORDER BY E.EnrollmentDate ASC, E.EnrollmentId ASC) AS rn
            FROM   dbo.Enrollments AS E) AS Earliest
    WHERE  rn = 1;
    SELECT   S.AdmissionId,
             S.StudentName,
             S.FatherName,
             S.Gender,
             S.RegistrationNumber,
             Findings.RuleCode,
             Findings.Severity,
             Findings.IssueDescription,
             Findings.SuggestedAction,
             Findings.RelatedEnrollmentId,
             Findings.SuggestedStatus,
             Findings.SuggestedWithdrawalDate,
             SYSDATETIME() AS DetectedAt
    FROM     dbo.Students AS S CROSS APPLY (SELECT N'STU_ACTIVE_NO_ENROLLMENT' AS RuleCode,
                                                   N'High' AS Severity,
                                                   N'Student is Active but has no Active enrollment row.' AS IssueDescription,
                                                   N'Confirm whether the student should be enrolled somewhere ' + N'(create/reopen an enrollment), or set Status to Left if ' + N'they have in fact left.' AS SuggestedAction,
                                                   CAST (NULL AS INT) AS RelatedEnrollmentId,
                                                   CAST (NULL AS NVARCHAR (30)) AS SuggestedStatus,
                                                   CAST (NULL AS DATE) AS SuggestedWithdrawalDate
                                            FROM   (VALUES (1)) AS Dummy(x)
                                            WHERE  S.Status = N'Active'
                                                   AND NOT EXISTS (SELECT 1
                                                                   FROM   #ActiveEnrollmentCounts AS AEC
                                                                   WHERE  AEC.AdmissionId = S.AdmissionId)
                                            UNION ALL
                                            SELECT N'STU_LEFT_ACTIVE_ENROLLMENT',
                                                   N'High',
                                                   N'Student is Left but still has an Active enrollment row.',
                                                   N'Close the Active enrollment (set to Left with a matching ' + N'LeavingDate), or set Status back to Active if the ' + N'student was in fact re-admitted.',
                                                   AEC.AnyActiveEnrollmentId,
                                                   N'Left',
                                                   S.WithdrawalDate
                                            FROM   #ActiveEnrollmentCounts AS AEC
                                            WHERE  AEC.AdmissionId = S.AdmissionId
                                                   AND S.Status = N'Left'
                                            UNION ALL
                                            SELECT N'MULTI_ACTIVE_ENROLLMENT',
                                                   N'High',
                                                   N'Student has more than one Active enrollment row at the ' + N'same time.',
                                                   N'Review the enrollment history and close all but the ' + N'correct current placement.',
                                                   CAST (NULL AS INT),
                                                   CAST (NULL AS NVARCHAR (30)),
                                                   CAST (NULL AS DATE)
                                            FROM   #ActiveEnrollmentCounts AS AEC
                                            WHERE  AEC.AdmissionId = S.AdmissionId
                                                   AND AEC.ActiveEnrollmentCount > 1
                                            UNION ALL
                                            SELECT N'LEAVECERT_STATUS_MISMATCH',
                                                   N'High',
                                                   N'A Leave Certificate exists (dated ' + CONVERT (NVARCHAR (10), LLC.LeaveDate, 23) + N') but Student Status is still Active.',
                                                   N'Set Status to Left with WithdrawalDate = ' + CONVERT (NVARCHAR (10), LLC.LeaveDate, 23) + N', and close any remaining Active enrollment to match.',
                                                   CAST (NULL AS INT),
                                                   N'Left',
                                                   LLC.LeaveDate
                                            FROM   #LatestLeaveCertificate AS LLC
                                            WHERE  LLC.AdmissionId = S.AdmissionId
                                                   AND LLC.rn = 1
                                                   AND S.Status = N'Active'
                                            UNION ALL
                                            SELECT N'WITHDRAWAL_DATE_MISMATCH',
                                                   N'Medium',
                                                   N'WithdrawalDate is set but Status is still Active.',
                                                   N'Set Status to Left (or clear WithdrawalDate if the ' + N'student did not actually leave).',
                                                   CAST (NULL AS INT),
                                                   N'Left',
                                                   S.WithdrawalDate
                                            FROM   (VALUES (1)) AS Dummy(x)
                                            WHERE  S.Status = N'Active'
                                                   AND S.WithdrawalDate IS NOT NULL
                                            UNION ALL
                                            SELECT N'WITHDRAWAL_DATE_MISMATCH',
                                                   N'Low',
                                                   N'Status is Left but no WithdrawalDate is recorded.',
                                                   N'Backfill WithdrawalDate, e.g. from the latest closed ' + N'enrollment''s LeavingDate or the Leave Certificate date.',
                                                   CAST (NULL AS INT),
                                                   CAST (NULL AS NVARCHAR (30)),
                                                   (SELECT MAX(E3.LeavingDate)
                                                    FROM   dbo.Enrollments AS E3
                                                    WHERE  E3.AdmissionId = S.AdmissionId
                                                           AND E3.Status = N'Left')
                                            FROM   (VALUES (1)) AS Dummy(x)
                                            WHERE  S.Status = N'Left'
                                                   AND S.WithdrawalDate IS NULL
                                            UNION ALL
                                            SELECT N'DANGLING_PROMOTION',
                                                   N'Medium',
                                                   N'An enrollment was marked Promoted on ' + CONVERT (NVARCHAR (10), EP.EnrollmentDate, 23) + N' but no later enrollment exists for this student.',
                                                   N'Create the follow-on enrollment for the destination ' + N'class/term, or correct this row''s Status if the ' + N'promotion did not actually happen.',
                                                   EP.EnrollmentId,
                                                   CAST (NULL AS NVARCHAR (30)),
                                                   CAST (NULL AS DATE)
                                            FROM   dbo.Enrollments AS EP
                                            WHERE  EP.AdmissionId = S.AdmissionId
                                                   AND EP.Status = N'Promoted'
                                                   AND S.Status = N'Active'
                                                   AND NOT EXISTS (SELECT 1
                                                                   FROM   dbo.Enrollments AS E2
                                                                   WHERE  E2.AdmissionId = EP.AdmissionId
                                                                          AND E2.EnrollmentId <> EP.EnrollmentId
                                                                          AND E2.EnrollmentDate > EP.EnrollmentDate)
                                            UNION ALL
                                            SELECT N'STATUS_HISTORY_MISMATCH',
                                                   N'Medium',
                                                   N'Current Status (' + S.Status + N') does not match the most recent StudentStatusHistory ' + N'entry (' + LSH.NewStatus + N', recorded ' + CONVERT (NVARCHAR (10), LSH.CreatedAt, 23) + N').',
                                                   N'Either the Status change bypassed history logging ' + N'(add a corrective history row), or Status was reverted ' + N'without a new history entry (add one to explain why).',
                                                   CAST (NULL AS INT),
                                                   LSH.NewStatus,
                                                   CAST (NULL AS DATE)
                                            FROM   #LatestStatusHistory AS LSH
                                            WHERE  LSH.AdmissionId = S.AdmissionId
                                                   AND LSH.rn = 1
                                                   AND LSH.NewStatus <> S.Status
                                            UNION ALL
                                            SELECT N'FEE_ASSIGNMENT_ORPHANED',
                                                   N'Medium',
                                                   N'A fee assignment is still active on enrollment #' + CONVERT (NVARCHAR (10), OFA.EnrollmentId) + N', which is now ' + OFA.EnrollmentStatus + N'.',
                                                   N'Deactivate the fee assignment from the fee screen for ' + N'that enrollment, or confirm it should still bill.',
                                                   OFA.EnrollmentId,
                                                   CAST (NULL AS NVARCHAR (30)),
                                                   CAST (NULL AS DATE)
                                            FROM   #OrphanedFeeAssignments AS OFA
                                            WHERE OFA.AdmissionId = S.AdmissionId AND 1=0
                                            UNION ALL
                                            SELECT N'ENROLLMENT_DATE_OVERLAP',
                                                   N'Medium',
                                                   N'Enrollment #' + CONVERT (NVARCHAR (10), R1.EnrollmentId) + N' and #' + CONVERT (NVARCHAR (10), R2.EnrollmentId) + N' have overlapping date ranges.',
                                                   N'Review both enrollment rows and correct the dates so ' + N'they no longer overlap.',
                                                   R1.EnrollmentId,
                                                   CAST (NULL AS NVARCHAR (30)),
                                                   CAST (NULL AS DATE)
                                            FROM   #EnrollmentRanges AS R1
                                                   INNER JOIN
                                                   #EnrollmentRanges AS R2
                                                   ON R2.AdmissionId = R1.AdmissionId
                                                      AND R2.EnrollmentId > R1.EnrollmentId
                                                      AND R1.EnrollmentDate <= R2.EffectiveEndDate
                                                      AND R2.EnrollmentDate <= R1.EffectiveEndDate
                                            WHERE  R1.AdmissionId = S.AdmissionId
                                            UNION ALL
                                            SELECT N'ENROLLMENT_BEFORE_ADMISSION',
                                                   N'Low',
                                                   N'Earliest enrollment date (' + CONVERT (NVARCHAR (10), EE.EarliestEnrollmentDate, 23) + N') is before the recorded admission date (' + CONVERT (NVARCHAR (10), S.AdmissionDate, 23) + N').',
                                                   N'Correct whichever date is wrong - admission date or the ' + N'enrollment date.',
                                                   EE.EnrollmentId,
                                                   CAST (NULL AS NVARCHAR (30)),
                                                   CAST (NULL AS DATE)
                                            FROM   #EarliestEnrollment AS EE
                                            WHERE  EE.AdmissionId = S.AdmissionId
                                                   AND EE.EarliestEnrollmentDate < S.AdmissionDate
    UNION ALL SELECT N'CERTIFICATE_ENROLLMENT_DATE_MISMATCH',N'High',
      N'Certificate '+L.CertificateNumber+N' leave date differs from enrollment #'+CONVERT(nvarchar(20),E.EnrollmentId)+N'.',
      N'Use Repair Lifecycle Dates to review and synchronize the enrollment, certificate and current withdrawal date together.',
      E.EnrollmentId,CAST(NULL AS nvarchar(30)),CAST(NULL AS date)
      FROM dbo.LeaveCertificates L JOIN dbo.Enrollments E ON E.EnrollmentId=L.EnrollmentId
      WHERE L.AdmissionId=S.AdmissionId AND (E.LeavingDate IS NULL OR L.LeaveDate<>E.LeavingDate)
    UNION ALL SELECT N'LEAVING_BEFORE_ENROLLMENT',N'High',
      N'Enrollment #'+CONVERT(nvarchar(20),E.EnrollmentId)+N' ends before it starts.',
      N'Review the full enrollment sequence using Repair Lifecycle Dates.',E.EnrollmentId,CAST(NULL AS nvarchar(30)),CAST(NULL AS date)
      FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND E.LeavingDate<E.EnrollmentDate
    UNION ALL SELECT N'WITHDRAWAL_ENROLLMENT_DATE_MISMATCH',N'High',
      N'Student withdrawal date differs from the latest closed enrollment.',
      N'Use Repair Lifecycle Dates to reconcile all related departure dates.',E.EnrollmentId,CAST(NULL AS nvarchar(30)),E.LeavingDate
      FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId AND S.Status=N'Left' AND E.Status=N'Left'
      AND E.EnrollmentId=(SELECT TOP(1) X.EnrollmentId FROM dbo.Enrollments X WHERE X.AdmissionId=S.AdmissionId AND X.Status=N'Left' ORDER BY X.EnrollmentDate DESC,X.EnrollmentId DESC)
      AND ISNULL(S.WithdrawalDate,'19000101')<>ISNULL(E.LeavingDate,'19000101')) AS Findings
    WHERE    @AdmissionId IS NULL
             OR S.AdmissionId = @AdmissionId
    ORDER BY S.AdmissionId, Findings.RuleCode;
    DROP TABLE #ActiveEnrollmentCounts;
    DROP TABLE #LatestStatusHistory;
    DROP TABLE #LatestLeaveCertificate;
    DROP TABLE #OrphanedFeeAssignments;
    DROP TABLE #EnrollmentRanges;
    DROP TABLE #EarliestEnrollment;
END
