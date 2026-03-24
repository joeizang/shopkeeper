import XCTest

final class SalesCreditsReportsUITests: BaseUITestCase {

    private func scrollUntilExists(_ element: XCUIElement, maxSwipes: Int = 4) {
        for _ in 0..<maxSwipes where !element.exists {
            app.swipeUp()
        }
    }

    private func scrollUntilHittable(_ element: XCUIElement, maxSwipes: Int = 4) {
        for _ in 0..<maxSwipes where !element.isHittable {
            app.swipeUp()
        }
    }

    func testOwnerCanCreateSaleAndLoadReport() {
        loginAsOwner()

        navigateToTab("sales")
        tapButton("sales.summary.add")
        waitAndAssert("sales.composer.root", timeout: 15)

        let customerName = appElement("sales.form.customerName")
        XCTAssertTrue(customerName.waitForExistence(timeout: 10))
        customerName.tap()
        customerName.typeText("iOS UITest Buyer")

        let saleProductName = seed.inventoryProductName
        let searchField = appElement("sales.form.searchInventory")
        XCTAssertTrue(searchField.waitForExistence(timeout: 10))
        searchField.tap()
        searchField.typeText(saleProductName)

        let addItemButton = app.buttons["sales.item.add.\(saleProductName)"]
        XCTAssertTrue(addItemButton.waitForExistence(timeout: 15))
        addItemButton.tap()

        assertTextVisible(saleProductName)

        app.swipeUp()

        let amountField = appElement("sales.form.paymentAmount")
        XCTAssertTrue(amountField.waitForExistence(timeout: 10))
        amountField.tap()
        amountField.typeText("322500")

        let cashTenderedField = appElement("sales.form.cashTendered")
        XCTAssertTrue(cashTenderedField.waitForExistence(timeout: 10))
        cashTenderedField.tap()
        cashTenderedField.typeText("322500")

        let referenceField = appElement("sales.form.paymentReference")
        XCTAssertTrue(referenceField.waitForExistence(timeout: 10))
        referenceField.tap()
        referenceField.typeText("IOS-UITEST-REF")

        app.swipeUp()
        tapButton("sales.form.save")

        XCTAssertTrue(appElement("sales.composer.root").waitForNonExistence(timeout: 15), "Sale composer should dismiss after saving")
        waitAndAssert("sales.summary.add", timeout: 15)

        navigateToTab("reports")
        waitAndAssert("reports.load", timeout: 15)
        tapButton("reports.load")
        assertTextVisible("Summary", timeout: 15)
    }

    func testSaleComposerShowsLineItemTotals() {
        loginAsOwner()

        navigateToTab("sales")
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

    func testOwnerCanRecordCreditRepayment() {
        loginAsOwner()

        navigateToTab("credits")
        waitAndAssert("credits.root", timeout: 20)
        waitAndAssert("credits.selector", timeout: 20)
        waitAndAssert("credits.repayment.root", timeout: 30)

        let amountField = appElement("credits.repayment.amount")
        scrollUntilExists(amountField)
        if !amountField.waitForExistence(timeout: 10) {
            XCTFail("credits.repayment.amount missing\n\(app.debugDescription)")
            return
        }
        scrollUntilHittable(amountField, maxSwipes: 2)
        clearAndType("credits.repayment.amount", text: "5000")
        dismissKeyboard()

        let submitButton = app.buttons["credits.repayment.submit"]
        scrollUntilExists(submitButton, maxSwipes: 3)
        XCTAssertTrue(submitButton.waitForExistence(timeout: 10))
        tapButton("credits.repayment.submit")
        let amountFieldAfterSubmit = app.textFields["credits.repayment.amount"]
        XCTAssertTrue(amountFieldAfterSubmit.waitForExistence(timeout: 10))
        let clearedPredicate = NSPredicate(format: "value == %@", "")
        expectation(for: clearedPredicate, evaluatedWith: amountFieldAfterSubmit)
        waitForExpectations(timeout: 15)
    }

    func testCreditViewShowsOpenCredits() {
        loginAsOwner()

        navigateToTab("credits")
        waitAndAssert("credits.root", timeout: 20)
        assertTextVisible("Open Credit Sales", timeout: 15)
        waitAndAssert("credits.repayment.root", timeout: 30)
        assertTextVisible("Outstanding")
    }

    func testReportsViewLoadsAndShowsSummary() {
        loginAsOwner()

        navigateToTab("reports")
        waitAndAssert("reports.root")
        waitAndAssert("reports.load")
        assertTextVisible("Filters")
        tapButton("reports.load")
        assertTextVisible("Summary", timeout: 15)
    }

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
        sellingField.typeText("6000")

        app.swipeUp()
        tapButton("inventory.form.save", timeout: 15)
        XCTAssertTrue(appElement("inventory.editor.root").waitForNonExistence(timeout: 15), "Inventory editor should dismiss after saving")
        assertTextVisible(instrumentedProductName, timeout: 20)

        navigateToTab("sales")
        waitAndAssert("sales.root", timeout: 20)
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
        paymentAmount.typeText("6450")

        let paymentCashTendered = appElement("sales.form.cashTendered")
        XCTAssertTrue(paymentCashTendered.waitForExistence(timeout: 10))
        paymentCashTendered.tap()
        paymentCashTendered.typeText("6450")

        let paymentRef = appElement("sales.form.paymentReference")
        XCTAssertTrue(paymentRef.waitForExistence(timeout: 10))
        paymentRef.tap()
        paymentRef.typeText("E2E-PAY-001")

        app.swipeUp()
        tapButton("sales.form.save", timeout: 15)

        XCTAssertTrue(appElement("sales.composer.root").waitForNonExistence(timeout: 15), "Sale composer should dismiss after saving")
        waitAndAssert("sales.summary.add", timeout: 15)

        navigateToTab("credits")
        waitAndAssert("credits.root", timeout: 20)
        waitAndAssert("credits.selector", timeout: 20)
        waitAndAssert("credits.repayment.root", timeout: 30)

        let repaymentAmount = appElement("credits.repayment.amount")
        scrollUntilExists(repaymentAmount)
        if !repaymentAmount.waitForExistence(timeout: 10) {
            XCTFail("credits.repayment.amount missing in combined flow\n\(app.debugDescription)")
            return
        }
        scrollUntilHittable(repaymentAmount, maxSwipes: 2)
        clearAndType("credits.repayment.amount", text: "5000")
        dismissKeyboard()

        let repaymentSubmit = app.buttons["credits.repayment.submit"]
        scrollUntilExists(repaymentSubmit, maxSwipes: 3)
        XCTAssertTrue(repaymentSubmit.waitForExistence(timeout: 10))
        tapButton("credits.repayment.submit")
        let amountFieldAfterSubmit = app.textFields["credits.repayment.amount"]
        XCTAssertTrue(amountFieldAfterSubmit.waitForExistence(timeout: 10))
        let clearedPredicate = NSPredicate(format: "value == %@", "")
        expectation(for: clearedPredicate, evaluatedWith: amountFieldAfterSubmit)
        waitForExpectations(timeout: 15)

        navigateToTab("reports")
        waitAndAssert("reports.load", timeout: 15)
        tapButton("reports.load")
        assertTextVisible("Summary", timeout: 15)
    }
}
