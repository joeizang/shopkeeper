# Android Duplicate Prevention Plan V5

## Verdict

**Ready for implementation.**

V5 incorporates the V4 review findings and resolves the remaining blockers:

1. queue claims now use ownership + timestamp, not a bare boolean
2. queue DAO methods now distinguish "release claim" from "release and increment retry"
3. credit-repayment success behavior now matches the actual current screen
4. payment-form behavior is corrected to match the existing implementation
5. method names, button names, and test scope now align with the real Android codebase

This version is the implementation baseline.

---

## Purpose

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
3. backend idempotency already exists for sale create, sale payment add, and credit repayment add
4. inventory create is not globally idempotent and must use local duplicate resistance instead

---

## Validated Architecture Context

These points were checked against the current code:

- The Android app uses Composable-managed state with `remember` and `rememberSaveable`.
- The app has no ViewModel or `SavedStateHandle` layer.
- Room uses `fallbackToDestructiveMigration()`.
- `runSyncOnce()` is called from multiple mutation paths and from WorkManager, so claim recovery must be lease-safe rather than a global boolean reset.
- Sale creation and credit repayment are queued writes.
- Sale payment addition is a direct API call.
- The sale payment form in `SalesScreen.kt` already dismisses on success and should keep doing so.
- `CreditScreen.kt` is a single-screen form flow. There is no separate "credit detail" sub-screen to return to.

Backend validation also matches the Android plan:

- `CreateSaleRequest` accepts `ClientRequestId`
- `AddSalePaymentRequest` accepts `ClientRequestId`
- `CreditRepaymentRequest` accepts `ClientRequestId`
- the backend idempotency service returns `409 Conflict` when an identical request is already in progress

---

## Resolved V4 Findings

### Finding 1: Bare `inFlight` is not safe enough

**Resolution:** replace `inFlight: Boolean` with:

```kotlin
val clientRequestId: String? = null
val claimToken: String? = null
val claimedAtEpochMs: Long? = null
```

Reason:

- a boolean cannot distinguish a stale crashed claim from a live active claim
- `runSyncOnce()` can run concurrently from UI-triggered syncs and `SyncWorker`
- claim ownership must be explicit so only the claimer can release or delete the row

### Finding 2: DAO contract must support both 409 and retryable failures

**Resolution:** split release operations into:

1. `releaseClaim()` for "leave row pending, do not increment retry count"
2. `releaseClaimAndIncrementRetry()` for transient failures
3. `deleteClaimedRow()` for successful completion or terminal removal

### Finding 3: Credit repayment success flow in V4 did not match the current UI

**Resolution:** do not introduce a nonexistent "return to credit detail" step.

On successful repayment:

1. re-enable the submit button
2. reset the repayment `clientRequestId`
3. clear the amount field
4. refresh open credits
5. keep the user on `CreditScreen`
6. if the selected sale is still open, keep it selected and reload its latest defaults
7. if the selected sale is now settled, select the next available open credit sale or clear the selection

### Finding 4: Payment-form dismissal statement was incorrect

**Resolution:** preserve the current successful-payment dismissal behavior already present in `SalesScreen.kt`.

