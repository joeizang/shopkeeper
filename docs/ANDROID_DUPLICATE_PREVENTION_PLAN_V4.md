# Android Duplicate Prevention Plan V4

## Purpose

This is the final implementation-ready version. It consolidates V1 through V3, resolves all open decisions, adds exact file paths, and fills the technical gaps found in V3 review.

The target is duplicate-safe execution across:

1. UI interaction
2. local form state
3. offline queueing
4. direct API mutations
5. retry behavior
6. backend idempotency alignment

This plan assumes:

1. iOS is out of scope
2. Android is offline-first
3. backend idempotency for financial writes already exists and must be used correctly
4. inventory create is not globally idempotent and must be handled differently from money-moving writes

---

## Architecture Context

The Android app uses:

- **No ViewModels.** All state is managed in Composables via `rememberSaveable` and `remember`.
- **No SavedStateHandle.** Process death recovery relies entirely on `rememberSaveable`.
- **Room with `fallbackToDestructiveMigration()`.** No explicit migration objects exist. Schema changes wipe the local database.
- **Direct Composable → Gateway pattern.** Screens call `ShopkeeperDataGateway` methods directly inside `rememberCoroutineScope` lambdas.
- **Mixed write paths.** Sale creation and credit repayment go through the Room sync queue. Sale payment addition is a direct API call.

These facts constrain how `clientRequestId` can be stored and how schema changes affect existing data.

---

## Resolved Decisions From V3 Review

### Decision 1: Expense idempotency scope

**Decision:** UI submission locking only. No backend idempotency for expenses in this pass.

Reason: sales, payments, and repayments are the highest-risk writes. Expenses can be hardened later without weakening the core financial integrity work.

### Decision 2: Transport retry policy

**Decision:** Rely on `clientRequestId` + backend idempotency for financial writes. Do not disable OkHttp retries.

Reason: OkHttp transparent retries are safe when every financial request carries a stable `clientRequestId`. Disabling retries globally would degrade resilience for read operations and non-financial writes.

### Decision 3: Room migration strategy

**Decision:** Destructive migration is acceptable.

Reason: The database already uses `fallbackToDestructiveMigration()`. The sync queue is transient by nature — items are either processed or pending. On app update, any pending queue items are lost, but this is already the existing behavior for all schema version bumps. No additive migration needed.

### Decision 4: `clientRequestId` lifecycle and process death

**Decision:** Generate `clientRequestId` via `rememberSaveable` in the Composable. Accept that process death generates a new ID.

Reason: The app has no ViewModel layer. Introducing `SavedStateHandle` just for this would require an architectural change disproportionate to the risk. Process death during an active form session is rare. If it happens:
- For queued writes: the original `clientRequestId` is already stored in the `SyncQueueEntity` and will be used when the queue is processed. The new form session gets a new ID, which is correct — it represents a new user intent.
- For direct API calls: the original call either completed or failed before the process died. A new form session with a new ID is the right behavior.

### Decision 5: Handling backend 409 Conflict responses

**Decision:** Treat 409 from the idempotency layer as "in progress — retry later."

The backend `IdempotencyService` returns 409 when a request with the same `clientRequestId` is still being processed by another in-flight request. Android should:
- In the sync queue processor: leave the queue row for the next sync cycle (do not delete, do not increment retry count).
- For direct API calls: surface a transient error to the UI ("Payment is being processed, please wait") and re-enable the button.

### Decision 6: Phases 2 and 3 from V3 are merged

**Decision:** Stable form submission identity and DTO alignment are one phase.

Reason: you cannot test identity generation without the DTO carrying it to the server. They are inseparable and ship together.

---

## File Reference

Every file that must be created or modified in this plan.

