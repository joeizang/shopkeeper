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

    func appElement(_ id: String) -> XCUIElement {
        app.descendants(matching: .any)[id]
    }

    func waitForAuthenticatedShell(file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(appElement("dashboard.root").waitForExistence(timeout: 15), file: file, line: line)
    }

    func loginAsOwner(file: StaticString = #filePath, line: UInt = #line) {
        if appElement("dashboard.root").waitForExistence(timeout: 2) {
            return
        }

        let emailField = app.textFields["auth.login.email"]
        XCTAssertTrue(emailField.waitForExistence(timeout: 10), file: file, line: line)
        emailField.tap()
        emailField.typeText(seed.ownerEmail)

        let passwordField = app.secureTextFields["auth.login.password"]
        XCTAssertTrue(passwordField.waitForExistence(timeout: 10), file: file, line: line)
        passwordField.tap()
        passwordField.typeText(seed.password)

        let signInButton = app.buttons["auth.login.submit"]
        XCTAssertTrue(signInButton.waitForExistence(timeout: 5), file: file, line: line)
        signInButton.tap()

        waitForAuthenticatedShell(file: file, line: line)
    }
}
