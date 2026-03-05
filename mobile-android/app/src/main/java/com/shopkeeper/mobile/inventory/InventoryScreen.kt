package com.shopkeeper.mobile.inventory

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import com.shopkeeper.mobile.core.data.NewInventoryInput
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.ConditionGradeDropdown
import com.shopkeeper.mobile.ui.components.ConditionGradeOption
import com.shopkeeper.mobile.ui.components.DatePickerField
import kotlinx.coroutines.launch
import java.io.File
import java.io.FileOutputStream
import java.time.Instant

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
    var pendingCameraAction by remember { mutableStateOf(CameraAction.ScanText) }

    val recognizer = remember { TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS) }

    val scanLauncher = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { bitmap: Bitmap? ->
        if (bitmap != null) {
            val image = InputImage.fromBitmap(bitmap, 0)
            recognizer.process(image)
                .addOnSuccessListener { result ->
                    val text = result.text
                    extractedSerial = findCandidate(text, "serial")
                    extractedModel = findCandidate(text, "model")
                }
                .addOnFailureListener {
                    status = "OCR failed: ${it.message.orEmpty()}"
                }
        }
    }

    val photoCaptureLauncher = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { bitmap: Bitmap? ->
        if (bitmap == null) {
            return@rememberLauncherForActivityResult
        }

        runCatching { saveBitmapToCache(context, bitmap) }
            .onSuccess { uri ->
                capturedPhotoUris = capturedPhotoUris + uri.toString()
                status = "Captured ${capturedPhotoUris.size} photo(s)"
            }
            .onFailure { ex ->
                status = "Photo capture failed: ${ex.message.orEmpty()}"
            }
    }

    val cameraPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            runCatching {
                when (pendingCameraAction) {
                    CameraAction.ScanText -> scanLauncher.launch(null)
                    CameraAction.CapturePhoto -> photoCaptureLauncher.launch(null)
                }
            }
                .onFailure { status = "Failed to open camera: ${it.message.orEmpty()}" }
        } else {
            status = "Camera permission denied"
        }
    }

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

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        if (!isAddingItem) {
            val totalProducts = inventoryItems.size
            val totalUnits = inventoryItems.sumOf { it.quantity }
            val totalWorth = inventoryItems.sumOf { it.costPrice * it.quantity }
            val lowStockItems = inventoryItems.count { it.quantity <= 2 }

            Text("Inventory Summary", style = MaterialTheme.typography.titleLarge)

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                InventoryMetricCard("Products", totalProducts.toString(), Modifier.weight(1f))
                InventoryMetricCard("Units", totalUnits.toString(), Modifier.weight(1f))
            }
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                InventoryMetricCard("Stock Worth", "NGN ${"%.2f".format(totalWorth)}", Modifier.weight(1f))
                InventoryMetricCard("Low Stock", lowStockItems.toString(), Modifier.weight(1f))
            }

            OutlinedTextField(
                value = searchQuery,
                onValueChange = { searchQuery = it },
                label = { Text("Search products") },
                modifier = Modifier.fillMaxWidth()
            )

            if (filteredItems.isEmpty()) {
                Text("No inventory items found.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                filteredItems.take(40).forEach { item ->
                    com.shopkeeper.mobile.ui.components.AccentCard(modifier = Modifier.fillMaxWidth()) {
                        Column(
                            modifier = Modifier.padding(12.dp),
                            verticalArrangement = Arrangement.spacedBy(4.dp)
                        ) {
                            Text(item.productName, color = MaterialTheme.colorScheme.onBackground)
                            Text(
                                "Qty: ${item.quantity} • Cost: NGN ${"%.2f".format(item.costPrice)} • Sell: NGN ${"%.2f".format(item.sellingPrice)}",
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(
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
                                    }
                                ) {
                                    Text("Edit")
                                }
                                Button(onClick = { pendingDeleteItemId = item.id }) {
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
                modifier = Modifier.fillMaxWidth()
            )
        } else {
            val launchCameraAction = { action: CameraAction ->
                pendingCameraAction = action
                val hasPermission = ContextCompat.checkSelfPermission(
                    context,
                    Manifest.permission.CAMERA
                ) == PackageManager.PERMISSION_GRANTED

                if (hasPermission) {
                    runCatching {
                        when (action) {
                            CameraAction.ScanText -> scanLauncher.launch(null)
                            CameraAction.CapturePhoto -> photoCaptureLauncher.launch(null)
                        }
                    }.onFailure { status = "Failed to open camera: ${it.message.orEmpty()}" }
                } else {
                    cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
                }
            }

            val isEditing = editingItemId.isNotBlank()
            Text(
                if (isEditing) "Edit Inventory Item" else "Add Inventory Item",
                style = MaterialTheme.typography.titleLarge
            )
            BrickButton(
                text = "Back To Summary",
                onClick = { isAddingItem = false },
                modifier = Modifier.fillMaxWidth()
            )
            BrickButton(
                text = "Scan With Camera",
                onClick = { launchCameraAction(CameraAction.ScanText) },
                modifier = Modifier.fillMaxWidth()
            )
            BrickButton(
                text = "Capture Item Photo",
                onClick = { launchCameraAction(CameraAction.CapturePhoto) },
                modifier = Modifier.fillMaxWidth()
            )
            Text(
                "Captured photos: ${capturedPhotoUris.size}",
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Text("Review OCR result before save", color = MaterialTheme.colorScheme.onSurfaceVariant)

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

            OutlinedTextField(
                value = productName,
                onValueChange = { productName = it },
                label = { Text("Product Name") },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = quantity,
                onValueChange = { quantity = it },
                label = { Text("Quantity") },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = costPrice,
                onValueChange = { costPrice = it },
                label = { Text("Cost Price") },
                modifier = Modifier.fillMaxWidth()
            )

            OutlinedTextField(
                value = sellingPrice,
                onValueChange = { sellingPrice = it },
                label = { Text("Selling Price") },
                modifier = Modifier.fillMaxWidth()
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

            BrickButton(text = if (isEditing) "Update Inventory Item" else "Save Inventory Item", onClick = {
                if (productName.isBlank()) {
                    status = "Product name is required"
                    return@BrickButton
                }

                scope.launch {
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
            }, modifier = Modifier.fillMaxWidth())
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

        if (status.isNotBlank()) {
            Text(status, color = MaterialTheme.colorScheme.secondary)
        }
    }
}

@Composable
private fun InventoryMetricCard(title: String, value: String, modifier: Modifier = Modifier) {
    com.shopkeeper.mobile.ui.components.AccentCard(modifier = modifier) {
        Column(
            modifier = Modifier.padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            Text(title, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(value, color = MaterialTheme.colorScheme.onBackground, style = MaterialTheme.typography.titleMedium)
        }
    }
}

private enum class CameraAction {
    ScanText,
    CapturePhoto
}

private fun saveBitmapToCache(context: android.content.Context, bitmap: Bitmap): Uri {
    val dir = File(context.cacheDir, "inventory-photos").apply { mkdirs() }
    val file = File(dir, "item-${Instant.now().toEpochMilli()}.jpg")
    FileOutputStream(file).use { out ->
        bitmap.compress(Bitmap.CompressFormat.JPEG, 90, out)
    }
    return Uri.fromFile(file)
}

private fun findCandidate(text: String, key: String): String {
    val lines = text.lines()
    val matchedLine = lines.firstOrNull { it.contains(key, ignoreCase = true) }
    return matchedLine?.substringAfter(':')?.trim().orEmpty()
}
