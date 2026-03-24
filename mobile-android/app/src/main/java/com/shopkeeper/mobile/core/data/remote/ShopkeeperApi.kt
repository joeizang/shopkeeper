package com.shopkeeper.mobile.core.data.remote

import okhttp3.ResponseBody
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.PATCH
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

interface ShopkeeperApi {
    @POST("api/v1/auth/register-owner")
    suspend fun registerOwner(@Body body: RegisterOwnerRequest): AuthResponse

    @POST("api/v1/auth/login")
    suspend fun login(@Body body: LoginRequest): AuthResponse

    @POST("api/v1/auth/google/mobile")
    suspend fun loginWithGoogle(@Body body: GoogleMobileAuthRequest): AuthResponse

    @POST("api/v1/auth/magic-link/request")
    suspend fun requestMagicLink(@Body body: MagicLinkRequestDto): MagicLinkRequestResponseDto

    @POST("api/v1/auth/magic-link/verify")
    suspend fun verifyMagicLink(@Body body: MagicLinkVerifyRequestDto): AuthResponse

    @GET("api/v1/account/me")
    suspend fun getAccountMe(): AccountProfileResponseDto

    @PATCH("api/v1/account/me")
    suspend fun updateAccountMe(@Body body: UpdateAccountProfileRequestDto): AccountProfileResponseDto

    @GET("api/v1/account/sessions")
    suspend fun getAccountSessions(): List<SessionViewDto>

    @POST("api/v1/account/sessions/revoke-all")
    suspend fun revokeAllAccountSessions(): Map<String, Any>

    @POST("api/v1/account/sessions/{id}/revoke")
    suspend fun revokeAccountSession(@Path("id") sessionId: String): Map<String, Any>

    @GET("api/v1/account/linked-identities")
    suspend fun getLinkedIdentities(): List<LinkedIdentityViewDto>

    @GET("api/v1/shops/me")
    suspend fun getMyShops(): List<ShopViewDto>

    @PATCH("api/v1/shops/{id}/settings")
    suspend fun updateShopVatSettings(
        @Path("id") shopId: String,
        @Body body: UpdateShopVatSettingsRequestDto
    ): ShopViewDto

    @GET("api/v1/shops/{id}/staff")
    suspend fun getShopStaff(@Path("id") shopId: String): List<StaffMembershipViewDto>

    @POST("api/v1/shops/{id}/staff/invite")
    suspend fun inviteShopStaff(
        @Path("id") shopId: String,
        @Body body: InviteStaffRequestDto
    ): StaffMembershipViewDto

    @PATCH("api/v1/shops/{id}/staff/{staffId}")
    suspend fun updateShopStaff(
        @Path("id") shopId: String,
        @Path("staffId") staffId: String,
        @Body body: UpdateStaffMembershipRequestDto
    ): StaffMembershipViewDto

    @GET("api/v1/inventory/items")
    suspend fun listInventory(): List<InventoryItemResponse>

    @POST("api/v1/inventory/items")
    suspend fun createInventoryItem(@Body body: CreateInventoryItemRequest): InventoryItemResponse

    @POST("api/v1/inventory/items/{id}/photos")
    suspend fun addInventoryItemPhoto(
        @Path("id") itemId: String,
        @Body body: AddItemPhotoRequest
    ): Map<String, Any>

    @POST("api/v1/sales")
    suspend fun createSale(@Body body: CreateSaleRequest): CreateSaleResponse

    @GET("api/v1/sales/{id}")
    suspend fun getSale(@Path("id") saleId: String): SaleDetailResponseDto

    @POST("api/v1/sales/{id}/payments")
    suspend fun addSalePayment(
        @Path("id") saleId: String,
        @Body body: AddSalePaymentRequest
    ): Map<String, Any>

    @GET("api/v1/sales/{id}/receipt")
    suspend fun getSaleReceipt(@Path("id") saleId: String): ReceiptViewDto

    @GET("api/v1/sales/{id}/receipt/owner")
    suspend fun getSaleOwnerReceipt(@Path("id") saleId: String): OwnerReceiptViewDto

    @POST("api/v1/credits/{saleId}/repayments")
    suspend fun addCreditRepayment(
        @Path("saleId") saleId: String,
        @Body body: CreditRepaymentRequest
    ): Map<String, Any>

    @GET("api/v1/credits/{saleId}")
    suspend fun getCreditDetails(@Path("saleId") saleId: String): CreditDetailResponseDto

    @POST("api/v1/sync/push")
    suspend fun pushChanges(@Body body: SyncPushRequest): SyncPushResponse

    @POST("api/v1/sync/pull")
    suspend fun pullChanges(@Body body: SyncPullRequest): SyncPullResponse

    @GET("api/v1/reports/inventory")
    suspend fun getInventoryReport(): InventoryReportResponseDto

    @GET("api/v1/reports/sales")
    suspend fun getSalesReport(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): SalesReportResponseDto

    @GET("api/v1/reports/profit-loss")
    suspend fun getProfitLossReport(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): ProfitLossReportResponseDto

    @GET("api/v1/reports/creditors")
    suspend fun getCreditorsReport(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): CreditorsReportResponseDto

    @GET("api/v1/expenses")
    suspend fun getExpenses(
        @Query("from") from: String?,
        @Query("to") to: String?
    ): List<ExpenseViewDto>

    @POST("api/v1/expenses")
    suspend fun createExpense(@Body body: CreateExpenseRequestDto): ExpenseViewDto

    @PATCH("api/v1/expenses/{id}")
    suspend fun updateExpense(
        @Path("id") expenseId: String,
        @Body body: UpdateExpenseRequestDto
    ): ExpenseViewDto

    @retrofit2.http.DELETE("api/v1/expenses/{id}")
    suspend fun deleteExpense(@Path("id") expenseId: String): Map<String, Any>

    @GET("api/v1/reports/jobs")
    suspend fun getReportJobs(): List<ReportJobViewDto>

    @POST("api/v1/reports/jobs")
    suspend fun queueReportJob(@Body body: QueueReportJobRequestDto): ReportJobViewDto

    @GET("api/v1/reports/jobs/{id}")
    suspend fun getReportJob(@Path("id") reportJobId: String): ReportJobViewDto

    @POST("api/v1/reports/jobs/{id}/retry")
    suspend fun retryReportJob(@Path("id") reportJobId: String): ReportJobViewDto

    @GET("api/v1/reports/files")
    suspend fun getReportFiles(): List<ReportFileViewDto>

    @GET("api/v1/reports/files/{id}/download")
    suspend fun downloadReportFile(@Path("id") reportFileId: String): ResponseBody

    @GET("api/v1/reports/{reportType}/export")
    suspend fun exportReport(
        @Path("reportType") reportType: String,
        @Query("format") format: String,
        @Query("from") from: String?,
        @Query("to") to: String?
    ): ResponseBody
}
