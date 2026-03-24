# Android Duplicate Prevention Plan V2

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
7. Tap debounce as a supplementary safety layer
8. Dequeue locking to prevent sync race conditions
9. Optimistic update rollback on sync failure

---

## Phase 0: Data Layer Prerequisites

Before any UI work, the Android data layer must be updated to support idempotency end-to-end.

### 0A. Add `clientRequestId` to Android API DTOs

**File:** `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/ApiDtos.kt`

The following request DTOs currently have **no `clientRequestId` field** and must be updated:
- `CreateSaleRequest` — add `val clientRequestId: String?`
- `AddSalePaymentRequest` — add `val clientRequestId: String?`
- `CreditRepaymentRequest` — add `val clientRequestId: String?`

The backend contracts already accept this field (`string? ClientRequestId = null` in `SalesContracts.cs` and `CreditContracts.cs`). The Android DTOs must match.

### 0B. Add `clientRequestId` column to Room sync queue

**File:** `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/local/Entities.kt`

`SyncQueueEntity` currently has: `entityName`, `entityId`, `operation`, `payloadJson`, `rowVersionBase64`, `enqueuedAtUtcIso`, `retryCount`.

It does **not** have a `clientRequestId` column. Add one:
- `val clientRequestId: String?` — nullable for non-financial operations

This requires a **Room schema migration** (increment database version, add migration that ALTERs the table).

### 0C. Add `inFlight` column to sync queue

Add `val inFlight: Boolean = false` to `SyncQueueEntity`.

This prevents the dequeue race condition (Phase 5B). A sync processor must mark a row `inFlight = true` before processing it, and other sync triggers must skip rows already in-flight.

### Why Phase 0 exists
The V1 plan assumed `clientRequestId` was a ViewModel-only concern. In reality it must flow through DTOs, the sync queue, and the Retrofit layer. This is a prerequisite for Phases 2 and 5.

---

## Phase 1: In-Flight Button Disabling + Debounce

Apply to every Android mutation form.

### Required behavior
- Add `isSubmitting` state to the ViewModel or Composable state holder.
- Ignore repeated taps while `isSubmitting == true`.
- Disable the primary action button while submitting.
- Show a loading state in the button text and/or with a spinner.
- Re-enable the button if the operation fails.
- Add a 300ms debounce on tap handlers as a supplementary measure. Compose recomposition cycles can lag behind rapid taps, so debounce catches edge cases where `isSubmitting` state hasn't propagated yet.

### Button state examples
- `Create Sale` -> `Creating...`
- `Add Payment` -> `Saving...`
- `Record Repayment` -> `Recording...`
- `Save Item` -> `Saving...`
- `Save Expense` -> `Saving...`

### Why
This is the fastest, safest way to stop accidental double-taps and reduce duplicate writes caused by laggy network conditions. Debounce adds a second layer of defense for the Compose recomposition timing gap.

---

## Phase 2: Financial Write Protection With `clientRequestId`

This applies to money-moving operations.

### Required flows
1. Sale creation
2. Add payment to existing sale
3. Credit repayment

### Required behavior
- Generate a `clientRequestId` (UUID) once when the form session starts.
- Keep it stable for the life of that form session.
- Send it with the financial write request via the updated DTOs (Phase 0A).
- Store it in the `SyncQueueEntity` row for queued operations (Phase 0B).
- Reuse the same value if the user retries from the same still-open form.
- Reset it only when the form is dismissed, reset, or definitively completed.

### Backend behavior reference
The backend `IdempotencyService` uses these fields to build a dedup key: `scope | userId | deviceId | clientRequestId`. When a duplicate is detected within the 10-minute window, the backend returns **the original successful response** (not an error). If the original request is still in progress, it returns 409 Conflict.

### Endpoints with idempotency support
- `POST /api/v1/sales` — scope: `sales.create`
- `POST /api/v1/sales/{id}/payments` — scope: `sales.payment:{id}`
- `POST /api/v1/credits/{saleId}/repayments` — scope: `credits.repayment:{saleId}`

### Why
The backend already has financial idempotency support. Android should use it properly.

Without this, the UI can still create duplicate submissions under ambiguous retry conditions.

---

## Phase 3: Prompt Success State Transition

After a successful submit, the form must stop being an active editable screen.

### Required behavior
- Dismiss sheet/dialog when appropriate, or
- Navigate back to summary/detail screen, or
- Replace editable form with a non-editable success state

### Specific flows requiring new post-success transitions

**Sale creation** (`SalesScreen.kt`): Already dismisses the form and shows a celebration overlay. No change needed.

**Payment addition** (`SalesScreen.kt`, payment section): Currently does **NOT** dismiss the form on success. The user stays on the same screen after `addSalePayment()` returns. Must add: dismiss the payment form/sheet and show a success indicator on the sale detail.

