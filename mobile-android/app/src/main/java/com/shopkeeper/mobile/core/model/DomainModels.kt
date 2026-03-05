package com.shopkeeper.mobile.core.model

import java.time.Instant
import java.time.LocalDate

enum class MembershipRole { OWNER, STAFF }
enum class ItemType { NEW, USED }
enum class ItemConditionGrade { A, B, C }
enum class PaymentMethod { CASH, BANK_TRANSFER, POS }
enum class SaleStatus { COMPLETED, PARTIALLY_PAID, VOID }

data class InventoryItemModel(
    val id: String,
    val productName: String,
    val modelNumber: String?,
    val serialNumber: String?,
    val quantity: Int,
    val expiryDate: LocalDate?,
    val costPrice: Double,
    val sellingPrice: Double,
    val itemType: ItemType,
    val conditionGrade: ItemConditionGrade?,
    val conditionNotes: String?,
    val rowVersionBase64: String
)

data class SaleReceiptModel(
    val saleId: String,
    val saleNumber: String,
    val createdAtUtc: Instant,
    val subtotal: Double,
    val vatAmount: Double,
    val discountAmount: Double,
    val totalAmount: Double,
    val paidAmount: Double,
    val outstandingAmount: Double
)
