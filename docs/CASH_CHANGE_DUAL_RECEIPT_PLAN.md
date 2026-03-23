# Plan: Cash Change Calculation + Dual Receipt Generation

## Overview

When a customer pays with cash, the app should:
1. Accept a "Cash Received" amount and calculate change due in real-time
2. Close the sale once payment is confirmed
3. Generate two distinct receipts:
   - **Customer Receipt** — shareable, itemized, shows change given
   - **Owner/Internal Receipt** — confidential, includes cost prices, profit margins, staff info

Three main areas of work: **backend data model + endpoints**, **Android**, **iOS**.

---

## 1. Backend API Changes

### 1a. Store `cashTendered` on cash payments

The `SalePayment` entity currently only has `Method`, `Amount`, and `Reference`. We need to persist how much cash was actually handed over so receipts are accurate later.

**Entity change — `SalePayment`:**
```
+ CashTendered: decimal?   // only populated for Method == Cash
+ ChangeGiven:  decimal?   // computed: CashTendered - Amount, stored for audit
```
**EF migration required.**

**Contract change — `SalePaymentRequest`:**
```
+ cashTendered: decimal?
```
Validation: if `method == Cash` and `cashTendered` is provided, it must be ≥ `amount`.

---

### 1b. Two distinct receipt response shapes

Keep the existing `GET /api/v1/sales/{id}/receipt` as the **customer receipt** (already usable for sharing). Add a separate owner-only endpoint:

```
GET /api/v1/sales/{id}/receipt/owner       [Authorize: Owner | Manager]
```

**Customer `ReceiptView`** (existing, minor additions):
```
+ cashTendered: decimal?
+ changeGiven:  decimal?
```

**Owner `OwnerReceiptView`** (new — everything in customer receipt plus):
```
+ createdByName:     string          // staff member who made the sale
+ lines[].costPrice: decimal         // CostPriceSnapshot (already stored on SaleLine)
+ lines[].lineProfit: decimal        // (unitPrice - costPrice) * quantity
+ totalCogs:         decimal         // Σ costPrice * quantity
+ grossProfit:       decimal         // totalAmount - totalCogs
+ grossMarginPct:    decimal         // grossProfit / totalAmount * 100
+ payments[].cashTendered: decimal?
+ payments[].changeGiven:  decimal?
```

The cost price is already snapshotted at sale time (`CostPriceSnapshot` on `SaleLine`) — the owner receipt just exposes it.

---

## 2. Android Changes

### 2a. Cash change UI in `SalesScreen.kt`

In the payment split section, when `paymentMethodCode == Cash`:
- Show a **"Cash Received"** field (amount the customer hands over)
- Real-time display: **"Change Due: NGN X"** — updates as user types
- Validation: cash received must be ≥ payment amount before the "Save Sale" button is enabled

```
┌─────────────────────────────────────┐
│  Payment Method:  [Cash ▼]          │
│  Amount:          ₦ 5,000           │
│  Cash Received:   ₦ 10,000          │
│  ─────────────────────────────────  │
│  Change Due:      ₦ 5,000  ✓        │
└─────────────────────────────────────┘
```

**State change in `UiSalePayment`:**
```kotlin
+ val cashTendered: Double?   // only relevant for Cash
```

### 2b. Dual PDF generation in `ReceiptPdfGenerator.kt`

Refactor/extend into two distinct generation functions:

**`generateCustomerReceipt(...)`**
- Thermal-style (current format, ~300px wide)
- Shop name + logo placeholder
- Sale number, date, cashier name
- Itemized line items (name, qty × price, line total)
- Subtotal / VAT / Discount / **Total**
- **Cash Received / Change Due** (if cash payment)
- "Thank you" footer

**`generateOwnerReceipt(...)`**
- A4 or wider format (internal document)
- All of the above PLUS:
  - Per-item: cost price, profit per line
  - Summary: Total COGS, Gross Profit, Margin %
  - Staff member name
  - All payment details (method, reference, change)
- Marked prominently: **"INTERNAL — CONFIDENTIAL"**

### 2c. Post-sale flow change

