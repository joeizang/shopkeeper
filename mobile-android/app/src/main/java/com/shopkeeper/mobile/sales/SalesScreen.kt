package com.shopkeeper.mobile.sales

import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Share
import androidx.compose.foundation.BorderStroke
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.NewSaleInput
import com.shopkeeper.mobile.core.data.RecordedSale
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.core.data.local.InventoryItemEntity
import com.shopkeeper.mobile.core.data.local.SaleEntity
import com.shopkeeper.mobile.receipts.ReceiptPdfGenerator
import com.shopkeeper.mobile.receipts.shareReceiptPdf
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.DatePickerField
import com.shopkeeper.mobile.ui.components.PaymentMethodDropdown
import com.shopkeeper.mobile.ui.components.PaymentMethodOption
import kotlinx.coroutines.launch
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun SalesScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }
    val isOwner = remember { gateway.isOwnerSession() }
    val clipboardManager = remember(context) {
        context.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
    }
    val recognizer = remember { com.google.mlkit.vision.text.TextRecognition.getClient(com.google.mlkit.vision.text.latin.TextRecognizerOptions.DEFAULT_OPTIONS) }

    var customerName by rememberSaveable { mutableStateOf("") }
    var customerPhone by rememberSaveable { mutableStateOf("") }
    var discount by rememberSaveable { mutableStateOf("0") }
    var paidAmount by rememberSaveable { mutableStateOf("0") }
    var paymentRef by rememberSaveable { mutableStateOf("") }
    var paymentMethodCode by rememberSaveable { mutableStateOf(PaymentMethodOption.Cash.code) }
    val paymentMethod = PaymentMethodOption.fromCode(paymentMethodCode)
    var dueDateUtc by rememberSaveable { mutableStateOf("") }
    var searchQuery by rememberSaveable { mutableStateOf("") }
    var selectedItemId by rememberSaveable { mutableStateOf("") }
    var selectedItemQuantity by rememberSaveable { mutableStateOf("1") }
    var saleLines by rememberSaveable(stateSaver = UiSaleLinesSaver) { mutableStateOf<List<UiSaleLine>>(emptyList()) }
    var pendingScanActionName by rememberSaveable { mutableStateOf(SalesScanAction.Reference.name) }
    val pendingScanAction = runCatching { SalesScanAction.valueOf(pendingScanActionName) }
        .getOrDefault(SalesScanAction.Reference)

    var inventory by remember { mutableStateOf<List<InventoryItemEntity>>(emptyList()) }
    var todayCompletedSales by remember { mutableStateOf<List<SaleEntity>>(emptyList()) }
    var status by rememberSaveable { mutableStateOf("") }
    var lastSale by remember { mutableStateOf<RecordedSale?>(null) }
    var isCreatingSale by rememberSaveable { mutableStateOf(false) }

    fun refreshSummary() {
        scope.launch {
            val inv = runCatching { gateway.refreshInventory() }
                .getOrElse { gateway.getLocalInventory() }
            inventory = inv
            todayCompletedSales = gateway.getTodayCompletedSales()
        }
    }

    fun addLine(item: InventoryItemEntity, qty: Int) {
        val quantity = qty.coerceAtLeast(1)
        val existing = saleLines.firstOrNull { it.inventoryItemId == item.id }
        saleLines = if (existing == null) {
            saleLines + UiSaleLine(
                inventoryItemId = item.id,
                productName = item.productName,
                quantity = quantity,
                unitPrice = item.sellingPrice
            )
        } else {
            saleLines.map {
                if (it.inventoryItemId == item.id) it.copy(quantity = quantity) else it
            }
        }
    }

    fun importCustomerDetails(text: String) {
        val parsed = parseCustomerDetails(text)
        if (parsed.first != null) {
            customerName = parsed.first.orEmpty()
        }
        if (parsed.second != null) {
            customerPhone = parsed.second.orEmpty()
        }
        status = "Customer details imported. You can edit them before saving."
    }

    val scanLauncher = androidx.activity.compose.rememberLauncherForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.TakePicturePreview()
    ) { bitmap: android.graphics.Bitmap? ->
        if (bitmap == null) {
            return@rememberLauncherForActivityResult
        }

        val image = com.google.mlkit.vision.common.InputImage.fromBitmap(bitmap, 0)
        recognizer.process(image)
            .addOnSuccessListener { result ->
                when (pendingScanAction) {
                    SalesScanAction.Reference -> {
                        paymentRef = extractReferenceText(result.text)
                        status = "Reference scanned. You can edit before saving."
                    }
                    SalesScanAction.Customer -> {
                        importCustomerDetails(result.text)
                    }
                }
            }
            .addOnFailureListener {
                status = "Scan failed: ${it.message.orEmpty()}"
            }
    }

    val cameraPermissionLauncher = androidx.activity.compose.rememberLauncherForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            scanLauncher.launch(null)
        } else {
            status = "Camera permission denied."
        }
    }

    fun launchScan(action: SalesScanAction) {
        pendingScanActionName = action.name
        val hasPermission = androidx.core.content.ContextCompat.checkSelfPermission(
            context,
            android.Manifest.permission.CAMERA
        ) == android.content.pm.PackageManager.PERMISSION_GRANTED

        if (hasPermission) {
            scanLauncher.launch(null)
        } else {
            cameraPermissionLauncher.launch(android.Manifest.permission.CAMERA)
        }
    }

    LaunchedEffect(Unit) {
        refreshSummary()
    }

    val filteredInventory = remember(inventory, searchQuery) {
        if (searchQuery.isBlank()) {
            inventory
        } else {
            val query = searchQuery.trim().lowercase()
            inventory.filter {
                it.productName.lowercase().contains(query) ||
                    it.modelNumber.orEmpty().lowercase().contains(query) ||
                    it.serialNumber.orEmpty().lowercase().contains(query)
            }
        }
    }

    val subtotal = saleLines.sumOf { it.quantity * it.unitPrice }
    val discountAmount = discount.toDoubleOrNull() ?: 0.0
    val totalAmount = (subtotal - discountAmount).coerceAtLeast(0.0)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        if (!isCreatingSale) {
            Text("Today's Sales", style = MaterialTheme.typography.titleLarge)

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SummaryCard("Completed", todayCompletedSales.size.toString(), Modifier.weight(1f))
                SummaryCard(
                    "Revenue",
                    "NGN ${"%.2f".format(todayCompletedSales.sumOf { it.totalAmount })}",
                    Modifier.weight(1f)
                )
            }

            BrickButton(
                text = "Add Sale",
                onClick = {
                    isCreatingSale = true
                    status = ""
                },
                modifier = Modifier.fillMaxWidth(),
                icon = Icons.Outlined.Add
            )

            if (todayCompletedSales.isEmpty()) {
                Text("No completed sales yet today.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    todayCompletedSales.take(20).forEach { sale ->
                        AccentCard(modifier = Modifier.fillMaxWidth()) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(14.dp),
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                                    Text(sale.saleNumber, style = MaterialTheme.typography.titleMedium)
                                    Text(formatSaleTime(sale.updatedAtUtcIso), color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                                Text(
                                    "NGN ${"%.2f".format(sale.totalAmount)}",
                                    color = MaterialTheme.colorScheme.secondary,
                                    style = MaterialTheme.typography.titleMedium
                                )
                            }
                        }
                    }
                }
            }

            if (lastSale != null) {
                BrickButton(
                    text = "Share Last Receipt",
                    onClick = {
                        val sale = lastSale ?: return@BrickButton
                        val generator = ReceiptPdfGenerator(context)
                        val pdfFile: File = generator.generateSampleReceipt(
                            saleNumber = sale.saleNumber,
                            customerName = customerName,
                            totalAmount = sale.totalAmount,
                            paymentReference = paymentRef
                        )
                        shareReceiptPdf(context, pdfFile)
                    },
                    modifier = Modifier.fillMaxWidth(),
                    icon = Icons.Outlined.Share
                )
            }
        } else {
            Text("Create Sale", style = MaterialTheme.typography.titleLarge)

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                BrickButton(
                    text = "Scan Customer",
                    onClick = { launchScan(SalesScanAction.Customer) },
                    modifier = Modifier.weight(1f)
                )
                BrickButton(
                    text = "Import Customer",
                    onClick = {
                        val clipText = clipboardManager.primaryClip
                            ?.getItemAt(0)
                            ?.coerceToText(context)
                            ?.toString()
                            .orEmpty()
                        if (clipText.isBlank()) {
                            status = "Copy customer text from WhatsApp/Telegram first."
                        } else {
                            importCustomerDetails(clipText)
                        }
                    },
                    modifier = Modifier.weight(1f)
                )
            }

            OutlinedTextField(
                value = customerName,
                onValueChange = { customerName = it },
                label = { Text("Customer Name") },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = customerPhone,
                onValueChange = { customerPhone = it },
                label = { Text("Customer Phone") },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = searchQuery,
                onValueChange = { searchQuery = it },
                label = { Text("Search inventory") },
                modifier = Modifier.fillMaxWidth()
            )

            Text("Select item from inventory", color = MaterialTheme.colorScheme.onSurfaceVariant)
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                filteredInventory.take(25).forEach { item ->
                    AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(10.dp),
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                Text(item.productName, color = MaterialTheme.colorScheme.onBackground)
                                Text(
                                    "Stock: ${item.quantity} • NGN ${"%.2f".format(item.sellingPrice)}",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                            Button(onClick = {
                                selectedItemId = item.id
                                selectedItemQuantity = "1"
                            }) {
                                Text("Pick")
                            }
                        }
                    }
                }
            }

            val selectedItem = inventory.firstOrNull { it.id == selectedItemId }
            if (selectedItem != null) {
                AccentCard(modifier = Modifier.fillMaxWidth()) {
                    Column(
                        modifier = Modifier.padding(12.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Text("Selected: ${selectedItem.productName}", color = MaterialTheme.colorScheme.onBackground)
                        OutlinedTextField(
                            value = selectedItemQuantity,
                            onValueChange = { selectedItemQuantity = it },
                            label = { Text("Quantity for selected item") },
                            modifier = Modifier.fillMaxWidth()
                        )
                        BrickButton(
                            text = "Add / Update Line",
                            onClick = {
                                addLine(selectedItem, selectedItemQuantity.toIntOrNull() ?: 1)
                                status = "Line item updated."
                            },
                            modifier = Modifier.fillMaxWidth()
                        )
                    }
                }
            }

            if (saleLines.isEmpty()) {
                Text("No line items yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                Text("Sale Line Items", style = MaterialTheme.typography.titleMedium)
                saleLines.forEach { line ->
                    AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(12.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                                Text(line.productName, color = MaterialTheme.colorScheme.onBackground)
                                Text(
                                    "NGN ${"%.2f".format(line.unitPrice)} each",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                            OutlinedTextField(
                                value = line.quantity.toString(),
                                onValueChange = { value ->
                                    val newQty = value.toIntOrNull() ?: line.quantity
                                    saleLines = saleLines.map {
                                        if (it.inventoryItemId == line.inventoryItemId) {
                                            it.copy(quantity = newQty.coerceAtLeast(1))
                                        } else {
                                            it
                                        }
                                    }
                                },
                                label = { Text("Qty") },
                                modifier = Modifier.weight(1f)
                            )
                            Button(onClick = {
                                saleLines = saleLines.filterNot { it.inventoryItemId == line.inventoryItemId }
                            }) {
                                Text("Remove")
                            }
                        }
                    }
                }
            }

            SummaryCard("Subtotal", "NGN ${"%.2f".format(subtotal)}", Modifier.fillMaxWidth())
            SummaryCard("Total", "NGN ${"%.2f".format(totalAmount)}", Modifier.fillMaxWidth())

            OutlinedTextField(
                value = discount,
                onValueChange = { discount = it },
                label = { Text("Discount") },
                enabled = isOwner,
                modifier = Modifier.fillMaxWidth()
            )
            if (!isOwner) {
                Text(
                    "Discount is editable by owner only.",
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            OutlinedTextField(
                value = paidAmount,
                onValueChange = { paidAmount = it },
                label = { Text("Paid Amount") },
                modifier = Modifier.fillMaxWidth()
            )

            PaymentMethodDropdown(
                selected = paymentMethod,
                onSelected = { paymentMethodCode = it.code },
                modifier = Modifier.fillMaxWidth()
            )

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = paymentRef,
                    onValueChange = { paymentRef = it },
                    label = { Text("POS/Transfer Ref (optional)") },
                    modifier = Modifier.weight(1f)
                )
                BrickButton(
                    text = "Scan Ref",
                    onClick = { launchScan(SalesScanAction.Reference) },
                    modifier = Modifier.weight(1f)
                )
            }

            DatePickerField(
                label = "Due Date (optional, for credit sale)",
                value = dueDateUtc,
                onValueChange = { dueDateUtc = it },
                modifier = Modifier.fillMaxWidth()
            )

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                BrickButton(
                    text = "Save Sale",
                    onClick = {
                        if (saleLines.isEmpty()) {
                            status = "Add at least one line item."
                            return@BrickButton
                        }

                        scope.launch {
                            val input = NewSaleInput(
                                lines = saleLines.map {
                                    com.shopkeeper.mobile.core.data.NewSaleLineInput(
                                        inventoryItemId = it.inventoryItemId,
                                        productName = it.productName,
                                        quantity = it.quantity,
                                        unitPrice = it.unitPrice
                                    )
                                },
                                discountAmount = if (isOwner) (discount.toDoubleOrNull() ?: 0.0) else 0.0,
                                paidAmount = paidAmount.toDoubleOrNull() ?: 0.0,
                                paymentMethodCode = paymentMethod.code,
                                paymentReference = paymentRef.ifBlank { null },
                                customerName = customerName.ifBlank { null },
                                customerPhone = customerPhone.ifBlank { null },
                                isCredit = dueDateUtc.isNotBlank(),
                                dueDateUtcIso = dueDateUtc.ifBlank { null }?.let { "${it}T00:00:00Z" }
                            )

                            val result = gateway.recordSale(input)
                            status = result.fold(
                                onSuccess = {
                                    lastSale = it
                                    isCreatingSale = false
                                    saleLines = emptyList()
                                    searchQuery = ""
                                    selectedItemId = ""
                                    selectedItemQuantity = "1"
                                    refreshSummary()
                                    if (it.synced) "Sale saved and synced (${it.saleNumber})" else "Sale saved locally, sync pending"
                                },
                                onFailure = { "Sale failed: ${it.message.orEmpty()}" }
                            )
                        }
                    },
                    modifier = Modifier.weight(1f)
                )

                Button(onClick = { isCreatingSale = false }, modifier = Modifier.weight(1f)) {
                    Text("Cancel")
                }
            }
        }

        if (status.isNotBlank()) {
            Text(status, color = MaterialTheme.colorScheme.secondary)
        }
    }
}

