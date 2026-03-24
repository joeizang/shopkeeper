# Plan v2: Cash Tendered + Offline-First Dual Receipts

## Status

This document replaces the original receipt plan as the implementation baseline. The original plan was directionally right but not implementation-ready because it treated receipts as if backend truth is always available immediately after save. That is incompatible with the app's offline-first sales flow.

This v2 plan is grounded in the current codebase:
- Backend Minimal API: `/Users/josephizang/Projects/vibes/shopkeeper/backend-api/src/Shopkeeper.Api/Endpoints/SalesEndpoints.cs`
- Backend sales contracts: `/Users/josephizang/Projects/vibes/shopkeeper/backend-api/src/Shopkeeper.Api/Contracts/SalesContracts.cs`
- Backend sales entities: `/Users/josephizang/Projects/vibes/shopkeeper/backend-api/src/Shopkeeper.Api/Domain/SalesEntities.cs`
- Android sales flow: `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt`
- Android data gateway: `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/ShopkeeperDataGateway.kt`
- Android receipt PDF generation: `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/receipts/ReceiptPdfGenerator.kt`
- iOS sales/data flow: `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios/ShopkeeperIOS/AppCore.swift`
- iOS sales UI and receipt actions: `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios/ShopkeeperIOS/FeatureViews.swift`

---

## 1. Final Decisions

### 1.1 Receipt availability
- Customer receipts must be available immediately after a sale is saved locally.
- Owner receipts must also be available immediately after a sale is saved locally.
- Backend receipt endpoints are not the primary post-sale path on the recording device.
- Backend receipt endpoints remain important for cross-device viewing and later canonical re-fetch.

### 1.2 Cash and change data model
- Persist `CashTendered` only.
- Do not persist `ChangeGiven`.
- Compute `ChangeGiven = CashTendered - Amount` in read models and local receipt builders.

### 1.3 Split payment rule
- A sale may contain multiple cash splits.
- Validation is per cash split: every cash split must satisfy `cashTendered >= split.amount`.
- Receipt display is aggregate: receipts show one `Total Cash Tendered` line and one `Change Due` line across all cash splits.

### 1.4 Owner receipt availability and sensitivity
- Owner receipts are first-class offline artifacts.
- Owner receipt generation on the local device uses local sale data, local cost snapshots, and the current session user's display name.
- Owner receipts are confidential and should not be share-first documents.

### 1.5 Sale number lifecycle
- Local-first sale save produces a provisional sale number like `LOCAL-{timestamp}`.
- Once sync assigns the canonical server sale number `SL-YYYYMMDD-XXXX`, receipts are regenerated silently and local metadata is updated.
- The system must treat provisional and canonical receipts as two versions of the same sale artifact, not as unrelated files.

---

## 2. Critical Corrections From v1

1. Receipt generation is no longer backend-first.
- v1 assumed receipt rendering would primarily follow `GET /api/v1/sales/{id}/receipt`.
- v2 makes local receipt generation the primary path and backend receipt endpoints the reconciliation and cross-device path.

2. `ChangeGiven` is removed from persistence scope.
- v1 proposed storing it.
- v2 removes that redundancy.

3. Split cash validation is explicit.
- v1 treated save gating too much like a single-payment flow.
- v2 validates each cash split individually and aggregates only for receipt display.

4. Owner receipt handling is explicitly security-aware.
- v1 described it as another PDF.
- v2 treats it as a confidential internal document with different UX and caching rules.

5. File/version handling is now defined.
- v1 used `{saleNumber}` filenames too casually.
- v2 uses sale identity plus receipt version metadata so local-to-server sale-number transitions do not corrupt receipt history.

6. Failure behavior is now part of the design.
- v1 assumed sale success implies receipt success.
- v2 defines receipt-generation failure states and retry behavior.

---

## 3. Architecture Overview

There are now three receipt paths.

### 3.1 Path A: Local provisional receipt generation
Used immediately after a sale is saved on the current device.

Input source:
- local sale draft / locally recorded sale
- local shop settings
- local line items
- local payment splits
- local `CostPriceSnapshot`
- local authenticated user display name

Outputs:
- customer receipt PDF
- owner receipt PDF
- receipt metadata record indicating:
  - provisional sale number
  - local sale id
  - receipt version `local`
  - generated-at timestamp
  - generation status

### 3.2 Path B: Sync reconciliation and silent regeneration
Used when the server accepts the sale and returns canonical identifiers.