| File | Path | Action |
|------|------|--------|
| ApiDtos.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/ApiDtos.kt` | Modify |
| Entities.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/local/Entities.kt` | Modify |
| ShopkeeperDatabase.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/local/ShopkeeperDatabase.kt` | Modify |
| Dao.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/local/Dao.kt` | Modify |
| ShopkeeperDataGateway.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/ShopkeeperDataGateway.kt` | Modify |
| ShopkeeperApi.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/ShopkeeperApi.kt` | No change needed |
| SalesScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt` | Modify |
| CreditScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/credits/CreditScreen.kt` | Modify |
| InventoryScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/inventory/InventoryScreen.kt` | Modify |
| ReportsScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/ReportsScreen.kt` | Modify |

---

## Execution Plan

### Phase 1: Data Layer Prerequisites

This phase must be completed before any UI work.

#### 1A. Add `clientRequestId` to financial DTOs

**File:** `ApiDtos.kt`

Add `val clientRequestId: String? = null` to:
- `CreateSaleRequest`
- `AddSalePaymentRequest`
- `CreditRepaymentRequest`

No changes needed to `ShopkeeperApi.kt` — the Retrofit interface accepts the full request body. The new field will be serialized by Moshi automatically.

#### 1B. Extend `SyncQueueEntity` schema

**File:** `Entities.kt`

Add to `SyncQueueEntity`:
```
val clientRequestId: String? = null
val inFlight: Boolean = false
```

#### 1C. Bump database version

**File:** `ShopkeeperDatabase.kt`

Change `version = 4` to `version = 5`. The existing `fallbackToDestructiveMigration()` handles the rest. No explicit migration object needed.

#### 1D. Add queue claim DAO methods

**File:** `Dao.kt`

Add to `SyncDao`:
```
@Query("UPDATE sync_queue SET inFlight = 1 WHERE id = :id AND inFlight = 0")
suspend fun claimRow(id: Long): Int

@Query("UPDATE sync_queue SET inFlight = 0, retryCount = retryCount + 1 WHERE id = :id")
suspend fun releaseRow(id: Long)

@Query("UPDATE sync_queue SET inFlight = 0 WHERE inFlight = 1")
suspend fun resetStaleClaims()
```

`claimRow` returns the number of affected rows. If it returns 0, another processor already claimed it. This is the atomic compare-and-set mechanism for Room.

#### Acceptance criteria

1. Financial DTOs include `clientRequestId`
2. `SyncQueueEntity` has `clientRequestId` and `inFlight` columns
3. Database version is bumped
4. DAO has atomic claim/release/reset methods

---

### Phase 2: Stable Submission Identity + Backend Alignment

This phase generates `clientRequestId` values and threads them through to the backend.

#### 2A. Financial form identity generation

In each financial form Composable, add:
```kotlin
val clientRequestId by rememberSaveable { mutableStateOf(UUID.randomUUID().toString()) }
```

This generates a stable ID when the form session begins. It survives configuration changes (rotation, dark mode). It does NOT survive process death, which is acceptable per Decision 4.

**Apply to:**
- Sale composer in `SalesScreen.kt`
- Payment addition section in `SalesScreen.kt`
- Repayment form in `CreditScreen.kt`

**Reset the ID** when the form is dismissed, reset, or completed:
```kotlin
clientRequestId = UUID.randomUUID().toString()
```

This ensures the next form session gets a fresh identity.

#### 2B. Thread `clientRequestId` through the gateway

**File:** `ShopkeeperDataGateway.kt`

**For `recordSale()`:**
- Accept `clientRequestId: String` as a parameter
- Include it in the `CreateSaleRequest` payload
- Store it in the `SyncQueueEntity` when enqueuing

**For `addSalePayment()`:**
- Accept `clientRequestId: String` as a parameter
- Include it in the `AddSalePaymentRequest` body sent to the API

**For `addCreditRepayment()`:**
- Accept `clientRequestId: String` as a parameter
- Include it in the `CreditRepaymentRequest` payload
- Store it in the `SyncQueueEntity` when enqueuing

#### 2C. Thread `clientRequestId` through sync processing

**File:** `ShopkeeperDataGateway.kt`

