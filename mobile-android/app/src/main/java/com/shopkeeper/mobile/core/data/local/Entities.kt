package com.shopkeeper.mobile.core.data.local

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "inventory_items")
data class InventoryItemEntity(
    @PrimaryKey val id: String,
    val productName: String,
    val modelNumber: String?,
    val serialNumber: String?,
    val quantity: Int,
    val expiryDateIso: String?,
    val costPrice: Double,
    val sellingPrice: Double,
    val itemType: String,
    val conditionGrade: String?,
    val conditionNotes: String?,
    val rowVersionBase64: String,
    val updatedAtUtcIso: String
)

@Entity(tableName = "sales")
data class SaleEntity(
    @PrimaryKey val id: String,
    val saleNumber: String,
    val customerName: String?,
    val customerPhone: String?,
    val lineItemsSummary: String,
    val subtotal: Double,
    val vatAmount: Double,
    val discountAmount: Double,
    val totalAmount: Double,
    val outstandingAmount: Double,
    val status: String,
    val isCredit: Boolean,
    val dueDateUtcIso: String?,
    val updatedAtUtcIso: String
)

@Entity(tableName = "sync_queue")
data class SyncQueueEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val entityName: String,
    val entityId: String,
    val operation: String,
    val payloadJson: String,
    val rowVersionBase64: String?,
    val enqueuedAtUtcIso: String,
    val retryCount: Int = 0
)

@Entity(tableName = "sync_conflicts")
data class SyncConflictEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val entityName: String,
    val entityId: String,
    val serverPayloadJson: String,
    val localPayloadJson: String,
    val conflictReason: String,
    val createdAtUtcIso: String
)

@Entity(tableName = "pending_item_photos")
data class PendingItemPhotoEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val localItemId: String,
    val photoUri: String,
    val createdAtUtcIso: String
)
