import XCTest

final class SalesCreditsReportsUITests: BaseUITestCase {

    // MARK: - Sales

    func testOwnerCanCreateSaleAndLoadReport() {
        loginAsOwner()

        navigateToTab("sales")
        waitAndAssert("sales.root", timeout: 15)
        tapButton("sales.summary.add")
        waitAndAssert("sales.composer.root", timeout: 15)

        let customerName = appElement("sales.form.customerName")
        XCTAssertTrue(customerName.waitForExistence(timeout: 10))
        customerName.tap()
        customerName.typeText("iOS UITest Buyer")

        let searchField = appElement("sales.form.searchInventory")
        XCTAssertTrue(searchField.waitForExistence(timeout: 10))
        searchField.tap()
        searchField.typeText(seed.inventoryProductName)

        let addItemButton = app.buttons["sales.item.add.\(seed.inventoryProductName)"]
        XCTAssertTrue(addItemButton.waitForExistence(timeout: 15))
        addItemButton.tap()

        assertTextVisible(seed.inventoryProductName)

        app.swipeUp()

        let amountField = appElement("sales.form.paymentAmount")
        XCTAssertTrue(amountField.waitForExistence(timeout: 10))
        amountField.tap()
        amountField.typeText("5912.5")

        let referenceField = appElement("sales.form.paymentReference")
        XCTAssertTrue(referenceField.waitForExistence(timeout: 10))
        referenceField.tap()
        referenceField.typeText("IOS-UITEST-REF")

        tapButton("sales.form.addPaymentSplit")
        app.swipeUp()
        tapButton("sales.form.save")

        XCTAssertTrue(appElement("sales.composer.root").waitForNonExistence(timeout: 15), "Sale composer should dismiss after saving")
        waitAndAssert("sales.summary.add", timeout: 15)

        navigateToTab("reports")
        waitAndAssert("reports.load")
        tapButton("reports.load")
        assertTextVisible("Summary", timeout: 15)
    }

    func testSaleComposerShowsLineItemTotals() {
        loginAsOwner()

        navigateToTab("sales")
        waitAndAssert("sales.root", timeout: 15)
        tapButton("sales.summary.add")
        waitAndAssert("sales.composer.root", timeout: 15)

        let customerName = appElement("sales.form.customerName")
        XCTAssertTrue(customerName.waitForExistence(timeout: 10))
        customerName.tap()
        customerName.typeText("Totals Test Buyer")

        let searchField = appElement("sales.form.searchInventory")
        XCTAssertTrue(searchField.waitForExistence(timeout: 10))
        searchField.tap()
        searchField.typeText(seed.inventoryProductName)

        let addItemButton = app.buttons["sales.item.add.\(seed.inventoryProductName)"]
        XCTAssertTrue(addItemButton.waitForExistence(timeout: 15))
        addItemButton.tap()

        app.swipeUp()

        assertTextVisible("Line Items")
        assertTextVisible(seed.inventoryProductName)
    }

    // MARK: - Credits

    func testOwnerCanRecordCreditRepayment() {
        loginAsOwner()

        navigateToTab("credits")
        waitAndAssert("credits.root", timeout: 20)
        waitAndAssert("credits.selector", timeout: 20)
        waitAndAssert("credits.repayment.root", timeout: 30)
        app.swipeUp()

        let amountField = app.textFields["credits.repayment.amount"]
        XCTAssertTrue(amountField.waitForExistence(timeout: 10))
        amountField.tap()
        amountField.typeText("5000")

        let referenceField = app.textFields["credits.repayment.reference"]
        XCTAssertTrue(referenceField.waitForExistence(timeout: 10))
        referenceField.tap()
        referenceField.typeText("IOS-CREDIT-001")

        app.swipeUp()

        let notesField = appElement("credits.repayment.notes")
        if notesField.waitForExistence(timeout: 5) {
            notesField.tap()
            notesField.typeText("UI repayment")
        }

        tapButton("credits.repayment.submit")
        assertTextVisible("Repayments", timeout: 15)
    }

    func testCreditViewShowsOpenCredits() {
        loginAsOwner()

        navigateToTab("credits")
        waitAndAssert("credits.root", timeout: 20)
        assertTextVisible("Open Credit Sales", timeout: 15)
        waitAndAssert("credits.repayment.root", timeout: 30)
        assertTextVisible("Outstanding")
    }

    // MARK: - Reports

