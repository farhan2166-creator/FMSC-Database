# Credit lifecycle audit — 4 November 2026

Reviewed main at `12f1d8f`, including the owner's latest `fmsc.dacpac`, the versioned migrations, collection/advance/counter forms, posting paths, collection writer, correction/refund handling, and receipt readers. This is an audit: no application code, migrations or school data were changed.

Validation: the 16 production advance-allocation/ownership checks passed on this checkout. SQL findings below are from the current DACPAC definitions checked against migration sources and C# call paths. A fresh SQL Server database and native Windows forms were not executed in this audit; this is not a claim of a completed live end-to-end test. Procedure definitions were read directly from `fmsc.dacpac`; the references below identify matching migration definitions and current C# call paths.

## Confirmed findings

### 1. Credit-only collection skips the enrollment-date validation used by cash collection

`ProcessFeeTransaction` rejects a collection dated before the selected enrollment date. `ApplyCollectionCredit` checks only that its date is present and not in the future, plus ownership, available credit and expected bill balances. Its charge check does not compare the date with the enrollment. The credit-only form path calls the writer without `ProcessFeeTransaction`, so the cash date guard never runs. `IssueCollectionReceipt` also only rejects future dates.

Example: a student enrolled on 1 November with usable advance can have a zero-cash settlement dated 31 October through this path, although the cash path would reject that date. Separately, credit availability is checked against today's ledger, not an as-of balance for the chosen date; backdating use of later deposits needs a clear policy.

Fix: apply the same placement-date guard in the credit SQL path and in the form, with a versioned migration. Decide the permitted credit backdating rule explicitly; future posted fees may still be prepaid deliberately.

