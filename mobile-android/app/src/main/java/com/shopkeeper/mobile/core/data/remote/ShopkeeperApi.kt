package com.shopkeeper.mobile.core.data.remote

import okhttp3.ResponseBody
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.PATCH
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

interface ShopkeeperApi {
    @POST("/api/v1/auth/register-owner")
    suspend fun registerOwner(@Body body: RegisterOwnerRequest): AuthResponse

    @POST("/api/v1/auth/login")
    suspend fun login(@Body body: LoginRequest): AuthResponse

    @POST("/api/v1/auth/google/mobile")
    suspend fun loginWithGoogle(@Body body: GoogleMobileAuthRequest): AuthResponse

    @POST("/api/v1/auth/magic-link/request")
    suspend fun requestMagicLink(@Body body: MagicLinkRequestDto): MagicLinkRequestResponseDto

    @POST("/api/v1/auth/magic-link/verify")
    suspend fun verifyMagicLink(@Body body: MagicLinkVerifyRequestDto): AuthResponse

    @GET("/api/v1/account/me")
    suspend fun getAccountMe(): AccountProfileResponseDto

    @PATCH("/api/v1/account/me")
    suspend fun updateAccountMe(@Body body: UpdateAccountProfileRequestDto): AccountProfileResponseDto

    @GET("/api/v1/account/sessions")
    suspend fun getAccountSessions(): List<SessionViewDto>

    @POST("/api/v1/account/sessions/{id}/revoke")
    suspend fun revokeAccountSession(@Path("id") sessionId: String): Map<String, Any>

    @GET("/api/v1/account/linked-identities")
    suspend fun getLinkedIdentities(): List<LinkedIdentityViewDto>

    @GET("/api/v1/inventory/items")
    suspend fun listInventory(): List<InventoryItemResponse>

    @POST("/api/v1/inventory/items")
    suspend fun createInventoryItem(@Body body: CreateInventoryItemRequest): InventoryItemResponse

    @POST("/api/v1/inventory/items/{id}/photos")
    suspend fun addInventoryItemPhoto(
        @Path("id") itemId: String,
        @Body body: AddItemPhotoRequest
    ): Map<String, Any>

    @POST("/api/v1/sales/")
    suspend fun createSale(@Body body: CreateSaleRequest): CreateSaleResponse

    @POST("/api/v1/credits/{saleId}/repayments")
    suspend fun addCreditRepayment(
        @Path("saleId") saleId: String,
        @Body body: CreditRepaymentRequest
    ): Map<String, Any>

    @GET("/api/v1/credits/{saleId}")
    suspend fun getCreditDetails(@Path("saleId") saleId: String): CreditDetailResponseDto

    @POST("/api/v1/sync/push")
    suspend fun pushChanges(@Body body: SyncPushRequest): SyncPushResponse

    @POST("/api/v1/sync/pull")
    suspend fun pullChanges(@Body body: SyncPullRequest): SyncPullResponse

    @GET("/api/v1/reports/inventory")
    suspend fun getInventoryReport(): InventoryReportResponseDto

    @GET("/api/v1/reports/sales")
    suspend fun getSalesReport(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): SalesReportResponseDto

    @GET("/api/v1/reports/profit-loss")
    suspend fun getProfitLossReport(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): ProfitLossReportResponseDto

    @GET("/api/v1/reports/creditors")
    suspend fun getCreditorsReport(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): CreditorsReportResponseDto

    @GET("/api/v1/reports/{reportType}/export")
    suspend fun exportReport(
        @Path("reportType") reportType: String,
        @Query("format") format: String,
        @Query("from") from: String?,
        @Query("to") to: String?
    ): ResponseBody
}