Input source:
- synced sale mapping: local sale identity -> server sale id / canonical sale number
- optionally backend receipt view for canonical read validation

Outputs:
- regenerated customer receipt PDF
- regenerated owner receipt PDF
- metadata updated from provisional to canonical
- UI continues to reference the latest version transparently

### 3.3 Path C: Cross-device backend receipt retrieval
Used when a device needs a receipt for a sale it did not record locally.

Input source:
- server sale id
- backend receipt endpoint(s)

Outputs:
- customer receipt view for local PDF generation or preview
- owner receipt view for authorized owner/manager use only

---

## 4. Backend Plan

## 4.1 Entity changes

### `SalePayment`
Current state:
- `Method`
- `Amount`
- `Reference`
- no `CashTendered`

Required change:
```csharp
CashTendered: decimal?
```
Rules:
- nullable
- only meaningful when `Method == Cash`
- must be null for non-cash payments
- must be `>= Amount` for cash payments when provided

No `ChangeGiven` column is added.

Migration required:
- add nullable `CashTendered` column to `SalePayments`

## 4.2 Contract changes

### `SalePaymentRequest`
Current state in `SalesContracts.cs`:
```csharp
public sealed record SalePaymentRequest(PaymentMethod Method, decimal Amount, string? Reference);
```

Required change:
```csharp
public sealed record SalePaymentRequest(
    PaymentMethod Method,
    decimal Amount,
    string? Reference,
    decimal? CashTendered);
```

Apply the same shape extension anywhere the API accepts or returns sale payments, including:
- initial sale payments on create sale
- add-payment endpoint request contract if cash add-payments are allowed
- receipt response models that currently reuse `SalePaymentRequest`

## 4.3 Validation rules

### Sale creation / add-payment validation
- `Amount > 0`
- if `Method == Cash`:
  - `CashTendered` is recommended for new clients
  - if provided, it must be `>= Amount`
- if `Method != Cash`:
  - `CashTendered` must be null
- aggregate sale payment validation stays separate from per-split cash validation
- existing due-date rules for outstanding balance remain unchanged

### Backward compatibility
- older clients may omit `CashTendered`
- backend should accept null and simply omit cash/change lines in backend-generated receipt views when cash tendered is unavailable
- once both mobile apps are updated, `CashTendered` becomes operationally expected for cash payments even if the API still technically tolerates null for backward compatibility

## 4.4 Receipt response models

The current `ReceiptView` in `SalesContracts.cs` is customer-oriented but still too thin for the new receipt model.

### Customer receipt response
Keep `GET /api/v1/sales/{id}/receipt` as customer receipt.

Extend the response with explicit aggregate cash data:
```csharp
TotalCashAmount: decimal?
TotalCashTendered: decimal?
ChangeDue: decimal?
```

Rules:
- these are aggregate cash-only fields across all cash splits
- if there are no cash payments, these are null
- `ChangeDue = TotalCashTendered - TotalCashAmount`

### Owner receipt response
Add a new response model instead of overloading the customer contract.

New endpoint:
```http
GET /api/v1/sales/{id}/receipt/owner
```
Authorization:
- `Owner`
- `ShopManager`
- not `Salesperson`

`OwnerReceiptView` should contain:
- everything needed for the customer receipt
- `CreatedByName`
- line-level `CostPrice`
- line-level `LineProfit`
- `TotalCogs`
- `GrossProfit`
- `GrossMarginPct`
- payment detail rows including `CashTendered` where applicable

Computation rules:
- cost values come from `CostPriceSnapshot` on `SaleLine`
- profit values are derived at read time from stored sale and snapshot values

## 4.5 Backend receipt generation responsibility
The backend does not need to return PDFs as the primary path for this feature.

Backend responsibility is:
- canonical data model
- canonical receipt views for cross-device use
- correct aggregation and authorization

Mobile responsibility is:
- immediate PDF generation after local save
- regeneration after sync

## 4.6 Security and cache behavior

### Customer receipt endpoint
- `Cache-Control: private, no-cache`
- ETag allowed if it matches the canonical sale row/version model

### Owner receipt endpoint
- `Cache-Control: private, no-store` is preferred
- if ETag is used at all, it must stay private and revalidated
- do not make owner receipt downloads or cached artifacts broadly shareable by default

Rationale:
- owner receipts contain confidential internal finance data
- the backend caching layer already exists, so cache behavior must be explicit here

