# Shopkeeper

Multi-tenant Shop Management SaaS with:
- Android app (`mobile-android`) built with Kotlin + Compose.
- Backend API (`backend-api`) built with ASP.NET Core Minimal API + EF Core + SQLite.

## Repository Layout

- `docs/SHOPKEEPER_IMPLEMENTATION_PLAN.md`: Source-of-truth implementation plan.
- `backend-api/`: .NET 10 Minimal API solution and tests.
- `mobile-android/`: Android app (offline-first scaffolding, OCR flow, receipts, sync).

## Backend Quick Start

```bash
cd backend-api
dotnet test Shopkeeper.sln
dotnet run --project src/Shopkeeper.Api
```

Backend configuration is loaded from dotenv files:
- `backend-api/.env` for actual runtime values.
- `backend-api/.env.local` for local/dummy overrides.

`Program.cs` loads `.env` first, then `.env.local` (override), and uses environment variables for JWT, DB, Google auth, and magic-link settings.

API base path: `/api/v1`

## Android Quick Start

```bash
cd mobile-android
./gradlew :app:assembleDebug
```

## Implemented Scope

### Backend
- JWT auth with register/login/refresh endpoints.
- Multi-shop tenancy model and owner/staff membership.
- Inventory endpoints including used-item fields and stock adjustment.
- Sales endpoints with VAT support, payment recording, and receipt payload.
- Credit account and repayment ledger endpoints.
- Sync push/pull endpoints with row-version conflict detection.
- SQLite persistence with EF Core entities and tenant-scoped uniqueness.
- Unit tests for totals, credit repayment status transitions, and password hashing.

### Android
- Compose navigation and screens for inventory, sales, credits, and conflict handling.
- Camera capture + on-device OCR extraction for model/serial candidates.
- Mandatory review/edit UI before save in inventory flow.
- Room local entities/DAO for inventory, sales, sync queue, and conflicts.
- WorkManager periodic sync worker scaffold.
- Local PDF receipt generation and Android share-sheet delivery.
- Secure token storage via EncryptedSharedPreferences.

## Notes

- POS payment in v1 is manual reference capture (no direct terminal integration).
- Receipt sharing uses Android share targets (Bluetooth/WhatsApp/Telegram/Messenger when available).
- Currency assumptions are NGN.
