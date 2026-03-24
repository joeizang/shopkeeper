import XCTest

struct UITestSeedData: Decodable {
    let shopId: String
    let shopCode: String
    let ownerEmail: String
    let managerEmail: String
    let salespersonEmail: String
    let password: String
    let inventoryProductName: String
    let creditSaleId: String
    let creditSaleNumber: String
}

class BaseUITestCase: XCTestCase {
    let app = XCUIApplication()
    var seed: UITestSeedData!

    var apiBaseUrl: String {
        ProcessInfo.processInfo.environment["SHOPKEEPER_UI_TEST_API_BASE_URL"] ?? "http://192.168.0.189/api/shopkeeper/"
    }

    var e2eAdminToken: String {
        ProcessInfo.processInfo.environment["SHOPKEEPER_E2E_ADMIN_TOKEN"] ?? "shopkeeper-e2e-token"
    }

    override func setUpWithError() throws {
        continueAfterFailure = false
        seed = try resetAndSeed()
        app.launchArguments = ["-uiTesting", "-resetSessionState", "-skipOnboarding"]
        app.launchEnvironment["SHOPKEEPER_API_BASE_URL"] = apiBaseUrl
        app.launch()
    }

    func resetAndSeed() throws -> UITestSeedData {
        let normalizedBase = apiBaseUrl.hasSuffix("/") ? String(apiBaseUrl.dropLast()) : apiBaseUrl
        let url = URL(string: "\(normalizedBase)/api/test/reset-and-seed")!
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.addValue(e2eAdminToken, forHTTPHeaderField: "X-E2E-Admin-Token")
        request.addValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = Data("{}".utf8)

        let semaphore = DispatchSemaphore(value: 0)
        var result: Result<UITestSeedData, Error>!
        URLSession.shared.dataTask(with: request) { data, response, error in
            defer { semaphore.signal() }
            if let error {
                result = .failure(error)
                return
            }
            guard let http = response as? HTTPURLResponse else {
                result = .failure(NSError(domain: "UITest", code: -1, userInfo: [NSLocalizedDescriptionKey: "Missing HTTP response"]))
                return
            }
            guard (200..<300).contains(http.statusCode), let data else {
                let body = data.flatMap { String(data: $0, encoding: .utf8) } ?? ""
                result = .failure(NSError(domain: "UITest", code: http.statusCode, userInfo: [NSLocalizedDescriptionKey: body]))
                return
            }
            do {
                result = .success(try JSONDecoder().decode(UITestSeedData.self, from: data))
            } catch {
                result = .failure(error)
            }
        }.resume()
        semaphore.wait()
        return try result.get()
    }

    // MARK: - Element Helpers

    func appElement(_ id: String) -> XCUIElement {
        app.descendants(matching: .any)[id]
    }

    /// Waits for an element to exist and asserts, with a custom message on failure.
    @discardableResult
    func waitAndAssert(
        _ id: String,
        timeout: TimeInterval = 10,
        message: String? = nil,
        file: StaticString = #filePath,
        line: UInt = #line
    ) -> XCUIElement {
        let element = appElement(id)
        let msg = message ?? "Element '\(id)' did not appear within \(Int(timeout))s"
        XCTAssertTrue(element.waitForExistence(timeout: timeout), msg, file: file, line: line)
        return element
    }

    /// Waits for a button to exist then taps it.
    func tapButton(
        _ id: String,
        timeout: TimeInterval = 10,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let button = app.buttons[id]
        XCTAssertTrue(button.waitForExistence(timeout: timeout), "Button '\(id)' not found", file: file, line: line)
        button.tap()
    }

