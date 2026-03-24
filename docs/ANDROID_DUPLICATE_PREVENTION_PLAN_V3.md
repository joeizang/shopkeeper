# Android Duplicate Prevention Plan V3

## Purpose

This version consolidates the useful parts of V1 and V2 into one implementation-ready plan for Android only.

The target is not just better button behavior. The target is duplicate-safe execution across:

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

## Consolidated Findings From V1

V1 got the product behavior and UX direction largely right.

### Improvements worth keeping from V1

1. Disable submit buttons during in-flight writes
   - `Create Sale`
   - `Add Payment`
   - `Record Repayment`
   - `Save Inventory`
   - `Save Expense`

2. Show visible progress state in the primary action
   - `Creating...`
   - `Saving...`
   - `Recording...`

3. Ignore repeated taps while the form is already submitting

4. Reset or dismiss the form quickly after success
   - do not leave a successful editable form active

5. Treat inventory differently from financial writes
   - do not invent naive backend dedupe for inventory
   - use warnings and session-level protection instead

6. Add Android test coverage for:
   - double tap protection
   - success reset/dismiss behavior
   - failure re-enable behavior

### Weaknesses in V1

1. It was too UI-centric
   - button disabling is necessary but not sufficient

2. It did not front-load the data-layer prerequisites
   - no concrete DTO changes
   - no concrete Room schema changes

3. It under-specified the offline queue problem
   - no atomic claim model
   - no queue-level duplicate identity

4. It treated financial mutation flows too uniformly
   - sale create, repayment, and add-payment do not currently behave the same in Android

5. It did not cover transport retry risk
   - HTTP/client retries can still replay writes

---

## Consolidated Findings From V2

V2 improved the plan materially by matching the real Android architecture.

### Improvements worth keeping from V2

1. Add `clientRequestId` to Android financial DTOs
   - sale create
   - add sale payment
   - credit repayment

2. Extend the Room sync queue schema
   - add `clientRequestId`
   - add `inFlight`
   - optionally add timestamps or ownership markers if needed for debugging/recovery

3. Separate duplicate-prevention strategy by write path
   - queued writes
   - direct-call writes

4. Add queue claim/lock semantics before processing
   - pending rows must be claimed atomically before processing

5. Call out the `addSalePayment()` outlier
   - it is a direct API call today, not queue-backed
   - it needs protection explicitly

6. Treat transport retries as part of the problem
   - duplicate protection is not only a UI concern

7. Expand tests beyond form behavior
   - queue races
   - retry stacking
   - duplicate replay
   - direct-call duplicate protection

8. Surface unresolved scope decisions explicitly
   - expense endpoint idempotency
   - retry behavior policy

### Weaknesses or cautions in V2

1. It can become too broad if implemented as one large pass

2. It risks over-emphasizing optional tactics like tap debounce
   - debounce is secondary
   - stable request identity and queue correctness are primary

3. Retry-policy changes need discipline
   - do not blindly disable all network retries without understanding impact

4. Optimistic rollback needs careful design
   - useful in principle
   - but can add state churn and bugs if not tightly defined

---

## Verified Current-State Findings In Android Code

These are the findings that must shape implementation.

### 1. Financial DTOs do not currently carry `clientRequestId`

Current gap exists in:

- `CreateSaleRequest`
- `AddSalePaymentRequest`
- `CreditRepaymentRequest`

This means backend idempotency is not being fully used by Android today.

### 2. `SyncQueueEntity` does not currently carry duplicate-prevention state

Current gap:

- no `clientRequestId`
- no `inFlight`

This means the queue cannot currently:

- identify a form submission cleanly
- atomically claim work for processing

### 3. Financial write paths are inconsistent

Current behavior:

- sale create is queue-backed
- credit repayment is queue-backed
- add sale payment is a direct API call

This means one protection model will not cover all financial flows unless the plan explicitly handles both paths.

### 4. Queue processing is race-prone

Current risk:

- pending rows are fetched and iterated
- they are not atomically claimed first

This means two sync triggers can process the same item twice.

### 5. Network retry behavior is part of the risk surface

The Android client currently uses a normal OkHttp client setup. That means replay risk must be handled by:

1. request identity
2. backend idempotency
3. queue correctness

not by UI locking alone.

### 6. Inventory still needs a different model

Inventory create does not naturally map to backend idempotency the same way financial writes do.

The correct approach is:

