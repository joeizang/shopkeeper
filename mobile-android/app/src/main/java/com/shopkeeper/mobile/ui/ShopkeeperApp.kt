package com.shopkeeper.mobile.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CreditCard
import androidx.compose.material.icons.outlined.Dashboard
import androidx.compose.material.icons.outlined.Inventory
import androidx.compose.material.icons.outlined.PointOfSale
import androidx.compose.material.icons.outlined.QueryStats
import androidx.compose.material.icons.outlined.SyncProblem
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Surface
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
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
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.dashboard.DashboardScreen
import com.shopkeeper.mobile.inventory.InventoryScreen
import com.shopkeeper.mobile.profile.ProfileScreen
import com.shopkeeper.mobile.ReportsScreen
import com.shopkeeper.mobile.sales.SalesScreen
import com.shopkeeper.mobile.sync.ConflictResolutionScreen
import com.shopkeeper.mobile.ui.components.ShopkeeperBackground
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.LocalContentColor
import androidx.compose.ui.platform.LocalContext

@Composable
fun ShopkeeperApp() {
    val context = LocalContext.current
    val gateway = androidx.compose.runtime.remember(context) { ShopkeeperDataGateway.get(context) }
    val capabilities = gateway.sessionCapabilities()
    val navController = rememberNavController()
    val tabs = buildList {
        add(NavTab("dashboard", "Home", Icons.Outlined.Dashboard))
        if (capabilities.canManageInventory) add(NavTab("inventory", "Stock", Icons.Outlined.Inventory))
        if (capabilities.canManageSales) add(NavTab("sales", "Sales", Icons.Outlined.PointOfSale))
        if (capabilities.canViewReports) add(NavTab("reports", "Reports", Icons.Outlined.QueryStats))
        if (capabilities.canManageSales) add(NavTab("credits", "Credit", Icons.Outlined.CreditCard))
        add(NavTab("conflicts", "Sync", Icons.Outlined.SyncProblem))
    }

    fun navigateToTab(route: String) {
        val currentRoute = navController.currentBackStackEntry?.destination?.route
        if (currentRoute == route) {
            return
        }

        val restoredExistingDestination = navController.popBackStack(route, inclusive = false)
        if (!restoredExistingDestination) {
            navController.navigate(route) {
                popUpTo(navController.graph.findStartDestination().id) {
                    saveState = true
                }
                launchSingleTop = true
                restoreState = true
            }
        }
    }

    ShopkeeperBackground(modifier = Modifier.fillMaxSize()) {
        Scaffold(
            containerColor = Color.Transparent,
            contentColor = MaterialTheme.colorScheme.onBackground,
            bottomBar = {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(bottom = 12.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Surface(
                        modifier = Modifier
                            .fillMaxWidth(0.88f)
                            .clip(RoundedCornerShape(14.dp)),
                        shape = RoundedCornerShape(14.dp),
                        color = MaterialTheme.colorScheme.surface.copy(alpha = 0.82f),
                        border = androidx.compose.foundation.BorderStroke(
                            1.dp,
                            MaterialTheme.colorScheme.outline.copy(alpha = 0.24f)
                        )
                    ) {
                        NavigationBar(
                            containerColor = Color.Transparent,
                            tonalElevation = 0.dp
                        ) {
                            val navBackStackEntry by navController.currentBackStackEntryAsState()
                            val currentDestination = navBackStackEntry?.destination
                            tabs.forEach { tab ->
                                val selected = currentDestination?.hierarchy?.any { it.route == tab.route } == true
                                NavigationBarItem(
                                    selected = selected,
                                    onClick = { navigateToTab(tab.route) },
                                    label = {
                                        Text(
                                            text = tab.label,
                                            maxLines = 1,
                                            style = MaterialTheme.typography.labelSmall
                                        )
                                    },
                                    icon = { Icon(tab.icon, contentDescription = tab.label) },
                                    alwaysShowLabel = true,
                                    colors = NavigationBarItemDefaults.colors(
                                        selectedIconColor = MaterialTheme.colorScheme.primary,
                                        selectedTextColor = MaterialTheme.colorScheme.primary,
                                        indicatorColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.14f),
                                        unselectedIconColor = MaterialTheme.colorScheme.onSurfaceVariant,
                                        unselectedTextColor = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                )
                            }
                        }
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
                        composable("dashboard") {
                            DashboardScreen(
                                onOpenProfile = {
                                    navController.navigate("profile") { launchSingleTop = true }
                                }
                            )
                        }
                        if (capabilities.canManageInventory) {
                            composable("inventory") { InventoryScreen() }
                        }
                        if (capabilities.canManageSales) {
                            composable("sales") { SalesScreen() }
                        }
                        if (capabilities.canViewReports) {
                            composable("reports") { ReportsScreen() }
                        }
                        composable("profile") { ProfileScreen() }
                        if (capabilities.canManageSales) {
                            composable("credits") { CreditScreen() }
                        }
                        composable("conflicts") { ConflictResolutionScreen() }
                    }
                }
            }
        }
    }
}

private data class NavTab(val route: String, val label: String, val icon: ImageVector)