    /// Waits for a text field to exist, taps it, clears existing text, and types new text.
    func clearAndType(
        _ id: String,
        text: String,
        isSecure: Bool = false,
        timeout: TimeInterval = 10,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let field: XCUIElement
        if isSecure {
            field = app.secureTextFields[id]
        } else {
            field = app.textFields[id]
        }
        XCTAssertTrue(field.waitForExistence(timeout: timeout), "Field '\(id)' not found", file: file, line: line)
        field.tap()

        // Select all existing text and delete before typing
        if let existing = field.value as? String, !existing.isEmpty {
            field.press(forDuration: 1.0)
            if app.menuItems["Select All"].waitForExistence(timeout: 2) {
                app.menuItems["Select All"].tap()
                field.typeText(XCUIKeyboardKey.delete.rawValue)
            }
        }

        field.typeText(text)
    }

    /// Asserts that a static text label containing the given string exists on screen.
    func assertTextVisible(
        _ text: String,
        timeout: TimeInterval = 10,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let predicate = NSPredicate(format: "label CONTAINS[c] %@", text)
        let element = app.staticTexts.matching(predicate).firstMatch
        XCTAssertTrue(element.waitForExistence(timeout: timeout), "Text '\(text)' not visible", file: file, line: line)
    }

    /// Asserts that a static text label containing the given string does NOT exist on screen.
    func assertTextNotVisible(
        _ text: String,
        timeout: TimeInterval = 3,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let predicate = NSPredicate(format: "label CONTAINS[c] %@", text)
        let element = app.staticTexts.matching(predicate).firstMatch
        // Give a short wait, then confirm it's gone
        if element.waitForExistence(timeout: timeout) {
            XCTFail("Text '\(text)' should NOT be visible but was found", file: file, line: line)
        }
    }

    /// Dismisses the keyboard if it's visible (taps outside of active fields).
    func dismissKeyboard() {
        if app.keyboards.count > 0 {
            app.tap()
            // Give a brief moment for the keyboard to fully dismiss
            _ = app.keyboards.element.waitForNonExistence(timeout: 2)
        }
    }

    // MARK: - Navigation Helpers

    func navigateToTab(
        _ tabId: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let navButton = appElement("ui.nav.\(tabId)")
        XCTAssertTrue(navButton.waitForExistence(timeout: 10), "Nav tab '\(tabId)' not found", file: file, line: line)
        navButton.tap()
    }

    // MARK: - Auth Helpers

    func waitForAuthenticatedShell(file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(appElement("dashboard.root").waitForExistence(timeout: 15), file: file, line: line)
    }

    func loginAsOwner(file: StaticString = #filePath, line: UInt = #line) {
        loginAs(email: seed.ownerEmail, password: seed.password, file: file, line: line)
    }

    func loginAsManager(file: StaticString = #filePath, line: UInt = #line) {
        loginAs(email: seed.managerEmail, password: seed.password, file: file, line: line)
    }

    func loginAsSalesperson(file: StaticString = #filePath, line: UInt = #line) {
        loginAs(email: seed.salespersonEmail, password: seed.password, file: file, line: line)
    }

    private func loginAs(email: String, password: String, file: StaticString, line: UInt) {
        // If already on dashboard, skip login
        if appElement("dashboard.root").waitForExistence(timeout: 2) {
            return
        }

        let emailField = app.textFields["auth.login.email"]
        XCTAssertTrue(emailField.waitForExistence(timeout: 10), "Login email field not found", file: file, line: line)
        emailField.tap()
        emailField.typeText(email)

        let passwordField = app.secureTextFields["auth.login.password"]
        XCTAssertTrue(passwordField.waitForExistence(timeout: 10), "Login password field not found", file: file, line: line)
        passwordField.tap()
        passwordField.typeText(password)

        let signInButton = app.buttons["auth.login.submit"]
        XCTAssertTrue(signInButton.waitForExistence(timeout: 5), "Sign in button not found", file: file, line: line)
        signInButton.tap()

        waitForAuthenticatedShell(file: file, line: line)
    }
}

// MARK: - XCUIElement Helpers

extension XCUIElement {
    /// Waits for the element to no longer exist.
    func waitForNonExistence(timeout: TimeInterval) -> Bool {
        let predicate = NSPredicate(format: "exists == false")
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: self)
        let result = XCTWaiter().wait(for: [expectation], timeout: timeout)
        return result == .completed
    }
}
