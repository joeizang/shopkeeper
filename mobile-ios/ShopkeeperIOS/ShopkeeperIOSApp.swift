import SwiftUI

@main
struct ShopkeeperIOSApp: App {
    @StateObject private var sessionStore = SessionStore()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environmentObject(sessionStore)
                .preferredColorScheme(sessionStore.themeColorScheme)
                .task {
                    await sessionStore.bootstrap()
                }
        }
    }
}
