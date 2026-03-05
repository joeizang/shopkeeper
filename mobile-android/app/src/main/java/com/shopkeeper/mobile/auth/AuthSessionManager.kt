package com.shopkeeper.mobile.auth

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

class AuthSessionManager(context: Context) {
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val prefs = EncryptedSharedPreferences.create(
        context,
        "shopkeeper_secure_session",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    fun saveSession(accessToken: String, refreshToken: String, shopId: String, role: String) {
        prefs.edit()
            .putString("access_token", accessToken)
            .putString("refresh_token", refreshToken)
            .putString("shop_id", shopId)
            .putString("role", role)
            .apply()
    }

    fun accessToken(): String? = prefs.getString("access_token", null)

    fun refreshToken(): String? = prefs.getString("refresh_token", null)

    fun shopId(): String? = prefs.getString("shop_id", null)

    fun role(): String? = prefs.getString("role", null)

    fun clear() {
        prefs.edit().clear().apply()
    }
}