    func testReportsViewLoadsAndShowsSummary() {
        loginAsOwner()

        navigateToTab("reports")
        waitAndAssert("reports.root")
        waitAndAssert("reports.load")
        assertTextVisible("Filters")
        tapButton("reports.load")
        assertTextVisible("Summary", timeout: 15)
    }

    // MARK: - Full Flow

    func testOwnerCanRecordSaleCreditRepaymentAndLoadReport() {
        loginAsOwner()

        let instrumentedProductName = "E2E Sales Item"

        navigateToTab("stock")
        waitAndAssert("inventory.summary.add")
        tapButton("inventory.summary.add")
        waitAndAssert("inventory.editor.root", timeout: 15)

        let productField = appElement("inventory.form.productName")
        XCTAssertTrue(productField.waitForExistence(timeout: 10))
        productField.tap()
        productField.typeText(instrumentedProductName)

        app.swipeUp()

        let quantityField = appElement("inventory.form.quantity")
        XCTAssertTrue(quantityField.waitForExistence(timeout: 10))
        quantityField.tap()
        quantityField.typeText("5")

        let costField = appElement("inventory.form.costPrice")
        XCTAssertTrue(costField.waitForExistence(timeout: 10))
        costField.tap()
        costField.typeText("4500")

        let sellingField = appElement("inventory.form.sellingPrice")
        XCTAssertTrue(sellingField.waitForExistence(timeout: 10))
        sellingField.tap()
        sellingField.typeText("5912.5")

        app.swipeUp()
        tapButton("inventory.form.save", timeout: 15)
        assertTextVisible(instrumentedProductName, timeout: 20)

        navigateToTab("sales")
        waitAndAssert("sales.summary.add", timeout: 20)
        tapButton("sales.summary.add", timeout: 20)
        waitAndAssert("sales.composer.root", timeout: 15)

        let customerName = appElement("sales.form.customerName")
        XCTAssertTrue(customerName.waitForExistence(timeout: 10))
        customerName.tap()
        customerName.typeText("E2E Buyer")

        let searchField = appElement("sales.form.searchInventory")
        XCTAssertTrue(searchField.waitForExistence(timeout: 10))
        searchField.tap()
        searchField.typeText(instrumentedProductName)

        let addItemButton = app.buttons["sales.item.add.\(instrumentedProductName)"]
        XCTAssertTrue(addItemButton.waitForExistence(timeout: 20))
        addItemButton.tap()

        app.swipeUp()

        let paymentAmount = appElement("sales.form.paymentAmount")
        XCTAssertTrue(paymentAmount.waitForExistence(timeout: 10))
        paymentAmount.tap()
        paymentAmount.typeText("5912.5")

        let paymentRef = appElement("sales.form.paymentReference")
        XCTAssertTrue(paymentRef.waitForExistence(timeout: 10))
        paymentRef.tap()
        paymentRef.typeText("E2E-PAY-001")

        tapButton("sales.form.addPaymentSplit")
        app.swipeUp()
        tapButton("sales.form.save", timeout: 15)

        XCTAssertTrue(appElement("sales.composer.root").waitForNonExistence(timeout: 15), "Sale composer should dismiss after saving")
        waitAndAssert("sales.summary.add", timeout: 15)
        navigateToTab("credits")
        waitAndAssert("credits.root", timeout: 20)
        waitAndAssert("credits.selector", timeout: 20)
        waitAndAssert("credits.repayment.root", timeout: 30)
        app.swipeUp()

        let repaymentAmount = app.textFields["credits.repayment.amount"]
        XCTAssertTrue(repaymentAmount.waitForExistence(timeout: 10))
        repaymentAmount.tap()
        repaymentAmount.typeText("5000")

        let repaymentRef = app.textFields["credits.repayment.reference"]
        XCTAssertTrue(repaymentRef.waitForExistence(timeout: 10))
        repaymentRef.tap()
        repaymentRef.typeText("E2E-TRX-900")

        app.swipeUp()

        let notesField = appElement("credits.repayment.notes")
        if notesField.waitForExistence(timeout: 5) {
            notesField.tap()
            notesField.typeText("Instrumentation repayment")
        }

        tapButton("credits.repayment.submit")
        assertTextVisible("Repayments", timeout: 15)

        navigateToTab("reports")
        waitAndAssert("reports.root")
        tapButton("reports.load")
        assertTextVisible("Summary", timeout: 15)
    }
}
