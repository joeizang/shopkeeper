# ShopKeeper Build-and-Delivery Plan (Save + Implement)

## Summary
Create a new greenfield repository at `/Users/josephizang/Projects/vibes/shopkeeper` for:
- Android app (Kotlin + Compose, offline-first, camera OCR, PDF receipt sharing).
- Backend API (.NET 10 Minimal API + EF Core 10 + SQLite).
- Multi-tenant SaaS model (Owner, ShopManager, Salesperson roles, tenant isolation, credit sales, used-item inventory).

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
- `shopkeeper/mobile-ios/`
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
- `GET /shops/{shopId}/staff`
- `POST /shops/{shopId}/staff/invite`
- `POST /shops/{shopId}/staff/{staffId}/activate`
- `PATCH /shops/{shopId}/staff/{staffId}`

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
- ShopManager: inventory, sales, receipts, repayments, non-financial admin views, non-P&L reporting.
- Salesperson: sales, receipts, and credit repayment only.
5. Add role-capability enforcement:
- Owner-only: VAT, discounts, expenses, P&L, staff management, account-wide admin actions.
- ShopManager: inventory mutations, sales, credits, conflict handling, inventory/sales/creditors reporting.
- Salesperson: sales creation, payments, receipts, credit repayments.
6. Implement tenant isolation:
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
13. Implement role-aware staff management endpoints:
- Invite staff with explicit role assignment.
- List current staff memberships.
- Update role or active status for a membership.
14. Implement reporting endpoints:
- Inventory, sales, P&L, and creditors previews.
- Date-range filters for sales, P&L, and creditors.
- Owner-only restriction for P&L.
15. Implement export pipeline:
- Format `pdf` for printable reports.
- Format `spreadsheet` for spreadsheet-compatible CSV download.
- Consistent totals between preview and exports.
16. Implement backend caching strategy with FusionCache and HTTP validators:
- Use `ZiggyCreatures.FusionCache` for application/data caching on tenant-scoped read endpoints.
- Prefer cache-aside service-layer caching over endpoint-only output caching because most API reads are authenticated and tenant-aware.
- Add per-feature cache keys that include `TenantId`, route identifiers, role-sensitive query parameters, and date filters where relevant.
- Use short TTLs for operational reads, medium TTLs for reporting summaries, and targeted invalidation after mutations.
- Add cache tags and key-prefix conventions for shop-wide invalidation after writes.
- Add fail-safe and stampede protection on report and list endpoints that are expensive to recompute.
17. Implement ETag/conditional request support:
- Add `ETag` headers on cacheable `GET` endpoints.
- Support `If-None-Match` for `304 Not Modified` responses on list/detail/report endpoints.
- Standardize ETag derivation from stable resource version inputs such as `RowVersion`, `UpdatedAtUtc`, aggregate max-update timestamps, and filter scopes.
- Keep optimistic concurrency for writes with existing `RowVersion` semantics, but expose HTTP `If-Match` as a later-compatible extension point.
18. Add cache invalidation rules:
- Shop settings writes invalidate shop/profile/dashboard/report summary caches for that tenant.
- Inventory writes invalidate inventory list/detail, dashboard, sales composer supporting reads, inventory report, and sync pull projections.
- Sales and repayment writes invalidate dashboard, credits, sale detail/receipt, sales report, creditors report, and P&L caches.
- Expense writes invalidate expenses list, P&L, report summaries, and export artifacts when applicable.
19. Add caching observability:
- Emit cache hit/miss/fail-safe metrics and logs.
- Track `304` rates, cache hit ratio by endpoint family, invalidation counts, and report recomputation duration.
- Expose enough telemetry to tune TTLs per endpoint after production usage data arrives.
20. Execute caching rollout in controlled stages:
- Stage 1: Add FusionCache package, options, cache-key helpers, ETag helper, and cache invalidation service.
- Stage 2: Cache bootstrap endpoints used heavily by mobile startup:
  - `GET /shops/me`
  - `GET /account/me`
  - `GET /account/linked-identities`
- Stage 3: Cache operational inventory and credits reads:
  - `GET /inventory/items`
  - `GET /inventory/items/{id}`
  - `GET /credits`
  - `GET /credits/{saleId}`
- Stage 4: Cache reporting preview and artifact metadata reads:
  - `GET /reports/inventory`
  - `GET /reports/sales`
  - `GET /reports/profit-loss`
  - `GET /reports/creditors`
  - `GET /reports/jobs`
  - `GET /reports/jobs/{id}`
  - `GET /reports/files`
- Stage 5: Add `ETag` and `If-None-Match` support to all Stage 2-4 `GET` endpoints.
- Stage 6: Add strong ETags and private immutable caching semantics to `GET /reports/files/{id}/download`.
- Stage 7: Update Android and iOS clients to store ETags per resource key, send `If-None-Match`, and reuse existing local data on `304`.
21. Introduce cache-version sources for collection endpoints:
- Add tenant-scoped resource version stamps for:
  - inventory
  - sales
  - credits
  - expenses
  - reports
  - staff
  - shop settings / bootstrap data