- prevent duplicate submission from the same form session
- warn on suspicious duplicates
- do not globally dedupe valid user intent

---

## V3 Strategic Conclusions

### Conclusion 1

The final plan must treat duplicate prevention as a full write-path integrity problem, not a button-state problem.

### Conclusion 2

V1 provides the right UX rules.

### Conclusion 3

V2 provides the right technical model.

### Conclusion 4

The correct final plan is:

- V1 UX behavior
- V2 data-layer and queue-layer protection
- phased execution

### Conclusion 5

Implementation should prioritize correctness in this order:

1. stable submission identity
2. queue claim safety
3. backend idempotency alignment
4. UI submission locking
5. duplicate-warning UX for inventory
6. broader retry-policy tuning only if needed

---

## V3 Execution Plan

## Phase 0: Scope and Rules

This work applies to Android only.

Operations in scope:

1. create sale
2. add sale payment
3. record credit repayment
4. create inventory item
5. update inventory item
6. create expense
7. update expense

Rules:

1. financial writes must become duplicate-safe
2. inventory writes must become duplicate-resistant
3. direct and queued mutation flows must both be covered
4. no shortcuts that leave queue replay risk unaddressed

---

## Phase 1: UI Submission Locking

Apply to all Android mutation screens.

### Requirements

1. add `isSubmitting` state per screen or sheet
2. disable the primary action while submitting
3. change button label or show progress indicator
4. ignore repeated clicks while `isSubmitting == true`
5. re-enable on failure
6. dismiss/reset on success

### Targets

1. sale composer
2. existing sale payment sheet
3. credit repayment form
4. inventory create/edit form
5. expense create/edit form

### Acceptance criteria

1. repeated taps during a single submit attempt do nothing
2. the UI clearly shows that a save is in progress
3. successful submit leaves no live duplicate-submit path onscreen

---

## Phase 2: Stable Form Submission Identity For Financial Writes

This phase creates the identity needed to align Android with backend idempotency.

### Requirements

1. generate a `clientRequestId` when a financial form session begins
2. persist it in form state for the lifetime of that form session
3. reuse the same value on retry from the same form session
4. retire it only when the form succeeds, is dismissed, or is explicitly reset

### Applies to

1. create sale
2. add sale payment
3. record credit repayment

### Acceptance criteria

1. same form retry reuses the same request identity
2. different intentional submissions use different request identities

---

## Phase 3: DTO Alignment

The Android API models must support the submission identity.

### Required DTO changes

Add `clientRequestId` to:

1. `CreateSaleRequest`
2. `AddSalePaymentRequest`
3. `CreditRepaymentRequest`

### Acceptance criteria

1. Android sends `clientRequestId` on all financial writes
2. backend logs and behavior show that repeated financial submissions from the same form are absorbed idempotently

---

## Phase 4: Queue Schema Upgrade

The Room queue must be capable of representing submission identity and processing ownership.

### Required `SyncQueueEntity` changes

Add:

1. `clientRequestId: String?`
2. `inFlight: Boolean`

Optional but recommended:

3. `claimedAtUtcIso: String?`
4. `lastAttemptAtUtcIso: String?`

### Why

This is required to:

1. prevent the same form session from being queued ambiguously
2. claim rows safely before processing
3. recover/debug stuck work

### Acceptance criteria

1. queued financial mutations carry `clientRequestId`
2. queue records can be marked in-flight during sync processing

---

## Phase 5: Queue Claim Safety

This phase addresses the structural duplicate risk in offline sync.

### Requirements

1. do not process pending queue items directly after a plain fetch
2. atomically claim rows before processing
3. only the worker that claimed a row may process it
4. clear or release claims correctly on success/failure
5. recover rows left in-flight after crash or interruption

### Minimum design

1. fetch candidate row ids
2. atomically mark them `inFlight = true`
3. process only claimed rows
4. delete on success or unclaim/update retry state on failure

### Acceptance criteria

1. two sync triggers cannot process the same queue row concurrently
2. crash recovery does not permanently orphan rows

---

## Phase 6: Direct-Call Financial Protection

This covers the current `addSalePayment()` outlier.

### Requirements

1. `addSalePayment()` must use a stable `clientRequestId`
2. its UI must also use `isSubmitting`
3. retries from the same open payment form must reuse the same request identity

### Optional future improvement

Unify add-payment with the queue model later if product behavior requires consistent offline add-payment support.

### Acceptance criteria

