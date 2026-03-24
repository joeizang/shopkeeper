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

    private val _authState = MutableStateFlow(!prefs.getString(KEY_ACCESS_TOKEN, null).isNullOrBlank())
    val authState: StateFlow<Boolean> = _authState.asStateFlow()

    fun saveSession(accessToken: String, refreshToken: String, shopId: String, role: String) {
        prefs.edit()
            .putString(KEY_ACCESS_TOKEN, accessToken)
            .putString(KEY_REFRESH_TOKEN, refreshToken)
            .putString(KEY_SHOP_ID, shopId)
            .putString(KEY_ROLE, role)
            .apply()
        _authState.value = true
    }

    fun saveDisplayName(fullName: String?) {
        prefs.edit().putString(KEY_FULL_NAME, fullName?.trim().orEmpty()).apply()
    }

    fun accessToken(): String? = prefs.getString(KEY_ACCESS_TOKEN, null)
    fun refreshToken(): String? = prefs.getString(KEY_REFRESH_TOKEN, null)
    fun shopId(): String? = prefs.getString(KEY_SHOP_ID, null)
    fun role(): String? = prefs.getString(KEY_ROLE, null)
    fun fullName(): String? = prefs.getString(KEY_FULL_NAME, null)?.ifBlank { null }
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
        private const val KEY_ACCESS_TOKEN = "access_token"
        private const val KEY_REFRESH_TOKEN = "refresh_token"
        private const val KEY_SHOP_ID = "shop_id"
        private const val KEY_ROLE = "role"
        private const val KEY_FULL_NAME = "full_name"
    }
}
