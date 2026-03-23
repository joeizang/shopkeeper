import XCTest

final class AuthFlowUITests: BaseUITestCase {
    func testOwnerCanSignInAndReachHome() {
        loginAsOwner()
        waitForAuthenticatedShell()
        XCTAssertTrue(appElement("dashboard.root").exists)
    }
}