**Credit repayment** (`CreditScreen.kt`): Currently does **NOT** dismiss the form on success. The repayment is optimistically applied and queued for sync, but the form stays editable. Must add: dismiss the repayment form and return to the credit detail view.

**Inventory create/edit** (`InventoryScreen.kt`): Verify current behavior and add dismiss/navigate-back if not already implemented.

**Expense create/edit**: Verify current behavior and add dismiss/navigate-back if not already implemented.

### Why
A successful form left onscreen invites repeat taps and accidental duplicate writes.

---

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

---

## Phase 5: Offline Queue Protection

Android is offline-capable, so local duplicate prevention matters before the backend sees anything.

### 5A. Form-session queue guard
- Once a form session queues a mutation, that same form session cannot queue the same mutation again.
- Financial writes must use the same `clientRequestId` locally (stored in `SyncQueueEntity.clientRequestId`) and remotely (sent in the API request body).
- Non-financial writes should still have a local submission/session guard even if they do not use backend idempotency.

### 5B. Dequeue race prevention
**Problem:** When a user submits offline, two sync triggers can fire nearly simultaneously:
1. The immediate `runSyncOnce()` call after enqueue
2. A WorkManager periodic or connectivity-triggered sync

Both can dequeue the same `SyncQueueEntity` and fire the same API call, causing duplicate server requests.

**Solution:** Use the `inFlight` column added in Phase 0C:
- Before processing a queue item, atomically set `inFlight = true` (use a Room `@Query("UPDATE ... WHERE id = :id AND inFlight = 0")` returning the affected row count).
- If affected rows = 0, skip (another processor already claimed it).
- On success: delete the queue row.
- On failure: set `inFlight = false`, increment `retryCount`.
- On app crash/restart: reset all `inFlight = true` rows back to `false` (stale lock recovery).

### 5C. Sale payment direct-call protection
**Problem:** `addSalePayment()` in `ShopkeeperDataGateway.kt` makes a **direct API call** — it does NOT go through the Room sync queue. This means:
- No queue-level deduplication protects it.
- OkHttp transparent retries can fire duplicate requests silently.
- A second tap can fire a second HTTP call if `isSubmitting` state hasn't propagated.

**Solution:** Either:
- Route sale payments through the sync queue like sales and repayments, OR
- Add an in-memory `Mutex` or `AtomicBoolean` guard in the gateway method so only one payment call can be in-flight per sale at a time, AND ensure `clientRequestId` is sent so the backend deduplicates any leaked retries.

### Why
A user should not be able to enqueue the same action twice just because the device is offline or slow to update UI state. And infrastructure-level retries (OkHttp, WorkManager) must not multiply a single user action into multiple server-side writes.

---

## Phase 6: OkHttp Transparent Retry Mitigation

### Problem
`NetworkFactory.kt` uses default OkHttp configuration, which **automatically retries** failed connections at the HTTP layer. This is invisible to the application. Combined with the manual sync retry loop in `runSyncOnce()`, a single user action can produce 3+ server requests:
1. OkHttp auto-retry (1-2 silent attempts on connection failure)
2. Manual sync retry (increments `retryCount`, re-sends payload)
3. User retry (taps again because no feedback was shown)

Without `clientRequestId`, all three look like distinct requests to the backend.

### Solution
Two options (choose one or both):
- **Option A:** Disable OkHttp's automatic retry for mutation requests by setting `retryOnConnectionFailure(false)` on the OkHttpClient, or using a separate client for mutations.
- **Option B:** Rely on `clientRequestId` being present on all financial mutations (Phase 2) so retries at any layer collapse into one server-side operation. Accept that non-financial mutations (inventory, expenses) don't have this protection and rely on UI-level guards only.

**Recommended:** Option B (less disruptive), but document the risk for non-financial mutations.

### Why
Transparent retries are the most insidious source of duplicates — they happen without user interaction and without application awareness.

---

## Phase 7: Optimistic Update Rollback

### Problem
Credit repayment in `ShopkeeperDataGateway.kt` optimistically updates the local `SaleEntity` **before** the sync queue processes the request. If the sync fails (server rejects, network permanently down, idempotency conflict), the user sees a repayment that was never persisted to the server. The local state is now inconsistent with server state.

### Required behavior
- On sync failure for a credit repayment: revert the optimistic local update, or mark it with an error/pending badge so the user knows it didn't persist.
- On sync failure for any queued mutation: the UI should reflect the true state, not the optimistic projection.
- On next successful sync/pull: overwrite local state with server truth.

### Implementation approach
- After sync failure, re-fetch the canonical entity from the server (if online) or mark the local entity as `syncStatus = failed`.
- Show a visual indicator (error badge, "pending" label) on entities with failed sync.
- The next `runSyncOnce()` or full pull should reconcile state.

### Why
Optimistic updates that don't roll back on failure create phantom data that the user trusts but the server never received. This is a data integrity issue, not just a UX issue.

---

## Phase 8: Expense Endpoint Idempotency (Backend Prerequisite)

