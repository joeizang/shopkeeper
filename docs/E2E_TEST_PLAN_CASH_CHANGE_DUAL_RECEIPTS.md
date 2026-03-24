# E2E Test Plan: Cash Change + Dual Receipts

## Context

This test plan covers the features described in `CASH_CHANGE_DUAL_RECEIPT_PLAN_V2.md`. It follows the existing test patterns established in the codebase:

- **Backend**: `WebApplicationFactory`-based integration tests in `ApiIntegrationTests.cs`
- **Android**: Compose UI tests in `SalesCreditsReportsE2ETest.kt` using `ShopkeeperTestTags`
- **iOS**: XCTest UI tests in `SalesCreditsReportsUITests.swift` using accessibility IDs

All mobile tests use the reset-and-seed pattern via `POST /api/test/reset-and-seed`.

---

## 1. New Test Tags / Accessibility IDs

### Android (`ShopkeeperTestTags.kt`)

```kotlin
const val SALES_PAYMENT_METHOD     = "sales.form.paymentMethod"
const val SALES_CASH_TENDERED      = "sales.form.cashTendered"
const val SALES_CHANGE_DUE         = "sales.form.changeDue"
const val SALES_RECEIPT_CUSTOMER   = "sales.receipt.customer"
const val SALES_RECEIPT_OWNER      = "sales.receipt.owner"
const val SALES_RECEIPT_RETRY      = "sales.receipt.retry"
```

### iOS (accessibility identifiers)

```
sales.form.paymentMethod
sales.form.cashTendered
sales.form.changeDue
sales.receipt.customer
sales.receipt.owner
sales.receipt.retry
```

---

## 2. Seed Data Changes (`E2ETestSeeder.cs`)

Add to the existing seed:

1. A completed cash sale with `CashTendered = 10000` on a split amount of 5912.50, so receipt-viewing tests can run without first creating a sale.
2. Ensure seeded sale lines include populated `CostPriceSnapshot` values (needed for owner receipt profit calculations).
3. Add a mixed-payment sale (one cash split + one transfer split) for aggregate receipt testing.

---

## 3. Backend Integration Tests (`ApiIntegrationTests.cs`)

### 3.1 Cash tendered validation

| # | Test name | Description |
|---|-----------|-------------|
| 1 | `CashSale_PersistsCashTendered` | Create sale with `cashTendered: 10000` on a 5912.50 cash split. GET sale details returns `cashTendered = 10000` on the payment row. |
| 2 | `CashSale_RejectsCashTenderedBelowAmount` | POST sale with `cashTendered: 3000` on a 5000 split. Expect 400 / validation error. |
| 3 | `NonCashPayment_RejectsCashTendered` | POST sale with `method: Transfer` and `cashTendered: 5000`. Expect 400. |
| 4 | `CashSale_OmittedCashTendered_AcceptedForBackwardCompat` | POST cash payment without `cashTendered` (null). Expect 200 — backward compatibility. |

### 3.2 Customer receipt endpoint

| # | Test name | Description |
|---|-----------|-------------|
| 5 | `CustomerReceipt_ReturnsAggregateCashChange` | Create sale with 2 cash splits. GET `/sales/{id}/receipt` returns correct `totalCashTendered`, `totalCashAmount`, `changeDue`. |
| 6 | `CustomerReceipt_NoCash_CashFieldsAreNull` | Create sale with transfer-only payments. Receipt returns null for all cash aggregate fields. |
| 7 | `MixedPaymentSale_AggregatesOnlyCashSplits` | Sale with 1 cash split (3000, tendered 5000) + 1 transfer split (2912.50). Receipt aggregates only the cash portion in `totalCashTendered` and `changeDue`. |

### 3.3 Owner receipt endpoint

| # | Test name | Description |
|---|-----------|-------------|
| 8 | `OwnerReceipt_ReturnsCostAndProfit` | Create sale. GET `/sales/{id}/receipt/owner` as owner. Response includes `costPrice`, `lineProfit`, `totalCogs`, `grossProfit`, `grossMarginPct`. |
| 9 | `OwnerReceipt_SalespersonDenied` | GET `/sales/{id}/receipt/owner` as salesperson. Expect 403. |
| 10 | `OwnerReceipt_ManagerAllowed` | GET `/sales/{id}/receipt/owner` as manager. Expect 200 with full payload. |

---

## 4. Android E2E Tests (`SalesCreditsReportsE2ETest.kt`)

### 4.1 Cash input and validation

| # | Test name | Steps | Assertions |
|---|-----------|-------|------------|
| 1 | `owner_can_create_cash_sale_with_change` | Login as owner → create inventory item → new sale → select Cash method → enter amount 5912.50 → enter cash tendered 10000 → add split → save | "Change Due" displays 4087.50. "Sale saved" text appears. |
| 2 | `cash_split_blocks_save_when_tendered_below_amount` | New sale → cash split amount 5000 → cash tendered 3000 → attempt save | Save button is disabled or a validation error is visible. Sale is NOT saved. |
| 3 | `cash_received_hidden_for_non_cash_method` | New sale → select Transfer method | `SALES_CASH_TENDERED` field does not exist in the compose tree. |

### 4.2 Dual receipt actions

| # | Test name | Steps | Assertions |
|---|-----------|-------|------------|
| 4 | `dual_receipt_actions_shown_after_sale` | Create and save a cash sale | Both `SALES_RECEIPT_CUSTOMER` ("Share Customer Receipt") and `SALES_RECEIPT_OWNER` ("View Owner Receipt") buttons are visible. |
| 5 | `customer_receipt_share_action_works` | Create sale → tap "Share Customer Receipt" | Share intent chooser / PDF viewer opens. |
| 6 | `owner_receipt_opens_in_viewer` | Create sale → tap "View Owner Receipt" | PDF viewer opens in-app. Text containing "INTERNAL" or "CONFIDENTIAL" is visible. |

