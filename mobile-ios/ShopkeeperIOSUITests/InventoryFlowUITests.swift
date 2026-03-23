import XCTest

final class InventoryFlowUITests: BaseUITestCase {
    func testOwnerCanCreateInventoryItem() {
        loginAsOwner()

        let stockTab = appElement("ui.nav.stock")
        XCTAssertTrue(stockTab.waitForExistence(timeout: 10))
        stockTab.tap()

        let addButton = app.buttons["inventory.summary.add"]
        XCTAssertTrue(addButton.waitForExistence(timeout: 10))
        addButton.tap()

        let editorRoot = appElement("inventory.editor.root")
        XCTAssertTrue(editorRoot.waitForExistence(timeout: 10))

        let productField = appElement("inventory.form.productName")
        XCTAssertTrue(productField.waitForExistence(timeout: 10))
        productField.tap()
        productField.typeText("UITest Item")

        let quantityField = appElement("inventory.form.quantity")
        quantityField.tap()
        quantityField.typeText("2")

        let costField = appElement("inventory.form.costPrice")
        costField.tap()
        costField.typeText("1000")

        let sellingField = appElement("inventory.form.sellingPrice")
        sellingField.tap()
        sellingField.typeText("1500")

        let saveButton = app.buttons["inventory.form.save"]
        XCTAssertTrue(saveButton.waitForExistence(timeout: 10))
        saveButton.tap()

        XCTAssertTrue(app.staticTexts["UITest Item"].waitForExistence(timeout: 10))
    }
}
