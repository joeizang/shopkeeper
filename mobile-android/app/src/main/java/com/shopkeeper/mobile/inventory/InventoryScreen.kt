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
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
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
    var searchQuery by remember { mutableStateOf("") }
    var isAddingItem by remember { mutableStateOf(false) }

    var extractedSerial by remember { mutableStateOf("") }
    var extractedModel by remember { mutableStateOf("") }
    var productName by remember { mutableStateOf("") }
    var quantity by remember { mutableStateOf("1") }
    var costPrice by remember { mutableStateOf("0") }
    var sellingPrice by remember { mutableStateOf("0") }
    var expiryDateIso by remember { mutableStateOf("") }
    var conditionNotes by remember { mutableStateOf("") }
    var selectedConditionGrade by remember { mutableStateOf(ConditionGradeOption.A) }
    var isUsed by remember { mutableStateOf(false) }
    var capturedPhotoUris by remember { mutableStateOf<List<String>>(emptyList()) }
    var status by remember { mutableStateOf("") }
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
                        }
                    }
                }
            }

            BrickButton(
                text = "Add Inventory Item",
                onClick = { isAddingItem = true },
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

            Text("Add Inventory Item", style = MaterialTheme.typography.titleLarge)
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

            OutlinedTextField(
                value = expiryDateIso,
                onValueChange = { expiryDateIso = it },
                label = { Text("Expiry (YYYY-MM-DD, optional)") },
                modifier = Modifier.fillMaxWidth()
            )

            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(checked = isUsed, onCheckedChange = { isUsed = it })
                Text("Second-hand / Used item")
            }

            if (isUsed) {
                ConditionGradeDropdown(
                    selected = selectedConditionGrade,
                    onSelected = { selectedConditionGrade = it },
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

            BrickButton(text = "Save Inventory Item", onClick = {
                if (productName.isBlank()) {
                    status = "Product name is required"
                    return@BrickButton
                }

                scope.launch {
                    val result = gateway.saveInventoryItem(
                        NewInventoryInput(
                            productName = productName,
                            modelNumber = extractedModel.ifBlank { null },
                            serialNumber = extractedSerial.ifBlank { null },
                            quantity = quantity.toIntOrNull() ?: 1,
                            expiryDateIso = expiryDateIso.ifBlank { null },
                            costPrice = costPrice.toDoubleOrNull() ?: 0.0,
                            sellingPrice = sellingPrice.toDoubleOrNull() ?: 0.0,
                            itemTypeCode = if (isUsed) 2 else 1,
                            conditionGradeCode = if (isUsed) selectedConditionGrade.code else null,
                            conditionNotes = conditionNotes.ifBlank { null },
                            photoUris = capturedPhotoUris
                        )
                    )

                    status = result.fold(
                        onSuccess = {
                            capturedPhotoUris = emptyList()
                            extractedSerial = ""
                            extractedModel = ""
                            productName = ""
                            quantity = "1"
                            costPrice = "0"
                            sellingPrice = "0"
                            expiryDateIso = ""
                            conditionNotes = ""
                            isUsed = false
                            isAddingItem = false
                            refreshInventorySummary()
                            "Saved locally and queued for sync"
                        },
                        onFailure = { "Save failed: ${it.message.orEmpty()}" }
                    )
                }
            }, modifier = Modifier.fillMaxWidth())
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
