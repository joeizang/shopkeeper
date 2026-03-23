package com.shopkeeper.mobile.sales

import android.net.Uri
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Share
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
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.NewSaleInput
import com.shopkeeper.mobile.core.data.NewSaleLineInput
import com.shopkeeper.mobile.core.data.NewSalePaymentInput
import com.shopkeeper.mobile.core.data.RecordedSale
import com.shopkeeper.mobile.core.data.ShopSummary
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.core.data.local.InventoryItemEntity
import com.shopkeeper.mobile.core.data.local.SaleEntity
import com.shopkeeper.mobile.receipts.ReceiptPdfGenerator
import com.shopkeeper.mobile.receipts.shareReceiptPdf
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.DatePickerField
import com.shopkeeper.mobile.ui.components.MetricCard
import com.shopkeeper.mobile.ui.components.PaymentMethodDropdown
import com.shopkeeper.mobile.ui.components.PaymentMethodOption
import com.shopkeeper.mobile.ui.components.SaleCelebrationOverlay
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
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
    val capabilities = gateway.sessionCapabilities()
    val clipboardManager = remember(context) {
        context.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
    }

    var customerName by rememberSaveable { mutableStateOf("") }
    var customerPhone by rememberSaveable { mutableStateOf("") }
    var applyShopDiscount by rememberSaveable { mutableStateOf(false) }
    var dueDateUtc by rememberSaveable { mutableStateOf("") }
    var searchQuery by rememberSaveable { mutableStateOf("") }
    var selectedItemId by rememberSaveable { mutableStateOf("") }
    var selectedItemQuantity by rememberSaveable { mutableStateOf("1") }
    var paymentAmountDraft by rememberSaveable { mutableStateOf("") }
    var paymentRefDraft by rememberSaveable { mutableStateOf("") }
    var paymentMethodCodeDraft by rememberSaveable { mutableStateOf(PaymentMethodOption.Cash.code) }
    val paymentMethodDraft = PaymentMethodOption.fromCode(paymentMethodCodeDraft)
    var saleLines by rememberSaveable(stateSaver = UiSaleLinesSaver) { mutableStateOf<List<UiSaleLine>>(emptyList()) }
    var salePayments by rememberSaveable(stateSaver = UiSalePaymentsSaver) { mutableStateOf<List<UiSalePayment>>(emptyList()) }
    var activeScanActionName by rememberSaveable { mutableStateOf("") }
    val activeScanAction = SalesScanAction.entries.firstOrNull { it.name == activeScanActionName }

    var inventory by remember { mutableStateOf<List<InventoryItemEntity>>(emptyList()) }
    var todayCompletedSales by remember { mutableStateOf<List<SaleEntity>>(emptyList()) }
    var shopSummary by remember { mutableStateOf<ShopSummary?>(null) }
    var status by rememberSaveable { mutableStateOf("") }
    var lastSale by remember { mutableStateOf<RecordedSale?>(null) }
    var isCreatingSale by rememberSaveable { mutableStateOf(false) }
    var showCelebration by remember { mutableStateOf(false) }

    fun refreshSummary() {
        scope.launch {
            val inv = runCatching { gateway.refreshInventory() }
                .getOrElse { gateway.getLocalInventory() }
            inventory = inv
            todayCompletedSales = gateway.getTodayCompletedSales()
            gateway.getCurrentShop()
                .onSuccess { shopSummary = it }
                .onFailure { status = "Could not load shop settings: ${it.message.orEmpty()}" }
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

    fun addPaymentSplit() {
        val amount = paymentAmountDraft.toDoubleOrNull()
        if (amount == null || amount <= 0.0) {
            status = "Enter a valid payment amount."
            return
        }

        salePayments = salePayments + UiSalePayment(
            amount = amount,
            paymentMethodCode = paymentMethodDraft.code,
            paymentReference = paymentRefDraft.ifBlank { null }
        )
        paymentAmountDraft = ""
        paymentRefDraft = ""
        paymentMethodCodeDraft = PaymentMethodOption.Cash.code
        status = "Payment split added."
    }

    fun launchScan(action: SalesScanAction) {
        activeScanActionName = action.name
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

    LaunchedEffect(searchQuery, filteredInventory.size) {
        if (searchQuery.isNotBlank() && filteredInventory.size == 1) {
            selectedItemId = filteredInventory.first().id
            selectedItemQuantity = "1"
        }
    }

    val subtotal = saleLines.sumOf { it.quantity * it.unitPrice }
    val configuredDiscountPercent = shopSummary?.defaultDiscountPercent ?: 0.0
    val discountAmount = if (applyShopDiscount) subtotal * configuredDiscountPercent else 0.0
    val vatEnabled = shopSummary?.vatEnabled ?: true
    val vatRate = shopSummary?.vatRate ?: 0.075
    val taxableBase = (subtotal - discountAmount).coerceAtLeast(0.0)
    val vatAmount = if (vatEnabled) taxableBase * vatRate else 0.0
    val totalAmount = taxableBase + vatAmount
    val paidAmount = salePayments.sumOf { it.amount }
    val outstandingAmount = (totalAmount - paidAmount).coerceAtLeast(0.0)

    Box(modifier = Modifier.fillMaxSize()) {
    ScreenColumn {
        ScreenHeader(
            title = "Sales",
            subtitle = if (isCreatingSale) {
                "Build the sale, add one or more payment splits, and review VAT before saving."
            } else {
                "Review today’s completed sales and issue receipts."
            }
        )

        if (!isCreatingSale) {
            SectionTitle(
                title = "Today",
                subtitle = "Completed sales and current revenue."
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                MetricCard("Completed", todayCompletedSales.size.toString(), Modifier.weight(1f))
                MetricCard(
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
                    refreshSummary()
                },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_ADD),
                icon = Icons.Outlined.Add
            )

            if (todayCompletedSales.isEmpty()) {
                Text("No completed sales yet today.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                SectionTitle(title = "Completed sales")
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
                            paymentReference = salePayments.firstOrNull()?.paymentReference
                        )
                        shareReceiptPdf(context, pdfFile)
                    },
                    modifier = Modifier.fillMaxWidth(),
                    icon = Icons.Outlined.Share
                )
            }
        } else {
            SectionTitle(
                title = "Create sale",
                subtitle = "Search inventory, add line items, then capture single or split payments."
            )

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

            SoftButton(
                text = "Back To Summary",
                onClick = { isCreatingSale = false },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = customerName,
                onValueChange = { customerName = it },
                label = { Text("Customer Name") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_CUSTOMER_NAME)
            )

            OutlinedTextField(
                value = customerPhone,
                onValueChange = { customerPhone = it },
                label = { Text("Customer Phone") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_CUSTOMER_PHONE)
            )

            OutlinedTextField(
                value = searchQuery,
                onValueChange = { searchQuery = it },
                label = { Text("Search inventory") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_SEARCH)
            )

            SectionTitle(
                title = "Inventory items",
                subtitle = "Search by name, model, or serial number, then add the selected quantity."
            )
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                filteredInventory.take(25).forEach { item ->
                    AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .testTag("sales.item.${item.productName}")
                                .clickable {
                                    selectedItemId = item.id
                                    selectedItemQuantity = "1"
                                }
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
                            SoftButton(
                                text = "Pick",
                                onClick = {
                                    selectedItemId = item.id
                                    selectedItemQuantity = "1"
                                },
                                modifier = Modifier.testTag("sales.pick.${item.productName}")
                            )
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
                            modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_SELECTED_QUANTITY)
                        )
                        BrickButton(
                            text = "Add / Update Line",
                            onClick = {
                                addLine(selectedItem, selectedItemQuantity.toIntOrNull() ?: 1)
                                status = "Line item updated."
                            },
                            modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_ADD_LINE)
                        )
                    }
                }
            }

            if (saleLines.isEmpty()) {
                Text("No line items yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                SectionTitle(title = "Sale line items")
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
                            SoftButton(
                                text = "Remove",
                                onClick = {
                                    saleLines = saleLines.filterNot { it.inventoryItemId == line.inventoryItemId }
                                }
                            )
                        }
                    }
                }
            }

            SectionTitle(
                title = "Tax and totals",
                subtitle = if (vatEnabled) "VAT is enabled for ${shopSummary?.name ?: "this shop"} at ${vatRate.toPercentString()}." else "VAT is disabled for this shop."
            )
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MetricCard("Subtotal", "NGN ${"%.2f".format(subtotal)}", Modifier.weight(1f))
                MetricCard("Discount", "NGN ${"%.2f".format(discountAmount)}", Modifier.weight(1f))
            }
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MetricCard("VAT", "NGN ${"%.2f".format(vatAmount)}", Modifier.weight(1f))
                MetricCard("Total", "NGN ${"%.2f".format(totalAmount)}", Modifier.weight(1f))
            }

            if (configuredDiscountPercent > 0.0) {
                SectionTitle(
                    title = "Shop discount",
                    subtitle = "This sale can apply the owner-configured discount of ${configuredDiscountPercent.toPercentString()}."
                )
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SoftButton(
                        text = if (applyShopDiscount) "Remove Discount" else "Apply Shop Discount",
                        onClick = { applyShopDiscount = !applyShopDiscount },
                        modifier = Modifier.weight(1f)
                    )
                    MetricCard(
                        title = "Configured Discount",
                        value = "NGN ${"%.2f".format(discountAmount)}",
                        modifier = Modifier.weight(1f)
                    )
                }
            } else {
                Text(
                    if (capabilities.canManageShopSettings) "No default discount is configured for this shop yet. Set it in Profile."
                    else "No shop discount is configured.",
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }

            SectionTitle(
                title = "Payment splits",
                subtitle = "Add one or more payments. Any outstanding balance becomes credit."
            )

            OutlinedTextField(
                value = paymentAmountDraft,
                onValueChange = { paymentAmountDraft = it },
                label = { Text("Payment Amount") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_PAYMENT_AMOUNT)
            )

            PaymentMethodDropdown(
                selected = paymentMethodDraft,
                onSelected = { paymentMethodCodeDraft = it.code },
                modifier = Modifier.fillMaxWidth()
            )

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = paymentRefDraft,
                    onValueChange = { paymentRefDraft = it },
                    label = { Text("POS/Transfer Ref (optional)") },
                    modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.SALES_PAYMENT_REFERENCE)
                )
                SoftButton(
                    text = "Scan Ref",
                    onClick = { launchScan(SalesScanAction.Reference) },
                    modifier = Modifier.weight(1f)
                )
            }

            BrickButton(
                text = "Add Payment Split",
                onClick = { addPaymentSplit() },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_ADD_PAYMENT_SPLIT)
            )

            if (salePayments.isEmpty()) {
                Text("No payment splits added yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                salePayments.forEachIndexed { index, payment ->
                    AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(12.dp),
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text(
                                    "${PaymentMethodOption.fromCode(payment.paymentMethodCode).label} • NGN ${"%.2f".format(payment.amount)}",
                                    color = MaterialTheme.colorScheme.onBackground
                                )
                                Text(
                                    payment.paymentReference ?: "No reference",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                            SoftButton(
                                text = "Remove",
                                onClick = { salePayments = salePayments.filterIndexed { paymentIndex, _ -> paymentIndex != index } }
                            )
                        }
                    }
                }
            }

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MetricCard("Paid", "NGN ${"%.2f".format(paidAmount)}", Modifier.weight(1f))
                MetricCard("Outstanding", "NGN ${"%.2f".format(outstandingAmount)}", Modifier.weight(1f))
            }

            DatePickerField(
                label = "Due Date (required when outstanding remains)",
                value = dueDateUtc,
                onValueChange = { dueDateUtc = it },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.SALES_DUE_DATE)
            )

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                BrickButton(
                    text = "Save Sale",
                    onClick = {
                        if (saleLines.isEmpty()) {
                            status = "Add at least one line item."
                            return@BrickButton
                        }

                        if (outstandingAmount > 0.0 && dueDateUtc.isBlank()) {
                            status = "Set a due date for any sale with outstanding balance."
                            return@BrickButton
                        }

                        scope.launch {
                            val input = NewSaleInput(
                                lines = saleLines.map {
                                    NewSaleLineInput(
                                        inventoryItemId = it.inventoryItemId,
                                        productName = it.productName,
                                        quantity = it.quantity,
                                        unitPrice = it.unitPrice
                                    )
                                },
                                discountAmount = discountAmount,
                                payments = salePayments.map {
                                    NewSalePaymentInput(
                                        amount = it.amount,
                                        paymentMethodCode = it.paymentMethodCode,
                                        paymentReference = it.paymentReference
                                    )
                                },
                                customerName = customerName.ifBlank { null },
                                customerPhone = customerPhone.ifBlank { null },
                                isCredit = outstandingAmount > 0.0,
                                dueDateUtcIso = dueDateUtc.ifBlank { null }?.let { "${it}T00:00:00Z" },
                                vatEnabled = vatEnabled,
                                vatRate = vatRate
                            )

                            val result = gateway.recordSale(input)
                            status = result.fold(
                                onSuccess = {
                                    lastSale = it
                                    isCreatingSale = false
                                    saleLines = emptyList()
                                    salePayments = emptyList()
                                    searchQuery = ""
                                    selectedItemId = ""
                                    selectedItemQuantity = "1"
                                    paymentAmountDraft = ""
                                    paymentRefDraft = ""
                                    customerName = ""
                                    customerPhone = ""
                                    dueDateUtc = ""
                                    applyShopDiscount = false
                                    showCelebration = true
                                    refreshSummary()
                                    if (it.synced) "Sale saved and synced (${it.saleNumber})" else "Sale saved locally, sync pending"
                                },
                                onFailure = { "Sale failed: ${it.message.orEmpty()}" }
                            )
                        }
                    },
                    modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.SALES_SAVE)
                )

                SoftButton(text = "Cancel", onClick = { isCreatingSale = false }, modifier = Modifier.weight(1f))
            }
        }

        if (activeScanAction != null) {
            com.shopkeeper.mobile.ui.components.CameraCaptureDialog(
                title = if (activeScanAction == SalesScanAction.Reference) "Scan Payment Reference" else "Scan Customer Details",
                subtitle = if (activeScanAction == SalesScanAction.Reference) {
                    "Capture the POS slip or transfer reference so OCR can pull the transaction text."
                } else {
                    "Capture a screenshot or contact card to import the customer name and phone details."
                },
                mode = com.shopkeeper.mobile.ui.components.CameraCaptureMode.ScanText,
                onDismissRequest = { activeScanActionName = "" },
                onTextCaptured = { text ->
                    when (activeScanAction) {
                        SalesScanAction.Reference -> {
                            paymentRefDraft = extractReferenceText(text)
                            status = if (paymentRefDraft.isBlank()) {
                                "Scan complete. Review the OCR text and enter the reference manually if needed."
                            } else {
                                "Reference scanned. You can edit it before adding the payment split."
                            }
                        }
                        SalesScanAction.Customer -> {
                            importCustomerDetails(text)
                        }
                    }
                },
                onError = { status = it }
            )
        }

        StatusBanner(status)
    }

    SaleCelebrationOverlay(
        visible = showCelebration,
        onDismiss = { showCelebration = false }
    )
    }
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

