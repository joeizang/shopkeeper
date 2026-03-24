package com.shopkeeper.mobile.inventory

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.TextButton
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.NewInventoryInput
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.ConditionGradeDropdown
import com.shopkeeper.mobile.ui.components.ConditionGradeOption
import com.shopkeeper.mobile.ui.components.DatePickerField
import com.shopkeeper.mobile.ui.components.MetricCard
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import kotlinx.coroutines.launch

@Composable
fun InventoryScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var inventoryItems by remember { mutableStateOf<List<com.shopkeeper.mobile.core.data.local.InventoryItemEntity>>(emptyList()) }
    var searchQuery by rememberSaveable { mutableStateOf("") }
    var isAddingItem by rememberSaveable { mutableStateOf(false) }
    var editingItemId by rememberSaveable { mutableStateOf("") }
    var pendingDeleteItemId by rememberSaveable { mutableStateOf("") }

    var extractedSerial by rememberSaveable { mutableStateOf("") }
    var extractedModel by rememberSaveable { mutableStateOf("") }
    var productName by rememberSaveable { mutableStateOf("") }
    var quantity by rememberSaveable { mutableStateOf("1") }
    var costPrice by rememberSaveable { mutableStateOf("0") }
    var sellingPrice by rememberSaveable { mutableStateOf("0") }
    var expiryDateIso by rememberSaveable { mutableStateOf("") }
    var conditionNotes by rememberSaveable { mutableStateOf("") }
    var conditionGradeCode by rememberSaveable { mutableStateOf(ConditionGradeOption.A.code) }
    var isUsed by rememberSaveable { mutableStateOf(false) }
    var capturedPhotoUris by rememberSaveable { mutableStateOf<List<String>>(emptyList()) }
    var status by rememberSaveable { mutableStateOf("") }
    var isSubmittingItem by rememberSaveable { mutableStateOf(false) }
    var activeCameraActionName by rememberSaveable { mutableStateOf("") }
    val activeCameraAction = CameraAction.entries.firstOrNull { it.name == activeCameraActionName }

    fun refreshInventorySummary() {
        scope.launch {
            inventoryItems = runCatching { gateway.refreshInventory() }
                .getOrElse { gateway.getLocalInventory() }
        }
    }

    androidx.compose.runtime.LaunchedEffect(Unit) {
        refreshInventorySummary()
    }

    val filteredItems = remember(inventoryItems, searchQuery) {
        if (searchQuery.isBlank()) {
            inventoryItems
        } else {
            val query = searchQuery.trim().lowercase()
            inventoryItems.filter {
                it.productName.lowercase().contains(query) ||
                    it.modelNumber.orEmpty().lowercase().contains(query) ||
                    it.serialNumber.orEmpty().lowercase().contains(query)
            }
        }
    }

    ScreenColumn {
        ScreenHeader(
            title = "Inventory",
            subtitle = if (isAddingItem) {
                if (editingItemId.isNotBlank()) "Review item details and update stock information." else "Capture details, review them, and save the item."
            } else {
                "Track stock levels, worth, and item condition."
            }
        )

        if (!isAddingItem) {
            val totalProducts = inventoryItems.size
            val totalUnits = inventoryItems.sumOf { it.quantity }
            val totalWorth = inventoryItems.sumOf { it.costPrice * it.quantity }
            val lowStockItems = inventoryItems.count { it.quantity <= 2 }

            SectionTitle(
                title = "Stock summary",
                subtitle = "Current inventory position from local records."
            )

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MetricCard("Products", totalProducts.toString(), Modifier.weight(1f))
                MetricCard("Units", totalUnits.toString(), Modifier.weight(1f))
            }
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MetricCard("Stock Worth", "NGN ${"%.2f".format(totalWorth)}", Modifier.weight(1f))
                MetricCard("Low Stock", lowStockItems.toString(), Modifier.weight(1f))
            }

            SectionTitle(
                title = "Products",
                subtitle = "Search by product name, serial number, or model number."
            )
            OutlinedTextField(
                value = searchQuery,
                onValueChange = { searchQuery = it },
                label = { Text("Search products") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_SEARCH)
            )

            if (filteredItems.isEmpty()) {
                Text("No inventory items found.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                filteredItems.take(40).forEach { item ->
                    AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Column(
                            modifier = Modifier.padding(14.dp),
                            verticalArrangement = Arrangement.spacedBy(6.dp)
                        ) {
                            Text(item.productName, color = MaterialTheme.colorScheme.onBackground, style = MaterialTheme.typography.titleMedium)
                            Text(
                                "Qty: ${item.quantity} • Cost: NGN ${"%.2f".format(item.costPrice)} • Sell: NGN ${"%.2f".format(item.sellingPrice)}",
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            val details = listOfNotNull(
                                item.modelNumber?.takeIf { it.isNotBlank() }?.let { "Model: $it" },
                                item.serialNumber?.takeIf { it.isNotBlank() }?.let { "Serial: $it" },
                                item.expiryDateIso?.takeIf { it.isNotBlank() }?.let { "Expiry: ${it.take(10)}" }
                            )
                            if (details.isNotEmpty()) {
                                Text(
                                    details.joinToString(" • "),
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                SoftButton(
                                    onClick = {
                                        editingItemId = item.id
                                        isAddingItem = true
                                        extractedSerial = item.serialNumber.orEmpty()
                                        extractedModel = item.modelNumber.orEmpty()
                                        productName = item.productName
                                        quantity = item.quantity.toString()
                                        costPrice = item.costPrice.toString()
                                        sellingPrice = item.sellingPrice.toString()
                                        expiryDateIso = item.expiryDateIso?.take(10).orEmpty()
                                        conditionNotes = item.conditionNotes.orEmpty()
                                        conditionGradeCode = item.conditionGrade?.toIntOrNull() ?: ConditionGradeOption.A.code
                                        isUsed = item.itemType == "2"
                                        capturedPhotoUris = emptyList()
                                        status = "Editing ${item.productName}"
                                    },
                                    text = "Edit",
                                    modifier = Modifier.weight(1f)
                                )
                                Button(onClick = { pendingDeleteItemId = item.id }, modifier = Modifier.weight(1f)) {
                                    Text("Delete")
                                }
                            }
                        }
                    }
                }
            }

            BrickButton(
                text = "Add Inventory Item",
                onClick = {
                    editingItemId = ""
                    isAddingItem = true
                },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_ADD)
            )
        } else {
            val launchCameraAction = { action: CameraAction ->
                activeCameraActionName = action.name
            }

            val isEditing = editingItemId.isNotBlank()
            SectionTitle(
                title = if (isEditing) "Edit item" else "Add item",
                subtitle = "Camera capture is optional. Review and edit all values before saving."
            )
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                SoftButton(
                    text = "Back To Summary",
                    onClick = { isAddingItem = false },
                    modifier = Modifier.weight(1f)
                )
                SoftButton(
                    text = "Scan Details",
                    onClick = { launchCameraAction(CameraAction.ScanText) },
                    modifier = Modifier.weight(1f)
                )
            }
            SoftButton(
                text = "Capture Item Photo",
                onClick = { launchCameraAction(CameraAction.CapturePhoto) },
                modifier = Modifier.fillMaxWidth()
            )
            Text(
                "Captured photos: ${capturedPhotoUris.size}",
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            StatusBanner("Review OCR results before saving. Camera capture never saves directly.")

            SectionTitle(title = "Identifiers")
            OutlinedTextField(
                value = extractedModel,
                onValueChange = { extractedModel = it },
                label = { Text("Model Number") },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = extractedSerial,
                onValueChange = { extractedSerial = it },
                label = { Text("Serial Number") },
                modifier = Modifier.fillMaxWidth()
            )

            SectionTitle(title = "Item details")
            OutlinedTextField(
                value = productName,
                onValueChange = { productName = it },
                label = { Text("Product Name") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_PRODUCT_NAME)
            )

            OutlinedTextField(
                value = quantity,
                onValueChange = { quantity = it },
                label = { Text("Quantity") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_QUANTITY)
            )

            OutlinedTextField(
                value = costPrice,
                onValueChange = { costPrice = it },
                label = { Text("Cost Price") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_COST_PRICE)
            )

            OutlinedTextField(
                value = sellingPrice,
                onValueChange = { sellingPrice = it },
                label = { Text("Selling Price") },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_SELLING_PRICE)
            )

            DatePickerField(
                label = "Expiry Date (optional)",
                value = expiryDateIso,
                onValueChange = { expiryDateIso = it },
                modifier = Modifier.fillMaxWidth()
            )

            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(checked = isUsed, onCheckedChange = { isUsed = it })
                Text("Second-hand / Used item")
            }

            if (isUsed) {
                SectionTitle(title = "Condition")
                val selectedConditionGrade = ConditionGradeOption.entries.firstOrNull { it.code == conditionGradeCode }
                    ?: ConditionGradeOption.A
                ConditionGradeDropdown(
                    selected = selectedConditionGrade,
                    onSelected = { conditionGradeCode = it.code },
                    modifier = Modifier.fillMaxWidth()
                )
            }

            OutlinedTextField(
                value = conditionNotes,
                onValueChange = { conditionNotes = it },
                label = { Text("Condition Notes") },
                minLines = 4,
                maxLines = 8,
                modifier = Modifier.fillMaxWidth()
            )

            BrickButton(
                text = if (isSubmittingItem) "Saving..." else if (isEditing) "Update Inventory Item" else "Save Inventory Item",
                enabled = !isSubmittingItem,
                onClick = {
                    if (productName.isBlank()) {
                        status = "Product name is required"
                        return@BrickButton
                    }

                    scope.launch {
                        isSubmittingItem = true
                        val input = NewInventoryInput(
                            productName = productName,
                            modelNumber = extractedModel.ifBlank { null },
                            serialNumber = extractedSerial.ifBlank { null },
                            quantity = quantity.toIntOrNull() ?: 1,
                            expiryDateIso = expiryDateIso.ifBlank { null },
                            costPrice = costPrice.toDoubleOrNull() ?: 0.0,
                            sellingPrice = sellingPrice.toDoubleOrNull() ?: 0.0,
                            itemTypeCode = if (isUsed) 2 else 1,
                            conditionGradeCode = if (isUsed) conditionGradeCode else null,
                            conditionNotes = conditionNotes.ifBlank { null },
                            photoUris = capturedPhotoUris
                        )

                        val result = if (isEditing) {
                            gateway.updateInventoryItem(editingItemId, input)
                        } else {
                            gateway.saveInventoryItem(input).map { Unit }
                        }

                        isSubmittingItem = false
                        status = result.fold(
                            onSuccess = {
                                val action = if (isEditing) "Updated" else "Saved"
                                editingItemId = ""
                                capturedPhotoUris = emptyList()
                                extractedSerial = ""
                                extractedModel = ""
                                productName = ""
                                quantity = "1"
                                costPrice = "0"
                                sellingPrice = "0"
                                expiryDateIso = ""
                                conditionNotes = ""
                                conditionGradeCode = ConditionGradeOption.A.code
                                isUsed = false
                                isAddingItem = false
                                refreshInventorySummary()
                                "$action locally and queued for sync"
                            },
                            onFailure = { "${if (isEditing) "Update" else "Save"} failed: ${it.message.orEmpty()}" }
                        )
                    }
                },
                modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.INVENTORY_SAVE)
            )
        }

        StatusBanner(status)

        if (activeCameraAction != null) {
            com.shopkeeper.mobile.ui.components.CameraCaptureDialog(
                title = if (activeCameraAction == CameraAction.ScanText) "Scan Item Details" else "Capture Item Photo",
                subtitle = if (activeCameraAction == CameraAction.ScanText) {
                    "Capture a clear image of the model or serial label, then review the extracted values before saving."
                } else {
                    "Capture a full-resolution photo of the item for inventory records."
                },
                mode = if (activeCameraAction == CameraAction.ScanText) {
                    com.shopkeeper.mobile.ui.components.CameraCaptureMode.ScanText
                } else {
                    com.shopkeeper.mobile.ui.components.CameraCaptureMode.CapturePhoto
                },
                onDismissRequest = { activeCameraActionName = "" },
                onTextCaptured = { text ->
                    extractedSerial = findCandidate(text, "serial")
                    extractedModel = findCandidate(text, "model")
                    status = if (extractedSerial.isBlank() && extractedModel.isBlank()) {
                        "Scan complete. Review the image results and enter any missing values manually."
                    } else {
                        "Scan complete. Review the extracted model and serial values before saving."
                    }
                },
                onPhotoCaptured = { uri ->
                    capturedPhotoUris = capturedPhotoUris + uri.toString()
                    status = "Captured ${capturedPhotoUris.size + 1} photo(s)"
                },
                onError = { status = it }
            )
        }

        if (pendingDeleteItemId.isNotBlank()) {
            AlertDialog(
                onDismissRequest = { pendingDeleteItemId = "" },
                title = { Text("Delete Item") },
                text = { Text("Are you sure you want to delete this inventory item?") },
                confirmButton = {
                    TextButton(onClick = {
                        val itemId = pendingDeleteItemId
                        pendingDeleteItemId = ""
                        scope.launch {
                            val result = gateway.deleteInventoryItem(itemId)
                            status = result.fold(
                                onSuccess = {
                                    if (editingItemId == itemId) {
                                        editingItemId = ""
                                        isAddingItem = false
                                    }
                                    refreshInventorySummary()
                                    "Inventory item deleted"
                                },
                                onFailure = { "Delete failed: ${it.message.orEmpty()}" }
                            )
                        }
                    }) {
                        Text("Delete")
                    }
                },
                dismissButton = {
                    TextButton(onClick = { pendingDeleteItemId = "" }) {
                        Text("Cancel")
                    }
                }
            )
        }
    }
}

private enum class CameraAction {
    ScanText,
    CapturePhoto
}


private fun findCandidate(text: String, key: String): String {
    val lines = text.lines()
    val matchedLine = lines.firstOrNull { it.contains(key, ignoreCase = true) }
    return matchedLine?.substringAfter(':')?.trim().orEmpty()
}
