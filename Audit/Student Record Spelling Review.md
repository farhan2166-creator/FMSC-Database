# Student-record spelling review

Reviewed 11 October 2026, Asia/Karachi. Read-only review of the current FMSC_DB at C:\ProgramData\FMSC_Database. Current student count: 6,590. This differs from the earlier scenario-audit snapshot of 6,592; this review uses the current dataset and does not infer why the count changed.

Update: the user authorized correcting certain spellings. A fresh COPY_ONLY/CHECKSUM backup verified successfully, a separate restored database passed integrity checking, and the corrections were tested there before live updates. Live result: 555 students corrected (6 addresses and 549 father occupations). Address corrections include four Sarband variants and two clear Peshawar typos. All 6,590 students remain, and AdmissionNumber and PlaceOfBirth are unchanged. Exact old-value guards and transaction rollback protected the updates. No SQL definitions or application code changed.

## Confirmed address spelling

| Existing token | Proposed token | Student records |
|---|---|---:|
| Sarbnad | Sarband | 2 |
| Sarabnd | Sarband | 1 |
| Sarbad | Sarband | 1 |

Only whole-word occurrences would be replaced, preserving the rest of each address. Four student addresses are affected. No fuzzy or substring-wide replacements are proposed.

## Proposed occupation spelling corrections

| Existing value | Proposed value | Records |
|---|---|---:|
| Busniess | Business | 472 |
| Bussines | Business | 4 |
| Bussiness | Business | 12 |
| Bussnies | Business | 1 |
| Famer | Farmer | 2 |
| GARDNER | Gardener | 1 |
| Govt Sarvent | Govt Servant | 18 |
| Govt Servent | Govt Servant | 27 |
| Govt Servnt | Govt Servant | 8 |
| Masson | Mason | 4 |

These are spelling proposals, not approved business classifications. Shop Keeper (190) and Shopkeeper (138) are an additional spacing-standardization decision. Do not automatically interpret abbreviations such as S G, S.G, G S, F, Bus or Govt. The occupation value 3404 (one record) needs source-record verification. Late/Deceased describe parental status and must not be replaced with a guessed occupation.

## Address terms needing local confirmation

Potential review groups include Sango/Sangu/Sangoo, Kale/Kali/Kalay/Kaly, Khel/Kheil/Khil, Ghari/Gari, Sheikh/Shaikh/Sheik and Peshawar/PESAHWAR/PESHWAR. These are candidates only; nearby spelling does not prove identical locations. Confirm each mapping before proposing bulk replacements. Existing valid addresses must retain their locality and detail.

## Place of Birth

Peshawar: 4,829 records; Sarband: one record; null/empty: 1,759; whitespace-only: one. No obvious misspelling was found in this field. Whitespace may be normalized separately, but blank values must not be filled by assuming the address equals birthplace.

## Private review files

Row-level before/after proposals with AdmissionId and AdmissionNumber: bin\SpellingAudit_20261011\proposed-corrections.csv.
All existing field values and counts: bin\SpellingAudit_20261011\field-frequencies.csv.
Read-only query and raw source extract are retained in the same ignored bin folder. The report deliberately omits student names and full addresses; row-level files should remain private.

## Application plan after review

Approve exact mappings, take a fresh backup, and test the changes on a fresh disposable copy. Apply by AdmissionId with an exact original-value check so edits made after this review are not overwritten. Keep before/after values for reversal and verify only approved rows and fields changed. Refresh suggestion caches afterward. For future data entry, use confirmed canonical suggestions and explicit known-typo mappings; do not fuzzy-correct place names or occupations automatically.

## Applied corrections and remaining decisions

Applied occupation spellings: 489 to Business; 53 to Govt Servant; 4 to Mason; 2 to Farmer; 1 to Gardener. Address: Sarbnad/Sarabnd/Sarbad to Sarband (4), PESAHWAR/PESHWAR to Peshawar (2). Final before/after export verifies 555 changed fields across 555 students and no differences in AdmissionNumber or PlaceOfBirth.

Backup: bin\SpellingAudit_20261011\BeforeCorrections_COPY_ONLY.bak. Tested copy: FMSC_SPELLING_20261011. Exact applied changes: bin\SpellingAudit_20261011\applied-corrections.csv. Uncertain row-level details: bin\SpellingAudit_20261011\uncertain-records.csv. These contain private record data and remain in ignored build output. Restart the app to refresh cached suggestions.

The following remain unchanged. Please provide the intended spelling/classification for each group, or say they are distinct and should stay separate:

1. Sango / Sangu / Sangoo.
2. Nawa Kale / Nawa Kali / Nawa Kalay / Nawan Kaly / Nawe kale.
3. Khel / Kheil / Khil in compound locality names (Masho, Mali and others).
4. Ghari / Gari; Ghareeb Abad / GHARIB ABAD.
5. Sheikh Muhammadi / Shaikh Muhammadi / Sheik Mohammadi. Keep Sheikhan separate unless confirmed otherwise.
6. Suleman Khel / Suliman Khel; Shahab Khel / Shahb Khel.
7. Bata Thal / Batathal / Batatal / Bata Tha Peshawar.
8. Pushtakhara / Pushta Khara.
9. Abdara / Abdarar Road (preserve Road if only the locality spelling is corrected).
10. Noshehra: confirm intended location/spelling.
11. Address values F (1), Prep (2), KARIGAR (1), Custom (1) and Custom Bara (1): source-record verification, not an automatic spelling correction.
12. Shop Keeper (190) / Shopkeeper (138): choose one display style.
13. S G (8), S.G (1), G S (1), G Servant (1), Govt (2), F (1), Bus (1), BShop Keeper (1): clarify occupations before mapping; abbreviations may have different meanings.
14. Dr (1) / Doctor (19); Imam Masjid (2) / Imam e Masjid (1): choose whether to standardize wording.
15. Father occupation 3404 (1): verify original source entry; do not guess its replacement.

