# Shopkeeper iOS Plan

## Goal
Create a new iPhone app under `/Users/josephizang/Projects/vibes/shopkeeper/mobile-ios` that speaks to the existing ASP.NET Core API and follows the same role model as Android:
- `Owner`
- `ShopManager`
- `Salesperson`

Minimum iOS version:
- `iOS 16.0`

## Delivery approach
1. Scaffold a SwiftUI app target and Xcode project.
2. Add a typed `APIClient` and `SessionStore`.
3. Implement login and owner registration first so the simulator build can sign in against the running backend.
4. Implement a role-aware root tab shell:
- Dashboard
- Inventory
- Sales
- Credits
- Reports
- Profile
5. Hide tabs and actions based on role capabilities:
- Owner: all tabs and admin controls
- ShopManager: inventory, sales, credits, reports, profile
- Salesperson: sales, credits, profile
6. Implement the first usable iOS feature set:
- dashboard summary
- inventory list
- sales summary
- credits list
- reports summary
- account/profile
- owner team management
- owner VAT/discount settings
7. Use simulator-friendly local backend defaults:
- `http://127.0.0.1:5057`
- ATS exception for local HTTP
8. Validate with:
- `xcodebuild`
- `simctl boot`
- `simctl install`
- `simctl launch`

## Phase breakdown
1. Phase 1: project scaffold, config, networking, session persistence.
2. Phase 2: login/register-owner flow.
3. Phase 3: role-aware shell and dashboard.
4. Phase 4: inventory, credits, reports, profile read flows.
5. Phase 5: owner admin tools for pricing and team management.
6. Phase 6: simulator build/install/run.
7. Phase 7: parity expansion:
- sales creation
- inventory create/edit
- PDF receipt generation/share
- OCR/camera flows
- offline-first sync

## Constraints
1. Start with SwiftUI only.
2. Avoid adding heavy third-party dependencies unless the platform API is clearly insufficient.
3. Keep contracts aligned with backend JSON and role capabilities.
4. Build cleanly on the local simulator before expanding parity scope.
