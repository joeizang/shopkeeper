import SwiftUI

struct RootView: View {
    @EnvironmentObject private var sessionStore: SessionStore

    var body: some View {
        Group {
            if sessionStore.isAuthenticated {
                TabView {
                    NavigationStack { DashboardView() }
                        .tabItem { Label("Home", systemImage: "rectangle.grid.2x2") }

                    if sessionStore.capabilities.canManageInventory {
                        NavigationStack { InventoryView() }
                            .tabItem { Label("Stock", systemImage: "shippingbox") }
                    }

                    if sessionStore.capabilities.canManageSales {
                        NavigationStack { SalesView() }
                            .tabItem { Label("Sales", systemImage: "cart") }
                        NavigationStack { CreditsView() }
                            .tabItem { Label("Credits", systemImage: "creditcard") }
                    }

                    if sessionStore.capabilities.canViewReports {
                        NavigationStack { ReportsView() }
                            .tabItem { Label("Reports", systemImage: "chart.bar") }
                    }

                    NavigationStack { SyncView() }
                        .tabItem { Label("Sync", systemImage: "arrow.triangle.2.circlepath") }

                    NavigationStack { ProfileView() }
                        .tabItem { Label("Profile", systemImage: "person.crop.circle") }
                }
            } else {
                NavigationStack {
                    LoginView()
                }
            }
        }
        .tint(.red)
        .overlay(alignment: .bottom) {
            if let status = sessionStore.statusMessage, !status.isEmpty {
                Text(status)
                    .font(.footnote)
                    .foregroundStyle(.white)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 10)
                    .background(Color.red.opacity(0.92))
                    .clipShape(RoundedRectangle(cornerRadius: 14))
                    .padding()
            }
        }
    }
}
