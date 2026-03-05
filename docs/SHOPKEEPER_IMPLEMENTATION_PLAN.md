# ShopKeeper Build-and-Delivery Plan (Save + Implement)

## Summary
Create a new greenfield repository at `/Users/josephizang/Projects/vibes/shopkeeper` for:
- Android app (Kotlin + Compose, offline-first, camera OCR, PDF receipt sharing).
- Backend API (.NET 10 Minimal API + EF Core 10 + SQLite).
- Multi-tenant SaaS model (Owner + Staff roles, tenant isolation, credit sales, used-item inventory).

Primary completion criteria:
- Inventory creation from camera OCR with mandatory review/edit before save.
- Sales with cash (NGN), transfer, and POS reference recording.
- Credit sales with due dates, partial repayments, and outstanding balance tracking.
- Receipt PDF generation and Android share to Bluetooth/WhatsApp/Telegram/Messenger targets.
- Offline-first sync with conflict detection and user resolution.

## Step 0: Save Plan File
Create:
- `/Users/josephizang/Projects/vibes/shopkeeper/docs/SHOPKEEPER_IMPLEMENTATION_PLAN.md`

Put this exact plan (all sections below) into that file as the source-of-truth implementation document.

## Repository Structure
Create:
- `shopkeeper/mobile-android/`
- `shopkeeper/backend-api/src/Shopkeeper.Api/`
- `shopkeeper/backend-api/tests/Shopkeeper.Api.Tests/`
- `shopkeeper/docs/`

## Public APIs / Interfaces / Types
Base path: `/api/v1`

Auth and tenancy:
- `POST /auth/register-owner`
- `POST /auth/login`
- `POST /auth/refresh`
- `POST /shops`
- `GET /shops/me`
- `POST /shops/{shopId}/staff/invite`
- `POST /shops/{shopId}/staff/{staffId}/activate`

Inventory:
- `POST /inventory/items`
- `GET /inventory/items`
- `GET /inventory/items/{id}`
- `PATCH /inventory/items/{id}`
- `POST /inventory/items/{id}/photos`
- `POST /inventory/stock-adjustments`

Sales and receipts:
- `POST /sales`
- `GET /sales/{id}`
- `POST /sales/{id}/payments`
- `POST /sales/{id}/void`
- `GET /sales/{id}/receipt`

Credit:
- `GET /credits`
- `GET /credits/{saleId}`
- `POST /credits/{saleId}/repayments`

Sync:
- `POST /sync/push`
- `POST /sync/pull`

Reporting:
- `GET /reports/inventory`
- `GET /reports/sales`
- `GET /reports/profit-loss`
- `GET /reports/creditors`
- `GET /reports/{reportType}/export`

Core server/domain types:
- `Shop`, `User`, `ShopMembership`
- `InventoryItem`, `ItemPhoto`, `StockAdjustment`
- `Sale`, `SaleLine`, `SalePayment`
- `CreditAccount`, `CreditRepayment`
- `SyncChange`, `DeviceCheckpoint`
- `Expense`, `ReportJob`, `ReportFile` (v2 hardening and async generation)

Cross-cutting fields on mutable business entities:
- `TenantId`, `UpdatedAtUtc`, `RowVersion`

## Backend Implementation Plan (.NET 10 / EF Core 10 / SQLite)
1. Bootstrap `Shopkeeper.Api` Minimal API with feature endpoint groups.
2. Add EF Core 10 SQLite DbContext, migrations, and tenant-aware query patterns.
3. Add JWT auth (access + refresh token flow).
4. Add authorization policies:
- Owner: full shop and staff management.
- Staff: inventory, sales, receipts, repayments.
5. Implement tenant isolation:
- Resolve tenant from claims.
- Apply tenant filter in every repository/query.
6. Implement inventory endpoints with used-item fields:
- `ConditionGrade` (A/B/C), `ConditionNotes`, `Photos`.
7. Implement sales workflow:
- VAT toggle/rate from shop settings (default 7.5%).
- Payment methods: cash, transfer, POS reference.
8. Implement credit lifecycle:
- Due date, outstanding, multi-repayment ledger, auto-close at zero balance.
9. Implement receipt API payload for PDF rendering parity with mobile.
10. Implement sync:
- Push local mutations in batches.
- Pull deltas from checkpoint.
- Detect row-version conflicts and return conflict payload.
11. Add audit logs for stock changes, sales, voids, and repayments.
12. Add standardized ProblemDetails responses and validation filters.
13. Implement reporting endpoints:
- Inventory, sales, P&L, and creditors previews.
- Date-range filters for sales, P&L, and creditors.
- Owner-only restriction for P&L.
14. Implement export pipeline:
- Format `pdf` for printable reports.
- Format `spreadsheet` for spreadsheet-compatible CSV download.
- Consistent totals between preview and exports.

## Android Implementation Plan (Kotlin + Compose)
1. Create app modules/packages:
- `auth`, `inventory`, `sales`, `credits`, `receipts`, `sync`, `core`.
2. Add local persistence (Room) and sync queue tables.
3. Add camera + OCR flow:
- Capture image.
- On-device ML Kit text recognition.
- Extract candidate serial/model text.
- Force user confirmation/edit before save.
4. Add inventory UI:
- Product name, qty, expiry optional, cost/selling price, condition, notes, photos.
5. Add sales UI:
- Cart, totals, VAT breakdown.
- Payment methods and optional split payments.
- Credit toggle with customer + due date.
6. Add receipt generation:
- Generate PDF locally with shop details, totals, payments, outstanding credit.
- Share through Android share sheet (`ACTION_SEND`) so installed targets handle WhatsApp/Telegram/Messenger/Bluetooth.
7. Add offline-first behavior:
- All writes local-first.
- WorkManager periodic/background sync.
- Sync status indicators in UI.
8. Add conflict resolution screens:
- Show local vs server values.
- User chooses keep-local or keep-server.
9. Add secure token storage and session handling.
10. Add reports module:
- Report selector for inventory, sales, P&L, and creditors.
- Date filters with date pickers.
- Export actions for PDF and spreadsheet.
- Download and share generated files through Android share sheet.

