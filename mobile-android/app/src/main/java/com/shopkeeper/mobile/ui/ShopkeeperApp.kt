package com.shopkeeper.mobile.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CreditCard
import androidx.compose.material.icons.outlined.Dashboard
import androidx.compose.material.icons.outlined.Inventory
import androidx.compose.material.icons.outlined.PointOfSale
import androidx.compose.material.icons.outlined.SyncProblem
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import androidx.navigation.NavDestination.Companion.hierarchy
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.shopkeeper.mobile.credits.CreditScreen
import com.shopkeeper.mobile.dashboard.DashboardScreen
import com.shopkeeper.mobile.inventory.InventoryScreen
import com.shopkeeper.mobile.sales.SalesScreen
import com.shopkeeper.mobile.sync.ConflictResolutionScreen
import com.shopkeeper.mobile.ui.components.ShopkeeperBackground
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.LocalContentColor

@Composable
fun ShopkeeperApp() {
    val navController = rememberNavController()
    val tabs = listOf(
        NavTab("dashboard", "Home", Icons.Outlined.Dashboard),
        NavTab("inventory", "Stock", Icons.Outlined.Inventory),
        NavTab("sales", "Sales", Icons.Outlined.PointOfSale),
        NavTab("credits", "Credit", Icons.Outlined.CreditCard),
        NavTab("conflicts", "Sync", Icons.Outlined.SyncProblem)
    )

    ShopkeeperBackground(modifier = Modifier.fillMaxSize()) {
        Scaffold(
            containerColor = Color.Transparent,
            contentColor = MaterialTheme.colorScheme.onBackground,
            bottomBar = {
                NavigationBar(
                    modifier = Modifier
                        .padding(horizontal = 12.dp, vertical = 8.dp)
                        .clip(RoundedCornerShape(22.dp)),
                    containerColor = MaterialTheme.colorScheme.surface.copy(alpha = 0.94f),
                    tonalElevation = 0.dp
                ) {
                    val navBackStackEntry by navController.currentBackStackEntryAsState()
                    val currentDestination = navBackStackEntry?.destination
                    tabs.forEach { tab ->
                        val selected = currentDestination?.hierarchy?.any { it.route == tab.route } == true
                        NavigationBarItem(
                            selected = selected,
                            onClick = {
                                navController.navigate(tab.route) {
                                    popUpTo(navController.graph.findStartDestination().id) {
                                        saveState = true
                                    }
                                    launchSingleTop = true
                                    restoreState = true
                                }
                            },
                            label = {
                                Text(
                                    text = tab.label,
                                    maxLines = 1,
                                    style = MaterialTheme.typography.labelSmall
                                )
                            },
                            icon = { Icon(tab.icon, contentDescription = tab.label) },
                            alwaysShowLabel = false,
                            colors = NavigationBarItemDefaults.colors(
                                selectedIconColor = MaterialTheme.colorScheme.onBackground,
                                selectedTextColor = MaterialTheme.colorScheme.onBackground,
                                indicatorColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.34f),
                                unselectedIconColor = MaterialTheme.colorScheme.onSurfaceVariant,
                                unselectedTextColor = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        )
                    }
                }
            }
        ) { paddingValues ->
            CompositionLocalProvider(LocalContentColor provides MaterialTheme.colorScheme.onBackground) {
                Box(modifier = Modifier.fillMaxSize().padding(paddingValues)) {
                    NavHost(
                        navController = navController,
                        startDestination = "dashboard"
                    ) {
                        composable("dashboard") { DashboardScreen() }
                        composable("inventory") { InventoryScreen() }
                        composable("sales") { SalesScreen() }
                        composable("credits") { CreditScreen() }
                        composable("conflicts") { ConflictResolutionScreen() }
                    }
                }
            }
        }
    }
}

private data class NavTab(val route: String, val label: String, val icon: ImageVector)
