import SwiftUI

struct RootView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @AppStorage("onboarding_completed") private var onboardingCompleted = false
    @State private var selectedTab = "home"

    private var skipOnboardingForTests: Bool {
        let processInfo = ProcessInfo.processInfo
        return processInfo.arguments.contains("-skipOnboarding") || processInfo.environment["SHOPKEEPER_SKIP_ONBOARDING"] == "1"
    }

    private var isUITesting: Bool {
        ProcessInfo.processInfo.arguments.contains("-uiTesting")
    }

    private var tabItems: [SKTabItem] {
        var items: [SKTabItem] = [
            SKTabItem(id: "home", title: "Home", icon: "rectangle.grid.2x2")
        ]
        if sessionStore.capabilities.canManageInventory {
            items.append(SKTabItem(id: "stock", title: "Stock", icon: "shippingbox"))
        }
        if sessionStore.capabilities.canManageSales {
            items.append(SKTabItem(id: "sales", title: "Sales", icon: "cart"))
            items.append(SKTabItem(id: "credits", title: "Credits", icon: "creditcard"))
        }
        if sessionStore.capabilities.canViewReports {
            items.append(SKTabItem(id: "reports", title: "Reports", icon: "chart.bar"))
        }
        items.append(SKTabItem(id: "sync", title: "Sync", icon: "arrow.triangle.2.circlepath"))
        items.append(SKTabItem(id: "profile", title: "Profile", icon: "person.crop.circle"))
        return items
    }

    var body: some View {
        Group {
            if !skipOnboardingForTests && !onboardingCompleted {
                OnboardingView {
                    onboardingCompleted = true
                }
            } else if sessionStore.isAuthenticated {
                ZStack(alignment: .bottom) {
                    // Active screen
                    Group {
                        switch selectedTab {
                        case "home":
                            NavigationStack { DashboardView() }
                        case "stock":
                            NavigationStack { InventoryView() }
                        case "sales":
                            NavigationStack { SalesView() }
                        case "credits":
                            NavigationStack { CreditsView() }
                        case "reports":
                            NavigationStack { ReportsView() }
                        case "sync":
                            NavigationStack { SyncView() }
                        case "profile":
                            NavigationStack { ProfileView() }
                        default:
                            NavigationStack { DashboardView() }
                        }
                    }
                    .transition(.opacity.combined(with: .scale(scale: 0.98)))
                    .animation(.easeInOut(duration: 0.3), value: selectedTab)

                    // Floating tab bar
                    FloatingTabBar(items: tabItems, selectedTab: $selectedTab)
                        .accessibilityIdentifier("nav.tabbar")

                    if isUITesting {
                        UITestTabSwitcher(items: tabItems, selectedTab: $selectedTab)
                    }
                }
            } else {
                NavigationStack {
                    LoginView()
                }
            }
        }
        .tint(.skPrimary)
        .overlay(alignment: .top) {
            if let status = sessionStore.statusMessage, !status.isEmpty,
               sessionStore.isAuthenticated {
                StatusBanner(
                    message: status,
                    kind: status.lowercased().contains("error") || status.lowercased().contains("fail") ? .error
                        : status.lowercased().contains("success") || status.lowercased().contains("complete") ? .success
                        : .info
                )
                .padding(.horizontal, SKSpacing.lg)
                .padding(.top, SKSpacing.sm)
                .transition(.move(edge: .top).combined(with: .opacity))
            }
        }
    }
}


private struct UITestTabSwitcher: View {
    let items: [SKTabItem]
    @Binding var selectedTab: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(items) { item in
                Button(item.title) {
                    selectedTab = item.id
                }
                .font(.caption2)
                .padding(.horizontal, 8)
                .padding(.vertical, 4)
                .background(Color.skSurfaceVariant.opacity(0.92))
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .accessibilityIdentifier("ui.nav.\(item.id)")
            }
        }
        .padding(.top, 8)
        .padding(.leading, 8)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}
