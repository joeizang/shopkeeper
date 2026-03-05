package com.shopkeeper.mobile.dashboard

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
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
import com.shopkeeper.mobile.ui.components.BrickButton
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.format.TextStyle
import java.util.Locale

@Composable
fun DashboardScreen() {
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

    Column(
        modifier = Modifier
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column {
                Text(
                    "Hello, Shop Owner",
                    style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.onBackground
                )
                Text("Dashboard overview", color = MaterialTheme.colorScheme.secondary)
            }
            Box(
                modifier = Modifier
                    .size(42.dp)
                    .clip(CircleShape)
                    .background(MaterialTheme.colorScheme.surfaceVariant),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    "SO",
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onBackground
                )
            }
        }

        summary?.let {
            BalanceCard(summary = it, onRefresh = { refresh() })
            RevenueCard(summary = it)
        }

        if (status.isNotBlank()) {
            Text(status, color = MaterialTheme.colorScheme.secondary)
        }
    }
}

@Composable
private fun BalanceCard(summary: DashboardSummary, onRefresh: () -> Unit) {
    AccentCard(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Text("Total Balance", color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(
                "NGN ${"%,.2f".format(summary.todayRevenue)}",
                style = MaterialTheme.typography.headlineMedium,
                color = MaterialTheme.colorScheme.onBackground
            )

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                HighlightTile(
                    title = "Total Inventory",
                    value = summary.inventoryItems.toString(),
                    container = MaterialTheme.colorScheme.primary.copy(alpha = 0.24f),
                    valueColor = MaterialTheme.colorScheme.onBackground,
                    modifier = Modifier.weight(1f)
                )
                HighlightTile(
                    title = "Conflicts",
                    value = summary.openConflicts.toString(),
                    container = MaterialTheme.colorScheme.secondary.copy(alpha = 0.16f),
                    valueColor = MaterialTheme.colorScheme.secondary,
                    modifier = Modifier.weight(1f)
                )
            }

            Text(
                "Today Sales: ${summary.todayCompletedSalesCount}",
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            BrickButton(
                text = "Refresh Summary",
                onClick = onRefresh,
                modifier = Modifier.fillMaxWidth()
            )
        }
    }
}

@Composable
private fun HighlightTile(
    title: String,
    value: String,
    container: androidx.compose.ui.graphics.Color,
    valueColor: androidx.compose.ui.graphics.Color,
    modifier: Modifier
) {
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(18.dp))
            .background(container)
    ) {
        Column(
            modifier = Modifier.padding(vertical = 12.dp, horizontal = 12.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            Text(
                title,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.labelSmall
            )
            Text(value, color = valueColor, style = MaterialTheme.typography.titleLarge)
        }
    }
}

@Composable
private fun RevenueCard(summary: DashboardSummary) {
    val values = summary.revenueLast7Days
    val max = (values.maxOrNull() ?: 0.0).coerceAtLeast(1.0)
    val startDate = LocalDate.now().minusDays((values.size - 1).toLong())

    AccentCard(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(
                "7-Day Revenue",
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onBackground
            )
            Text(
                "Low Stock Items: ${summary.lowStockItems}",
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
