package com.shopkeeper.mobile.e2e

import android.content.Context
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextClearance
import androidx.compose.ui.test.performTextInput
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.shopkeeper.mobile.MainActivity
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.core.data.local.ShopkeeperDatabase
import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.runner.RunWith

@OptIn(ExperimentalTestApi::class)
@RunWith(AndroidJUnit4::class)
abstract class BaseE2ETest {
    @get:Rule
    val composeRule = createEmptyComposeRule()

    protected lateinit var seed: E2ESeedData
    private var scenario: ActivityScenario<MainActivity>? = null

    @Before
    fun baseSetUp() {
        val targetContext = InstrumentationRegistry.getInstrumentation().targetContext
        clearLocalState(targetContext)
        seed = E2EBackendController.resetAndSeed()

        val launchIntent = targetContext.packageManager
            .getLaunchIntentForPackage(targetContext.packageName)
            ?.apply {
                putExtra(MainActivity.EXTRA_SKIP_ONBOARDING_FOR_TESTS, true)
                addFlags(android.content.Intent.FLAG_ACTIVITY_CLEAR_TASK)
            }
            ?: error("Unable to resolve launch intent for ${targetContext.packageName}")

        scenario = ActivityScenario.launch(launchIntent)
        waitForInitialScreen()
    }

    @After
    fun baseTearDown() {
        scenario?.close()
    }

    protected fun loginAsOwner() {
        if (hasTag(ShopkeeperTestTags.NAV_HOME)) {
            return
        }

        waitForTag(ShopkeeperTestTags.AUTH_MODE_SIGN_IN)
        enterText(ShopkeeperTestTags.AUTH_LOGIN_EMAIL, seed.ownerEmail)
        enterText(ShopkeeperTestTags.AUTH_LOGIN_PASSWORD, seed.password)
        clickTag(ShopkeeperTestTags.AUTH_LOGIN_SUBMIT)
        waitForTag(ShopkeeperTestTags.NAV_HOME)
    }

    protected fun waitForTag(tag: String, timeoutMillis: Long = 15_000) {
        composeRule.waitUntil(timeoutMillis) {
            hasTag(tag)
        }
    }

    protected fun assertTagVisible(tag: String, timeoutMillis: Long = 15_000) {
        waitForTag(tag, timeoutMillis)
    }

    protected fun assertTextVisible(text: String, timeoutMillis: Long = 15_000) {
        composeRule.waitUntil(timeoutMillis) {
            composeRule.onAllNodesWithText(text, substring = true, useUnmergedTree = true)
                .fetchSemanticsNodes().isNotEmpty()
        }
    }

    protected fun tapFirstText(text: String, timeoutMillis: Long = 15_000) {
        assertTextVisible(text, timeoutMillis)
        composeRule.onAllNodesWithText(text, substring = true, useUnmergedTree = true)
            .onFirst()
            .performClick()
    }

    protected fun tapFirstContentDescription(description: String, timeoutMillis: Long = 15_000) {
        composeRule.waitUntil(timeoutMillis) {
            composeRule.onAllNodesWithContentDescription(description, substring = true, useUnmergedTree = true)
                .fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onAllNodesWithContentDescription(description, substring = true, useUnmergedTree = true)
            .onFirst()
            .performClick()
    }

    protected fun clickTag(tag: String) {
        val node = composeRule.onNodeWithTag(tag, useUnmergedTree = true)
        runCatching { node.performScrollTo() }
        node.performClick()
    }

    protected fun enterText(tag: String, value: String) {
        val node = composeRule.onNodeWithTag(tag, useUnmergedTree = true)
        runCatching { node.performScrollTo() }
        node.performTextClearance()
        node.performTextInput(value)
    }

    private fun hasTag(tag: String): Boolean {
        return composeRule.onAllNodesWithTag(tag, useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty()
    }

    private fun waitForInitialScreen(timeoutMillis: Long = 15_000) {
        composeRule.waitUntil(timeoutMillis) {
            hasTag(ShopkeeperTestTags.AUTH_MODE_SIGN_IN) || hasTag(ShopkeeperTestTags.NAV_HOME)
        }
    }

    private fun clearLocalState(context: Context) {
        runCatching { context.deleteSharedPreferences("shopkeeper_prefs") }
        runCatching { context.deleteSharedPreferences("shopkeeper_secure_session") }
        runCatching { context.deleteSharedPreferences("shopkeeper_secure_session_fallback") }
        runCatching { context.deleteSharedPreferences("shopkeeper_sync_meta") }
        runCatching {
            context.createDeviceProtectedStorageContext()
                .deleteSharedPreferences("shopkeeper_secure_session_fallback")
        }
        runCatching {
            context.createDeviceProtectedStorageContext()
                .deleteSharedPreferences("shopkeeper_sync_meta")
        }
        ShopkeeperDataGateway.clearInstance()
        ShopkeeperDatabase.closeAndClear()
        context.deleteDatabase("shopkeeper-mobile.db")
    }
}