- Use these version stamps as the primary input for list/report ETags and cache invalidation.
- Continue using entity `RowVersion` for single-resource detail ETags where applicable.
22. Define initial TTL policy:
- Bootstrap/profile reads: 2-5 minutes.
- Inventory and credits operational reads: 10-30 seconds.
- Report previews: 30-120 seconds depending on computational cost.
- Report jobs polling reads: 2-5 seconds.
- Report files metadata: 10-30 seconds.
- Binary report downloads: long-lived private immutable caching with strong ETags.
23. Define invalidation matrix:
- Shop settings changes invalidate bootstrap/profile/shop/report summary caches.
- Staff changes invalidate staff list and bootstrap caches.
- Inventory changes invalidate inventory detail/list, supporting sales reads, dashboard aggregates, report previews, and dependent report artifacts.
- Sales creation/payment/void invalidates sales detail/receipt, credits, reports, dashboard aggregates, and related ETags.
- Credit repayments invalidate credits, affected sale detail/receipt, reports, and dashboard aggregates.
- Expense changes invalidate expenses and profit/loss related caches.
24. Add implementation safeguards:
- Cache keys must always include `TenantId` and any filter dimensions; user-specific reads must also include `UserId`.
- Do not cache auth endpoints, write endpoints, or sync endpoints.
- Do not use shared HTTP response caching for authenticated tenant data.
- Prefer weak ETags for JSON projections and strong ETags only for immutable file downloads.
25. Add delivery tests for caching:
- Verify `304 Not Modified` on repeated `GET` requests with matching `If-None-Match`.
- Verify stale cache invalidation after inventory, sales, credit, expense, staff, and shop-settings mutations.
- Verify no cross-tenant cache leakage under parallel requests.
- Verify mobile clients preserve local data correctly when backend returns `304`.

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
11. Add role-aware frontend behavior:
- Owner sees pricing/settings and team-management tools in Profile.
- ShopManager sees inventory, sales, credits, sync, and non-P&L reports.
- Salesperson sees dashboard, sales, credits, sync, and personal profile only.
- Navigation tabs and action buttons are filtered by role capabilities.

## iOS Implementation Plan (SwiftUI)
1. Create a new iPhone app under `mobile-ios/` using SwiftUI and Swift 6 toolchain compatibility.
2. Target minimum iOS version `16.0` to cover iPhone OS versions from four years ago.
3. Mirror the core app shell and role model from Android:
- `Owner`, `ShopManager`, `Salesperson`
- role-aware tabs and feature visibility
4. Build shared app foundation:
- `AppConfig`
- `APIClient`
- `SessionStore`
- typed network contracts matching backend JSON
5. Implement authentication flow:
- login
- owner registration
- secure session persistence
6. Implement initial iPhone feature set:
- dashboard summary
- inventory list
- sales summary screen
- credits list
- reports summary screen
- profile/account screen
7. Implement owner admin controls in iOS profile:
- VAT and default discount settings
- team management with invite, role update, activation state update
8. Add iOS networking rules for local backend development:
- local HTTP access through App Transport Security exceptions
- simulator default base URL pointing to `http://127.0.0.1:5057`
9. Add simulator delivery path:
- build with `xcodebuild`
- boot an iPhone simulator
- install and launch app with `simctl`
10. Expand toward parity after the shell is stable:
- sales creation workflow
- inventory create/edit workflow
- receipts/share flow
- offline caching and sync queue
- camera OCR and scan flows

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
- Role model:
- `Owner`: full admin and financial controls.
- `ShopManager`: operational management without owner-only financial/admin controls.
- `Salesperson`: transaction execution and repayment collection only.
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
8. Phase 8: iOS app shell, role-aware navigation, and backend integration.
9. Phase 9: iOS parity for sales, inventory, receipts, OCR, and offline sync.
10. Phase 10: Backend read caching, ETags, invalidation, and cache telemetry hardening.
11. Phase 11: Mobile conditional GET support and post-rollout cache tuning.

## Test Cases and Scenarios
Backend:
- Tenant isolation blocks cross-shop access.
- Owner/shop-manager/salesperson policy checks.
- Sale totals, VAT, and payment consistency.
- Credit partial repayments and close-out logic.
- Conflict response on stale `RowVersion`.
- Reporting totals are consistent between JSON preview and exported file.
- P&L endpoint rejects non-owner users.
- Salesperson is denied inventory/report/staff-management endpoints.
- ShopManager is denied owner-only shop settings and expense endpoints.
- Cached endpoints return `304 Not Modified` when `If-None-Match` matches current representation.
- Cache invalidation occurs after inventory, sales, credit, expense, and shop-settings mutations.
- Tenant-scoped cache keys never leak data across shops or roles.
- Reporting caches respect date-range filters and role restrictions.
- FusionCache fail-safe does not serve cross-tenant or cross-filter data.
- Single-resource detail ETags are derived from stable entity version inputs.
- Collection/report ETags are derived from tenant-scoped resource version stamps plus filter scope.
- Report file downloads return strong ETags and do not regenerate content unnecessarily when unchanged.

Android:
- OCR extraction requires manual confirmation before save.
- Offline inventory and sales creation sync later.
- Conflict resolution UI applies chosen action correctly.
- PDF generation and share intents work with file URI and mime.
- Credit outstanding updates correctly after each repayment.
- Reports screen loads each report type and applies date filters.
- Exported PDF/spreadsheet files download and share correctly.
- Role-based navigation hides unauthorized tabs.
- Owner profile exposes staff management; non-owner profiles do not.
- Android stores ETags for high-value `GET` resources and sends `If-None-Match` on revalidation.
- Android correctly handles `304` by reusing current local state without treating the call as an error.

iOS:
- iOS stores ETags for high-value `GET` resources and sends `If-None-Match` on revalidation.
- iOS correctly handles `304` by reusing current local state without treating the call as an error.

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