The plan should harden this path, not redesign it.

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
| SalesScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt` | Modify |
| CreditScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/credits/CreditScreen.kt` | Modify |
| InventoryScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/inventory/InventoryScreen.kt` | Modify |
| ReportsScreen.kt | `mobile-android/app/src/main/java/com/shopkeeper/mobile/ReportsScreen.kt` | Modify |
| SyncQueueClaimInstrumentedTest.kt | `mobile-android/app/src/androidTest/java/com/shopkeeper/mobile/core/data/SyncQueueClaimInstrumentedTest.kt` | Create |
| SalesCreditsReportsE2ETest.kt | `mobile-android/app/src/androidTest/java/com/shopkeeper/mobile/e2e/SalesCreditsReportsE2ETest.kt` | Modify |
| InventoryFlowE2ETest.kt | `mobile-android/app/src/androidTest/java/com/shopkeeper/mobile/e2e/InventoryFlowE2ETest.kt` | Modify |

`ShopkeeperApi.kt` does not need an interface change. The DTO body changes are sufficient.

---

## Execution Plan

### Phase 1: Data Layer Prerequisites

This phase lands before UI changes.

#### 1A. Add `clientRequestId` to financial DTOs

**File:** `ApiDtos.kt`

Add `val clientRequestId: String? = null` to:

- `CreateSaleRequest`
- `AddSalePaymentRequest`
- `CreditRepaymentRequest`

No Retrofit interface change is needed.

#### 1B. Extend `SyncQueueEntity`

**File:** `Entities.kt`

Add to `SyncQueueEntity`:

```kotlin
val clientRequestId: String? = null
val claimToken: String? = null
val claimedAtEpochMs: Long? = null
```

Do not add `inFlight: Boolean`.

#### 1C. Bump database version

**File:** `ShopkeeperDatabase.kt`

Change `version = 4` to `version = 5`.

Destructive migration remains acceptable because the database already uses `fallbackToDestructiveMigration()`.

#### 1D. Add lease-safe queue claim DAO methods

**File:** `Dao.kt`

Add to `SyncDao`:

```kotlin
@Query(
    """
    UPDATE sync_queue
    SET claimToken = :claimToken,
        claimedAtEpochMs = :claimedAtEpochMs
    WHERE id = :id
      AND (
        claimToken IS NULL
        OR claimedAtEpochMs IS NULL
        OR claimedAtEpochMs < :staleBeforeEpochMs
      )
    """
)
suspend fun claimRow(
    id: Long,
    claimToken: String,
    claimedAtEpochMs: Long,
    staleBeforeEpochMs: Long
): Int

@Query(
    """
    UPDATE sync_queue
    SET claimToken = NULL,
        claimedAtEpochMs = NULL
    WHERE id = :id AND claimToken = :claimToken
    """
)
suspend fun releaseClaim(id: Long, claimToken: String): Int

@Query(
    """
    UPDATE sync_queue
    SET claimToken = NULL,
        claimedAtEpochMs = NULL,
        retryCount = retryCount + 1
    WHERE id = :id AND claimToken = :claimToken
    """
)
suspend fun releaseClaimAndIncrementRetry(id: Long, claimToken: String): Int

@Query("DELETE FROM sync_queue WHERE id = :id AND claimToken = :claimToken")
suspend fun deleteClaimedRow(id: Long, claimToken: String): Int

