package com.shopkeeper.mobile.e2e

import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import org.junit.Test

class AuthFlowE2ETest : BaseE2ETest() {
    @Test
    fun owner_can_sign_in_and_land_on_home() {
        loginAsOwner()
        assertTagVisible(ShopkeeperTestTags.NAV_HOME)
    }
}