## 4.7 Backend implementation slices

### Files likely to change
- `/Users/josephizang/Projects/vibes/shopkeeper/backend-api/src/Shopkeeper.Api/Domain/SalesEntities.cs`
- `/Users/josephizang/Projects/vibes/shopkeeper/backend-api/src/Shopkeeper.Api/Contracts/SalesContracts.cs`
- `/Users/josephizang/Projects/vibes/shopkeeper/backend-api/src/Shopkeeper.Api/Endpoints/SalesEndpoints.cs`
- backend migration file under `Migrations/`

### Expected changes
1. Add `CashTendered` to `SalePayment`
2. Extend request contracts
3. Persist `CashTendered` on sale creation and add-payment flows
4. Extend customer receipt response with aggregate cash/change fields
5. Add `OwnerReceiptView`
6. Add `GET /api/v1/sales/{id}/receipt/owner`
7. Enforce authorization and cache headers
8. Add integration tests for:
   - cash split validation
   - aggregate change calculation
   - owner endpoint authorization
   - owner receipt payload correctness

---

## 5. Android Plan

## 5.1 Sales composer changes

Current file:
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt`

Current local state:
- `UiSalePayment` has no `cashTendered`

Required state change:
```kotlin
cashTendered: Double?
```

### UI behavior
When payment method is cash:
- show `Cash Received` input for that split
- show split-level `Change Due` immediately
- validate `cashTendered >= amount` for that split

When payment method is not cash:
- hide `Cash Received`
- clear any stale `cashTendered`

### Save gating
The sale can be saved only when:
- each payment split is valid
- each cash split individually satisfies the tendered-amount rule
- existing credit due-date rules are satisfied
- existing total and item validations are satisfied

This is not a single-field cash gate. It is split-aware validation.

## 5.2 Android DTO and gateway changes

### DTO changes
File:
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/ApiDtos.kt`

Extend `SalePaymentRequest` with:
```kotlin
val cashTendered: Double?
```

### Gateway changes
File:
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/ShopkeeperDataGateway.kt`

Required changes:
- extend `NewSalePaymentInput`
- persist local `cashTendered`
- include `cashTendered` in outbound sale sync payloads
- maintain enough local sale/payment data to build provisional receipts immediately after save

## 5.3 Android receipt model and generation

Current generator file:
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/receipts/ReceiptPdfGenerator.kt`

Current state:
- only `generateSampleReceipt(...)`

Required replacement:
```kotlin
fun generateCustomerReceipt(...): File
fun generateOwnerReceipt(...): File
```

### Customer receipt requirements
- compact thermal-style layout
- shop name
- sale number
- sale timestamp
- cashier name
- line items
- subtotal / VAT / discount / total
- aggregate `Total Cash Tendered` and `Change Due` when cash exists
- customer-safe footer

### Owner receipt requirements
- internal layout, printable and readable
- all customer receipt data plus:
  - line cost price
  - line profit
  - total COGS
  - gross profit
  - gross margin %
  - internal staff identifier/name
- clear `INTERNAL / CONFIDENTIAL` marking
- open in viewer by default, not share-first

## 5.4 Android receipt metadata and file lifecycle

Do not key only by `saleNumber`.

Required local metadata concept:
- sale local id or stable local sale key
- optional server sale id
- receipt kind: `customer` or `owner`
- version: `local` or `canonical`
- file path
- generated-at timestamp
- generation status

Suggested filename pattern:
- provisional: `{localSaleId}-local-customer.pdf`
- provisional: `{localSaleId}-local-owner.pdf`
- canonical: `{saleId}-canonical-customer.pdf`
- canonical: `{saleId}-canonical-owner.pdf`

The app should always resolve "current receipt for this sale and kind" through metadata, not by guessing filenames.

## 5.5 Android post-sale UX

After local save succeeds:
1. record sale locally
2. generate customer receipt locally
3. generate owner receipt locally
4. show success state with two distinct actions:
   - `Share Customer Receipt`
   - `View Owner Receipt`
5. if sync later canonicalizes the sale number, regenerate silently and update the backing file references

### Failure behavior
Possible states:
- sale saved, both receipts generated
- sale saved, customer receipt failed
- sale saved, owner receipt failed
- sale saved, both receipts failed

Required UX:
- do not mark the sale itself as failed because PDF generation failed
- surface receipt-generation failure separately
- provide `Retry Receipt Generation`

