package com.shopkeeper.mobile

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.ExpenseInput
import com.shopkeeper.mobile.core.data.ExpenseRecord
import com.shopkeeper.mobile.core.data.ReportExportFormat
import com.shopkeeper.mobile.core.data.ReportFileRecord
import com.shopkeeper.mobile.core.data.ReportJobRecord
import com.shopkeeper.mobile.core.data.ReportPreview
import com.shopkeeper.mobile.core.data.ReportType
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.receipts.shareReportFile
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.DatePickerField
import com.shopkeeper.mobile.ui.components.MetricCard
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SelectionPill
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun ReportsScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }
    val capabilities = gateway.sessionCapabilities()
    val canViewProfitLoss = capabilities.canViewProfitLoss

    var selectedTypeName by rememberSaveable { mutableStateOf(ReportType.Inventory.name) }
    var fromDate by rememberSaveable { mutableStateOf("") }
    var toDate by rememberSaveable { mutableStateOf("") }
    var status by rememberSaveable { mutableStateOf("") }
    var preview by remember { mutableStateOf<ReportPreview?>(null) }
    var isLoading by remember { mutableStateOf(false) }

    var expenses by remember { mutableStateOf<List<ExpenseRecord>>(emptyList()) }
    var reportJobs by remember { mutableStateOf<List<ReportJobRecord>>(emptyList()) }
    var reportFiles by remember { mutableStateOf<List<ReportFileRecord>>(emptyList()) }

    var editingExpenseId by rememberSaveable { mutableStateOf("") }
    var editingExpenseRowVersion by rememberSaveable { mutableStateOf("") }
    var expenseTitle by rememberSaveable { mutableStateOf("") }
    var expenseCategory by rememberSaveable { mutableStateOf("Operations") }
    var expenseAmount by rememberSaveable { mutableStateOf("") }
    var expenseDate by rememberSaveable { mutableStateOf("") }
    var expenseNotes by rememberSaveable { mutableStateOf("") }
    var isSubmittingExpense by rememberSaveable { mutableStateOf(false) }

    val selectedType = runCatching { ReportType.valueOf(selectedTypeName) }.getOrDefault(ReportType.Inventory)
    val showDateRange = selectedType != ReportType.Inventory
    val filteredExpensesTotal = expenses.sumOf { it.amount }
    val filteredExpenseCategories = expenses.map { it.category.trim().lowercase() }.distinct().count()

    fun resetExpenseEditor() {
        editingExpenseId = ""
        editingExpenseRowVersion = ""
        expenseTitle = ""
        expenseCategory = "Operations"
        expenseAmount = ""
        expenseDate = ""
        expenseNotes = ""
    }

    fun loadPreview() {
        scope.launch {
            isLoading = true
            val result = gateway.fetchReportPreview(selectedType, fromDate.ifBlank { null }, toDate.ifBlank { null })
            result.fold(
                onSuccess = {
                    preview = it
                    status = "${selectedType.label} report loaded."
                },
                onFailure = {
                    status = "Failed to load report: ${it.message.orEmpty()}"
                }
            )
            isLoading = false
        }
    }

    fun refreshHistory() {
        scope.launch {
            runCatching {
                val jobsResult = gateway.getReportJobs().getOrThrow()
                val filesResult = gateway.getReportFiles().getOrThrow()
                val expensesResult = if (canViewProfitLoss) gateway.getExpenses(fromDate.ifBlank { null }, toDate.ifBlank { null }).getOrThrow() else emptyList()
                Triple(jobsResult, filesResult, expensesResult)
            }.onSuccess { result ->
                reportJobs = result.first
                reportFiles = result.second
                expenses = result.third
            }.onFailure {
                status = "Failed to refresh reporting data: ${it.message.orEmpty()}"
            }
        }
    }

    fun queueExport(format: ReportExportFormat) {
        scope.launch {
            isLoading = true
            val result = gateway.queueReportJob(selectedType, format, fromDate.ifBlank { null }, toDate.ifBlank { null })
            result.fold(
                onSuccess = { job ->
                    status = "${format.label} export queued."
                    reportJobs = listOf(job) + reportJobs.filterNot { it.id == job.id }
                    refreshHistory()
                },
                onFailure = {
                    status = "Queue failed: ${it.message.orEmpty()}"
                }
            )
            isLoading = false
        }
    }

    fun saveExpense() {
        val amount = expenseAmount.toDoubleOrNull()
        if (expenseTitle.isBlank()) {
            status = "Expense title is required."
            return
        }
        if (amount == null || amount <= 0.0) {
            status = "Enter a valid expense amount."
            return
        }
        if (expenseDate.isBlank()) {
            status = "Pick the expense date."
            return
        }

        scope.launch {
            isLoading = true
            isSubmittingExpense = true
            val result = if (editingExpenseId.isBlank()) {
                gateway.createExpense(
                    ExpenseInput(
                        title = expenseTitle,
                        category = expenseCategory,
                        amount = amount,
                        expenseDateUtcIso = "${expenseDate}T00:00:00Z",
                        notes = expenseNotes.ifBlank { null }
                    )
                )
            } else {
                gateway.updateExpense(
                    ExpenseRecord(
                        id = editingExpenseId,
                        title = expenseTitle,
                        category = expenseCategory,
                        amount = amount,
                        expenseDateUtcIso = "${expenseDate}T00:00:00Z",
                        notes = expenseNotes.ifBlank { null },
                        createdAtUtc = "",
                        rowVersionBase64 = editingExpenseRowVersion
                    )
                )
            }

            result.onSuccess {
                status = if (editingExpenseId.isBlank()) "Expense recorded." else "Expense updated."
                resetExpenseEditor()
                refreshHistory()
                loadPreview()
            }.onFailure {
                status = "Expense save failed: ${it.message.orEmpty()}"
            }
            isSubmittingExpense = false
            isLoading = false
        }
    }

    fun editExpense(expense: ExpenseRecord) {
        editingExpenseId = expense.id
        editingExpenseRowVersion = expense.rowVersionBase64
        expenseTitle = expense.title
        expenseCategory = expense.category
        expenseAmount = expense.amount.toString()
        expenseDate = expense.expenseDateUtcIso.take(10)
        expenseNotes = expense.notes.orEmpty()
        status = "Editing ${expense.title}."
    }

    fun deleteExpense(expenseId: String) {
        scope.launch {
            isLoading = true
            gateway.deleteExpense(expenseId)
                .onSuccess {
                    if (editingExpenseId == expenseId) {
                        resetExpenseEditor()
                    }
                    status = "Expense deleted."
                    refreshHistory()
                    loadPreview()
                }
                .onFailure {
                    status = "Expense delete failed: ${it.message.orEmpty()}"
                }
            isLoading = false
        }
    }

    fun shareSavedFile(fileRecord: ReportFileRecord) {
        scope.launch {
            gateway.downloadReportFile(fileRecord.id, fileRecord.fileName)
                .onSuccess { file ->
                    shareReportFile(context, file, fileRecord.contentType)
                    status = "${fileRecord.fileName} ready to share."
                }
                .onFailure {
                    status = "Could not download ${fileRecord.fileName}: ${it.message.orEmpty()}"
                }
        }
    }

    fun retryJob(jobId: String) {
        scope.launch {
            isLoading = true
            gateway.retryReportJob(jobId)
                .onSuccess {
                    status = "Report job queued again."
                    refreshHistory()
                }
                .onFailure {
                    status = "Retry failed: ${it.message.orEmpty()}"
                }
            isLoading = false
        }
    }

    fun shareJobFile(job: ReportJobRecord) {
        val reportFileId = job.reportFileId ?: return
        val fileName = reportFiles.firstOrNull { it.id == reportFileId }?.fileName
            ?: "${job.reportType}-${job.id}.${if (job.format.equals("pdf", true)) "pdf" else "xlsx"}"
        scope.launch {
            gateway.downloadReportFile(reportFileId, fileName)
                .onSuccess { file ->
                    shareReportFile(
                        context,
                        file,
                        if (job.format.equals("pdf", true)) ReportExportFormat.Pdf.mimeType else ReportExportFormat.Spreadsheet.mimeType
                    )
                    status = "${job.reportType.replaceFirstChar { it.uppercase() }} export ready to share."
                }
                .onFailure {
                    status = "Could not download report file: ${it.message.orEmpty()}"
                }
        }
    }

    LaunchedEffect(Unit) {
        refreshHistory()
    }

    LaunchedEffect(selectedType, fromDate, toDate) {
        if (canViewProfitLoss && selectedType == ReportType.ProfitLoss) {
            refreshHistory()
        }
    }

    LaunchedEffect(reportJobs.any { it.status.equals("Pending", true) || it.status.equals("InProgress", true) }) {
        while (reportJobs.any { it.status.equals("Pending", true) || it.status.equals("InProgress", true) }) {
            delay(2000)
            gateway.getReportJobs()
                .onSuccess { reportJobs = it }
                .onFailure { status = "Could not refresh job status: ${it.message.orEmpty()}" }
            gateway.getReportFiles()
                .onSuccess { reportFiles = it }
        }
    }

    val availableReportTypes = remember(canViewProfitLoss) {
        if (canViewProfitLoss) ReportType.values().toList() else ReportType.values().filterNot { it == ReportType.ProfitLoss }
    }
    LaunchedEffect(canViewProfitLoss, selectedTypeName) {
        if (!canViewProfitLoss && selectedType == ReportType.ProfitLoss) {
            selectedTypeName = ReportType.Inventory.name
        }
    }

    ScreenColumn {
        ScreenHeader(
            title = "Reports",
            subtitle = "Preview totals, export files, manage expenses, and reuse saved report runs."
        )

        SectionTitle(
            title = "Report type",
            subtitle = "Choose a report before previewing or exporting."
        )
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            availableReportTypes.take(2).forEach { type ->
                SelectionPill(
                    text = type.label,
                    selected = selectedType == type,
                    onClick = { selectedTypeName = type.name },
                    modifier = Modifier.weight(1f)
                )
            }
        }
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            availableReportTypes.drop(2).forEach { type ->
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
                subtitle = "Use date filters for sales, profit and loss, or creditors."
            )
            DatePickerField(
                label = "From Date",
                value = fromDate,
                onValueChange = { fromDate = it },
                modifier = Modifier.fillMaxWidth()
            )
            DatePickerField(
                label = "To Date",
                value = toDate,
                onValueChange = { toDate = it },
                modifier = Modifier.fillMaxWidth()
            )
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            BrickButton(
                text = if (isLoading) "Loading..." else "Load Report",
                onClick = { if (!isLoading) loadPreview() },
                modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.REPORTS_LOAD)
            )
            SoftButton(
                text = "Refresh History",
                onClick = { if (!isLoading) refreshHistory() },
                modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.REPORTS_QUEUE_PDF)
            )
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            SoftButton(
                text = "Queue PDF",
                onClick = { if (!isLoading) queueExport(ReportExportFormat.Pdf) },
                modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.REPORTS_QUEUE_SPREADSHEET)
            )
            SoftButton(
                text = "Queue Spreadsheet",
                onClick = { if (!isLoading) queueExport(ReportExportFormat.Spreadsheet) },
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
                        Text(
                            line,
                            color = MaterialTheme.colorScheme.onBackground,
                            style = MaterialTheme.typography.bodyMedium
                        )
                    }
                }
            }
        }

        if (selectedType == ReportType.ProfitLoss && canViewProfitLoss) {
            SectionTitle(
                title = "Expenses",
                subtitle = "These entries feed directly into profit and loss."
            )

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MetricCard(
                    title = "Filtered Expenses",
                    value = "NGN ${"%,.2f".format(filteredExpensesTotal)}",
                    modifier = Modifier.weight(1f)
                )
                MetricCard(
                    title = "Categories",
                    value = filteredExpenseCategories.toString(),
                    supporting = "${expenses.size} entries",
                    modifier = Modifier.weight(1f)
                )
            }

            AccentCard(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(14.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    SectionTitle(
                        title = if (editingExpenseId.isBlank()) "Add expense" else "Edit expense",
                        subtitle = "Capture title, category, amount, date, and notes."
                    )
                    OutlinedTextField(
                        value = expenseTitle,
                        onValueChange = { expenseTitle = it },
                        label = { Text("Title") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = expenseCategory,
                        onValueChange = { expenseCategory = it },
                        label = { Text("Category") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = expenseAmount,
                        onValueChange = { expenseAmount = it },
                        label = { Text("Amount (NGN)") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    DatePickerField(
                        label = "Expense Date",
                        value = expenseDate,
                        onValueChange = { expenseDate = it },
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = expenseNotes,
                        onValueChange = { expenseNotes = it },
                        label = { Text("Notes") },
                        minLines = 3,
                        maxLines = 5,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        BrickButton(
                            text = if (isSubmittingExpense) "Saving..." else if (editingExpenseId.isBlank()) "Save Expense" else "Update Expense",
                            enabled = !isSubmittingExpense,
                            onClick = { if (!isLoading) saveExpense() },
                            modifier = Modifier.weight(1f)
                        )
                        SoftButton(
                            text = "Clear",
                            onClick = { resetExpenseEditor() },
                            modifier = Modifier.weight(1f)
                        )
                    }
                }
            }

            if (expenses.isEmpty()) {
                Text("No expenses found for the selected range.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                expenses.take(10).forEach { expense ->
                    AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Column(
                            modifier = Modifier.padding(14.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                                    Text(expense.title, style = MaterialTheme.typography.titleMedium)
                                    Text(
                                        "${expense.category} • ${formatUtcDate(expense.expenseDateUtcIso)}",
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        style = MaterialTheme.typography.bodySmall
                                    )
                                }
                                Text(
                                    "NGN ${"%,.2f".format(expense.amount)}",
                                    color = MaterialTheme.colorScheme.secondary,
                                    style = MaterialTheme.typography.titleMedium
                                )
                            }
                            if (!expense.notes.isNullOrBlank()) {
                                Text(expense.notes, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                SoftButton(
                                    text = "Edit",
                                    onClick = { editExpense(expense) },
                                    modifier = Modifier.weight(1f)
                                )
                                SoftButton(
                                    text = "Delete",
                                    onClick = { if (!isLoading) deleteExpense(expense.id) },
                                    modifier = Modifier.weight(1f)
                                )
                            }
                        }
                    }
                }
            }
        }

        SectionTitle(
            title = "Saved exports",
            subtitle = "Share previously generated files without rebuilding the report."
        )
        if (reportFiles.isEmpty()) {
            Text("No saved exports yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        } else {
            reportFiles.take(8).forEach { file ->
                AccentCard(modifier = Modifier.fillMaxWidth()) {
                    Column(
                        modifier = Modifier.padding(14.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Text(file.fileName, style = MaterialTheme.typography.titleMedium)
                        Text(
                            "${file.reportType.uppercase()} • ${file.format.uppercase()} • ${file.byteLength} bytes • ${formatUtcDateTime(file.createdAtUtc)}",
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            style = MaterialTheme.typography.bodySmall
                        )
                        SoftButton(
                            text = "Download And Share",
                            onClick = { shareSavedFile(file) },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }
            }
        }

        SectionTitle(
            title = "Recent report jobs",
            subtitle = "Latest generated runs for this shop."
        )
        if (reportJobs.isEmpty()) {
            Text("No report jobs recorded yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        } else {
            reportJobs.take(8).forEach { job ->
                AccentCard(modifier = Modifier.fillMaxWidth()) {
                    Column(
                        modifier = Modifier.padding(14.dp),
                        verticalArrangement = Arrangement.spacedBy(5.dp)
                    ) {
                        Text(
                            "${job.reportType.uppercase()} • ${job.format.uppercase()}",
                            style = MaterialTheme.typography.titleMedium
                        )
                        Text(
                            "Status: ${job.status} • Requested ${formatUtcDateTime(job.requestedAtUtc)}",
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            style = MaterialTheme.typography.bodySmall
                        )
                        if (!job.completedAtUtc.isNullOrBlank()) {
                            Text(
                                "Completed ${formatUtcDateTime(job.completedAtUtc)}",
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                        if (!job.failureReason.isNullOrBlank()) {
                            Text(
                                job.failureReason,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (!job.reportFileId.isNullOrBlank()) {
                                SoftButton(
                                    text = "Download And Share",
                                    onClick = { shareJobFile(job) },
                                    modifier = Modifier.weight(1f)
                                )
                            }
                            if (job.status.equals("Failed", true)) {
                                SoftButton(
                                    text = "Retry",
                                    onClick = { if (!isLoading) retryJob(job.id) },
                                    modifier = Modifier.weight(1f)
                                )
                            }
                        }
                    }
                }
            }
        }

        StatusBanner(status)
    }
}

private fun formatUtcDate(value: String): String {
    return runCatching {
        Instant.parse(value).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd MMM yyyy"))
    }.getOrDefault(value.take(10))
}

private fun formatUtcDateTime(value: String): String {
    return runCatching {
        Instant.parse(value).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd MMM yyyy, HH:mm"))
    }.getOrDefault(value.take(19))
}
