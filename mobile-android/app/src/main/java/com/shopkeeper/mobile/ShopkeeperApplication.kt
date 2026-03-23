package com.shopkeeper.mobile

import android.app.Application
import android.os.UserManager
import androidx.work.Configuration
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import com.shopkeeper.mobile.sync.SyncWorker
import java.util.concurrent.TimeUnit

class ShopkeeperApplication : Application(), Configuration.Provider {
    override val workManagerConfiguration: Configuration
        get() = Configuration.Builder()
            .setMinimumLoggingLevel(android.util.Log.INFO)
            .build()

    override fun onCreate() {
        super.onCreate()

        val userManager = getSystemService(UserManager::class.java)
        if (userManager?.isUserUnlocked == false) {
            return
        }

        val constraints = Constraints.Builder()
            .setRequiredNetworkType(NetworkType.CONNECTED)
            .build()

        val syncRequest = PeriodicWorkRequestBuilder<SyncWorker>(1, TimeUnit.HOURS)
            .setConstraints(constraints)
            .build()

        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            "shopkeeper-periodic-sync",
            ExistingPeriodicWorkPolicy.KEEP,
            syncRequest
        )
    }
}