## 5.6 Android files likely to change
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/sales/SalesScreen.kt`
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/remote/ApiDtos.kt`
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/core/data/ShopkeeperDataGateway.kt`
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-android/app/src/main/java/com/shopkeeper/mobile/receipts/ReceiptPdfGenerator.kt`
- local persistence files if receipt metadata is stored in Room or app storage indexes

---

## 6. iOS Plan

## 6.1 Sales composer changes

Current iOS files:
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios/ShopkeeperIOS/AppCore.swift`
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios/ShopkeeperIOS/FeatureViews.swift`

The v1 plan referenced `SaleComposerView` and `SalesView` conceptually. Implementation must align with the actual current code in `FeatureViews.swift` and `AppCore.swift`.

Required model change:
- extend `SalePaymentRequest` with `cashTendered`

UI behavior mirrors Android:
- for cash payments, show `Cash Received`
- compute split-level change live
- validate each cash split individually
- aggregate cash/change only for receipt display

## 6.2 iOS receipt generation

Current state:
- `AppCore.swift` currently fetches `api/v1/sales/{id}/receipt` after sale creation
- `generateReceiptPdf(_ receipt: ReceiptView)` currently builds a single receipt PDF
- `lastReceipt` is singular and not sufficient for the new flow

Required replacement:
```swift
func generateCustomerReceiptPdf(_ receipt: CustomerReceiptModel) throws -> URL
func generateOwnerReceiptPdf(_ receipt: OwnerReceiptModel) throws -> URL
```

These should be able to work from local models, not only server response models.

## 6.3 iOS local-first sale flow

Current issue:
- the current create-sale path is too backend-receipt-oriented

Required behavior:
1. save sale locally / submit sale through existing flow
2. build provisional customer receipt model locally
3. build provisional owner receipt model locally
4. generate both PDFs locally
5. keep receipt metadata for later canonical regeneration
6. when server canonicalizes the sale number, regenerate silently

## 6.4 iOS receipt actions and confidentiality

Replace the single post-sale receipt action with:
- `Share Customer Receipt`
- `View Owner Receipt`

Owner receipt behavior:
- open in-app by default
- not share-first
- explicit export/share only if the role and UX allow it