@Query(
    """
    UPDATE sync_queue
    SET claimToken = NULL,
        claimedAtEpochMs = NULL
    WHERE claimedAtEpochMs IS NOT NULL
      AND claimedAtEpochMs < :staleBeforeEpochMs
    """
)
suspend fun releaseStaleClaims(staleBeforeEpochMs: Long): Int
```

#### Acceptance criteria

1. Financial DTOs include `clientRequestId`
2. `SyncQueueEntity` has `clientRequestId`, `claimToken`, and `claimedAtEpochMs`
3. Database version is bumped
4. DAO supports claim, plain release, release-plus-retry, claimed delete, and stale lease cleanup

---

### Phase 2: Stable Submission Identity + Backend Alignment

This phase generates `clientRequestId` values and threads them through to the backend.

#### 2A. Financial form identity generation

Use mutable `rememberSaveable` state, not a read-only delegated `val`.

Apply this pattern:

```kotlin
var clientRequestId by rememberSaveable { mutableStateOf(UUID.randomUUID().toString()) }
```

Apply to:

- sale creation form in `SalesScreen.kt`
- existing-sale payment form in `SalesScreen.kt`
- repayment form in `CreditScreen.kt`

Reset the ID whenever a form session ends:

```kotlin
clientRequestId = UUID.randomUUID().toString()
```

Reset points:

- after successful submit
- when the form is cancelled or dismissed
- when a different sale is selected for repayment
- when a different existing sale is selected for payment

#### 2B. Thread `clientRequestId` through gateway methods

**File:** `ShopkeeperDataGateway.kt`

Update these signatures:

- `recordSale(input: NewSaleInput, clientRequestId: String)`
- `addSalePayment(..., clientRequestId: String)`
- `addCreditRepayment(input: CreditRepaymentInput, clientRequestId: String)`

Behavior:

- include `clientRequestId` in the DTO sent to the backend
- for queued writes, store the same `clientRequestId` in `SyncQueueEntity.clientRequestId`

#### 2C. Use queue row identity as the sync source of truth

**File:** `ShopkeeperDataGateway.kt`

For queued financial writes:

- write `clientRequestId` into the payload DTO when enqueuing
- also store it in `SyncQueueEntity.clientRequestId`
- during sync, prefer `SyncQueueEntity.clientRequestId` as the canonical value

Concrete rule:

- `syncSaleCreate()` deserializes `CreateSaleRequest`, then sends `payload.copy(clientRequestId = change.clientRequestId ?: payload.clientRequestId)`
- `syncCreditRepayment()` does the same for `CreditRepaymentRequest`

#### 2D. Handle `409 Conflict` correctly by path

**File:** `ShopkeeperDataGateway.kt`

For queued financial writes:

- `SaleCreate` `409` means "same request already processing"
- `CreditRepaymentCreate` `409` means "same request already processing"
- response handling: `releaseClaim()` only
- do not increment retry count
- do not record a conflict
- do not delete the row

For inventory create and generic sync conflicts:

- preserve the current conflict behavior
- inventory and generic sync `409` values are not treated as idempotency-layer in-progress responses

For direct sale payment calls:

- catch `HttpException`
- if `code() == 409`, return a dedicated transient failure such as `DuplicateInProgressException("Payment is being processed, please wait.")`
- re-enable the button in the UI and surface that message

#### Acceptance criteria

1. The same form session always uses the same `clientRequestId`
2. A new form session generates a new `clientRequestId`
3. Queued financial writes store `clientRequestId` in the queue row
4. Direct sale payment calls include `clientRequestId`
5. Financial `409` handling is retry-later, not conflict/deletion
6. Inventory conflict handling remains distinct from financial idempotency handling

---

### Phase 3: Queue Claim Safety

This phase prevents two sync runs from processing the same queue row at the same time.

#### 3A. Use a lease timeout, not a blanket reset

**File:** `ShopkeeperDataGateway.kt`

Add:

```kotlin
private const val SYNC_CLAIM_TIMEOUT_MS = 2 * 60_000L
```

At the start of `runSyncOnce()`:

1. compute `nowMs`
2. compute `staleBeforeMs = nowMs - SYNC_CLAIM_TIMEOUT_MS`
3. call `syncDao.releaseStaleClaims(staleBeforeMs)`

This only releases genuinely stale claims. It does not clear active claims owned by a live sync run.

#### 3B. Claim rows with ownership

**File:** `ShopkeeperDataGateway.kt`

In each `runSyncOnce()` invocation:

1. generate one `runClaimToken = UUID.randomUUID().toString()`
2. read candidate rows with `getPending(100)`
3. for each row, call `claimRow(row.id, runClaimToken, nowMs, staleBeforeMs)`
4. if the result is `0`, skip the row
5. only the claimer may later release or delete the row

#### 3C. Use claim-aware completion paths

**File:** `ShopkeeperDataGateway.kt`

On success:

- call `deleteClaimedRow(row.id, runClaimToken)`

On transient retryable failure:

- call `releaseClaimAndIncrementRetry(row.id, runClaimToken)`

On financial `409` in-progress:

- call `releaseClaim(row.id, runClaimToken)`

On terminal conflict/removal:

- record the conflict if needed
- call `deleteClaimedRow(row.id, runClaimToken)`

#### 3D. Direct sale payment concurrency guard

**File:** `ShopkeeperDataGateway.kt`

Use a per-sale mutex instead of one app-wide mutex:

```kotlin
private val paymentMutexes = ConcurrentHashMap<String, Mutex>()
```

Rule:

- `addSalePayment()` guards by `saleId`
- if the mutex for that sale is already locked, return `IllegalStateException("Payment already in progress")`

This serializes duplicate taps for the same sale without unnecessarily serializing unrelated sales.

#### Acceptance criteria

1. Two sync triggers cannot process the same queue row concurrently
2. Stale claim recovery does not clear an active live claim
3. Only the owning claimer can release or delete a row
4. Two concurrent `addSalePayment()` calls for the same sale are serialized

---

### Phase 4: UI Submission Locking

Apply to all Android mutation screens.

#### 4A. Sale creation form

**File:** `SalesScreen.kt`

Add:

```kotlin
var isSubmittingSale by rememberSaveable { mutableStateOf(false) }
```

Behavior:

- before calling `gateway.recordSale(...)`, set `isSubmittingSale = true`
- pass the sale form `clientRequestId`
- on success or failure, set `isSubmittingSale = false`
- on success, reset the sale `clientRequestId`
- disable the `Save Sale` button while submitting
- change button text from `Save Sale` to `Saving...` while submitting

#### 4B. Existing-sale payment form

**File:** `SalesScreen.kt`

Add:

```kotlin
var isSubmittingPayment by rememberSaveable { mutableStateOf(false) }
```

Behavior:

- before calling `gateway.addSalePayment(...)`, set `isSubmittingPayment = true`
- pass the payment form `clientRequestId`
- on success or failure, set `isSubmittingPayment = false`
- on success, preserve the existing dismissal behavior by clearing `selectedExistingSaleId`
- on success, reset the payment `clientRequestId`
- disable the `Save Payment` button while submitting
- change button text to `Saving...` while submitting

#### 4C. Credit repayment form

**File:** `CreditScreen.kt`

Add:

```kotlin
var isSubmittingRepayment by rememberSaveable { mutableStateOf(false) }
```

Behavior:

- before calling `gateway.addCreditRepayment(...)`, set `isSubmittingRepayment = true`
- pass the repayment form `clientRequestId`
- on success or failure, set `isSubmittingRepayment = false`
- on success, reset the repayment `clientRequestId`
- on success, clear `repaymentAmount`
- on success, refresh open credits and keep the user on the same screen
- if the repaid sale is still open, keep it selected
- if the repaid sale is fully settled, select the next open credit sale or clear the selection
- disable the `Apply Repayment` button while submitting
- change button text to `Recording...` while submitting

#### 4D. Inventory create/edit

**File:** `InventoryScreen.kt`

Add:

```kotlin
var isSubmittingItem by rememberSaveable { mutableStateOf(false) }
```

Behavior:

- before calling `gateway.saveInventoryItem()` or `gateway.updateInventoryItem()`, set `isSubmittingItem = true`
- on success or failure, set `isSubmittingItem = false`
- disable the save button while submitting
- use `Saving...` while submitting

#### 4E. Expense create/edit

**File:** `ReportsScreen.kt`

Add a dedicated expense-submit flag rather than reusing page-wide `isLoading`:

```kotlin
var isSubmittingExpense by rememberSaveable { mutableStateOf(false) }
```

Behavior:

- before calling `gateway.createExpense()` or `gateway.updateExpense()`, set `isSubmittingExpense = true`
- on success or failure, set `isSubmittingExpense = false`
- keep `isLoading` for report loading/export actions
- disable the expense save button while submitting
- use `Saving...` while submitting

#### Acceptance criteria

1. Repeated taps during one submit attempt do nothing
2. The UI shows visible in-progress state
3. Successful submit leaves no active duplicate-submit path onscreen
4. Existing-sale payment form still dismisses on success
5. Credit repayment success matches the actual single-screen credit flow
6. Failed submit re-enables the button

---

### Phase 5: Inventory Duplicate Resistance

Inventory is not a generic backend-idempotent write.

#### 5A. Form-session duplicate guard

`isSubmittingItem` prevents repeated taps from launching duplicate create/update coroutines in the same form session.

#### 5B. Local suspicious-duplicate warnings

**Files:** `ShopkeeperDataGateway.kt`, `InventoryScreen.kt`

Add a gateway helper:

```kotlin
suspend fun getInventoryDuplicateWarnings(
    editingItemId: String?,
    productName: String,
    modelNumber: String?,
    serialNumber: String?
): List<InventoryDuplicateWarning>
```

Implementation approach:

- load local inventory from Room
- normalize strings with `trim().lowercase()`
- exclude `editingItemId`
- detect:
  1. exact serial-number match
  2. exact product-name + model-number match
  3. same product name warning

UI behavior:

- before saving a new or edited item, call the helper
- if warnings exist, show an `AlertDialog`
- the dialog lists the warning reason and offers:
  - `Continue`
  - `Cancel`
- `Continue` proceeds with the existing save path
- `Cancel` returns to the form without saving

#### Acceptance criteria

1. Same form session cannot create duplicate inventory rows through repeated taps
2. Suspicious duplicate inventory inputs produce a visible warning
3. The user can explicitly continue

---

### Phase 6: Testing

This phase is required.

#### 6A. Queue claim tests

**File:** `SyncQueueClaimInstrumentedTest.kt`

Cover:

1. second claimant gets `0` while a live claim is active
2. stale claim becomes claimable after timeout
3. `releaseClaim()` does not increment retry count
4. `releaseClaimAndIncrementRetry()` increments retry count exactly once
5. `deleteClaimedRow()` only deletes when the claim token matches

#### 6B. Financial flow E2E tests

**File:** `SalesCreditsReportsE2ETest.kt`

Add or extend coverage for:

1. `Save Sale` button disables after first tap
2. successful sale creation still completes normally
3. `Apply Repayment` button disables after first tap
4. successful repayment keeps the user on `CreditScreen` and refreshes state
5. payment add flow still dismisses the payment form on success

#### 6C. Inventory tests

**File:** `InventoryFlowE2ETest.kt`

Add or extend coverage for:

1. inventory save button disables after first tap
2. duplicate warning appears for matching serial number
3. cancel path does not save
4. continue path still saves

#### 6D. Manual verification checklist

Before merge:

1. confirm duplicate sale submits with the same `clientRequestId` produce one effective backend sale
2. confirm duplicate sale-payment submits with the same `clientRequestId` produce one effective backend payment
3. confirm duplicate repayment submits with the same `clientRequestId` produce one effective backend repayment
4. confirm financial `409` leaves queued rows pending without retry-count increment
5. confirm inventory conflicts still behave as inventory conflicts, not as idempotency retries

---

## Implementation Order

1. Phase 1: DTOs, Room schema, DAO claim API
2. Phase 2: `clientRequestId` generation and backend threading
3. Phase 3: lease-safe sync claiming and direct payment guard
4. Phase 4: UI submission locking
5. Phase 5: inventory duplicate warnings
6. Phase 6: tests and manual verification

Rationale:

- data contracts must exist before UI can pass stable request identity
- queue safety must be correct before UI locking gives false confidence
- UI locking is straightforward once the data path is correct
- inventory warning UX is independent of financial idempotency

---

## Definition Of Done

This work is complete only when all of the following are true:

1. no Android financial form can be submitted twice from repeated taps
2. the same financial form session always reuses the same `clientRequestId`
3. queued financial writes cannot be processed twice concurrently by sync
4. only the owning sync run can release or delete a claimed queue row
5. financial `409` responses are handled as retry-later without duplicate effects
6. inventory duplicate creation from repeated taps is blocked
7. suspicious inventory duplicates produce a warning
8. successful existing-sale payment still dismisses its form
9. successful credit repayment follows the actual single-screen credit flow
10. failure cases re-enable the UI correctly
11. automated tests cover queue claims, UI locking, and duplicate-prevention behavior

---

## Ready-To-Implement Summary

V5 is ready because the remaining V4 ambiguities are now resolved in concrete, code-aligned terms:

- queue claims are lease-based and owner-aware
- 409 handling is split by financial-idempotent versus non-idempotent paths
- repayment UX now matches the current screen instead of an imagined screen
- button names, gateway methods, and test targets match the real files

This is the version to implement.