private data class UiSalePayment(
    val amount: Double,
    val paymentMethodCode: Int,
    val paymentReference: String?
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

private val UiSalePaymentsSaver = listSaver<List<UiSalePayment>, String>(
    save = { payments ->
        payments.map { payment ->
            listOf(
                payment.amount.toString(),
                payment.paymentMethodCode.toString(),
                Uri.encode(payment.paymentReference.orEmpty())
            ).joinToString("::")
        }
    },
    restore = { encoded ->
        encoded.mapNotNull { raw ->
            val parts = raw.split("::")
            if (parts.size != 3) {
                null
            } else {
                UiSalePayment(
                    amount = parts[0].toDoubleOrNull() ?: 0.0,
                    paymentMethodCode = parts[1].toIntOrNull() ?: PaymentMethodOption.Cash.code,
                    paymentReference = Uri.decode(parts[2]).ifBlank { null }
                )
            }
        }
    }
)

private fun formatSaleTime(iso: String): String {
    return runCatching {
        val instant = Instant.parse(iso)
        val local = instant.atZone(ZoneId.systemDefault())
        local.format(DateTimeFormatter.ofPattern("HH:mm"))
    }.getOrElse { "--:--" }
}

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

private fun Double.toPercentString(): String = "%.1f%%".format(this * 100)
