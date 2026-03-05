package com.shopkeeper.mobile.core.data.remote

import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path

interface ShopkeeperApi {
    @POST("/api/v1/auth/register-owner")
    suspend fun registerOwner(@Body body: RegisterOwnerRequest): AuthResponse

    @POST("/api/v1/auth/login")
    suspend fun login(@Body body: LoginRequest): AuthResponse

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

    @POST("/api/v1/sync/push")
    suspend fun pushChanges(@Body body: SyncPushRequest): SyncPushResponse

    @POST("/api/v1/sync/pull")
    suspend fun pullChanges(@Body body: SyncPullRequest): SyncPullResponse
}
