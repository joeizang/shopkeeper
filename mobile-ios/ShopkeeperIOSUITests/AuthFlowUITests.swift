import XCTest

final class AuthFlowUITests: BaseUITestCase {
    func testOwnerCanSignInAndReachHome() {
        loginAsOwner()
        waitForAuthenticatedShell()
        XCTAssertTrue(appElement("dashboard.root").exists)
    }

    func testDashboardShowsKeyMetrics() {
        loginAsOwner()
        waitForAuthenticatedShell()

        // Verify key dashboard sections are visible
        assertTextVisible("Today's Revenue")
        assertTextVisible("Inventory Worth")
        assertTextVisible("Open Credits")
        assertTextVisible("Products")
    }

    func testAllNavigationTabsAreAccessible() {
        loginAsOwner()
        waitForAuthenticatedShell()

        // Verify all nav tabs exist
        waitAndAssert("ui.nav.home")
        waitAndAssert("ui.nav.stock")
        waitAndAssert("ui.nav.sales")
        waitAndAssert("ui.nav.credits")
        waitAndAssert("ui.nav.reports")
        waitAndAssert("ui.nav.sync")
        waitAndAssert("ui.nav.profile")

        // Navigate to each tab and verify the root view loads
        navigateToTab("stock")
        waitAndAssert("inventory.summary.add")

        navigateToTab("sales")
        waitAndAssert("sales.summary.add")

        navigateToTab("credits")
        assertTextVisible("Credits")

        navigateToTab("reports")
        waitAndAssert("reports.load")

        navigateToTab("sync")
        waitAndAssert("sync.root", timeout: 15)

        navigateToTab("profile")
        waitAndAssert("profile.root")

        // Navigate back to home
        navigateToTab("home")
        waitAndAssert("dashboard.root", timeout: 15)
    }
}
