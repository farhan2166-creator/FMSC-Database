# Evidence boundaries

`report.json` and `summary.md` are the full 45-scenario Level 3 matrix generated from actual execution metadata. `sql/` is the complete sanitized successful Codex Cloud SQL report at commit `84d019c`, with a clean tree when execution began. Source code and baseline fixtures were unchanged during that run; later commits add evidence/documentation only.

`install/report.json` is explicitly **normalized GitHub run/job metadata**, not an installer-produced artifact: no runner was assigned and the harness never started. `workflow-runs.json`, `desktop-run.json` and `security-run.json` preserve those actual failed scheduling attempts. The owner confirmed the GitHub repository Actions limit is exhausted. No Windows/installer or GitHub scanner pass is claimed. The private installer artifact list was empty.

`supplementary-checks.json` records independently executed Linux builds, security, PowerShell contracts and reporting/fixture guard checks. These are supplementary evidence and do not turn local acceptance rows into passes.

`cancellation-before.json` is a sanitized extraction from the first **uncommitted** diagnostic implementation, identified by base commit and dirty state. SqlClient reported requested cancellation as SQL error 0. The corrected committed execution in `sql/report.json` verifies cancellation plus independent rollback. No production fix was needed.

`intentional-provision-failure.json` preserves the expected nonzero diagnostic result and its successful owned cleanup. It is not a failing application test hidden as a pass. `comparison-baseline.json` records the earlier successful workload run and its limitations. Timings are bounded cloud baselines, not production performance guarantees.

No student rows, message bodies, phone numbers, SQL credentials, raw logs, school exports, MDF/LDF or backups are included. Only the pinned data-free SQL fixture and fictional records inside owned disposable environments were used. No automatic DACPAC verification or approval was added.
