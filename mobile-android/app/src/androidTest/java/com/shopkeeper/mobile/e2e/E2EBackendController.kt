package com.shopkeeper.mobile.e2e

import com.shopkeeper.mobile.BuildConfig
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.concurrent.TimeUnit

data class E2ESeedData(
    val shopId: String,
    val shopCode: String,
    val ownerEmail: String,
    val managerEmail: String,
    val salespersonEmail: String,
    val password: String,
    val inventoryProductName: String,
    val creditSaleId: String,
    val creditSaleNumber: String
)

object E2EBackendController {
    private val client = OkHttpClient.Builder()
        .callTimeout(30, TimeUnit.SECONDS)
        .build()

    fun resetAndSeed(): E2ESeedData {
        val baseUrl = BuildConfig.API_BASE_URL.trimEnd('/')
        val request = Request.Builder()
            .url("$baseUrl/api/test/reset-and-seed")
            .header("X-E2E-Admin-Token", BuildConfig.E2E_ADMIN_TOKEN)
            .post("{}".toRequestBody("application/json".toMediaType()))
            .build()

        client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) {
                error("E2E reset failed with HTTP ${response.code}: ${response.body?.string().orEmpty()}")
            }
            val json = JSONObject(response.body?.string().orEmpty())
            return E2ESeedData(
                shopId = json.getString("shopId"),
                shopCode = json.getString("shopCode"),
                ownerEmail = json.getString("ownerEmail"),
                managerEmail = json.getString("managerEmail"),
                salespersonEmail = json.getString("salespersonEmail"),
                password = json.getString("password"),
                inventoryProductName = json.getString("inventoryProductName"),
                creditSaleId = json.getString("creditSaleId"),
                creditSaleNumber = json.getString("creditSaleNumber")
            )
        }
    }
}
