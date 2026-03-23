import XCTest

final class SalesCreditsReportsUITests: BaseUITestCase {
    func testOwnerCanCreateSaleAndLoadReport() {
        loginAsOwner()

        let salesTab = appElement("ui.nav.sales")
        XCTAssertTrue(salesTab.waitForExistence(timeout: 10))
        salesTab.tap()

        let addSale = app.buttons["sales.summary.add"]
        XCTAssertTrue(addSale.waitForExistence(timeout: 10))
        addSale.tap()

        let composerRoot = appElement("sales.composer.root")
        XCTAssertTrue(composerRoot.waitForExistence(timeout: 10))

        let customerName = appElement("sales.form.customerName")
        XCTAssertTrue(customerName.waitForExistence(timeout: 10))
        customerName.tap()
        customerName.typeText("iOS UITest Buyer")

        let searchField = appElement("sales.form.searchInventory")
        XCTAssertTrue(searchField.waitForExistence(timeout: 10))
        searchField.tap()
        searchField.typeText(seed.inventoryProductName)

        let addItemButton = app.buttons["sales.item.add.\(seed.inventoryProductName)"]
        XCTAssertTrue(addItemButton.waitForExistence(timeout: 10))
        addItemButton.tap()

        let amountField = appElement("sales.form.paymentAmount")
        XCTAssertTrue(amountField.waitForExistence(timeout: 10))
        amountField.tap()
        amountField.typeText("5912.5")

        let referenceField = appElement("sales.form.paymentReference")
        referenceField.tap()
        referenceField.typeText("IOS-UITEST-REF")

        let addPaymentButton = app.buttons["sales.form.addPaymentSplit"]
        XCTAssertTrue(addPaymentButton.waitForExistence(timeout: 10))
        addPaymentButton.tap()

        let saveButton = app.buttons["sales.form.save"]
        XCTAssertTrue(saveButton.waitForExistence(timeout: 10))
        saveButton.tap()

        XCTAssertTrue(app.staticTexts["iOS UITest Buyer"].waitForExistence(timeout: 10))

        let reportsTab = appElement("ui.nav.reports")
        XCTAssertTrue(reportsTab.waitForExistence(timeout: 10))
        reportsTab.tap()

        let loadSummary = app.buttons["reports.load"]
        XCTAssertTrue(loadSummary.waitForExistence(timeout: 10))
        loadSummary.tap()

        XCTAssertTrue(app.staticTexts["Summary"].waitForExistence(timeout: 10))
    }

    func testOwnerCanRecordCreditRepayment() {
        loginAsOwner()

        let creditsTab = appElement("ui.nav.credits")
        XCTAssertTrue(creditsTab.waitForExistence(timeout: 10))
        creditsTab.tap()

        let repaymentRoot = appElement("credits.repayment.root")
        XCTAssertTrue(repaymentRoot.waitForExistence(timeout: 15))

        let amountField = appElement("credits.repayment.amount")
        XCTAssertTrue(amountField.waitForExistence(timeout: 10))
        amountField.tap()
        amountField.typeText("5000")

        let referenceField = appElement("credits.repayment.reference")
        referenceField.tap()
        referenceField.typeText("IOS-CREDIT-001")

        let notesField = appElement("credits.repayment.notes")
        if notesField.waitForExistence(timeout: 5) {
            notesField.tap()
            notesField.typeText("UI repayment")
        }

        let submitButton = app.buttons["credits.repayment.submit"]
        XCTAssertTrue(submitButton.waitForExistence(timeout: 10))
        submitButton.tap()

        XCTAssertTrue(app.staticTexts["Repayments"].waitForExistence(timeout: 10))
    }
}
