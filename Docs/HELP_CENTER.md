# FMSC_SMS — Help & FAQ Center

The **Help & FAQ** dashboard entry opens a read-only, searchable operator guide. It does not connect to SQL or require additional permissions. It is intended to explain business workflows, not to grant access to them.

## Contents

The initial catalog has **77 question-and-answer pairs** across **15 categories**: Getting Started; School Setup; Admissions; Student Search; Students & Families; Promotion; Fees & Rates; Billing; Collection; Reports; Staff; Permissions; Database & Recovery; Communication; and Administration.

Each entry includes the exact operator question, a plain-English answer, synonyms/search keywords, an indication of whether the decision is a protected database/application rule or a user-configurable choice, and the menu/workspace where the operator should continue.

Search performs case-insensitive AND matching on space-separated terms against questions, answers, keywords, category, decision source and destination. Topic filtering and search can be combined. The result count and a readable empty state update as users type. Results and answers remain local/offline, so no school data is sent to a search service.

## Source-of-truth boundaries

Entries are operator-facing summaries grounded in: `Docs/USER_GUIDE.md`, `ADMISSION_WORKFLOW.md`, `Docs/BACKUP_RECOVERY.md`, `Docs/COMMUNICATION_CENTER.md`, `Docs/INSTALLATION.md`, `PROJECT_STRUCTURE.md`, `Core/PermissionCatalog.json`, current navigation in `MainDashboardForm.cs`, and constraints in `Schema/Constraints`.

**The FAQ is not an executable policy.** On any disagreement, actual live schema/stored procedures, runtime compatibility, current user permissions, and C# application behavior take precedence. Historical documents can lag the current schema (currently v6). Labels such as “SQL financial restriction” describe the documented ownership of a rule, not proof of a full code-path audit for that entry. Re-check source and SQL against each FAQ when the associated workflow changes.

The help catalog is in `Pages/Help/HelpCatalog.cs` and its UI is in `Pages/Help/HelpCenterPage.cs`. To add an entry, provide category, natural-language question, answer, keyword synonyms, decision owner/source, and exact navigation hint. Avoid developer jargon in answers; keep them actionable and never suggest bypassing permissions or direct SQL edits. Don't put credentials, connection strings or individual student data in help text.

## Follow-up acceptance

- Verify release build and permission-independent navigation from the dashboard.
- On a Windows UI session, test search, changing categories, keyboard selection, no-result state, narrow DPI/window layout, and theme changes.
- Ask an operator unfamiliar with the implementation to locate solutions for “pending registration paid,” “unknown payment save,” “student not posted,” “missing permission,” and “restore older backup.”
- Before claiming exhaustive coverage, conduct a separate C# ↔ live schema stored-procedure audit for finer-grained flags and edge cases. The initial FAQ is a substantial operator guide, not a guarantee that every stored procedure branch is documented.