### 4.3 Mixed payment receipts

| # | Test name | Steps | Assertions |
|---|-----------|-------|------------|
| 7 | `mixed_payment_sale_shows_aggregate_cash_change` | Sale with 1 cash split (3000, tendered 5000) + 1 transfer split (2912.50) → save | Receipt shows "Total Cash Tendered: 5,000" and "Change Due: 2,000". |

---

## 5. iOS E2E Tests (`SalesCreditsReportsUITests.swift`)

### 5.1 Cash input and validation

| # | Test name | Steps | Assertions |
|---|-----------|-------|------------|
| 1 | `testOwnerCanCreateCashSaleWithChange` | Login as owner → create inventory → new sale → cash method → amount 5912.50 → cash tendered 10000 → add split → save | "Change Due" text with 4087.50 visible. Composer dismisses. |
| 2 | `testCashSplitBlocksSaveWhenTenderedBelowAmount` | New sale → cash split 5000 → tendered 3000 → attempt save | Save button disabled or validation message shown. |
| 3 | `testCashReceivedFieldHiddenForTransfer` | New sale → select Transfer | `sales.form.cashTendered` element does not exist. |

### 5.2 Dual receipt actions

| # | Test name | Steps | Assertions |
|---|-----------|-------|------------|
| 4 | `testDualReceiptActionsShownAfterSale` | Create and save a cash sale | `sales.receipt.customer` and `sales.receipt.owner` buttons exist. |
| 5 | `testCustomerReceiptShareable` | Create sale → tap Share Customer Receipt | Share sheet / UIActivityViewController appears. |
| 6 | `testOwnerReceiptOpensInApp` | Create sale → tap View Owner Receipt | In-app PDF viewer visible. "CONFIDENTIAL" text present. |

### 5.3 Role-based receipt visibility

| # | Test name | Steps | Assertions |
|---|-----------|-------|------------|
| 7 | `testSalespersonCannotSeeOwnerReceipt` | Login as salesperson → create sale | `sales.receipt.owner` button does NOT exist. `sales.receipt.customer` button exists. |

---

## 6. Cross-Cutting E2E Tests

These can be implemented on either platform (or both). They test offline-first receipt lifecycle behavior.

| # | Test name | Description |
|---|-----------|-------------|
| 1 | `receiptSurvivesSaleNumberUpgrade` | Create sale offline → verify provisional receipt exists with `LOCAL-*` sale number → trigger sync → verify receipt regenerated with canonical `SL-*` sale number. Check via displayed sale number on the receipt or filename metadata. |
| 2 | `receiptGenerationFailureDoesNotBlockSale` | Simulate PDF generation failure (if testable). Sale is still saved. "Retry Receipt Generation" action (`sales.receipt.retry`) is visible. |

**Note**: Test #1 requires network control (airplane mode toggle or test-mode sync delay). Test #2 may need a test hook to force PDF generation failure. Both are higher complexity and may be deferred to manual verification initially.

---

## 7. Implementation Checklist

| Area | Test | Status |
|------|------|--------|
| Backend | `CashSale_PersistsCashTendered` | [ ] |
| Backend | `CashSale_RejectsCashTenderedBelowAmount` | [ ] |
| Backend | `NonCashPayment_RejectsCashTendered` | [ ] |
| Backend | `CashSale_OmittedCashTendered_AcceptedForBackwardCompat` | [ ] |
| Backend | `CustomerReceipt_ReturnsAggregateCashChange` | [ ] |
| Backend | `CustomerReceipt_NoCash_CashFieldsAreNull` | [ ] |
| Backend | `MixedPaymentSale_AggregatesOnlyCashSplits` | [ ] |
| Backend | `OwnerReceipt_ReturnsCostAndProfit` | [ ] |
| Backend | `OwnerReceipt_SalespersonDenied` | [ ] |
| Backend | `OwnerReceipt_ManagerAllowed` | [ ] |
| Android | `owner_can_create_cash_sale_with_change` | [ ] |
| Android | `cash_split_blocks_save_when_tendered_below_amount` | [ ] |
| Android | `cash_received_hidden_for_non_cash_method` | [ ] |
| Android | `dual_receipt_actions_shown_after_sale` | [ ] |
| Android | `customer_receipt_share_action_works` | [ ] |
| Android | `owner_receipt_opens_in_viewer` | [ ] |
| Android | `mixed_payment_sale_shows_aggregate_cash_change` | [ ] |
| iOS | `testOwnerCanCreateCashSaleWithChange` | [ ] |
| iOS | `testCashSplitBlocksSaveWhenTenderedBelowAmount` | [ ] |
| iOS | `testCashReceivedFieldHiddenForTransfer` | [ ] |
| iOS | `testDualReceiptActionsShownAfterSale` | [ ] |
| iOS | `testCustomerReceiptShareable` | [ ] |
| iOS | `testOwnerReceiptOpensInApp` | [ ] |
| iOS | `testSalespersonCannotSeeOwnerReceipt` | [ ] |
| Cross | `receiptSurvivesSaleNumberUpgrade` | [ ] |
| Cross | `receiptGenerationFailureDoesNotBlockSale` | [ ] |

---

## 8. Dependencies

Tests should be implemented **after** the corresponding feature slices from the v2 plan:

- Backend tests (section 3) → after Phase 1 (backend shape and validation)
- Android tests (section 4) → after Phase 3 (Android UX and PDF generation)
- iOS tests (section 5) → after Phase 4 (iOS UX and PDF generation)
- Cross-cutting tests (section 6) → after Phase 5 (cross-device and hardening)
