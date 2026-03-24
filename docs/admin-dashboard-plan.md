# Shopkeeper Platform Admin Dashboard — Implementation Plan

## Context

Shopkeeper is currently a mobile-first POS/shop management system with an ASP.NET Core 10 backend, PostgreSQL, and iOS/Android apps. There is **no web frontend**, **no platform admin concept**, and **no billing/subscription layer**. This plan introduces an admin dashboard for managing all shop owners, their shops, and per-shop billing via Paystack.

---

## Tech Choices

| Layer | Technology |
|-------|-----------|
| Frontend | TanStack Start (React), TailwindCSS, TanStack Query, Recharts |
| Backend | Extend existing ASP.NET Core 10 Minimal APIs |
| Billing | Paystack (NGN/kobo) |
| Pricing | Per-shop flat monthly rate |
| Location | `/admin-dashboard/` in the monorepo |

---

## Phase 1: Database Schema + Platform Admin Auth

### New Entities → `Domain/BillingEntities.cs`

**PlatformAdmin** — separate from UserAccount (no tenant context, different auth model)
- `Id`, `Email` (unique), `PasswordHash`, `FullName`, `IsSuperAdmin`, `IsActive`, `CreatedAtUtc`, `LastLoginAtUtc`

**SubscriptionPlan** — available tiers, mapped to Paystack plan codes
- `Id`, `Name`, `Slug` (unique), `Description`, `MonthlyPriceKobo` (long), `Currency`, `MaxShops`, `PaystackPlanCode?`, `IsActive`, `CreatedAtUtc`

**Subscription** — one per Shop (per-shop billing)
- `Id`, `ShopId` (FK, unique), `SubscriptionPlanId` (FK), `UserAccountId` (FK — the paying owner), `PaystackSubscriptionCode?`, `PaystackCustomerCode?`, `Status` (enum), `CurrentPeriodStart`, `CurrentPeriodEnd`, `CancelledAtUtc?`, `CreatedAtUtc`, `UpdatedAtUtc`

**Invoice** — payment history, populated from Paystack webhooks
- `Id`, `SubscriptionId` (FK), `ShopId` (FK), `UserAccountId` (FK), `AmountKobo`, `Currency`, `Status` (enum), `PaystackReference?` (unique), `PaidAtUtc?`, `DueAtUtc`, `CreatedAtUtc`

**PaystackEvent** — idempotent webhook log
- `Id`, `EventType`, `PaystackEventId` (unique), `PayloadJson`, `ProcessedAtUtc?`, `CreatedAtUtc`

### New Enums → `Domain/Enums.cs`
- `SubscriptionStatus { Trialing, Active, PastDue, Cancelled, Expired }`
- `InvoiceStatus { Pending, Paid, Failed, Refunded }`

### DbContext → `Data/ShopkeeperDbContext.cs`
- Add 5 new `DbSet<>` properties
- Configure unique indexes, FK relationships in `OnModelCreating`

### Admin Auth Strategy
- Reuse existing JWT infrastructure (same signing key/issuer/audience)
- Add `platform_role: "Admin"` claim — no `tenant_id` or `membership_id`
- New auth policy: `PlatformAdmin` → `RequireClaim("platform_role", "Admin")`

### Files to Create
- `Domain/BillingEntities.cs`
- `Infrastructure/AdminTokenService.cs` (or extend `AuthTokenService`)
- `Endpoints/AdminAuthEndpoints.cs` — `POST /api/v1/admin/auth/login`, `POST /api/v1/admin/auth/refresh`

### Files to Modify
- `Domain/Enums.cs` — add billing enums
- `Data/ShopkeeperDbContext.cs` — add DbSets + model config
- `Data/DevelopmentSeeder.cs` — seed default admin + 3 plans (Free/Starter/Pro)
- `Infrastructure/CustomClaimTypes.cs` — add `PlatformRole`
- `Program.cs` — add `PlatformAdmin` auth policy

### Migration
- `dotnet ef migrations add AddBillingAndPlatformAdmin`

---

## Phase 2: Admin API Endpoints

All admin endpoints require `PlatformAdmin` authorization policy. Grouped under `/api/v1/admin/`.

### Dashboard Stats → `Endpoints/AdminDashboardEndpoints.cs`
- `GET /api/v1/admin/dashboard/stats` — totals: shops, owners, active subscriptions, monthly revenue, new this month

### Owner Management → `Endpoints/AdminOwnerEndpoints.cs`
- `GET /api/v1/admin/owners` — paginated, searchable list of all shop owners
- `GET /api/v1/admin/owners/{userId}` — detail: user info + shops + subscriptions
- `POST /api/v1/admin/owners/{userId}/suspend` — deactivate all memberships
- `POST /api/v1/admin/owners/{userId}/activate` — reactivate

### Shop Management → `Endpoints/AdminShopEndpoints.cs`
- `GET /api/v1/admin/shops` — paginated list with owner + subscription status
- `GET /api/v1/admin/shops/{shopId}` — detail: info, owner, subscription, staff count, sales count

### Billing/Subscriptions → `Endpoints/AdminBillingEndpoints.cs`
- `GET /api/v1/admin/subscriptions` — paginated list
- `GET /api/v1/admin/subscriptions/{id}` — detail + invoice history
- `PATCH /api/v1/admin/subscriptions/{id}/plan` — admin plan override
- `GET /api/v1/admin/invoices` — paginated, filterable by status/date
- `GET /api/v1/admin/plans` — list plans
- `POST /api/v1/admin/plans` — create plan
- `PATCH /api/v1/admin/plans/{id}` — update plan