### Problem
The V1 plan lists expenses in the targeted flows, but `ExpensesEndpoints.cs` does **NOT** use `IdempotencyService`. There is no server-side deduplication for expense creation. Button disabling (Phase 1) is the only protection.

### Options
- **Option A (Recommended):** Add backend idempotency to `POST /api/v1/expenses` following the same pattern as sales. Add `ClientRequestId` to the expense request contract. This gives full protection.
- **Option B:** Accept the gap. Document that expenses rely on UI-level guards only. Since expenses are typically lower-frequency and lower-stakes than sales/payments, this may be acceptable.

### Decision needed
Choose Option A or B before implementation begins.

---

## Phase 9: Android Test Coverage

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
9. **Sync dequeue race:** two concurrent sync triggers processing the same queue item — only one should fire the API call.
10. **OkHttp retry stacking:** a mutation that fails at the connection level and is retried by OkHttp + manual retry — verify only one server-side record is created (requires `clientRequestId`).
11. **Optimistic rollback:** a credit repayment that fails sync — verify local state reverts or shows error state.
12. **Direct-call payment guard:** two rapid `addSalePayment()` calls for the same sale — verify only one HTTP request fires.

---

## Android Implementation Targets

### Specific files to change

1. `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/ApiDtos.kt` — add `clientRequestId` to financial request DTOs
2. `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/local/Entities.kt` — add `clientRequestId` and `inFlight` columns to `SyncQueueEntity`, Room migration
3. `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/ShopkeeperDataGateway.kt` — generate `clientRequestId` for financial writes, pass through queue and API calls, add `inFlight` locking to sync processor, add mutex/guard for direct payment calls
4. `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/NetworkFactory.kt` — evaluate OkHttp retry policy for mutations
5. `mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt` — add `isSubmitting` state, button disabling, debounce, payment form dismissal on success
6. `mobile-android/app/src/main/java/com/shopkeeper/mobile/credits/CreditScreen.kt` — add `isSubmitting` state, button disabling, debounce, repayment form dismissal on success
7. `mobile-android/app/src/main/java/com/shopkeeper/mobile/inventory/InventoryScreen.kt` — add `isSubmitting` state, button disabling, debounce, duplicate warnings
8. Expense create/edit Compose UI files — add `isSubmitting` state, button disabling, debounce

### Backend file (if Option A for Phase 8)
9. `backend-api/src/Shopkeeper.Api/Endpoints/ExpensesEndpoints.cs` — add idempotency to `POST /api/v1/expenses`
10. `backend-api/src/Shopkeeper.Api/Contracts/ExpenseContracts.cs` — add `ClientRequestId` to create request

---

## Suggested Implementation Order
1. **Phase 0** — Data layer prerequisites (DTOs, Room migration, sync queue columns)
2. **Phase 1** — Button disabling + debounce across all forms
3. **Phase 2** — `clientRequestId` generation and propagation for financial writes
4. **Phase 3** — Post-success form dismissal (payment addition, credit repayment)
5. **Phase 5** — Offline queue guards (dequeue locking, payment direct-call guard)
6. **Phase 6** — OkHttp retry decision
7. **Phase 4** — Inventory duplicate warnings
8. **Phase 7** — Optimistic update rollback
9. **Phase 8** — Expense idempotency decision and implementation
10. **Phase 9** — Test coverage

---

## Expected Outcome
After implementation:
- Accidental double-taps should no longer create duplicate writes from the same Android form session.
- Financial retries should be safely idempotent end-to-end — from UI through offline queue through OkHttp through the server.
- Infrastructure-level retries (OkHttp, WorkManager) cannot multiply a single action into multiple server-side records.
- Two concurrent sync processors cannot fire the same queued mutation twice.
- Inventory saves should be harder to duplicate accidentally.
- The user should always see clear submit progress and unambiguous success/failure state.
- Optimistic updates that fail to sync are visibly flagged, not silently accepted.

## Non-Goals
1. No iOS changes.
2. No broad backend inventory dedupe policy.
3. No speculative cross-platform refactor as part of this plan.

## Changes from V1
- Added Phase 0 (data layer prerequisites) — `clientRequestId` in DTOs, Room migration for sync queue
- Added Phase 5B (dequeue race prevention) — `inFlight` column and atomic claim
- Added Phase 5C (sale payment direct-call protection) — in-memory guard for non-queued mutations
- Added Phase 6 (OkHttp transparent retry mitigation) — previously unaccounted for
- Added Phase 7 (optimistic update rollback) — data integrity for failed syncs
- Added Phase 8 (expense endpoint idempotency) — backend gap identified
- Expanded Phase 9 tests to cover retry stacking, dequeue races, optimistic rollback, and direct-call guards
- Added debounce as supplementary measure in Phase 1
- Specified exact files and specific post-success transition gaps for payment addition and credit repayment
- Reordered implementation sequence to front-load data layer prerequisites
