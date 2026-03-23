package com.shopkeeper.mobile.auth

import android.content.Context
import android.content.SharedPreferences
import android.os.UserManager
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class AuthSessionManager(context: Context) {
    private val prefs = createPreferences(context.applicationContext)

    private val _authState = MutableStateFlow(!prefs.getString("access_token", null).isNullOrBlank())
    val authState: StateFlow<Boolean> = _authState.asStateFlow()

    fun saveSession(accessToken: String, refreshToken: String, shopId: String, role: String) {
        prefs.edit()
            .putString("access_token", accessToken)
            .putString("refresh_token", refreshToken)
            .putString("shop_id", shopId)
            .putString("role", role)
            .apply()
        _authState.value = true
    }

    fun accessToken(): String? = prefs.getString("access_token", null)

    fun refreshToken(): String? = prefs.getString("refresh_token", null)

    fun shopId(): String? = prefs.getString("shop_id", null)

    fun role(): String? = prefs.getString("role", null)

    fun hasActiveSession(): Boolean = !accessToken().isNullOrBlank()

    fun clear() {
        prefs.edit().clear().apply()
        _authState.value = false
    }

    private fun createPreferences(context: Context): SharedPreferences {
        val storageContext = if (isUserUnlocked(context)) {
            context
        } else {
            context.createDeviceProtectedStorageContext()
        }

        return try {
            createEncryptedPreferences(storageContext)
        } catch (_: IllegalStateException) {
            storageContext.getSharedPreferences(FALLBACK_PREFS_NAME, Context.MODE_PRIVATE)
        } catch (_: Exception) {
            storageContext.getSharedPreferences(FALLBACK_PREFS_NAME, Context.MODE_PRIVATE)
        }
    }

    private fun createEncryptedPreferences(context: Context): SharedPreferences {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        return EncryptedSharedPreferences.create(
            context,
            SECURE_PREFS_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    private fun isUserUnlocked(context: Context): Boolean {
        val userManager = context.getSystemService(UserManager::class.java)
        return userManager?.isUserUnlocked != false
    }

    companion object {
        private const val SECURE_PREFS_NAME = "shopkeeper_secure_session"
        private const val FALLBACK_PREFS_NAME = "shopkeeper_secure_session_fallback"
    }
}
