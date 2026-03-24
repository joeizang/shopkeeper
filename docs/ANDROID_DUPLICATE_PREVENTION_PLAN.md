# Android Duplicate Prevention Plan

## Scope
This plan is Android-only.

Do not make any changes to the iOS app as part of this work.

The goal is to reduce accidental duplicate writes caused by repeated taps, slow mobile round-trips, offline queueing, and unclear in-flight UI states.

## Primary Objectives
1. Prevent duplicate sales.
2. Prevent duplicate sale payments.
3. Prevent duplicate credit repayments.
4. Prevent accidental duplicate inventory saves from the same form session.
5. Make submission state obvious to the user.
6. Align Android financial writes with backend idempotency.

## Targeted Android Flows
1. Create sale
2. Add payment to existing sale
3. Record credit repayment
4. Create inventory item
5. Update inventory item
6. Create expense
7. Update expense

## Core Design
The solution should not rely on one mechanism.

It should combine:
1. Client-side in-flight submission locking
2. Clear loading/disabled UI states
3. Stable `clientRequestId` values for financial writes
4. Prompt post-success form dismissal or reset
5. Local duplicate-session protection for offline queueing
6. Inventory duplicate warnings instead of unsafe hard dedupe

## Phase 1: In-Flight Button Disabling
Apply to every Android mutation form.

### Required behavior
- Add `isSubmitting` state.
- Ignore repeated taps while `isSubmitting == true`.
- Disable the primary action button while submitting.
- Show a loading state in the button text and/or with a spinner.
- Re-enable the button if the operation fails.

### Button state examples
- `Create Sale` -> `Creating...`
- `Add Payment` -> `Saving...`
- `Record Repayment` -> `Recording...`
- `Save Item` -> `Saving...`
- `Save Expense` -> `Saving...`

### Why
This is the fastest, safest way to stop accidental double-taps and reduce duplicate writes caused by laggy network conditions.

## Phase 2: Financial Write Protection With `clientRequestId`
This applies to money-moving operations.

### Required flows
1. Sale creation
2. Add payment to existing sale
3. Credit repayment

### Required behavior
- Generate a `clientRequestId` once when the form session starts.
- Keep it stable for the life of that form session.
- Send it with the financial write request.
- Reuse the same value if the user retries from the same still-open form.
- Reset it only when the form is dismissed, reset, or definitively completed.

### Why
The backend already has financial idempotency support. Android should use it properly.

Without this, the UI can still create duplicate submissions under ambiguous retry conditions.

## Phase 3: Prompt Success State Transition
After a successful submit, the form must stop being an active editable screen.

### Required behavior
- Dismiss sheet/dialog when appropriate, or
- Navigate back to summary/detail screen, or
- Replace editable form with a non-editable success state

### Why
A successful form left onscreen invites repeat taps and accidental duplicate writes.

## Phase 4: Inventory Duplicate Protection
Inventory is not the same as financial idempotency.

### What not to do
- Do not dedupe inventory creates by product name alone.
- Do not treat two similar inventory entries as automatically the same business record.

### What to do instead
- Prevent the exact same form session from submitting twice.
- Add local duplicate warnings for suspicious matches such as:
  - same serial number
  - same model number + same product name
  - same product name in the same shop, where useful
- Allow the user to continue after warning unless there is a true uniqueness rule.

### Why
This reduces mistakes without blocking legitimate inventory work.

## Phase 5: Offline Queue Protection
Android is offline-capable, so local duplicate prevention matters before the backend sees anything.

### Required behavior
- Once a form session queues a mutation, that same form session cannot queue the same mutation again.
- Financial writes should use the same `clientRequestId` locally and remotely.
- Non-financial writes should still have a local submission/session guard even if they do not use backend idempotency.

### Why
A user should not be able to enqueue the same action twice just because the device is offline or slow to update UI state.

## Phase 6: Android Test Coverage
Add or update Android tests to verify the protection works.

### Required tests
1. Sale submit button disables after first tap.
2. Add payment button disables after first tap.
3. Credit repayment button disables after first tap.
4. Inventory save button disables after first tap.
5. Failed submit re-enables the button.
6. Successful submit dismisses or resets the form.
7. Same form session cannot queue the same mutation twice.
8. Financial retries reuse the same `clientRequestId`.

## Android Implementation Targets
Likely files to change:

1. `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt`
2. `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/inventory/InventoryScreen.kt`
3. credit repayment Compose UI files in the Android app
4. existing sale payment Compose UI files in the Android app
5. `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/ShopkeeperDataGateway.kt`

## Suggested Implementation Order
1. Sale composer
2. Existing sale payment flow
3. Credit repayment flow
4. Inventory create/edit flow
5. Expense flow
6. Android E2E and instrumentation coverage

## Expected Outcome
After implementation:
- accidental double-taps should no longer create duplicate writes from the same Android form session
- financial retries should be safely idempotent end-to-end
- inventory saves should be harder to duplicate accidentally
- the user should always see clear submit progress and unambiguous success/failure state

## Non-Goals
1. No iOS changes
2. No broad backend inventory dedupe policy
3. No speculative cross-platform refactor as part of this plan
