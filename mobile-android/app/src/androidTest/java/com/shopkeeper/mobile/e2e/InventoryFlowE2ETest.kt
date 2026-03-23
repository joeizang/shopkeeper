package com.shopkeeper.mobile.e2e

import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import org.junit.Test

class InventoryFlowE2ETest : BaseE2ETest() {
    @Test
    fun owner_can_create_inventory_item() {
        loginAsOwner()
        clickTag(ShopkeeperTestTags.NAV_STOCK)
        waitForTag(ShopkeeperTestTags.INVENTORY_ADD)
        clickTag(ShopkeeperTestTags.INVENTORY_ADD)
        waitForTag(ShopkeeperTestTags.INVENTORY_PRODUCT_NAME)
        enterText(ShopkeeperTestTags.INVENTORY_PRODUCT_NAME, "Instrumented Item")
        enterText(ShopkeeperTestTags.INVENTORY_QUANTITY, "2")
        enterText(ShopkeeperTestTags.INVENTORY_COST_PRICE, "1000")
        enterText(ShopkeeperTestTags.INVENTORY_SELLING_PRICE, "1500")
        clickTag(ShopkeeperTestTags.INVENTORY_SAVE)
        assertTextVisible("queued for sync", timeoutMillis = 20_000)
        assertTextVisible("Instrumented Item")
    }
}