## Data Model and Rules
- Multi-tenant single SQLite DB in v1; all business rows include `TenantId`.
- Unique per tenant:
- `SaleNumber`
- `SerialNumber` when provided
- Financial integrity:
- `SalePayment` and `CreditRepayment` are append-only ledger records.
- Used product handling:
- Inventory can be `New` or `Used`.
- Used items support grade, notes, and photos.
- Tax handling:
- Shop-level `VatEnabled` and `VatRate`.
- Reporting metrics:
- Inventory value = `SUM(quantity * costPrice)`.
- Revenue = sum of non-void sales in period.
- COGS (v1 estimate) = sum of `lineQty * current item cost`.
- Gross profit = revenue - COGS.
- Net profit/loss = gross profit - expenses (expenses fixed at zero until expense module lands).
- Creditors list includes only unsettled credit accounts with outstanding amount > 0.

## Delivery Phases
1. Phase 1: Repo setup, auth, tenancy, base schema.
2. Phase 2: Inventory + OCR + photos + stock adjustment.
3. Phase 3: Sales + payments + VAT + receipts.
4. Phase 4: Credit sales + repayment ledger.
5. Phase 5: Offline sync + conflict resolution.
6. Phase 6: Hardening, QA, observability, deployment packaging.
7. Phase 7: Reporting module (preview + export + mobile share flow).

## Test Cases and Scenarios
Backend:
- Tenant isolation blocks cross-shop access.
- Owner/staff policy checks.
- Sale totals, VAT, and payment consistency.
- Credit partial repayments and close-out logic.
- Conflict response on stale `RowVersion`.
- Reporting totals are consistent between JSON preview and exported file.
- P&L endpoint rejects non-owner users.

Android:
- OCR extraction requires manual confirmation before save.
- Offline inventory and sales creation sync later.
- Conflict resolution UI applies chosen action correctly.
- PDF generation and share intents work with file URI and mime.
- Credit outstanding updates correctly after each repayment.
- Reports screen loads each report type and applies date filters.
- Exported PDF/spreadsheet files download and share correctly.

End-to-end:
- Owner self-signup, create shop, add staff, sell used item, issue receipt.
- Sale with transfer/POS/cash.
- Credit sale with due date and multiple repayments until settled.

## Assumptions and Defaults
- Currency is NGN.
- POS means recording external terminal transaction references only in v1.
- Receipt delivery uses Android share sheet targets, not direct third-party API integrations.
- SQLite is acceptable for v1 scale; migration to PostgreSQL is deferred.
- English locale defaults; timezone and timestamps stored UTC with local display.

## Auth and Account Refactor Plan (Phases A-F)
### Scope Decision
- Apple auth is deferred.
- Google auth remains in scope.
- Magic-link flow plumbing is implemented now, while actual email delivery is deferred.
- Mobile app adds a profile/account module for account management.

### Phase A: Identity Foundation
- Migrate backend authentication internals to Microsoft ASP.NET Core Identity (`AddIdentityCore`) with EF stores.
- Keep current JWT access/refresh contract for mobile compatibility.
- Introduce identity-focused entities for linked providers and sessions.
- Add configuration guards for production auth secrets and token settings.

### Phase B: Authorization Hardening
- Enforce resource-level shop ownership checks on owner-only routes using `shopId` (not role claim alone).
- Require active membership validation for shop-scoped mutations.
- Add tests for cross-tenant authorization denial and policy enforcement.

### Phase C: Magic Link Plumbing (No Actual Sending Yet)
- Add `POST /auth/magic-link/request` and `POST /auth/magic-link/verify`.
- Add `MagicLinkChallenge` persistence with hashed one-time token, expiry, and consume semantics.
- Add email outbox records for deferred dispatch (`EmailOutboxMessage`) without calling SMTP/provider APIs.
- Add anti-enumeration behavior and basic request throttling.
- Return development-only debug token to allow completion of manual test flows before sender integration.

### Phase D: Google Auth
- Add `POST /auth/google/mobile` endpoint for mobile sign-in via Google ID token.
- Validate token issuer/audience and extract trusted identity claims.
- Upsert local user identity links (`google` provider), then issue app JWT/refresh tokens.
- Preserve multi-shop membership resolution with optional `shopId`.

### Phase E: Mobile Profile and Account Module
- Add `Profile` tab/screen in mobile navigation.
- Add account endpoints consumption:
- `GET /account/me`, `PATCH /account/me`
- `GET /account/sessions`, `POST /account/sessions/{id}/revoke`
- `GET /account/linked-identities`
- Show/edit profile details, linked sign-in methods, and active session controls.
- Keep form state resilient across navigation/process recreation.

### Phase F: Hardening, Testing, and Observability
- Add unit/integration tests for identity login flows, magic-link challenge lifecycle, and resource authorization.
- Add structured auth audit logs for key events (login, refresh, magic-link request/verify, session revoke).
- Add cleanup strategy for expired/consumed magic-link challenges and stale sessions.
- Validate no credentials/secrets are hardcoded or committed.