### Supporting Files
- `Contracts/AdminContracts.cs` — all admin DTOs
- `Services/AdminReadService.cs` — cross-tenant queries, cached dashboard stats

---

## Phase 3: Paystack Integration (parallel with Phase 2)

### Configuration → `Infrastructure/PaystackOptions.cs`
- `SecretKey`, `PublicKey`, `WebhookSecret`, `BaseUrl`

### HTTP Client → `Services/PaystackClient.cs`
- Typed HttpClient via `AddHttpClient<PaystackClient>()`
- Methods: `CreatePlan`, `CreateCustomer`, `InitializeSubscription`, `CancelSubscription`, `FetchSubscription`, `VerifyTransaction`

### Webhook Handler → `Endpoints/PaystackWebhookEndpoints.cs`
- `POST /api/v1/webhooks/paystack` — no auth, HMAC-SHA512 signature verification
- Deduplication via `PaystackEvent.PaystackEventId`
- Handles: `charge.success`, `subscription.create`, `subscription.not_renew`, `subscription.disable`, `invoice.create`, `invoice.payment_failed`

### Subscription Lifecycle → `Services/SubscriptionService.cs`
- `CreateSubscriptionForShop`, `ChangePlan`, `CancelSubscription`, `CheckStatus`

---

## Phase 4: Admin Dashboard Frontend

### Project Setup → `/admin-dashboard/`
- TanStack Start, TailwindCSS, TanStack Query, Recharts, Lucide icons
- `app.config.ts`, `tsconfig.json`, `tailwind.config.ts`, `Dockerfile`

### Route Structure
```
app/routes/
  __root.tsx                    — root layout with sidebar
  _authed.tsx                   — auth guard layout
  _authed/
    index.tsx                   — dashboard overview (stats + charts)
    owners/
      index.tsx                 — owners list (search/filter/paginate)
      $ownerId.tsx              — owner detail
    shops/
      index.tsx                 — shops list
      $shopId.tsx               — shop detail
    subscriptions/
      index.tsx                 — subscriptions list
      $subscriptionId.tsx       — subscription detail + invoices
    billing/
      index.tsx                 — invoices list, revenue overview
      plans.tsx                 — manage subscription plans
    settings.tsx                — admin settings
  login.tsx                     — admin login
```

### Key Components
```
app/components/
  layout/    — Sidebar, Header, PageShell
  ui/        — DataTable, SearchInput, StatusBadge, StatCard, Chart, Modal, Spinner
  owners/    — OwnerRow, OwnerDetail, SuspendOwnerModal
  shops/     — ShopRow, ShopDetail
  billing/   — InvoiceRow, SubscriptionCard, PlanForm
```

### API Layer
```
app/lib/
  auth.ts           — JWT storage, login/logout
  api-client.ts     — fetch wrapper with auth headers + token refresh
  query-client.ts   — TanStack Query config

app/hooks/
  use-dashboard-stats.ts, use-owners.ts, use-shops.ts,
  use-subscriptions.ts, use-invoices.ts, use-plans.ts,
  use-admin-mutations.ts
```

---

## Phase 5: Deployment

### Docker → `docker-compose.yml`
- Add `admin-dashboard` service (Node, port 3000, depends on API)

### Reverse Proxy → `deploy/Caddyfile`
- Route `/admin/*` → `admin-dashboard:3000`
- Existing `/api/shopkeeper/*` → API unchanged

### CORS → `Program.cs` / environment
- Add admin dashboard origin to allowed origins

### Environment Variables
- `Paystack__SecretKey`, `Paystack__PublicKey`, `Paystack__WebhookSecret`

---

## Phase 6: Hardening

- Admin-specific rate limiter (30 req/min)
- Audit logging for all admin mutations (`AuditLog` with `TenantId = Guid.Empty`)
- (Future) Subscription enforcement middleware: return 402 on expired subscriptions for tenant API calls

---

## Key Design Decisions

1. **Separate PlatformAdmin entity** — platform admins have no shop membership or tenant context; reusing UserAccount would pollute the tenant model
2. **Same JWT infrastructure, different claims** — avoids a second auth server; `platform_role` claim + policy check cleanly separates admin vs tenant auth
3. **Subscription per Shop** — per-shop flat rate means each shop has its own subscription and billing cycle
4. **TanStack Start** — vanilla React, self-hostable anywhere, no Vercel lock-in
5. **Paystack amounts in kobo (long)** — matches Paystack API, avoids floating-point issues
6. **Webhook idempotency** — `PaystackEvent` table ensures exactly-once processing

---

## Verification Plan

1. **Phase 1**: Run migration, verify tables in PostgreSQL. Seed admin, confirm login via `curl POST /api/v1/admin/auth/login`
2. **Phase 2**: Hit each admin endpoint with the admin JWT. Verify pagination, search, suspend/activate flows
3. **Phase 3**: Create a test subscription via Paystack test mode. Send mock webhooks and verify Invoice/Subscription updates
4. **Phase 4**: `npm run dev` in `/admin-dashboard/`, verify login → dashboard → owners → shops → billing flows
5. **Phase 5**: `docker compose up --build`, verify Caddy routes both `/admin` and `/api/shopkeeper` correctly
6. **E2E**: Extend `E2ETestSeeder` with admin + subscription seed data for automated testing
