import SwiftUI

@main
struct ShopkeeperIOSApp: App {
    @StateObject private var sessionStore = SessionStore()

    init() {
        let arguments = ProcessInfo.processInfo.arguments
        if arguments.contains("-uiTesting") {
            if arguments.contains("-resetSessionState") {
                UserDefaults.standard.removeObject(forKey: "onboarding_completed")
                UserDefaults.standard.removeObject(forKey: "selectedTheme")
                UserDefaults.standard.removeObject(forKey: "ios_access_token")
                UserDefaults.standard.removeObject(forKey: "ios_refresh_token")
                UserDefaults.standard.removeObject(forKey: "ios_shop_id")
                UserDefaults.standard.removeObject(forKey: "ios_role")
            }
            if arguments.contains("-skipOnboarding") {
                UserDefaults.standard.set(true, forKey: "onboarding_completed")
            }
        }
    }

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
