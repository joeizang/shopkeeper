package com.shopkeeper.mobile.e2e

import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import org.junit.Test
import java.time.LocalDate

class SalesCreditsReportsE2ETest : BaseE2ETest() {
    @Test
    fun owner_can_record_sale_credit_repayment_and_load_report() {
        loginAsOwner()

        val instrumentedProductName = "E2E Sales Item"
        val dueDate = LocalDate.now().plusDays(2).toString()

        clickTag(ShopkeeperTestTags.NAV_STOCK)
        waitForTag(ShopkeeperTestTags.INVENTORY_ADD)
        clickTag(ShopkeeperTestTags.INVENTORY_ADD)
        waitForTag(ShopkeeperTestTags.INVENTORY_PRODUCT_NAME)
        enterText(ShopkeeperTestTags.INVENTORY_PRODUCT_NAME, instrumentedProductName)
        enterText(ShopkeeperTestTags.INVENTORY_QUANTITY, "5")
        enterText(ShopkeeperTestTags.INVENTORY_COST_PRICE, "4500")
        enterText(ShopkeeperTestTags.INVENTORY_SELLING_PRICE, "5912.5")
        clickTag(ShopkeeperTestTags.INVENTORY_SAVE)
        assertTextVisible("queued for sync", timeoutMillis = 20_000)
        assertTextVisible(instrumentedProductName)

        clickTag(ShopkeeperTestTags.NAV_SALES)
        waitForTag(ShopkeeperTestTags.SALES_ADD)
        clickTag(ShopkeeperTestTags.SALES_ADD)
        waitForTag(ShopkeeperTestTags.SALES_CUSTOMER_NAME)
        enterText(ShopkeeperTestTags.SALES_CUSTOMER_NAME, "E2E Buyer")
        enterText(ShopkeeperTestTags.SALES_SEARCH, instrumentedProductName)
        waitForTag(ShopkeeperTestTags.SALES_ADD_LINE, timeoutMillis = 20_000)
        clickTag(ShopkeeperTestTags.SALES_ADD_LINE)
        enterText(ShopkeeperTestTags.SALES_PAYMENT_AMOUNT, "5912.5")
        enterText(ShopkeeperTestTags.SALES_PAYMENT_REFERENCE, "E2E-PAY-001")
        clickTag(ShopkeeperTestTags.SALES_ADD_PAYMENT_SPLIT)
        enterText(ShopkeeperTestTags.SALES_DUE_DATE, dueDate)
        clickTag(ShopkeeperTestTags.SALES_SAVE)

        clickTag(ShopkeeperTestTags.NAV_CREDIT)
        waitForTag(ShopkeeperTestTags.CREDITS_AMOUNT)
        enterText(ShopkeeperTestTags.CREDITS_AMOUNT, "5000")
        enterText(ShopkeeperTestTags.CREDITS_REFERENCE, "E2E-TRX-900")
        enterText(ShopkeeperTestTags.CREDITS_NOTES, "Instrumentation repayment")
        clickTag(ShopkeeperTestTags.CREDITS_SUBMIT)
        assertTextVisible("Repayment saved")

        clickTag(ShopkeeperTestTags.NAV_REPORTS)
        waitForTag(ShopkeeperTestTags.REPORTS_LOAD)
        clickTag(ShopkeeperTestTags.REPORTS_LOAD)
        assertTextVisible("report loaded")
    }
}
