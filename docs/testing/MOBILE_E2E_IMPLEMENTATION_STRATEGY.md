# Mobile E2E Implementation Strategy

## Objective
Build a repeatable, deterministic end-to-end testing system for Shopkeeper that validates the real business workflows across:
- backend API
- Android app
- iOS app
- auth and role boundaries
- offline sync and conflict handling
- reporting and receipt/export flows

## Phase 1: Foundations
1. Add stable UI selectors to production UI surfaces.
2. Add a guarded backend E2E reset/seed mechanism.
3. Add a local Docker E2E environment override.
4. Document selector naming, run order, and seeded data.

### Selector conventions
Use stable business identifiers, not visual labels.

Examples:
- `auth.login.email`
- `auth.login.password`
- `auth.login.submit`
- `nav.dashboard`
- `nav.inventory`
- `inventory.summary.add`
- `inventory.form.productName`
- `inventory.form.save`
- `sales.summary.add`
- `sales.form.searchInventory`
- `sales.form.addPayment`
- `sales.form.save`
- `credits.form.sale`
- `credits.form.amount`
- `credits.form.submit`
- `reports.load`
- `reports.queuePdf`

## Phase 2: Android harness
1. Add `androidTest` dependencies and runner support.
2. Add Compose-based E2E tests that target the real backend.
3. Add an Android backend-reset helper that calls the E2E admin endpoint.
4. Start with happy-path coverage for:
- login
- inventory create
- sale create
- report load

## Phase 3: iOS harness
1. Add XCUITest target and source layout.
2. Add runtime launch overrides for API base URL and onboarding bypass.
3. Add iOS backend-reset helper that calls the E2E admin endpoint.
4. Start with happy-path coverage for:
- login
- inventory create
- sale screen open
- report load

## Phase 4: Full-stack E2E wiring
Run Android and iOS tests against the Dockerized backend in `E2E` mode.

### Backend E2E environment
Use a Docker override to set:
- `ASPNETCORE_ENVIRONMENT=E2E`
- `E2E__AdminToken`

### Reset flow
Before every mobile E2E test or suite:
1. call `POST /api/test/reset-and-seed`
2. use the returned credentials
3. launch app with onboarding bypass
4. drive UI against seeded data

### Seeded dataset
The E2E seeder should create:
- owner account
- shop manager account
- salesperson account
- one shop with VAT and default discount configured
- inventory items available for new sales
- one open credit sale with prior repayment history
- one completed sale for reports
- one expense for profit and loss reports

## Initial suites delivered in phases 1-4
### Android
- `AuthFlowE2ETest`
- `InventoryFlowE2ETest`
- `SalesAndReportsE2ETest`

### iOS
- `AuthFlowUITests`
- `InventoryFlowUITests`
- `ReportsFlowUITests`

## Local run model
### Backend
```bash
cd /Users/josephizang/Projects/vibes/shopkeeper
docker compose -f docker-compose.yml -f docker-compose.e2e.yml up --build
```

### Android
Build the debug app with the Mac-local debug URL and E2E token.

### iOS
Run the debug app and UI tests with launch environment:
- `SHOPKEEPER_API_BASE_URL`
- `SHOPKEEPER_SKIP_ONBOARDING=1`

## What comes after phase 4
1. Offline sync and conflict-resolution automation.
2. Role-based UI and authorization E2E coverage.
3. Camera/OCR deterministic fixture tests.
4. Receipt generation/share assertions.
5. Cross-platform smoke tests via Appium.
6. CI orchestration and artifact collection.
