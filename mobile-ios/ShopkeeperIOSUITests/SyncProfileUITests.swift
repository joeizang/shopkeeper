import XCTest

final class SyncProfileUITests: BaseUITestCase {

    // MARK: - Sync

    func testSyncViewShowsStatusAndRunsSync() {
        loginAsOwner()

        navigateToTab("sync")
        waitAndAssert("sync.root", timeout: 15)
        assertTextVisible("Sync Status")
        assertTextVisible("Last Pull")
        assertTextVisible("Accepted Pushes")
        waitAndAssert("sync.runNow")
        tapButton("sync.runNow")
        waitAndAssert("sync.root", timeout: 15)
    }

    func testSyncViewShowsConflictsSection() {
        loginAsOwner()

        navigateToTab("sync")
        waitAndAssert("sync.root", timeout: 15)
        assertTextVisible("Conflicts")
    }

    // MARK: - Profile

    func testProfileViewShowsUserInfo() {
        loginAsOwner()

        navigateToTab("profile")
        waitAndAssert("profile.root")
        assertTextVisible("Account")
        assertTextVisible(seed.ownerEmail)
    }

    func testProfileViewShowsShopSettings() {
        loginAsOwner()

        navigateToTab("profile")
        waitAndAssert("profile.root")
        assertTextVisible("Current Shop", timeout: 15)
        let saveButton = app.buttons["profile.shopSettings.save"]
        for _ in 0..<10 where !saveButton.exists || !saveButton.isHittable {
            app.swipeUp()
        }
        XCTAssertTrue(saveButton.waitForExistence(timeout: 10), "Shop settings save button should be visible")
    }

    func testProfileViewShowsSessionsSection() {
        loginAsOwner()

        navigateToTab("profile")
        waitAndAssert("profile.root")
        app.swipeUp()
        app.swipeUp()
        assertTextVisible("Sessions", timeout: 10)
    }

    func testLogoutReturnsToLoginScreen() {
        loginAsOwner()

        navigateToTab("profile")
        waitAndAssert("profile.root")
        app.swipeUp()
        app.swipeUp()
        app.swipeUp()

        let logoutButton = app.buttons["profile.logout"]
        if logoutButton.waitForExistence(timeout: 5) {
            logoutButton.tap()
        } else {
            app.swipeUp()
            tapButton("profile.logout")
        }

        let emailField = app.textFields["auth.login.email"]
        XCTAssertTrue(emailField.waitForExistence(timeout: 15), "Login screen should appear after logout")
    }
}
