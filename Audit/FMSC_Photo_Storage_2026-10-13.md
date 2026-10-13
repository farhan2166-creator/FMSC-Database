# Proportional photo preparation and consolidated management — 2026-10-13

The Admission screen already had JPEG resizing/compression: maximum dimension
600 pixels and maximum stored size 300 × 1024 bytes. Its old warning suggested
manual cropping; it did not actually crop. The separate Student Photo page
accepted and stored original files up to 5 MB, bypassing that preparation.

Photo management now uses the existing Admission/Student Details screen. The
separate page and sidebar entry are removed. Student Profile → Manage Photo opens
the selected student's Admission information and focuses the photo action. Photo
permission remains Students.Administer, matching the existing SQL procedure.

Preparation fits the full image within 600 pixels on its longest side using one
scale factor, preserves portrait/landscape proportions, and does not upscale or
crop. It corrects camera EXIF orientation, uses a white background for transparent
PNG areas, and encodes JPEG at the highest of the existing quality steps that fits
within 300 KB (307,200 bytes). Small images are not enlarged or padded to reach
that size. The original file is unchanged.

Source files up to the Admission flow's existing 25 MB limit are allowed, checked
before allocating the input buffer. Preparation runs off the UI thread. Only the
prepared JPEG is passed to the application's sole SaveStudentPhoto call; the save
method independently rejects empty/over-limit pending data. Filename extension
and MIME type match the JPEG bytes. Preview status shows the prepared size and
whether it is saved or pending admission.

Photo preparation/saving/removal participates in the existing busy/navigation
guard. Latest-read cancellation, record/version checks, and pending-photo handling
prevent an older read or changed student from receiving the prepared image. Failed
saves retain the pending photo. Profile-hosted dialogs also respect busy and
unsaved changes when closing.

Existing stored photos are not rewritten. This change enforces new application
uploads; no SQL procedure, migration, schema version or DACPAC is changed. The
existing SaveStudentPhoto SQL procedure itself has no byte-size constraint, so
direct SQL writes outside this application are not covered by the application
limit. A database-wide constraint or historical-photo conversion would be a
separate database change.

Validation: 265 portable audit checks pass, including the 25 MB input boundary,
300 KB storage boundary, landscape/portrait fitting, no upscaling and cancellation.
The application and Windows smoke harness cross-build with zero warnings/errors.
Native image tests cover JPEG output/size, proportional geometry, keeping both
ends of the original image, high-detail compression, EXIF orientation,
transparent PNG handling and cancellation. They are
compiled but unrun in this Linux environment.

On Windows, run the smoke harness and inspect normal/high-detail JPEGs, transparent
PNGs and EXIF-rotated phone photos. Check the Profile shortcut, new/existing student
saves, failed-save retry, removal, draft closure and both application themes/DPI.
No school database was accessed for this change.
