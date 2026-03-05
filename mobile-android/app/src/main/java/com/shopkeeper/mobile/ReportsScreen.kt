package com.shopkeeper.mobile

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
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

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Text("Reports", style = MaterialTheme.typography.titleLarge)
        Text("Preview and export inventory, sales, P&L, and creditors reports.", color = MaterialTheme.colorScheme.onSurfaceVariant)

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ReportType.values().take(2).forEach { type ->
                Button(
                    onClick = { selectedTypeName = type.name },
                    modifier = Modifier.weight(1f)
                ) {
                    Text(type.label)
                }
            }
        }
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ReportType.values().drop(2).forEach { type ->
                Button(
                    onClick = { selectedTypeName = type.name },
                    modifier = Modifier.weight(1f)
                ) {
                    Text(type.label)
                }
            }
        }

        Text("Selected: ${selectedType.label}", color = MaterialTheme.colorScheme.secondary)

        if (showDateRange) {
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
            BrickButton(
                text = "Export PDF",
                onClick = { if (!isLoading) export(ReportExportFormat.Pdf) },
                modifier = Modifier.weight(1f)
            )
            BrickButton(
                text = "Export Spreadsheet",
                onClick = { if (!isLoading) export(ReportExportFormat.Spreadsheet) },
                modifier = Modifier.weight(1f)
            )
        }

        preview?.let { report ->
            AccentCard(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(12.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    Text(report.title, style = MaterialTheme.typography.titleMedium)
                    report.lines.forEach { line ->
                        Text(line, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }

        if (status.isNotBlank()) {
            Text(status, color = MaterialTheme.colorScheme.secondary)
        }
    }
}