**For `syncSaleCreate()`:**
- Read `clientRequestId` from `SyncQueueEntity.clientRequestId`
- Deserialize the `CreateSaleRequest` payload
- Copy the `clientRequestId` into the request before sending (or ensure it was already stored in the payload JSON)

**For `syncCreditRepayment()`:**
- Same approach: read `clientRequestId` from the queue entity and include it in the API request

#### 2D. Handle 409 Conflict in sync processing

**File:** `ShopkeeperDataGateway.kt`

In `runSyncOnce()`, when processing a queue item:
- If the API returns 409: do NOT increment retry count, do NOT delete the row. Release the claim and leave it for the next sync cycle.
- If the API returns 2xx: delete the row (success).
- If the API returns other errors: release the claim and increment retry count.

For direct API calls (`addSalePayment()`):
- If the API returns 409: return a result indicating "payment is being processed" so the UI can show a transient message and re-enable the button.

#### Acceptance criteria

1. Same form session always sends the same `clientRequestId`
2. New form session generates a new `clientRequestId`
3. Queued financial writes store `clientRequestId` in the queue row and send it to the server
4. Direct API calls include `clientRequestId`
5. 409 responses are handled correctly (retry later, don't duplicate)
6. Backend logs show duplicate submissions from the same form are absorbed idempotently

---

### Phase 3: Queue Claim Safety

This phase prevents two sync triggers from processing the same queue item concurrently.

#### 3A. Stale claim recovery on app start

**File:** `ShopkeeperDataGateway.kt`

At the start of `runSyncOnce()` (or on gateway initialization), call:
```kotlin
syncDao.resetStaleClaims()
```

This recovers rows left `inFlight = true` from a previous crash or interruption.

#### 3B. Atomic claim before processing

**File:** `ShopkeeperDataGateway.kt`

Replace the current pattern in `runSyncOnce()`:
```
// BEFORE (race-prone):
val pending = syncDao.getPending(100)
for (item in pending) { process(item) }
```

With:
```
// AFTER (claim-safe):
val candidates = syncDao.getPending(100)
for (item in candidates) {
    val claimed = syncDao.claimRow(item.id)
    if (claimed == 0) continue  // another processor got it
    try {
        process(item)
        syncDao.deleteById(item.id)
    } catch (e: Exception) {
        syncDao.releaseRow(item.id)
        // handle error (409 vs transient vs permanent)
    }
}
```

#### 3C. Direct-call payment guard

**File:** `ShopkeeperDataGateway.kt`

`addSalePayment()` is a direct API call and does not go through the queue. Add an in-memory guard:

```kotlin
private val paymentMutex = Mutex()

suspend fun addSalePayment(..., clientRequestId: String): Result<RecordedSale> {
    if (!paymentMutex.tryLock()) {
        return Result.failure(IllegalStateException("Payment already in progress"))
    }
    try {
        // existing API call logic with clientRequestId
    } finally {
        paymentMutex.unlock()
    }
}
```

This prevents two concurrent coroutines from firing the same payment call. Combined with `clientRequestId`, even if OkHttp transparently retries, the backend deduplicates.

#### Acceptance criteria

1. Two sync triggers cannot process the same queue row concurrently
2. Crash recovery resets stale in-flight rows
3. Two concurrent `addSalePayment()` calls for the same sale are serialized

---

### Phase 4: UI Submission Locking

Apply to all Android mutation screens. This is the user-visible layer.

#### 4A. Sale composer

**File:** `SalesScreen.kt`

- Add `var isSubmittingSale by rememberSaveable { mutableStateOf(false) }` near the existing `isCreatingSale` state
- Before calling `gateway.recordSale()`: set `isSubmittingSale = true`
- On success: set `isSubmittingSale = false`, reset `clientRequestId`, proceed with existing dismissal logic
- On failure: set `isSubmittingSale = false`
- Disable the "Create Sale" button when `isSubmittingSale == true`
- Change button text to `"Creating..."` when submitting

#### 4B. Payment addition

**File:** `SalesScreen.kt`

- Add `var isSubmittingPayment by rememberSaveable { mutableStateOf(false) }` in the payment section
- Before calling `gateway.addSalePayment()`: set `isSubmittingPayment = true`
- On success: set `isSubmittingPayment = false`, reset `clientRequestId`, **dismiss the payment form** (currently does NOT dismiss — this is a new behavior)
- On failure: set `isSubmittingPayment = false`
- Disable the "Add Payment" button when `isSubmittingPayment == true`
- Change button text to `"Saving..."` when submitting

#### 4C. Credit repayment

**File:** `CreditScreen.kt`

- Add `var isSubmittingRepayment by rememberSaveable { mutableStateOf(false) }` in the repayment section
- Before calling `gateway.addCreditRepayment()`: set `isSubmittingRepayment = true`
- On success: set `isSubmittingRepayment = false`, reset `clientRequestId`, **dismiss the repayment form and return to credit detail** (currently does NOT dismiss — this is a new behavior)
- On failure: set `isSubmittingRepayment = false`
- Disable the "Record Repayment" button when `isSubmittingRepayment == true`
- Change button text to `"Recording..."` when submitting

#### 4D. Inventory create/edit

**File:** `InventoryScreen.kt`

- Add `var isSubmittingItem by rememberSaveable { mutableStateOf(false) }` near the form state
- Before calling `gateway.createInventoryItem()` or `gateway.updateInventoryItem()`: set `isSubmittingItem = true`
- On success: set `isSubmittingItem = false`, proceed with existing form reset (already clears fields and sets `isAddingItem = false`)
- On failure: set `isSubmittingItem = false`
- Disable the "Save Item" button when `isSubmittingItem == true`
- Change button text to `"Saving..."` when submitting

#### 4E. Expense create/edit

**File:** `ReportsScreen.kt`

- Add `var isSubmittingExpense by rememberSaveable { mutableStateOf(false) }` near the expense form state
- Before calling `gateway.createExpense()` or `gateway.updateExpense()`: set `isSubmittingExpense = true`
- On success: set `isSubmittingExpense = false`, proceed with existing `resetExpenseEditor()` logic
- On failure: set `isSubmittingExpense = false`
- Disable the "Save Expense" button when `isSubmittingExpense == true`
- Change button text to `"Saving..."` when submitting

#### Acceptance criteria

1. Repeated taps during a single submit attempt do nothing
2. The UI clearly shows that a save is in progress
3. Successful submit leaves no live duplicate-submit path onscreen
4. Payment addition form dismisses on success (new behavior)
5. Credit repayment form dismisses on success (new behavior)
6. Failed submit re-enables the button

---

### Phase 5: Inventory Duplicate Resistance

Inventory is not a generic backend-idempotent write.

#### 5A. Form-session duplicate guard

**File:** `InventoryScreen.kt`

The UI submission lock from Phase 4D already prevents the same form session from submitting twice via repeated taps. No additional queue-level guard is needed because `isSubmittingItem` blocks the coroutine launch.

#### 5B. Suspicious-duplicate warnings

**File:** `InventoryScreen.kt` (or `ShopkeeperDataGateway.kt` for the query)

Before saving a new inventory item, query the local database for potential duplicates:

1. Same serial number (if provided) — exact match
2. Same model number + same product name — exact match
3. Same product name in the same shop — fuzzy warning

If a match is found, show an alert dialog:
- "An item with this serial number already exists. Continue anyway?"
- "A similar item (same model and name) already exists. Continue anyway?"

Allow the user to proceed or cancel. Do not hard-block unless there is a true uniqueness rule.

#### Acceptance criteria

1. Same form session cannot create duplicate inventory entries through repeated taps
2. Suspicious duplicate inventory items produce a visible warning before save
3. User can explicitly continue past the warning

---

### Phase 6: Testing

This must be implemented, not deferred.

#### UI and integration tests

1. Sale submit button disables after first tap
2. Payment submit button disables after first tap
3. Repayment submit button disables after first tap
4. Inventory save disables after first tap
5. Expense save disables after first tap
6. Failed submit re-enables button
7. Successful sale creation dismisses the form
8. Successful payment addition dismisses the payment form
9. Successful repayment dismisses the repayment form

#### Data and queue tests

10. Same financial form session reuses `clientRequestId`
11. New form session generates a different `clientRequestId`
12. Same form session cannot enqueue the same queued write twice
13. Two sync triggers do not process the same row concurrently (`claimRow` returns 0 for the second caller)
14. `resetStaleClaims()` recovers rows left in-flight after crash
15. `addSalePayment()` mutex prevents concurrent calls

#### Financial correctness tests

16. Duplicate sale create with same `clientRequestId` yields one effective sale on the backend
17. Duplicate payment add with same `clientRequestId` yields one effective payment
18. Duplicate repayment with same `clientRequestId` yields one effective repayment
19. 409 Conflict response leaves queue row for retry without incrementing retry count

#### Inventory behavior tests

20. Suspicious duplicate inventory input (same serial number) shows warning
21. Save-anyway path still works when explicitly confirmed

---

## Implementation Order

This is the recommended sequence. Each step builds on the previous.

1. **Phase 1A–1D:** Data layer prerequisites (DTOs, Room schema, DAO methods)
2. **Phase 2A–2D:** Submission identity generation + gateway threading + 409 handling
3. **Phase 3A–3C:** Queue claim safety + payment mutex
4. **Phase 4A–4E:** UI submission locking across all forms
5. **Phase 5A–5B:** Inventory duplicate warnings
6. **Phase 6:** Tests

Rationale for this order:
- Data layer first because UI and queue changes depend on it
- Identity and queue safety before UI locking because correctness is more important than visibility
- UI locking after data layer because it's the easiest to verify visually and gives immediate feedback during development
- Inventory warnings last because they are independent of the financial write safety chain
- Tests last because they validate the full stack, not individual phases

This order is consistent with the strategic priority: identity → queue safety → backend alignment → UI locking → inventory UX.

---

## Definition Of Done

This work is complete only when all of the following are true:

1. No Android financial form can be submitted twice from repeated user taps
2. The same financial form session always reuses the same `clientRequestId`
3. Queued financial writes cannot be processed twice concurrently by sync
4. Direct-call payment add is protected by both a mutex and `clientRequestId`
5. 409 Conflict responses are handled correctly (retry without duplication)
6. Inventory duplicate creation from the same form session is blocked
7. Suspicious inventory duplicates produce a warning
8. Payment addition form dismisses on success
9. Credit repayment form dismisses on success
10. Failure cases re-enable the UI correctly
11. Automated tests prove duplicate-prevention behavior across UI, queue, and backend-aligned financial writes

---

## Changes From V3

1. **Resolved all open decisions** — expense scope, retry policy, Room migration, process death, 409 handling, phase merging
2. **Added exact file paths** for every file that must be modified
3. **Added architecture context** — no ViewModels, no SavedStateHandle, destructive migrations, mixed write paths
4. **Merged Phases 2 and 3** (identity + DTO alignment) into one phase because they are inseparable
5. **Aligned implementation order with strategic conclusions** — identity and queue safety before UI locking
6. **Specified the Room atomic claim mechanism** — `claimRow` returns affected row count, 0 means already claimed
7. **Specified 409 handling** — retry without incrementing count for queue, transient error for direct calls
8. **Specified `clientRequestId` lifecycle** — `rememberSaveable`, reset on success/dismiss, new ID on new session
9. **Specified payment form and repayment form dismissal** as new behaviors that do not exist today
10. **Removed tap debounce** — it was secondary noise; `isSubmitting` + `clientRequestId` + mutex are sufficient
11. **Removed optimistic rollback phase** — useful in principle but risks scope creep and state churn; the existing pull-on-sync-success pattern reconciles state adequately for now
