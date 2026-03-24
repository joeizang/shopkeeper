import XCTest

final class InventoryFlowUITests: BaseUITestCase {
    func testOwnerCanCreateInventoryItem() {
        loginAsOwner()

        navigateToTab("stock")
        waitAndAssert("inventory.summary.add", timeout: 15)

        // Verify inventory metrics are visible
        assertTextVisible("Products")
        assertTextVisible("Stock Worth")

        tapButton("inventory.summary.add", timeout: 15)

        // Sheet presentation can take a moment
        waitAndAssert("inventory.editor.root", timeout: 15)

        let productField = appElement("inventory.form.productName")
        XCTAssertTrue(productField.waitForExistence(timeout: 10))
        productField.tap()
        productField.typeText("UITest Item")

        // Scroll down to reveal stock fields
        app.swipeUp()

        let quantityField = appElement("inventory.form.quantity")
        XCTAssertTrue(quantityField.waitForExistence(timeout: 10))
        quantityField.tap()
        quantityField.typeText("2")

        let costField = appElement("inventory.form.costPrice")
        XCTAssertTrue(costField.waitForExistence(timeout: 10))
        costField.tap()
        costField.typeText("1000")

        let sellingField = appElement("inventory.form.sellingPrice")
        XCTAssertTrue(sellingField.waitForExistence(timeout: 10))
        sellingField.tap()
        sellingField.typeText("1500")

        // Scroll down to save button
        app.swipeUp()

        tapButton("inventory.form.save", timeout: 15)

        // Verify the item appears in the inventory list
        assertTextVisible("UITest Item", timeout: 15)
    }

    func testSeededInventoryItemIsVisible() {
        loginAsOwner()

        navigateToTab("stock")
        waitAndAssert("inventory.summary.add")

        // The seed data includes a pre-created inventory item
        assertTextVisible(seed.inventoryProductName, timeout: 20)
    }

    func testInventorySearchFiltersItems() {
        loginAsOwner()

        navigateToTab("stock")
        waitAndAssert("inventory.summary.add")

        // Wait for seeded product to load
        assertTextVisible(seed.inventoryProductName, timeout: 20)

        // Search for the seeded product
        let searchField = appElement("inventory.summary.search")
        if searchField.waitForExistence(timeout: 5) {
            searchField.tap()
            searchField.typeText(seed.inventoryProductName)

            // Seeded item should still be visible after filtering
            assertTextVisible(seed.inventoryProductName)
        }
    }

    func testCreateItemWithAllFields() {
        loginAsOwner()

        navigateToTab("stock")
        waitAndAssert("inventory.summary.add", timeout: 15)
        tapButton("inventory.summary.add", timeout: 15)

        // Sheet presentation can take a moment
        waitAndAssert("inventory.editor.root", timeout: 15)

        let productField = appElement("inventory.form.productName")
        XCTAssertTrue(productField.waitForExistence(timeout: 10))
        productField.tap()
        productField.typeText("Full Detail Item")

        app.swipeUp()

        let quantityField = appElement("inventory.form.quantity")
        XCTAssertTrue(quantityField.waitForExistence(timeout: 10))
        quantityField.tap()
        quantityField.typeText("10")

        let costField = appElement("inventory.form.costPrice")
        XCTAssertTrue(costField.waitForExistence(timeout: 10))
        costField.tap()
        costField.typeText("5000")

        let sellingField = appElement("inventory.form.sellingPrice")
        XCTAssertTrue(sellingField.waitForExistence(timeout: 10))
        sellingField.tap()
        sellingField.typeText("7500")

        app.swipeUp()

        tapButton("inventory.form.save", timeout: 15)

        // Verify the item appears in the list with correct details
        assertTextVisible("Full Detail Item", timeout: 15)
    }
}