## 6.5 iOS files likely to change
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios/ShopkeeperIOS/AppCore.swift`
- `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios/ShopkeeperIOS/FeatureViews.swift`
- any iOS PDF/viewer helper file if receipt rendering is split out from `AppCore.swift`

---

## 7. Reconciliation Rules

## 7.1 Local -> canonical receipt upgrade
When sync succeeds and the sale receives a canonical server sale number:
1. locate receipt metadata for the local sale
2. build canonical receipt models using canonical sale number and any server-confirmed identifiers
3. regenerate both customer and owner PDFs
4. mark canonical receipt files as current
5. optionally delete provisional files or keep them only until replacement succeeds

## 7.2 If sync has not completed
- customer receipt remains usable with local sale number
- owner receipt remains usable with local sale number
- UI should not imply the sale failed merely because it is still provisional

## 7.3 If sync fails permanently or is delayed
- provisional receipts stay available
- the app may show a subtle badge that the sale is pending sync, not that the receipt is invalid

---

## 8. Data Shapes To Introduce

This feature is simpler if mobile receipt generation uses dedicated local models instead of directly coupling PDF generation to server DTOs.

Recommended conceptual models:
- `CustomerReceiptModel`
- `OwnerReceiptModel`
- `ReceiptFileMetadata`

These can be mapped from:
- local sale + shop + user state
- or backend receipt DTOs for cross-device fetch

That avoids forcing all local receipt generation through backend-only response types.

---

## 9. Testing Plan

## 9.1 Backend tests
Add integration tests for:
1. create sale with one cash payment and valid `CashTendered`
2. reject cash payment where `CashTendered < Amount`
3. reject non-cash payment that includes `CashTendered`
4. customer receipt returns aggregate cash/change
5. owner receipt returns cost/profit fields
6. salesperson cannot access owner receipt endpoint
7. owner and manager can access owner receipt endpoint

## 9.2 Android tests
Add or extend tests for:
1. cash split shows `Cash Received`
2. split-level change updates live
3. invalid cash split disables sale save
4. local customer receipt generates immediately after offline save
5. local owner receipt generates immediately after offline save
6. synced sale silently regenerates receipt with canonical sale number
7. share customer receipt works
8. owner receipt opens in viewer, not share-first

## 9.3 iOS tests
Add or extend tests for:
1. cash split input and change calculation
2. save blocked for invalid cash split
3. local customer receipt generated after save
4. local owner receipt generated after save
5. canonical regeneration after sync updates sale number
6. owner receipt view opens in-app

## 9.4 Manual verification checklist
1. offline sale save at the counter generates a usable customer receipt immediately
2. owner receipt shows cost and margin data offline
3. mixed payment sale with one cash split computes aggregate receipt cash/change correctly
4. receipt file naming does not break when sale number changes after sync
5. cross-device owner receipt fetch works only for owner/manager roles

---

## 10. Implementation Order

### Phase 1: Backend shape and validation
1. add `CashTendered` to `SalePayment`
2. add migration
3. extend request contracts
4. enforce cash validation
5. extend customer receipt response
6. add owner receipt endpoint and tests

### Phase 2: Local receipt models
1. introduce customer/owner local receipt models on Android and iOS
2. stop treating backend receipt DTO as the only receipt source
3. add receipt metadata tracking

### Phase 3: Android UX and PDF generation
1. add split cash input and validation
2. replace sample receipt generator
3. add dual receipt actions
4. add regeneration path after sync

### Phase 4: iOS UX and PDF generation
1. add split cash input and validation
2. replace single receipt path with dual receipt path
3. add regeneration path after sync

### Phase 5: Cross-device and hardening
1. consume owner receipt endpoint for non-local sales
2. apply confidentiality and cache rules consistently
3. add failure/retry UX for PDF generation
4. add full test coverage

---

## 11. Scope Checklist

| Area | Work Item | Status |
|---|---|---|
| Backend | Add `CashTendered` to `SalePayment` | [ ] |
| Backend | Extend `SalePaymentRequest` and add-payment contracts | [ ] |
| Backend | Enforce per-cash-split validation | [ ] |
| Backend | Extend customer receipt with aggregate cash/change fields | [ ] |
| Backend | Add `OwnerReceiptView` | [ ] |
| Backend | Add `GET /sales/{id}/receipt/owner` with role enforcement | [ ] |
| Backend | Add backend receipt integration tests | [ ] |
| Android | Add `cashTendered` to sale payment UI state | [ ] |
| Android | Add split cash input and validation | [ ] |
| Android | Extend DTOs/gateway/local sale mapping | [ ] |
| Android | Replace sample receipt PDF generation with real dual generators | [ ] |
| Android | Add receipt metadata/version tracking | [ ] |
| Android | Add post-sale `Share Customer Receipt` / `View Owner Receipt` flow | [ ] |
| Android | Add silent canonical regeneration after sync | [ ] |
| iOS | Add `cashTendered` to payment model | [ ] |
| iOS | Add split cash input and validation | [ ] |
| iOS | Replace single receipt generation with dual local generators | [ ] |
| iOS | Add receipt metadata/version tracking | [ ] |
| iOS | Add post-sale customer/owner receipt actions | [ ] |
| iOS | Add silent canonical regeneration after sync | [ ] |
| Cross-platform | Verify local-to-canonical receipt transition | [ ] |
| Cross-platform | Verify owner receipt confidentiality behavior | [ ] |

---

## 12. Ready-For-Implementation Standard

This feature is ready to implement only if the team agrees to these constraints:
1. local receipts are the primary path
2. canonical backend receipts are secondary and cross-device oriented
3. `CashTendered` is persisted, `ChangeGiven` is not
4. receipt display aggregates cash/change while validation stays split-specific
5. owner receipts are confidential and offline-capable
6. receipt files are versioned by sale identity, not naive sale-number filenames

If any of those change, this plan needs revision before implementation starts.


## Finalized Decisions
- Repayments do not regenerate customer or owner receipts. Credit repayment records remain separate financial events.
- Additional sale payments should produce a new receipt version that still references the same sale. The sale remains the anchor document unless the sale is cancelled.
- Owner receipt export/view policy is `Owner + ShopManager`.
- Receipt metadata lives in platform-local structured storage:
  - Android: Room
  - iOS: Core Data-backed metadata store
- The primary mapping path is `localSaleId -> serverSaleId -> canonicalSaleNumber`.
  - `localSaleId` is the stable offline anchor for receipt metadata and file versioning.
  - `serverSaleId` is attached after sync/server acceptance.
  - `saleNumber` is display data only and may change from provisional `LOCAL-*` to canonical `SL-*`.

