package com.shopkeeper.mobile

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.ReportExportFormat
import com.shopkeeper.mobile.core.data.ReportPreview
import com.shopkeeper.mobile.core.data.ReportType
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.receipts.shareReportFile
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.DatePickerField
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SelectionPill
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import kotlinx.coroutines.launch

@Composable
fun ReportsScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var selectedTypeName by rememberSaveable { mutableStateOf(ReportType.Inventory.name) }
    var fromDate by rememberSaveable { mutableStateOf("") }
    var toDate by rememberSaveable { mutableStateOf("") }
    var status by rememberSaveable { mutableStateOf("") }
    var preview by remember { mutableStateOf<ReportPreview?>(null) }
    var isLoading by remember { mutableStateOf(false) }

    val selectedType = runCatching { ReportType.valueOf(selectedTypeName) }.getOrDefault(ReportType.Inventory)
    val showDateRange = selectedType != ReportType.Inventory

    fun loadPreview() {
        scope.launch {
            isLoading = true
            val result = gateway.fetchReportPreview(selectedType, fromDate.ifBlank { null }, toDate.ifBlank { null })
            result.fold(
                onSuccess = {
                    preview = it
                    status = "Report loaded."
                },
                onFailure = {
                    status = "Failed to load report: ${it.message.orEmpty()}"
                }
            )
            isLoading = false
        }
    }

    fun export(format: ReportExportFormat) {
        scope.launch {
            isLoading = true
            val result = gateway.exportReportFile(selectedType, format, fromDate.ifBlank { null }, toDate.ifBlank { null })
            result.fold(
                onSuccess = { file ->
                    shareReportFile(context, file, format.mimeType)
                    status = "${format.label} export ready."
                },
                onFailure = {
                    status = "Export failed: ${it.message.orEmpty()}"
                }
            )
            isLoading = false
        }
    }

    ScreenColumn {
        ScreenHeader(
            title = "Reports",
            subtitle = "Preview and export inventory, sales, profit, and credit reports."
        )

        SectionTitle(
            title = "Report type",
            subtitle = "Choose what you want to preview or export."
        )
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ReportType.values().take(2).forEach { type ->
                SelectionPill(
                    text = type.label,
                    selected = selectedType == type,
                    onClick = { selectedTypeName = type.name },
                    modifier = Modifier.weight(1f)
                )
            }
        }
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ReportType.values().drop(2).forEach { type ->
                SelectionPill(
                    text = type.label,
                    selected = selectedType == type,
                    onClick = { selectedTypeName = type.name },
                    modifier = Modifier.weight(1f)
                )
            }
        }

        if (showDateRange) {
            SectionTitle(
                title = "Date range",
                subtitle = "Leave blank to use the server defaults."
            )
            DatePickerField(
                label = "From Date (optional)",
                value = fromDate,
                onValueChange = { fromDate = it },
                modifier = Modifier.fillMaxWidth()
            )
            DatePickerField(
                label = "To Date (optional)",
                value = toDate,
                onValueChange = { toDate = it },
                modifier = Modifier.fillMaxWidth()
            )
        }

        BrickButton(
            text = if (isLoading) "Loading..." else "Load Report",
            onClick = { if (!isLoading) loadPreview() },
            modifier = Modifier.fillMaxWidth()
        )

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            SoftButton(
                text = "Export PDF",
                onClick = { if (!isLoading) export(ReportExportFormat.Pdf) },
                modifier = Modifier.weight(1f)
            )
            SoftButton(
                text = "Export Spreadsheet",
                onClick = { if (!isLoading) export(ReportExportFormat.Spreadsheet) },
                modifier = Modifier.weight(1f)
            )
        }

        preview?.let { report ->
            SectionTitle(
                title = report.title,
                subtitle = "Preview"
            )
            AccentCard(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(14.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    report.lines.forEach { line ->
                        Text(line, color = MaterialTheme.colorScheme.onBackground, style = MaterialTheme.typography.bodyMedium)
                    }
                }
            }
        }

        StatusBanner(status)
    }
}