@Composable
private fun SummaryCard(title: String, value: String, modifier: Modifier = Modifier) {
    Card(
        modifier = modifier,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.35f))
    ) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(title, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(value, color = MaterialTheme.colorScheme.secondary)
        }
    }
}

private fun formatSaleTime(iso: String): String {
    return runCatching {
        val instant = Instant.parse(iso)
        val local = instant.atZone(ZoneId.systemDefault())
        local.format(DateTimeFormatter.ofPattern("HH:mm"))
    }.getOrElse { "--:--" }
}

private enum class SalesScanAction {
    Reference,
    Customer
}

private data class UiSaleLine(
    val inventoryItemId: String,
    val productName: String,
    val quantity: Int,
    val unitPrice: Double
)

private val UiSaleLinesSaver = listSaver<List<UiSaleLine>, String>(
    save = { lines ->
        lines.map { line ->
            listOf(
                line.inventoryItemId,
                Uri.encode(line.productName),
                line.quantity.toString(),
                line.unitPrice.toString()
            ).joinToString("::")
        }
    },
    restore = { encoded ->
        encoded.mapNotNull { raw ->
            val parts = raw.split("::")
            if (parts.size != 4) {
                null
            } else {
                UiSaleLine(
                    inventoryItemId = parts[0],
                    productName = Uri.decode(parts[1]),
                    quantity = parts[2].toIntOrNull() ?: 1,
                    unitPrice = parts[3].toDoubleOrNull() ?: 0.0
                )
            }
        }
    }
)

private fun extractReferenceText(text: String): String {
    val lines = text.lines().map { it.trim() }.filter { it.isNotBlank() }
    val candidate = lines.firstOrNull { it.length >= 4 } ?: ""
    return candidate.take(64)
}

private fun parseCustomerDetails(text: String): Pair<String?, String?> {
    val compact = text.replace('\n', ' ')
    val phoneMatch = Regex("""(\+?\d[\d\s\-]{7,}\d)""").find(compact)?.value
    val phone = phoneMatch?.replace(" ", "")?.replace("-", "")
    val nameLine = text.lines()
        .map { it.trim() }
        .firstOrNull { it.isNotBlank() && (phone == null || !it.contains(phone)) }

    return nameLine to phone
}