Sources: [ApplyCollectionCredit date validation](../Migrations/22.sql#L17); [credit-only write branch](../Pages/Finance/Collection/FeeCollectionPage.cs#L3510). The cash enrollment-date guard was verified directly in the DACPAC definition of `ProcessFeeTransaction`, alongside `ApplyCollectionCredit` and `IssueCollectionReceipt`. No payment-date trigger in the DACPAC supplies the missing enrollment check.

### 2. Closing Advance Balance can discard the allocation while retaining Amount Received

The Advance Balance button reloads charges after the dialog closes, even when cancelled. The loader clears the grid and creates every charge with Pay Now = 0 and an unselected marker. It retains the entered Amount Received, but does not call the existing allocation refresh afterward. Save then rejects the mismatch between entered cash and allocated cash.

Example: allocate Rs. 2,300, open Advance Balance, cancel/close it. The amount remains 2,300 while Pay Now resets to zero. Use Full Amount only assigns the amount text; if it is already 2,300, no TextChanged event fires, so that button need not repair the missing allocation. Changing the amount away and back does trigger allocation.

Fix: refresh the allocation after the account reload, while revalidating the payable limit. Make Use Full Amount explicitly allocate even when its numeric text is unchanged. Preserve or explicitly reset manual splits rather than silently discarding them.

Sources: [Advance Balance return handler](../Pages/Finance/Collection/FeeCollectionPage.cs#L309); [Use Full Amount](../Pages/Finance/Collection/FeeCollectionPage.cs#L910); [amount editor setter](../Core/ModernTextBox.cs#L153). `AppendChargesAsync` initializes the new rows at zero.

### 3. Automatic and manual advance apply to different fees first

Manual collection orders monthly fees by oldest month, then other fees by smallest remaining payable balance. Automatic SQL applies tuition first, then billing month/charge date and charge ID. For one child, both routes can use exactly the same advance but settle different fees.

Example: October transport Rs. 1,000, November tuition Rs. 2,000, advance Rs. 1,000. Manual collection clears October transport. Automatic application clears Rs. 1,000 of November tuition and leaves October transport owing.

Fix: use one agreed allocation order in C# and SQL. This is a versioned SQL change as well as focused regression coverage; it should not rewrite previous allocations.

Sources: [automatic allocation](../Migrations/60.sql#L184); [manual allocation order](../Pages/Finance/Collection/FeeCollectionPage.Allocation.cs#L50).

### 4. Newly automatic credit applications use the historical receipt fallback

`ApplyStudentCredit_Internal` records payments, allocations and ledger deductions, but does not issue a saved CollectionReceipt snapshot. It creates internal CR-GUID payment references. The current GetCollectionReceipt fallback returns an invented FMSC-CR-year-paymentId display reference, labels the application historical, and obtains outstanding/advance balances and fee values from current tables. C# also adds a legacy-receipt warning because that result has no modern SnapshotVersion.

Consequently an application created today can print as historical/legacy; printing it later can show different balances or fee descriptions. Manual credit-only collection already issues a normal saved R/FR receipt, so the two paths differ.

Fix: issue a durable normal receipt/snapshot for new automatic settlements, retaining the fallback only for genuinely existing unissued records. Preserve all historical stored numbers and payments.

Sources: [automatic payment creation](../Migrations/60.sql#L195); [automatic/historical receipt reader](../Migrations/56.sql#L279); [legacy receipt classification](../Core/Reports/Fees/FeeReceiptDocuments.cs#L46).

### 5. Monthly-posting confirmation promises an effect that SQL can contradict

The confirmation says existing bills are untouched and posting does not record payment. PostSelectedMonthlyFees nevertheless calls ApplyStudentCredit_Internal for each selected student. That can consume held advance on already due bills, including bills other than the newly posted month. The automatic-billing status reports the number of new charges but not how much advance was used.

Example: post a future December fee while the child has unpaid October fees and held advance. The new future fee is not automatically settled early, but the posting operation can settle October with that advance. The operator's confirmation did not disclose that effect.

Fix: preview/report advance use and resulting balances, or keep posting and settlement separate according to the chosen policy. At minimum remove the inaccurate promise. No additional filter-heavy screen is needed.

Sources: [posting confirmation](../Pages/Finance/Billing/FeePostingPage.cs#L464); `PostSelectedMonthlyFees` in `fmsc.dacpac`; [automatic billing status](../Core/AutomaticMonthlyBilling.cs#L50).

## Current lifecycle, by scenario

| Scenario | Current result | Assessment |
|---|---|---|
| Deposit before any fee exists | Money received is recorded once and added to that child's advance; there is no fee allocation yet. | Ownership and cash distinction are sound. |
| Advance exists when current fees are posted | Posting can automatically use it on that child's eligible charges, leaving the exact remainder. | Financially capped; findings 3–5 affect order, disclosure and receipt history. |
| Future monthly fees posted early | Automatic application excludes a charge whose ChargeDate is still future. Monthly ChargeDate and DueDate are normally both the first. Older arrived charges can still consume advance during that operation. | Do not promise all advance stays untouched merely because the new month is future. |
| Operator deliberately prepays a posted future fee | Fee Collection can apply own advance to that future fee after confirmation, with zero cash if fully covered. | Existing explicit prepayment policy; different from automatic early application. |
| Deposit after fees are already due | Deposit itself does not settle them. Collection can use the refreshed advance; later posting or a billing run can also apply it. | Timing depends on the route. Make this clear rather than silently adding auto-settlement to deposits. |
| Dashboard revisited after a deposit | Automatic billing normally checks once per actor/month. Returning to the dashboard does not guarantee a new credit sweep; force-run/restart/month change can change that. | Deposits must not promise immediate automatic settlement. |
| Advance Rs. 5,000; owner's dues Rs. 2,300 | Plan uses Rs. 2,300, cash needed is zero, Rs. 2,700 remains. | Covered by passing production allocation checks; credit-only date caveat above. |
| Advance Rs. 1,000; owner's dues Rs. 2,300 | Plan uses Rs. 1,000 and needs Rs. 1,300 cash for full settlement. | Own balance is capped; no implicit deposit of excess cash. |
| Main student has advance, sibling has dues | Ordinary collection leaves the advance with its owner. Sibling dues require cash or an explicit transfer. | Matches the owner's latest decision. |
| Only a selected sibling has advance | Ordinary collection does not automatically opt in that sibling's advance. Select that child as main to use it on their own fees, or explicitly transfer funds. | Matches the owner's latest decision. |
| Explicit sibling transfer | Sender decreases and recipient increases in one transaction; amount cannot exceed available balance, and SQL requires same-family membership. No new cash or fee settlement is recorded by the transfer. | Ownership and conservation guards are present. |
| Fixed family counter | Each included child can use their own advance; no balance crosses accounts. | Deliberately retained difference from the main-student collection screen. |
| Advance checkbox unticked | Manual collection leaves advance untouched and restores the cash payable. | This is a choice for that collection, not a global opt-out from automatic posting paths. |
| Refund unused advance | Refund is capped at the available ledger balance and decreases both cash and that child's advance. It does not undo fees already settled by credit. | Correct separation from payment adjustment. |
| Reduce an already paid bill | Explicit choice returns the released paid portion as advance or refunds it; the original payment remains in history. | Both cash and credit settlement history are retained. |
| Another operator changes fees or advance after load | Review tokens, expected balances, expected credit and account locks reject stale settlement; receipt and money writes share one transaction. | Protection against overspending and partial-save/receipt mismatch is present. |
| Connection fails during commit acknowledgement | Collection writer preserves its recovery intent and warns against repeating an unconfirmed payment. Deposit/transfer/refund also use the recovery journal. | Recovery protection is present; no automatic retry should be added. |
| Reporting cash versus credit | Deposits count as money received once; later credit application is noncash, and transfers do not create cash receipts. | No double-counting path was found in the reviewed collection aggregation. |

## Recommended release scope

Fix the missing date guard and allocation reload first. Then align SQL/C# settlement order and posting disclosure. Give new automatic credit applications durable receipt snapshots while preserving existing history. These are focused corrections; no new account type, family wallet or large filter panel is required.

The ordinary/main-student policy, explicit sibling transfers, and the fixed-counter exception should remain documented until the owner decides otherwise. Automatic use should be a clearly stated rule rather than a side effect that depends on which form happens to run.