1. payment add cannot be duplicated by repeated user taps
2. payment add cannot be duplicated by ambiguous client retry using the same request identity

---

## Phase 7: Inventory Duplicate Resistance

Inventory is not a generic backend-idempotent write.

### Requirements

1. apply UI submission lock to inventory save
2. block repeat queueing from the same form session
3. add local suspicious-duplicate checks before save

### Duplicate-warning heuristics

1. same serial number
2. same model + product name
3. same exact product name in same shop

### UX behavior

1. warn the user
2. allow explicit continue where business rules permit
3. only hard-block if there is a real uniqueness rule such as serial-number uniqueness

### Acceptance criteria

1. same form session cannot create duplicate inventory entries through repeated taps
2. suspicious inventory duplicates produce a visible warning before save

---

## Phase 8: Expense Write Decision

Expenses need an explicit scope decision.

### Option A

Treat expense create/update as idempotent-protected writes similar to financial flows.

Use when:

- expense duplication is operationally costly
- expense submission happens under unstable network conditions

### Option B

Apply only UI submission locking and form-session duplicate prevention for now.

Use when:

- you want to keep scope tighter
- expense duplication risk is lower than sales/payment risk

### Recommended choice

Option B for first implementation pass, unless expense duplication has already proven to be a real operational issue.

Reason:

- sales/payments/repayments are the highest-risk writes
- expenses can be hardened later without weakening the core financial integrity work

---

## Phase 9: Transport Retry Policy

Do not overreact here.

### Rule

Do not treat "disable all OkHttp retries" as the primary solution.

### Recommended approach

1. rely first on `clientRequestId` and backend idempotency for financial writes
2. rely on queue claim safety for queued mutations
3. keep UI submission locking in place
4. only narrow retry behavior if test evidence shows replay patterns that survive the protections above

### Acceptance criteria

1. financial writes remain safe even if transport retries occur
2. retry policy is evidence-driven, not guess-driven

---

## Phase 10: Testing Plan

This must be implemented, not deferred.

### UI and integration tests

1. sale submit button disables after first tap
2. payment submit button disables after first tap
3. repayment submit button disables after first tap
4. inventory save disables after first tap
5. failed submit re-enables button
6. success dismisses or resets the form

### Data and queue tests

7. same financial form session reuses `clientRequestId`
8. same form session cannot enqueue the same queued write twice
9. two sync triggers do not process the same row concurrently
10. in-flight row recovery works after interrupted sync

### Financial correctness tests

11. duplicate sale create with same `clientRequestId` yields one effective sale
12. duplicate payment add with same `clientRequestId` yields one effective payment
13. duplicate repayment with same `clientRequestId` yields one effective repayment

### Inventory behavior tests

14. suspicious duplicate inventory input shows warning
15. save-anyway path still works when explicitly confirmed

---

## Phase 11: Implementation Order

This is the recommended order for flawless execution.

1. add UI submission locking to financial forms
2. add `clientRequestId` to Android financial DTOs
3. introduce stable form-session request identity
4. update Room queue schema with `clientRequestId` and `inFlight`
5. implement queue claim safety in sync processing
6. protect direct-call `addSalePayment()` path
7. add inventory duplicate-warning flow
8. apply expense locking
9. add tests for UI, queue, and financial replay
10. review retry behavior only after the above is complete

---

## Definition Of Done

This work is complete only when all of the following are true:

1. no Android financial form can be submitted twice from repeated user taps
2. the same financial form session always reuses the same `clientRequestId`
3. queued financial writes cannot be processed twice concurrently by sync
4. direct-call payment add is protected at the same integrity level as queued financial writes
5. inventory duplicate creation from the same form session is blocked
6. suspicious inventory duplicates produce a warning
7. failure cases re-enable the UI correctly
8. automated tests prove duplicate-prevention behavior across UI, queue, and backend-aligned financial writes

---

## Final Recommendation

Use this V3 plan as the implementation baseline.

Why:

1. it preserves V1's useful UX constraints
2. it preserves V2's architecture-aware protections
3. it removes the weak parts of both plans
4. it gives a realistic order of execution
5. it is strict enough to avoid partial duplicate-prevention theater

The critical engineering rule is simple:

**Do not ship this as "fixed" if only the buttons are disabled.**

The real fix is:

1. UI lock
2. stable submission identity
3. queue claim safety
4. backend idempotency alignment

That is the standard required for duplicate-safe Android execution here.
