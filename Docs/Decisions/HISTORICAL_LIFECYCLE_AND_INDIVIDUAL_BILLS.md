# Historical lifecycle actions and individual outstanding bills — 29 October 2026

The owner approved closing the historical-placement gaps while preserving useful
history-building operations. The rule concerns the placement's academic year,
not how long ago the student was originally admitted.

- Historical transfers remain available for Active placements. Their effective
  date must fit the recorded academic year and follow the enrollment start. A
  reason is required. The UI requires an explicit historical date selection.
- Class/campus transfers preserve existing fee plans for historical or nonbilling
  placements. The transfer writer and internal fee-sync writer independently
  enforce this. Historical transfers never enable BillingReady.
- Historical departure remains available with an explicit leaving date within
  the recorded placement/year and the existing reason/financial review. Its date
  is not silently assumed to be today or the year-end date.
- Hold, resume and the exceptional Left-as-hold review require a placement
  covering today. Prepare the current placement before resuming attendance;
  earlier factual corrections remain in Placement History. SQL also enforces
  the current-year requirement, including for backdated requests.
- Personal details, photos, history maintenance, promotion/repeat and genuine
  readmission keep their existing paths and permissions.

Migration 61 delivers all SQL changes and exposes year boundaries to Admission
and the transfer editor. Application and installer require schema 61. Existing
students, financial records, operational histories and recovery markers remain.

Fee Collection retains its existing selected-family bill behavior and grouping.
The adjacent buttons are labelled Print Family Bill and Print Student Bill, with
matching widths, heights and centered captions. Print Student Bill always passes
only the main selected admission to the existing outstanding-bill preview and
uses individual layout. It does not change checked siblings or bill grouping.
Family printing retains its fee-change acknowledgement fallback when applicable;
the individual outstanding button always uses the outstanding-bill preview.

Validation uses a new disposable schema-only SQL database with fictional records,
plus UI checks for menu/date restrictions, print scopes, permissions, stale
previews and rendered button alignment. No school database is used by these tests.