Blank values remain blank. Parental-status entries Late and Deceased remain unchanged; this spelling pass does not infer an occupation or alter their meaning. Case/spacing-only changes throughout otherwise valid addresses were not applied.

## Second approved correction pass

User confirmed Sangu, Nawan Kaly, Khel, Ghari, Ghareeb Abad, Sheikh Muhammadi, Suleman Khel, Shahab Khel, Batathal, Pushtakhara, Abdara/Abdara Road and Shopkeeper. G S was confirmed to mean Govt. Servant.

Applied on live data after a fresh verified COPY_ONLY backup and successful testing on a separate restored copy: **900 Address fields and 693 FatherOccupation fields across 1,462 students**. Some students had both fields corrected. Address changes preserve remaining locality/district/road text; Abdarar Road became Abdara Road. Occupation changes: 190 Shop Keeper to Shopkeeper, 502 Govt Servant to Govt. Servant, and one G S to Govt. Servant. Existing correctly spelled values remain valid. This pass does not equate S G/S.G with G S without explicit confirmation.

Post-update export confirmed 1,593 changed fields, unchanged student count, and no differences in AdmissionNumber or PlaceOfBirth. Backup and private before/after evidence: bin\SpellingAudit_20261011\Phase2. Remaining row-level review file: remaining-uncertain-records.csv in that folder. No application code or SQL definitions were changed.

### Remaining father-occupation abbreviations

| Value | Records | Decision |
|---|---:|---|
| S G | 8 | Confirm meaning; could differ from G S |
| S.G | 1 | Confirm meaning |
| G Servant | 1 | Confirm whether Govt. Servant |
| Govt | 2 | Incomplete occupation; confirm intended wording |
| F | 1 | Confirm meaning |
| Bus | 1 | Confirm whether Business or a transport occupation |
| Dr | 1 | Confirm whether to expand to Doctor |
| Rtd. Armed Forces | 3 | Confirm whether to expand to Retired Armed Forces |

### Other values needing source confirmation

- FatherOccupation: BShop Keeper (1), 3404 (1). Neither was guessed.
- Address: F (1), Prep (2), KARIGAR (1), Custom (1), Custom Bara (1), Noshehra (1). Confirm whether the wording is valid or incomplete.
- Imam Masjid (2) / Imam e Masjid (1): remaining wording standardization decision, separate from abbreviations.

Restart the app to refresh text-suggestion caches after these database changes.

## Third approved correction pass

Applied the user's confirmed meanings to 12 live FatherOccupation records after a fresh verified backup and successful isolated-copy test: G Servant (1), S.G (1) and S G (8) became Govt. Servant; Dr (1) became Doctor; Bus (1) became Bus Driver. Final before/after export confirmed exactly these 12 field changes, unchanged student count, and unchanged Address, AdmissionNumber and PlaceOfBirth.

Rtd. Armed Forces (3) is confirmed correct and remains unchanged. F (1 occupation record) is unknown and remains unchanged. Still unresolved: Govt (2), BShop Keeper (1), 3404 (1), the previously listed address values and Imam Masjid/Imam e Masjid wording. Backup and private evidence are retained in bin\SpellingAudit_20261011\Phase3. No application code or SQL definitions changed.

## Fourth approved correction pass

User confirmed BShop Keeper means Shopkeeper and Govt/Govt. means Govt Servant. Applied to three live occupation records: BShop Keeper (1) to Shopkeeper; Govt (2) to the established canonical display Govt. Servant. No Govt. abbreviation was present in the current extract. A fresh verified backup and successful separate-copy test preceded the live transaction. Final comparison confirmed only three FatherOccupation changes, unchanged student count and unchanged Address, AdmissionNumber and PlaceOfBirth. Private backup and before/after evidence: bin\SpellingAudit_20261011\Phase4.

Remaining unknown occupation values: F (1) and 3404 (1), both untouched. Other unresolved address/wording questions remain as previously listed. Rtd. Armed Forces is confirmed correct and remains unchanged.

## Suggestion-field Backspace fix

The user reported Backspace could not shorten fields populated with database suggestions. Shared LiveTextSuggestions treated Backspace/Delete/cut like typing and immediately appended the matching suffix again after the edit. The fix preserves the matching list but suppresses automatic inline completion after Backspace, Delete, Ctrl+Backspace/Delete, cut, or a suggestion-source refresh. Normal typing/paste and explicit arrow/mouse selection remain available.

The change is in Core\LiveTextSuggestions.cs and therefore covers the ModernTextBox fields using custom SuggestAppend suggestions, including Father Occupation, Address, Place of Birth, Previous School and Caste/Tribe on admissions. No database data or procedures changed for this fix.

Validation: application build passed with zero warnings/errors. Tests\LiveTextSuggestions\LiveTextSuggestions.Tests.csproj runs the production suggestion logic with simulated controls and a deferred event queue; 14 checks passed for repeated deletion, deleting a selected suffix, Ctrl deletion, cut, paste, queued edits, source refresh, middle edits, casing, Escape and explicit arrow selection. This is a portable control-logic regression test, not interactive WinForms keyboard automation.
