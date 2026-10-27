# Promotion choices — 27 October 2026

Promotion uses the selected source campus and academic year, followed by class
and section. Source class and section choices contain only active placements
returned by the existing promotion-candidate rules. Changing campus or year
reloads these choices; completing a batch removes sources that are now empty.

Promote advances to the immediate next class in the same academic program,
according to the full class catalog's display order and class-ID tie breaker.
That exact next class must have sections configured at the source campus. An
unconfigured intermediate class cannot be skipped in favor of a higher class.
The destination is checked again against fresh catalog and campus assignments
before processing. Destination sections may be empty of students.

Repeat Class moves a student into the destination academic year while keeping
the same class and section. Unticking excludes a student from the batch.
The form, tooltips, review, result and history use Repeat Class / Repeated Class.
Historical placements do not offer Confirm Return; placements in the current
year offer it for both promoted and repeating students before billing.

Existing HOLD decisions and the closed placement's Promoted status remain
unchanged in storage. Placement History reads the saved repeat decision to
display Repeated Class without changing lifecycle records or correction rules.
These are application changes; no database migration or record repair is needed.
