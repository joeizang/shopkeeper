package com.shopkeeper.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.shopkeeper.mobile.ui.ShopkeeperApp
import com.shopkeeper.mobile.ui.theme.ShopkeeperTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            ShopkeeperTheme {
                ShopkeeperApp()
            }
        }
    }
}
