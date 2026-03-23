package com.shopkeeper.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.shopkeeper.mobile.ui.ShopkeeperApp
import com.shopkeeper.mobile.ui.theme.ShopkeeperTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val skipOnboardingForTests = intent?.getBooleanExtra(EXTRA_SKIP_ONBOARDING_FOR_TESTS, false) == true
        setContent {
            ShopkeeperTheme {
                ShopkeeperApp(skipOnboardingForTests = skipOnboardingForTests)
            }
        }
    }

    companion object {
        const val EXTRA_SKIP_ONBOARDING_FOR_TESTS = "skip_onboarding_for_tests"
    }
}
