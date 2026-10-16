# Level 3 results

Implementation and execution are in progress on `test/level3-cloud-acceptance`. No deployment readiness is claimed. This document will be replaced with the verified scenario totals and exact GitHub run links after the new checks complete.

The initial untouched-main prerequisite execution at `ff48ec0568fa83760aaa0a9d447b8fd5327c1178` passed all 45 existing Level 1 report stages, with owned-container cleanup confirmed. Those are Level 1 stages, not the 45 Level 3 scenarios.

The first expanded local run reproduced one test expectation issue: Microsoft.Data.SqlClient 7.1.1 reports requested command cancellation as SQL error 0 in this environment, rather than an `OperationCanceledException`. The test now recognizes that response only after its cancellation token was requested and still verifies rollback independently. No production behavior was changed. Backup/restore, CHECKDB, runtime compatibility refusals, concurrency, timeout recovery and killed-process journal retention executed successfully in that diagnostic run. Corrected complete-run evidence remains required.
