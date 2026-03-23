package com.shopkeeper.mobile.dashboard

import androidx.compose.foundation.Canvas
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
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
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
import com.shopkeeper.mobile.ui.components.SkeletonLoadingBox
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StaggeredAnimatedVisibility
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
    var profileInitials by remember { mutableStateOf("SK") }

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

    LaunchedEffect(Unit) {
        refresh()
        // Load profile initials
        runCatching { gateway.getAccountProfile().getOrThrow() }
            .onSuccess { profile ->
                val parts = profile.fullName.trim().split(" ").filter { it.isNotBlank() }
                profileInitials = if (parts.isEmpty()) "SK"
                else parts.take(2).joinToString("") { it.take(1).uppercase() }
            }
    }

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
                        profileInitials,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                }
            }
        )

        if (summary == null) {
            // Skeleton loading state
            SectionTitle(title = "Today", subtitle = "Loading...")
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SkeletonLoadingBox(modifier = Modifier.weight(1f), height = 90.dp)
                SkeletonLoadingBox(modifier = Modifier.weight(1f), height = 90.dp)
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SkeletonLoadingBox(modifier = Modifier.weight(1f), height = 90.dp)
                SkeletonLoadingBox(modifier = Modifier.weight(1f), height = 90.dp)
            }
            SkeletonLoadingBox(height = 160.dp)
        }

        summary?.let {
            SummarySection(summary = it, onRefresh = { refresh() })
            RevenueChart(summary = it)
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
        StaggeredAnimatedVisibility(index = 0, modifier = Modifier.weight(1f)) {
            MetricCard(
                title = "Revenue",
                value = "NGN ${"%,.2f".format(summary.todayRevenue)}",
                supporting = "${summary.todayCompletedSalesCount} completed sales"
            )
        }
        StaggeredAnimatedVisibility(index = 1, modifier = Modifier.weight(1f)) {
            MetricCard(
                title = "Inventory",
                value = summary.inventoryItems.toString(),
                supporting = "${summary.lowStockItems} low stock"
            )
        }
    }
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        StaggeredAnimatedVisibility(index = 2, modifier = Modifier.weight(1f)) {
            MetricCard(
                title = "Conflicts",
                value = summary.openConflicts.toString(),
                supporting = if (summary.openConflicts == 0) "No sync issues" else "Needs review"
            )
        }
        StaggeredAnimatedVisibility(index = 3, modifier = Modifier.weight(1f)) {
            MetricCard(
                title = "Sales Count",
                value = summary.todayCompletedSalesCount.toString(),
                supporting = "Updated from local records"
            )
        }
    }
    SoftButton(
        text = "Refresh Summary",
        onClick = onRefresh,
        modifier = Modifier.fillMaxWidth()
    )
}

@Composable
private fun RevenueChart(summary: DashboardSummary) {
    val values = summary.revenueLast7Days
    if (values.isEmpty()) return

    val max = (values.maxOrNull() ?: 0.0).coerceAtLeast(1.0)
    val startDate = LocalDate.now().minusDays((values.size - 1).toLong())
    val primaryColor = MaterialTheme.colorScheme.primary

    SectionTitle(
        title = "Revenue trend",
        subtitle = "Last 7 days"
    )
    AccentCard(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            // Area chart
            Canvas(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(120.dp)
            ) {
                val w = size.width
                val h = size.height
                val padding = 8f
                val chartW = w - padding * 2
                val chartH = h - padding * 2
                val stepX = if (values.size > 1) chartW / (values.size - 1) else chartW

                val points = values.mapIndexed { index, value ->
                    val x = padding + index * stepX
                    val y = padding + chartH * (1f - (value / max).toFloat())
                    Offset(x, y)
                }

                // Filled gradient area
                if (points.size >= 2) {
                    val areaPath = Path().apply {
                        moveTo(points.first().x, h)
                        points.forEach { lineTo(it.x, it.y) }
                        lineTo(points.last().x, h)
                        close()
                    }
                    drawPath(
                        path = areaPath,
                        brush = Brush.verticalGradient(
                            colors = listOf(
                                primaryColor.copy(alpha = 0.3f),
                                Color.Transparent
                            ),
                            startY = 0f,
                            endY = h
                        )
                    )

                    // Line on top
                    val linePath = Path().apply {
                        moveTo(points.first().x, points.first().y)
                        for (i in 1 until points.size) {
                            lineTo(points[i].x, points[i].y)
                        }
                    }
                    drawPath(
                        path = linePath,
                        color = primaryColor,
                        style = Stroke(width = 3f, cap = StrokeCap.Round)
                    )
                }

                // Data point dots
                points.forEach { point ->
                    drawCircle(
                        color = primaryColor,
                        radius = 5f,
                        center = point
                    )
                    drawCircle(
                        color = Color.White,
                        radius = 2.5f,
                        center = point
                    )
                }
            }

            // Day labels
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                values.indices.forEach { index ->
                    val dayLabel = startDate.plusDays(index.toLong())
                        .dayOfWeek
                        .getDisplayName(TextStyle.SHORT, Locale.getDefault())
                    Text(
                        dayLabel,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.labelSmall
                    )
                }
            }
        }
    }
}
