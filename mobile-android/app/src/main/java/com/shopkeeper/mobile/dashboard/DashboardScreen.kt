package com.shopkeeper.mobile.dashboard

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.DashboardSummary
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.MetricCard
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.format.TextStyle
import java.util.Locale

@Composable
fun DashboardScreen(onOpenProfile: () -> Unit = {}) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var summary by remember { mutableStateOf<DashboardSummary?>(null) }
    var status by remember { mutableStateOf("") }

    fun refresh() {
        scope.launch {
            runCatching { gateway.getDashboardSummary() }
                .onSuccess {
                    summary = it
                    status = ""
                }
                .onFailure {
                    status = "Could not refresh dashboard: ${it.message.orEmpty()}"
                }
        }
    }

    LaunchedEffect(Unit) { refresh() }

    ScreenColumn {
        ScreenHeader(
            title = "Dashboard",
            subtitle = "Sales, stock, and sync status for today.",
            trailing = {
                Box(
                    modifier = Modifier
                        .size(42.dp)
                        .clip(CircleShape)
                        .background(MaterialTheme.colorScheme.primary)
                        .clickable(onClick = onOpenProfile),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        "SO",
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                }
            }
        )

        summary?.let {
            SummarySection(summary = it, onRefresh = { refresh() })
            RevenueCard(summary = it)
        }

        StatusBanner(status)
    }
}

@Composable
private fun SummarySection(summary: DashboardSummary, onRefresh: () -> Unit) {
    SectionTitle(
        title = "Today",
        subtitle = "Current balance, completed sales, inventory, and conflicts."
    )
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        MetricCard(
            title = "Revenue",
            value = "NGN ${"%,.2f".format(summary.todayRevenue)}",
            supporting = "${summary.todayCompletedSalesCount} completed sales",
            modifier = Modifier.weight(1f)
        )
        MetricCard(
            title = "Inventory",
            value = summary.inventoryItems.toString(),
            supporting = "${summary.lowStockItems} low stock",
            modifier = Modifier.weight(1f)
        )
    }
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        MetricCard(
            title = "Conflicts",
            value = summary.openConflicts.toString(),
            supporting = if (summary.openConflicts == 0) "No sync issues" else "Needs review",
            modifier = Modifier.weight(1f)
        )
        MetricCard(
            title = "Sales Count",
            value = summary.todayCompletedSalesCount.toString(),
            supporting = "Updated from local records",
            modifier = Modifier.weight(1f)
        )
    }
    SoftButton(
        text = "Refresh Summary",
        onClick = onRefresh,
        modifier = Modifier.fillMaxWidth()
    )
}

@Composable
private fun RevenueCard(summary: DashboardSummary) {
    val values = summary.revenueLast7Days
    val max = (values.maxOrNull() ?: 0.0).coerceAtLeast(1.0)
    val startDate = LocalDate.now().minusDays((values.size - 1).toLong())

    SectionTitle(
        title = "Revenue trend",
        subtitle = "Last 7 days"
    )
    AccentCard(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(
                "Low stock items: ${summary.lowStockItems}",
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(5.dp),
                verticalAlignment = Alignment.Bottom
            ) {
                values.forEachIndexed { index, value ->
                    val fraction = (value / max).toFloat()
                    val barHeight = (fraction * 100f).coerceAtLeast(6f)
                    val dayLabel = startDate.plusDays(index.toLong())
                        .dayOfWeek
                        .getDisplayName(TextStyle.SHORT, Locale.getDefault())
                    val tone = if (index % 2 == 0) {
                        MaterialTheme.colorScheme.primary
                    } else {
                        MaterialTheme.colorScheme.secondary
                    }

                    Column(
                        modifier = Modifier.weight(1f),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(4.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .height(barHeight.dp)
                                .width(10.dp)
                                .clip(MaterialTheme.shapes.small)
                                .background(tone)
                        )
                        Text(dayLabel, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.labelSmall)
                    }
                }
            }
        }
    }
}
