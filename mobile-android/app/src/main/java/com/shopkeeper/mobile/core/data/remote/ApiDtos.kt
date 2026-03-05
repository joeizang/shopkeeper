package com.shopkeeper.mobile.core.data.remote

data class RegisterOwnerRequest(
    val fullName: String,
    val email: String?,
    val phone: String?,
    val password: String,
    val shopName: String,
    val vatEnabled: Boolean,
    val vatRate: Double
)

data class LoginRequest(
    val login: String,
    val password: String,
    val shopId: String?
)

data class GoogleMobileAuthRequest(
    val idToken: String,
    val shopId: String?
)

data class MagicLinkRequestDto(
    val email: String,
    val shopId: String?
)

data class MagicLinkVerifyRequestDto(
    val token: String,
    val shopId: String?
)

data class MagicLinkRequestResponseDto(
    val requestId: String,
    val expiresAtUtc: String,
    val message: String,
    val debugToken: String?
)

data class AuthResponse(
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiresAtUtc: String,
    val shopId: String,
    val role: String
)

data class AccountProfileResponseDto(
    val userId: String,
    val fullName: String,
    val email: String?,
    val phone: String?,
    val avatarUrl: String?,
    val preferredLanguage: String?,
    val timezone: String?,
    val createdAtUtc: String
)

data class UpdateAccountProfileRequestDto(
    val fullName: String,
    val phone: String?,
    val avatarUrl: String?,
    val preferredLanguage: String?,
    val timezone: String?
)

data class SessionViewDto(
    val sessionId: String,
    val shopId: String,
    val role: String,
    val deviceId: String?,
    val deviceName: String?,
    val createdAtUtc: String,
    val expiresAtUtc: String,
    val lastSeenAtUtc: String?,
    val isRevoked: Boolean
)

data class LinkedIdentityViewDto(
    val provider: String,
    val providerSubject: String,
    val email: String?,
    val emailVerified: Boolean,
    val createdAtUtc: String,
    val lastUsedAtUtc: String
)

data class InventoryItemResponse(
    val id: String,
    val productName: String,
    val modelNumber: String?,
    val serialNumber: String?,
    val quantity: Int,
    val expiryDate: String?,
    val costPrice: Double,
    val sellingPrice: Double,
    val itemType: Int,
    val conditionGrade: Int?,
    val conditionNotes: String?,
    val photoUris: List<String>,
    val rowVersionBase64: String
)

data class CreateInventoryItemRequest(
    val productName: String,
    val modelNumber: String?,
    val serialNumber: String?,
    val quantity: Int,
    val expiryDate: String?,
    val costPrice: Double,
    val sellingPrice: Double,
    val itemType: Int,
    val conditionGrade: Int?,
    val conditionNotes: String?
)

data class AddItemPhotoRequest(
    val photoUri: String
)

data class SaleLineRequest(
    val inventoryItemId: String,
    val quantity: Int,
    val unitPrice: Double
)

data class SalePaymentRequest(
    val method: Int,
    val amount: Double,
    val reference: String?
)

data class CreateSaleRequest(
    val customerName: String?,
    val customerPhone: String?,
    val discountAmount: Double,
    val isCredit: Boolean,
    val dueDateUtc: String?,
    val lines: List<SaleLineRequest>,
    val initialPayments: List<SalePaymentRequest>?
)

data class CreateSaleResponse(
    val id: String,
    val saleNumber: String,
    val totalAmount: Double,
    val outstandingAmount: Double
)

data class CreditRepaymentRequest(
    val amount: Double,
    val method: Int,
    val reference: String?,
    val notes: String?
)

data class SyncPushChange(
    val deviceId: String,
    val entityName: String,
    val entityId: String,
    val operation: Int,
    val payloadJson: String,
    val clientUpdatedAtUtc: String,
    val rowVersionBase64: String?
)

data class SyncPushRequest(val changes: List<SyncPushChange>)

data class SyncConflictView(
    val changeId: String,
    val entityName: String,
    val entityId: String,
    val reason: String,
    val serverPayloadJson: String? = null,
    val serverRowVersionBase64: String? = null
)

data class SyncPushResponse(
    val acceptedCount: Int,
    val conflicts: List<SyncConflictView>
)

data class SyncPullRequest(
    val deviceId: String,
    val sinceUtc: String?
)

data class SyncPullResponse(
    val serverTimestampUtc: String,
    val changes: List<SyncPushChange>
)

data class SaleSyncPayload(
    val id: String,
    val saleNumber: String,
    val subtotal: Double,
    val vatAmount: Double,
    val discountAmount: Double,
    val totalAmount: Double,
    val outstandingAmount: Double,
    val status: String,
    val isCredit: Boolean,
    val dueDateUtc: String?,
    val updatedAtUtc: String
)

data class InventoryReportRowDto(
    val itemId: String,
    val productName: String,
    val quantity: Int,
    val costPrice: Double,
    val sellingPrice: Double,
    val costValue: Double,
    val sellingValue: Double,
    val expiryDate: String?
)

data class InventoryReportResponseDto(
    val generatedAtUtc: String,
    val totalProducts: Int,
    val totalUnits: Int,
    val lowStockItems: Int,
    val totalCostValue: Double,
    val totalSellingValue: Double,
    val items: List<InventoryReportRowDto>
)

data class SalesDailySummaryRowDto(
    val date: String,
    val salesCount: Int,
    val revenue: Double,
    val outstanding: Double
)

data class SalesPaymentSummaryRowDto(
    val method: String,
    val amount: Double
)

data class SalesReportResponseDto(
    val generatedAtUtc: String,
    val fromUtc: String,
    val toUtc: String,
    val salesCount: Int,
    val revenue: Double,
    val vatAmount: Double,
    val discountAmount: Double,
    val outstandingAmount: Double,
    val daily: List<SalesDailySummaryRowDto>,
    val payments: List<SalesPaymentSummaryRowDto>
)

data class ProfitLossReportResponseDto(
    val generatedAtUtc: String,
    val fromUtc: String,
    val toUtc: String,
    val revenue: Double,
    val cogs: Double,
    val grossProfit: Double,
    val expenses: Double,
    val netProfitLoss: Double
)

data class CreditorReportRowDto(
    val creditAccountId: String,
    val saleId: String,
    val saleNumber: String,
    val customerName: String,
    val itemsSummary: String,
    val dueDateUtc: String,
    val daysOverdue: Int,
    val outstandingAmount: Double,
    val status: String
)

data class CreditorsReportResponseDto(
    val generatedAtUtc: String,
    val fromUtc: String?,
    val toUtc: String?,
    val openCredits: Int,
    val totalOutstanding: Double,
    val credits: List<CreditorReportRowDto>
)
