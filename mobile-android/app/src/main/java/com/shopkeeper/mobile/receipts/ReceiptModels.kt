package com.shopkeeper.mobile.receipts

data class ReceiptSourcePayload(
    val localSaleId: String,
    val saleId: String?,
    val saleNumber: String,
    val shopName: String,
    val customerName: String?,
    val cashierName: String,
    val createdAtUtcIso: String,
    val subtotal: Double,
    val vatAmount: Double,
    val discountAmount: Double,
    val totalAmount: Double,
    val paidAmount: Double,
    val outstandingAmount: Double,
    val lines: List<ReceiptLinePayload>,
    val payments: List<ReceiptPaymentPayload>
)

data class ReceiptLinePayload(
    val productName: String,
    val quantity: Int,
    val unitPrice: Double,
    val lineTotal: Double,
    val costPrice: Double
)

data class ReceiptPaymentPayload(
    val methodCode: Int,
    val amount: Double,
    val reference: String?,
    val cashTendered: Double?
)

object ReceiptKinds {
    const val Customer = "customer"
    const val Owner = "owner"
}

object ReceiptVersions {
    const val Local = "local"
    const val Canonical = "canonical"
}

object ReceiptGenerationStatus {
    const val Ready = "ready"
    const val Failed = "failed"
}