After a sale is successfully recorded:
1. Generate both PDFs in the background
2. Show success overlay (current) with two action buttons:
   - **"Share Customer Receipt"** → Android share intent (WhatsApp, print, etc.)
   - **"View Owner Receipt"** → opens the owner PDF in-app viewer (not shared)
3. Both PDFs cached in app storage under `/receipts/{saleNumber}-customer.pdf` and `/receipts/{saleNumber}-owner.pdf`

---

## 3. iOS Changes

### 3a. Cash change UI in `FeatureViews.swift` — `SaleComposerView`

Same logic as Android, within the payments section:
- When selected payment method is `.cash`, show `cashTendered` `TextField`
- Computed property: `var changeDue: Double { cashTendered - paymentAmount }`
- Show change in green if ≥ 0, red if insufficient

### 3b. Dual receipt generation

Extend/replace the existing `generateReceiptPdf(_ receipt: ReceiptView) -> URL`:

```swift
func generateCustomerReceipt(_ receipt: ReceiptView) -> URL
func generateOwnerReceipt(_ receipt: OwnerReceiptView) -> URL
```

**Customer receipt** — compact, shareable via `ShareSheet`
**Owner receipt** — A4, confidential, opens in-app preview (not in share sheet by default; explicit "Export" button for owner/manager role)

### 3c. Post-sale flow

After sale success in `SalesView`:
- Two buttons replace the current single "Share Receipt PDF":
  - **"Share Customer Receipt"** — `ShareSheet` with customer PDF
  - **"Owner Summary"** — in-app full-screen sheet showing owner receipt PDF

---

## 4. Data Flow Summary

```
User enters Cash Received in payment split
        ↓
App computes Change Due in real-time
        ↓
User taps "Save Sale"
        ↓
cashTendered included in SalePaymentRequest
        ↓
Backend stores on SalePayment entity
        ↓
Backend computes changeGiven = cashTendered - amount
        ↓
GET /receipt       → customer ReceiptView (change shown)
GET /receipt/owner → OwnerReceiptView (cost prices, profit, staff)
        ↓
Mobile generates two PDFs from receipt data
```

---

## 5. Scope Checklist

| Area | Work Item | Status |
|---|---|---|
| Backend | EF migration: add `CashTendered`, `ChangeGiven` to `SalePayment` | [ ] |
| Backend | Update `SalePaymentRequest` contract + validation | [ ] |
| Backend | Extend `ReceiptView` with `cashTendered`, `changeGiven` fields | [ ] |
| Backend | New `OwnerReceiptView` response shape | [ ] |
| Backend | New `GET /sales/{id}/receipt/owner` endpoint (Owner/Manager auth) | [ ] |
| Android | `UiSalePayment` + state: add `cashTendered` field | [ ] |
| Android | Cash Received input + Change Due display in `SalesScreen.kt` | [ ] |
| Android | `SalePaymentRequest` DTO update (`cashTendered`) | [ ] |
| Android | `generateCustomerReceipt` in `ReceiptPdfGenerator.kt` | [ ] |
| Android | `generateOwnerReceipt` in `ReceiptPdfGenerator.kt` | [ ] |
| Android | Post-sale action buttons (Share Customer / View Owner) | [ ] |
| iOS | Cash Received input + Change Due in `SaleComposerView` | [ ] |
| iOS | `SalePaymentRequest` model update | [ ] |
| iOS | `generateCustomerReceipt` function | [ ] |
| iOS | `generateOwnerReceipt` function | [ ] |
| iOS | Post-sale UI: two receipt action buttons | [ ] |

---

## 6. Key Decisions & Notes

- **No new dependencies required.** Android uses existing `PdfDocument` API; iOS uses its existing PDF generation path.
- **Cost price data is already stored.** `CostPriceSnapshot` is already captured on `SaleLine` at sale time — the owner receipt simply exposes it in a new view shape.
- **Owner receipt is never shared by default.** It opens in an in-app viewer only. Explicit "Export" action required.
- **`cashTendered` is optional.** If not provided on a cash payment (e.g. older API clients), change fields are omitted from the receipt.
- **Change is only relevant for Cash payments.** BankTransfer and POS do not have a change concept.
- **Receipt PDFs are cached locally** by sale number so they can be regenerated or re-shared without a network call.
